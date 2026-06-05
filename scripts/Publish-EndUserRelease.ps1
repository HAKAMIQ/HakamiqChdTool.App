#Requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet("Release")]
    [string] $Configuration = "Release",

    [string] $Output = ".\Release"
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = (Resolve-Path -LiteralPath (Join-Path $ScriptDir "..")).Path
$ReleaseRoot = [System.IO.Path]::GetFullPath((Join-Path $ProjectRoot "Release"))
$MainProject = Join-Path $ProjectRoot "HakamiqChdTool.App.csproj"
$RepoConventionsScript = Join-Path $ProjectRoot "scripts\Verify-RepoConventions.ps1"
$EndUserReleaseGateScript = Join-Path $ProjectRoot "scripts\Verify-EndUserRelease.ps1"
$ReleaseManifestScript = Join-Path $ProjectRoot "scripts\Generate-ReleaseManifest.ps1"
$StopProcessesScript = Join-Path $ProjectRoot "scripts\Stop-RepoProcesses.ps1"
$PowerShellExe = Join-Path $PSHOME "powershell.exe"

if (-not (Test-Path -LiteralPath $PowerShellExe -PathType Leaf)) {
    $PowerShellExe = "powershell.exe"
}

$OutputPath = if ([System.IO.Path]::IsPathRooted($Output)) {
    [System.IO.Path]::GetFullPath($Output)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $ProjectRoot $Output))
}

$RuntimeIdentifier = "win-x64"
$ExeName = "HakamiqChdTool.exe"

function Get-NormalizedFullPath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    return [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
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

function Assert-CommandExists {
    param(
        [Parameter(Mandatory = $true)]
        [string] $CommandName
    )

    if ($null -eq (Get-Command $CommandName -ErrorAction SilentlyContinue)) {
        throw "Required command is not available: $CommandName"
    }
}

function Assert-FileExists {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RelativePath
    )

    $path = Join-Path $ProjectRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required release file is missing: $RelativePath"
    }
}

function Assert-PathIsInsideProject {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    if (-not (Test-PathIsSameOrChild -Path $Path -Parent $ProjectRoot)) {
        throw "Path is outside project root: $([System.IO.Path]::GetFullPath($Path))"
    }
}

function Assert-OutputPathIsSafe {
    Assert-PathIsInsideProject $OutputPath

    if ((Get-NormalizedFullPath $OutputPath).Equals((Get-NormalizedFullPath $ProjectRoot), [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Output path cannot be the project root."
    }

    if (-not (Test-PathIsSameOrChild -Path $OutputPath -Parent $ReleaseRoot)) {
        throw "Output path must be inside the Release directory: $([System.IO.Path]::GetFullPath($OutputPath))"
    }

    foreach ($blockedName in @("scripts", "Tools", "Resources", "Style", "Views", "Services", "Core", "Models", "ViewModels", "bin", "obj", ".git")) {
        $blockedPath = Join-Path $ProjectRoot $blockedName

        if (Test-PathIsSameOrChild -Path $OutputPath -Parent $blockedPath) {
            throw "Output path cannot target protected project directory: $([System.IO.Path]::GetFullPath($OutputPath))"
        }
    }
}

function Test-PathIsInsideOutput {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    return Test-PathIsSameOrChild -Path $Path -Parent $OutputPath
}

function Remove-BuildArtifacts {
    $artifactDirectories = Get-ChildItem -LiteralPath $ProjectRoot -Directory -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object {
            ($_.Name -eq "bin" -or $_.Name -eq "obj") -and
            $_.FullName -notmatch '\\.git(\\|$)' -and
            -not (Test-PathIsInsideOutput $_.FullName)
        } |
        Sort-Object { $_.FullName.Length } -Descending

    foreach ($directory in $artifactDirectories) {
        Assert-PathIsInsideProject $directory.FullName
        Remove-Item -LiteralPath $directory.FullName -Recurse -Force -ErrorAction Stop
        Write-Host "[INFO] Removed build artifact directory: $($directory.FullName)" -ForegroundColor Cyan
    }
}

function Assert-NoStaleSourceArtifacts {
    $staleFiles = Get-ChildItem -LiteralPath $ProjectRoot -File -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object {
            $_.FullName -notmatch '\\.git(\\|$)' -and
            -not (Test-PathIsInsideOutput $_.FullName) -and
            ($_.Extension.ToLowerInvariant() -in @(".zip", ".tmp"))
        }

    if ($staleFiles) {
        foreach ($file in $staleFiles) {
            Write-Host "[ERROR] Stale source artifact detected: $($file.FullName)" -ForegroundColor Red
        }

        throw "Source tree contains stale ZIP/temp files. Remove them before publishing."
    }
}

function Assert-NoUnsupportedMameTools {
    $toolsRoot = Join-Path $ProjectRoot "Tools"
    if (-not (Test-Path -LiteralPath $toolsRoot -PathType Container)) {
        return
    }

    $unsupported = @(
        "castool.exe",
        "floptool.exe",
        "imgtool.exe",
        "romcmp.exe",
        "unidasm.exe",
        "jedutil.exe",
        "nltool.exe",
        "nlwav.exe",
        "pngcmp.exe",
        "ledutil.exe",
        "ldverify.exe",
        "ldresample.exe"
    )

    $matches = Get-ChildItem -LiteralPath $toolsRoot -File -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object { $unsupported -contains $_.Name.ToLowerInvariant() }

    if ($matches) {
        foreach ($match in $matches) {
            Write-Host "[ERROR] Unsupported MAME external tool is bundled without explicit approval: $($match.FullName)" -ForegroundColor Red
        }

        throw "Unsupported MAME tools detected."
    }
}

function Invoke-ReleaseComplianceChecks {
    foreach ($required in @(
        "LICENSE",
        "docs\legal\LEGAL.md",
        "docs\legal\THIRD_PARTY_NOTICES.txt",
        "docs\legal\CHDMAN_NOTICE.md",
        "docs\legal\MAME_COPYING.txt",
        "docs\legal\MAME_GPL-2.0.txt",
        "docs\legal\SEVENZIP_NOTICE.md",
        "Tools\7zip\7z.exe",
        "Tools\7zip\7z.dll",
        "Tools\7zip\License.txt",
        "Resources\HakamiqLogo.ico",
        "App.xaml",
        "HakamiqChdTool.App.csproj",
        "scripts\Verify-RepoConventions.ps1",
        "scripts\Verify-EndUserRelease.ps1",
        "scripts\Generate-ReleaseManifest.ps1"
    )) {
        Assert-FileExists $required
    }

    Assert-NoStaleSourceArtifacts
    Assert-NoUnsupportedMameTools
}

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string] $FilePath,

        [Parameter(Mandatory = $true)]
        [string[]] $Arguments
    )

    Write-Host "[INFO] $FilePath $($Arguments -join ' ')" -ForegroundColor Cyan

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

    Invoke-NativeCommand $PowerShellExe $powerShellArguments
}

