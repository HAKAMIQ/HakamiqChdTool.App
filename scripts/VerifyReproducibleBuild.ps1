#Requires -Version 5.1
[CmdletBinding()]
param(
    [string] $ReportPath = ".\TestResults\Security\reproducible-build-report.json",

    [switch] $KeepOutputs
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = (Resolve-Path -LiteralPath (Join-Path $ScriptDir "..")).Path
$ReleaseRoot = [System.IO.Path]::GetFullPath((Join-Path $ProjectRoot "Release"))
$PublishScript = Join-Path $ScriptDir "PublishRel.ps1"
$RunId = [Guid]::NewGuid().ToString("N")
$WorkRoot = Join-Path $ReleaseRoot "_repro-$RunId"
$FirstOutput = Join-Path $WorkRoot "first"
$SecondOutput = Join-Path $WorkRoot "second"

function Get-NormalizedFullPath {
    param([Parameter(Mandatory = $true)][string] $Path)
    return [System.IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
}

function Test-PathIsSameOrChild {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Parent
    )

    $candidate = Get-NormalizedFullPath $Path
    $root = Get-NormalizedFullPath $Parent
    return $candidate.Equals($root, [System.StringComparison]::OrdinalIgnoreCase) -or
        $candidate.StartsWith($root + "\", [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-RelativePathCompat {
    param(
        [Parameter(Mandatory = $true)][string] $BasePath,
        [Parameter(Mandatory = $true)][string] $FullPath
    )

    $baseFullPath = [System.IO.Path]::GetFullPath($BasePath).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $baseUri = [System.Uri]::new($baseFullPath)
    $fileUri = [System.Uri]::new([System.IO.Path]::GetFullPath($FullPath))
    return [System.Uri]::UnescapeDataString($baseUri.MakeRelativeUri($fileUri).ToString()).Replace('/', '\')
}

function Get-FileMap {
    param([Parameter(Mandatory = $true)][string] $Root)

    $map = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($file in Get-ChildItem -LiteralPath $Root -File -Recurse -Force | Sort-Object FullName) {
        $relativePath = Get-RelativePathCompat -BasePath $Root -FullPath $file.FullName
        $map[$relativePath] = [ordered]@{
            sizeBytes = $file.Length
            sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
        }
    }
    return $map
}

function Invoke-Publish {
    param([Parameter(Mandatory = $true)][string] $Output)

    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $PublishScript -Configuration Release -Output $Output
    if ($LASTEXITCODE -ne 0) {
        throw "Deterministic publish failed for output: $Output"
    }
}

if (-not (Test-PathIsSameOrChild -Path $WorkRoot -Parent $ReleaseRoot) -or
    -not ([System.IO.Path]::GetFileName($WorkRoot).StartsWith("_repro-", [System.StringComparison]::Ordinal))) {
    throw "Unsafe reproducible-build work root: $WorkRoot"
}

$reportFullPath = if ([System.IO.Path]::IsPathRooted($ReportPath)) {
    [System.IO.Path]::GetFullPath($ReportPath)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $ProjectRoot $ReportPath))
}

try {
    New-Item -ItemType Directory -Path $WorkRoot -Force | Out-Null

    Invoke-Publish -Output $FirstOutput
    Invoke-Publish -Output $SecondOutput

    $firstMap = Get-FileMap $FirstOutput
    $secondMap = Get-FileMap $SecondOutput
    $differences = @()
    $allPaths = @($firstMap.Keys + $secondMap.Keys | Sort-Object -Unique)

    foreach ($relativePath in $allPaths) {
        if (-not $firstMap.ContainsKey($relativePath)) {
            $differences += [ordered]@{ path = $relativePath; reason = "missing-from-first" }
            continue
        }
        if (-not $secondMap.ContainsKey($relativePath)) {
            $differences += [ordered]@{ path = $relativePath; reason = "missing-from-second" }
            continue
        }

        $first = $firstMap[$relativePath]
        $second = $secondMap[$relativePath]
        if ($first.sizeBytes -ne $second.sizeBytes -or $first.sha256 -ne $second.sha256) {
            $differences += [ordered]@{
                path = $relativePath
                reason = "content-mismatch"
                first = $first
                second = $second
            }
        }
    }

    $reportDirectory = Split-Path -Parent $reportFullPath
    if (-not [string]::IsNullOrWhiteSpace($reportDirectory)) {
        New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
    }

    $report = [ordered]@{
        format = "HakamiqReproducibleBuildReport.v1"
        completedAtUtc = [System.DateTimeOffset]::UtcNow.ToString("O")
        scope = "two clean end-user publishes on the same Windows runner and source checkout"
        independentReproduction = $false
        fileCount = $allPaths.Count
        match = ($differences.Count -eq 0)
        differences = $differences
        sourceDateEpoch = [System.Environment]::GetEnvironmentVariable("SOURCE_DATE_EPOCH")
    }
    [System.IO.File]::WriteAllText(
        $reportFullPath,
        ($report | ConvertTo-Json -Depth 12),
        [System.Text.UTF8Encoding]::new($false))

    if ($differences.Count -gt 0) {
        throw "Reproducible-build comparison failed for $($differences.Count) path(s). Report: $reportFullPath"
    }

    Write-Host "[PASS] Same-runner reproducible-build gate passed for $($allPaths.Count) files: $reportFullPath" -ForegroundColor Green
}
finally {
    if (-not $KeepOutputs -and (Test-Path -LiteralPath $WorkRoot -PathType Container)) {
        if (-not (Test-PathIsSameOrChild -Path $WorkRoot -Parent $ReleaseRoot) -or
            -not ([System.IO.Path]::GetFileName($WorkRoot).StartsWith("_repro-", [System.StringComparison]::Ordinal))) {
            throw "Refusing to clean unsafe reproducible-build work root: $WorkRoot"
        }
        Remove-Item -LiteralPath $WorkRoot -Recurse -Force
    }
}
