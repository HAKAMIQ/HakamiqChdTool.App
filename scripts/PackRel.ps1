#Requires -Version 5.1
[CmdletBinding()]
param(
    [Alias("OutputPath")]
    [string] $ReleaseOutput = ".\Release\_output-gate",

    [string] $PackageDirectory = ".\Release\packages",

    [string] $PackageName = "HakamiqChdTool-win-x64-runtime-required.zip",

    [switch] $KeepVerificationOutput
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.IO.Compression

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = (Resolve-Path -LiteralPath (Join-Path $ScriptDir "..")).Path
$ReleaseRoot = [System.IO.Path]::GetFullPath((Join-Path $ProjectRoot "Release"))
$VerifyReleaseScript = Join-Path $ScriptDir "VerifyRelease.ps1"
$PowerShellExe = Join-Path $PSHOME "powershell.exe"

if (-not (Test-Path -LiteralPath $PowerShellExe -PathType Leaf)) {
    $PowerShellExe = "powershell.exe"
}

function Resolve-ProjectPath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $ProjectRoot $Path))
}

$ReleaseOutputPath = Resolve-ProjectPath $ReleaseOutput
$PackageDirectoryPath = Resolve-ProjectPath $PackageDirectory
$VerificationOutputPath = Join-Path $ReleaseRoot "_package-verify"
$ZipPath = Join-Path $PackageDirectoryPath $PackageName
$ShaPath = "$ZipPath.sha256"

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
        return $fullPath.TrimEnd("/")
    }

    return $fullPath.TrimEnd("\", "/")
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

function Assert-PathInsideReleaseRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $Label
    )

    if (-not (Test-PathIsSameOrChild -Path $Path -Parent $ReleaseRoot)) {
        throw "$Label must be inside the Release directory: $Path"
    }
}

function Assert-PackagePathsAreSafe {
    Assert-PathInsideReleaseRoot -Path $ReleaseOutputPath -Label "Release output"
    Assert-PathInsideReleaseRoot -Path $PackageDirectoryPath -Label "Package directory"
    Assert-PathInsideReleaseRoot -Path $VerificationOutputPath -Label "Package verification output"

    if ((Get-NormalizedFullPath $ReleaseOutputPath).Equals((Get-NormalizedFullPath $ReleaseRoot), [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "PackRel.ps1 requires a release output subdirectory so the package files are not created inside their own source."
    }

    if (Test-PathIsSameOrChild -Path $PackageDirectoryPath -Parent $ReleaseOutputPath) {
        throw "Package directory cannot be inside the release output being zipped: $PackageDirectoryPath"
    }

    if (Test-PathIsSameOrChild -Path $ReleaseOutputPath -Parent $PackageDirectoryPath) {
        throw "Release output cannot be inside the package directory: $ReleaseOutputPath"
    }

    if (Test-PathIsSameOrChild -Path $VerificationOutputPath -Parent $ReleaseOutputPath) {
        throw "Package verification output cannot be inside the release output being zipped: $VerificationOutputPath"
    }

    if (Test-PathIsSameOrChild -Path $VerificationOutputPath -Parent $PackageDirectoryPath) {
        throw "Package verification output cannot be inside the upload package directory: $VerificationOutputPath"
    }

    if ($PackageName -match '[\\/:]' -or -not $PackageName.EndsWith(".zip", [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "PackageName must be a simple .zip file name: $PackageName"
    }
}

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string] $FilePath,

        [Parameter(Mandatory = $true)]
        [string[]] $Arguments
    )

    Write-Info "$FilePath $($Arguments -join ' ')"

    $global:LASTEXITCODE = 0
    & $FilePath @Arguments

    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath"
    }
}

function Invoke-PowerShellFile {
    param(
        [Parameter(Mandatory = $true)]
        [string] $ScriptPath,

        [string[]] $Arguments = @()
    )

    if (-not (Test-Path -LiteralPath $ScriptPath -PathType Leaf)) {
        throw "PowerShell script was not found: $ScriptPath"
    }

    $powerShellArguments = @(
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        $ScriptPath
    ) + $Arguments

    Invoke-NativeCommand -FilePath $PowerShellExe -Arguments $powerShellArguments
}

