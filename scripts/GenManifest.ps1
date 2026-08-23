#Requires -Version 5.1
[CmdletBinding()]
param(
    [string] $Output = ".\Release"
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = (Resolve-Path -LiteralPath (Join-Path $ScriptDir "..")).Path
$ReleaseRoot = [System.IO.Path]::GetFullPath((Join-Path $ProjectRoot "Release"))

$OutputPath = if ([System.IO.Path]::IsPathRooted($Output)) {
    [System.IO.Path]::GetFullPath($Output)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $ProjectRoot $Output))
}

$ManifestRelativePath = "release-manifest.json"
$ManifestPath = Join-Path $OutputPath $ManifestRelativePath

function Write-Info {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Message
    )

    Write-Host "[INFO] $Message" -ForegroundColor Cyan
}

function Get-NormalizedFullPath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $root = [System.IO.Path]::GetPathRoot($fullPath)

    if ($fullPath.Equals($root, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath.TrimEnd('/')
    }

    return $fullPath.TrimEnd('\', '/')
}

function Test-PathIsSameOrChild {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $Parent
    )

    $fullPath = Get-NormalizedFullPath $Path
    $fullParent = Get-NormalizedFullPath $Parent

    if ($fullPath.Equals($fullParent, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    return $fullPath.StartsWith($fullParent + "\", [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-OutputPathIsSafe {
    if (-not (Test-PathIsSameOrChild -Path $OutputPath -Parent $ReleaseRoot)) {
        throw "Release manifest output must be inside the Release directory: $OutputPath"
    }

    if ((Get-NormalizedFullPath $OutputPath).Equals((Get-NormalizedFullPath $ProjectRoot), [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Release manifest output cannot be the project root."
    }
}

function Get-RelativeReleasePath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $FilePath
    )

    $fullPath = [System.IO.Path]::GetFullPath($FilePath)
    if (-not (Test-PathIsSameOrChild -Path $fullPath -Parent $OutputPath)) {
        throw "File is outside release output: $fullPath"
    }

    return $fullPath.Substring((Get-NormalizedFullPath $OutputPath).Length).TrimStart('\', '/').Replace('/', '\')
}

function Get-Sha256Hex {
    param(
        [Parameter(Mandatory = $true)]
        [string] $FilePath
    )

    $stream = [System.IO.File]::OpenRead($FilePath)
    try {
        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        try {
            $hash = $sha256.ComputeHash($stream)
            return ([System.BitConverter]::ToString($hash)).Replace('-', '').ToUpperInvariant()
        }
        finally {
            $sha256.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Get-DeterministicManifestTimestamp {
    $epochText = [System.Environment]::GetEnvironmentVariable("SOURCE_DATE_EPOCH")
    [Int64] $epoch = 0

    if (-not [string]::IsNullOrWhiteSpace($epochText) -and
        [Int64]::TryParse($epochText, [ref] $epoch) -and
        $epoch -ge 0) {
        return [System.DateTimeOffset]::FromUnixTimeSeconds($epoch).ToUniversalTime()
    }

    $git = Get-Command git -ErrorAction SilentlyContinue
    if ($null -ne $git) {
        [string] $commitEpochText = (& $git.Source -C $ProjectRoot show -s --format=%ct HEAD 2>$null | Select-Object -First 1)
        if ($LASTEXITCODE -eq 0 -and
            -not [string]::IsNullOrWhiteSpace($commitEpochText) -and
            [Int64]::TryParse($commitEpochText, [ref] $epoch) -and
            $epoch -ge 0) {
            return [System.DateTimeOffset]::FromUnixTimeSeconds($epoch).ToUniversalTime()
        }
    }

    # A fixed fallback keeps source-package builds deterministic when Git metadata is unavailable.
    return [System.DateTimeOffset]::FromUnixTimeSeconds(0).ToUniversalTime()
}

function New-ReleaseManifest {
    if (Test-Path -LiteralPath $ManifestPath -PathType Leaf) {
        Remove-Item -LiteralPath $ManifestPath -Force -ErrorAction Stop
    }

    $files = Get-ChildItem -LiteralPath $OutputPath -File -Recurse -Force -ErrorAction Stop |
        Where-Object {
            $relativePath = Get-RelativeReleasePath $_.FullName
            -not $relativePath.Equals($ManifestRelativePath, [System.StringComparison]::OrdinalIgnoreCase)
        } |
        Sort-Object { Get-RelativeReleasePath $_.FullName }

    $entries = @()
    foreach ($file in $files) {
        $entries += [ordered]@{
            path = Get-RelativeReleasePath $file.FullName
            sizeBytes = $file.Length
            sha256 = Get-Sha256Hex $file.FullName
        }
    }

    $manifest = [ordered]@{
        format = "HakamiqReleaseManifest.v1"
        app = "HakamiqChdTool"
        generatedUtc = (Get-DeterministicManifestTimestamp).ToString("O", [System.Globalization.CultureInfo]::InvariantCulture)
        fileCount = $entries.Count
        files = $entries
    }

    $json = $manifest | ConvertTo-Json -Depth 8
    [System.IO.File]::WriteAllText($ManifestPath, $json, [System.Text.UTF8Encoding]::new($false))

    Write-Info "Generated release manifest: $ManifestPath"
}

Assert-OutputPathIsSafe

if (-not (Test-Path -LiteralPath $OutputPath -PathType Container)) {
    throw "Release output directory was not found: $OutputPath"
}

New-ReleaseManifest