Push-Location $ProjectRoot
try {
    Assert-CommandExists "dotnet"
    Assert-OutputPathIsSafe

    if (-not (Test-Path -LiteralPath $MainProject -PathType Leaf)) {
        throw "Main project file was not found: $MainProject"
    }

    if (Test-Path -LiteralPath $StopProcessesScript -PathType Leaf) {
        Write-Host "[INFO] Stopping running app instances from this repository..." -ForegroundColor Cyan
        Invoke-PowerShellFile $StopProcessesScript
    }

    if (Test-Path -LiteralPath $OutputPath -PathType Container) {
        Remove-Item -LiteralPath $OutputPath -Recurse -Force -ErrorAction Stop
    }

    Remove-BuildArtifacts
    Invoke-ReleaseComplianceChecks

    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null

    Write-Host "[INFO] Running repository conventions gate..." -ForegroundColor Cyan
    Invoke-PowerShellFile $RepoConventionsScript

    Invoke-NativeCommand "dotnet" @(
        "restore",
        $MainProject,
        "-r",
        $RuntimeIdentifier
    )

    Invoke-NativeCommand "dotnet" @(
        "build",
        $MainProject,
        "-c",
        "Debug",
        "--no-restore"
    )

    Invoke-NativeCommand "dotnet" @(
        "build",
        $MainProject,
        "-c",
        $Configuration,
        "-r",
        $RuntimeIdentifier,
        "--no-restore",
        "-p:DebugType=none",
        "-p:DebugSymbols=false"
    )

    Invoke-NativeCommand "dotnet" @(
        "publish",
        $MainProject,
        "-c",
        $Configuration,
        "-r",
        $RuntimeIdentifier,
        "-o",
        $OutputPath,
        "--no-restore",
        "--no-build",
        "-p:DebugType=none",
        "-p:DebugSymbols=false"
    )

    foreach ($doc in @(
        "LICENSE",
        "docs\legal\LEGAL.md",
        "docs\legal\THIRD_PARTY_NOTICES.txt",
        "docs\legal\CHDMAN_NOTICE.md",
        "docs\legal\MAME_COPYING.txt",
        "docs\legal\MAME_GPL-2.0.txt",
        "docs\legal\SEVENZIP_NOTICE.md",
        "README.md",
        "docs\release-notes\CHANGELOG.md"
    )) {
        $sourcePath = Join-Path $ProjectRoot $doc
        if (Test-Path -LiteralPath $sourcePath -PathType Leaf) {
            $destinationName = Split-Path -Path $doc -Leaf
            Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $OutputPath $destinationName) -Force
            Write-Host "[INFO] Copied $destinationName" -ForegroundColor Cyan
        }
    }

    $exePath = Join-Path $OutputPath $ExeName
    if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
        throw "Expected exe was not found: $exePath"
    }

    Write-Host "[INFO] Generating end-user release manifest..." -ForegroundColor Cyan
    Invoke-PowerShellFile $ReleaseManifestScript @(
        "-Output",
        $OutputPath
    )

    Write-Host "[INFO] Running end-user release security gate..." -ForegroundColor Cyan
    Invoke-PowerShellFile $EndUserReleaseGateScript @(
        "-Output",
        $OutputPath
    )

    Write-Host "[PASS] End-user release is ready: $OutputPath" -ForegroundColor Green
}
finally {
    Pop-Location
}