function Get-RelativePathCompat {
    param(
        [Parameter(Mandatory = $true)]
        [string] $BasePath,

        [Parameter(Mandatory = $true)]
        [string] $FullPath
    )

    $baseFullPath = [System.IO.Path]::GetFullPath($BasePath).TrimEnd("\", "/") + [System.IO.Path]::DirectorySeparatorChar
    $itemFullPath = [System.IO.Path]::GetFullPath($FullPath)

    $baseUri = [System.Uri]::new($baseFullPath)
    $itemUri = [System.Uri]::new($itemFullPath)
    $relativeUri = $baseUri.MakeRelativeUri($itemUri)
    $relativePath = [System.Uri]::UnescapeDataString($relativeUri.ToString())

    return $relativePath.Replace("/", [System.IO.Path]::DirectorySeparatorChar)
}

function New-DeterministicReleaseZip {
    if (Test-Path -LiteralPath $ZipPath -PathType Leaf) {
        Remove-Item -LiteralPath $ZipPath -Force -ErrorAction Stop
    }

    $fixedTimestamp = [System.DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [System.TimeSpan]::Zero)
    $files = Get-ChildItem -LiteralPath $ReleaseOutputPath -File -Recurse -Force -ErrorAction Stop |
        Sort-Object { Get-RelativePathCompat -BasePath $ReleaseOutputPath -FullPath $_.FullName }

    $zipStream = [System.IO.File]::Open($ZipPath, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
    try {
        $zip = [System.IO.Compression.ZipArchive]::new($zipStream, [System.IO.Compression.ZipArchiveMode]::Create, $false)
        try {
            foreach ($file in $files) {
                $relativePath = (Get-RelativePathCompat -BasePath $ReleaseOutputPath -FullPath $file.FullName).Replace("\", "/")
                $entry = $zip.CreateEntry($relativePath, [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $fixedTimestamp

                $entryStream = $entry.Open()
                try {
                    $sourceStream = [System.IO.File]::OpenRead($file.FullName)
                    try {
                        $sourceStream.CopyTo($entryStream)
                    }
                    finally {
                        $sourceStream.Dispose()
                    }
                }
                finally {
                    $entryStream.Dispose()
                }
            }
        }
        finally {
            $zip.Dispose()
        }
    }
    finally {
        $zipStream.Dispose()
    }
}

function Write-Sha256File {
    $hash = (Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash.ToUpperInvariant()
    $line = "$hash  $PackageName"
    [System.IO.File]::WriteAllText($ShaPath, $line + "`r`n", [System.Text.UTF8Encoding]::new($false))
}

function Assert-Sha256FileMatchesZip {
    if (-not (Test-Path -LiteralPath $ShaPath -PathType Leaf)) {
        throw "Release SHA256 file was not created: $ShaPath"
    }

    $shaText = (Get-Content -LiteralPath $ShaPath -Raw -Encoding UTF8).Trim()
    $match = [regex]::Match($shaText, '^(?<hash>[0-9A-Fa-f]{64})\s\s(?<name>.+\.zip)$')
    if (-not $match.Success) {
        throw "Release SHA256 file has an invalid format: $ShaPath"
    }

    $expectedPackageName = $match.Groups["name"].Value
    if (-not $expectedPackageName.Equals($PackageName, [System.StringComparison]::Ordinal)) {
        throw "Release SHA256 file references the wrong package. Expected $PackageName, found $expectedPackageName."
    }

    $expectedHash = $match.Groups["hash"].Value.ToUpperInvariant()
    $actualHash = (Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($expectedHash -ne $actualHash) {
        throw "Release SHA256 file does not match ZIP hash. Expected $expectedHash, actual $actualHash."
    }
}

function Assert-PackageDirectoryClean {
    $items = @(Get-ChildItem -LiteralPath $PackageDirectoryPath -Force -ErrorAction Stop)
    $approved = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    [void]$approved.Add($PackageName)
    [void]$approved.Add("$PackageName.sha256")

    foreach ($item in $items) {
        if ($item.PSIsContainer) {
            throw "Upload package directory must not contain subdirectories: $($item.FullName)"
        }

        if (-not $approved.Contains($item.Name)) {
            throw "Upload package directory contains an unapproved file: $($item.FullName)"
        }

        if ($item.Length -le 0) {
            throw "Upload package file is empty: $($item.FullName)"
        }
    }

    if (-not (Test-Path -LiteralPath $ZipPath -PathType Leaf)) {
        throw "Release ZIP was not created: $ZipPath"
    }

    Assert-Sha256FileMatchesZip
}

Assert-PackagePathsAreSafe

if (-not (Test-Path -LiteralPath $ReleaseOutputPath -PathType Container)) {
    throw "Release output directory was not found: $ReleaseOutputPath"
}

Push-Location $ProjectRoot
try {
    Write-Info "Verifying release output before packaging: $ReleaseOutputPath"
    Invoke-PowerShellFile -ScriptPath $VerifyReleaseScript -Arguments @("-Output", $ReleaseOutputPath)

    if (Test-Path -LiteralPath $PackageDirectoryPath -PathType Container) {
        Remove-Item -LiteralPath $PackageDirectoryPath -Recurse -Force -ErrorAction Stop
    }

    New-Item -ItemType Directory -Path $PackageDirectoryPath -Force | Out-Null

    Write-Info "Creating deterministic release ZIP: $ZipPath"
    New-DeterministicReleaseZip
    Write-Sha256File
    Assert-Sha256FileMatchesZip

    if (Test-Path -LiteralPath $VerificationOutputPath -PathType Container) {
        Remove-Item -LiteralPath $VerificationOutputPath -Recurse -Force -ErrorAction Stop
    }

    New-Item -ItemType Directory -Path $VerificationOutputPath -Force | Out-Null

    Write-Info "Extracting release ZIP for verification: $VerificationOutputPath"
    Expand-Archive -LiteralPath $ZipPath -DestinationPath $VerificationOutputPath -Force

    Write-Info "Verifying extracted release ZIP contents ..."
    Invoke-PowerShellFile -ScriptPath $VerifyReleaseScript -Arguments @("-Output", $VerificationOutputPath)

    Assert-PackageDirectoryClean

    Write-Host "[PASS] Release package is ready: $ZipPath" -ForegroundColor Green
    Write-Host "[PASS] Release package SHA256: $ShaPath" -ForegroundColor Green
}
finally {
    Pop-Location

    if (-not $KeepVerificationOutput -and (Test-Path -LiteralPath $VerificationOutputPath -PathType Container)) {
        Remove-Item -LiteralPath $VerificationOutputPath -Recurse -Force -ErrorAction SilentlyContinue
        Write-Info "Removed package verification output: $VerificationOutputPath"
    }
}
