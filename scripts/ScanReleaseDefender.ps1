#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Target,

    [string] $ReportPath
)

$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = (Resolve-Path -LiteralPath (Join-Path $ScriptDir '..')).Path

function Get-NormalizedFullPath {
    param([Parameter(Mandatory = $true)][string] $Path)

    return [System.IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
}

function Test-PathsEqual {
    param(
        [Parameter(Mandatory = $true)][string] $Left,
        [Parameter(Mandatory = $true)][string] $Right
    )

    return (Get-NormalizedFullPath $Left).Equals(
        (Get-NormalizedFullPath $Right),
        [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-HasReparsePointInExistingPath {
    param([Parameter(Mandatory = $true)][string] $Path)

    try {
        $current = [System.IO.Path]::GetFullPath($Path)
        $root = [System.IO.Path]::GetPathRoot($current)
        if ([string]::IsNullOrWhiteSpace($root)) {
            return $true
        }

        while ($true) {
            if ((Test-Path -LiteralPath $current) -and
                ((Get-Item -LiteralPath $current -Force).Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
                return $true
            }

            if (Test-PathsEqual -Left $current -Right $root) {
                return $false
            }

            $parent = [System.IO.Directory]::GetParent($current)
            if ($null -eq $parent -or (Test-PathsEqual -Left $parent.FullName -Right $current)) {
                return $true
            }

            $current = $parent.FullName
        }
    }
    catch {
        return $true
    }
}

function Resolve-MpCmdRun {
    $platformRoot = Join-Path $env:ProgramData 'Microsoft\Windows Defender\Platform'
    if (Test-Path -LiteralPath $platformRoot -PathType Container) {
        $versionedDirectories = foreach ($directory in Get-ChildItem -LiteralPath $platformRoot -Directory -Force -ErrorAction SilentlyContinue) {
            $parsedVersion = $null
            if ([System.Version]::TryParse($directory.Name, [ref] $parsedVersion)) {
                [pscustomobject]@{
                    Directory = $directory
                    Version = $parsedVersion
                }
            }
        }

        foreach ($entry in @($versionedDirectories | Sort-Object Version -Descending)) {
            $candidate = Join-Path $entry.Directory.FullName 'MpCmdRun.exe'
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                return (Resolve-Path -LiteralPath $candidate).Path
            }
        }
    }

    $fallback = Join-Path $env:ProgramFiles 'Windows Defender\MpCmdRun.exe'
    if (Test-Path -LiteralPath $fallback -PathType Leaf) {
        return (Resolve-Path -LiteralPath $fallback).Path
    }

    throw 'Microsoft Defender MpCmdRun.exe is unavailable.'
}

if ($env:OS -ne 'Windows_NT') {
    throw 'Microsoft Defender release scanning requires Windows.'
}

$TargetPath = if ([System.IO.Path]::IsPathRooted($Target)) {
    [System.IO.Path]::GetFullPath($Target)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $ProjectRoot $Target))
}

if (-not (Test-Path -LiteralPath $TargetPath)) {
    throw "Microsoft Defender scan target does not exist: $TargetPath"
}

$targetRoot = [System.IO.Path]::GetPathRoot($TargetPath)
if ([string]::IsNullOrWhiteSpace($targetRoot) -or
    (Test-PathsEqual -Left $TargetPath -Right $targetRoot) -or
    (Test-HasReparsePointInExistingPath -Path $TargetPath)) {
    throw "Microsoft Defender scan target is unsafe: $TargetPath"
}

if ($null -eq (Get-Command Get-MpComputerStatus -ErrorAction SilentlyContinue)) {
    throw 'Microsoft Defender status cmdlet Get-MpComputerStatus is unavailable.'
}

$status = Get-MpComputerStatus
if ($null -eq $status -or
    $status.AMServiceEnabled -ne $true -or
    $status.AntivirusEnabled -ne $true -or
    $status.RealTimeProtectionEnabled -ne $true) {
    throw 'Microsoft Defender Antivirus is unavailable, disabled, or not actively protecting this Windows host.'
}

if ([string]::IsNullOrWhiteSpace([string] $status.AMProductVersion) -or
    [string]::IsNullOrWhiteSpace([string] $status.AntivirusSignatureVersion)) {
    throw 'Microsoft Defender engine or security intelligence version is unavailable.'
}

$mpCmdRun = Resolve-MpCmdRun

# Microsoft Learn documents ScanType 3 as a custom scan, -File as its target,
# -DisableRemediation as a custom-scan-only option, and return codes 0 and 2.
# With remediation disabled, this gate accepts only the documented clean-success code 0.
$scanArguments = @(
    '-Scan',
    '-ScanType',
    '3',
    '-File',
    $TargetPath,
    '-DisableRemediation'
)

Write-Host "[INFO] Microsoft Defender: $mpCmdRun $($scanArguments -join ' ')" -ForegroundColor Cyan
$scanOutput = @(& $mpCmdRun @scanArguments 2>&1)
$scanExitCode = $LASTEXITCODE

foreach ($line in $scanOutput) {
    Write-Host $line.ToString()
}

$report = [ordered]@{
    format = 'HakamiqDefenderScanReport.v1'
    scannedAtUtc = [System.DateTimeOffset]::UtcNow.ToString('O')
    target = $TargetPath
    targetKind = if (Test-Path -LiteralPath $TargetPath -PathType Container) { 'Directory' } else { 'File' }
    remediationDisabled = $true
    mpCmdRunPath = $mpCmdRun
    exitCode = $scanExitCode
    passed = ($scanExitCode -eq 0)
    defender = [ordered]@{
        productVersion = [string] $status.AMProductVersion
        engineVersion = [string] $status.AMEngineVersion
        signatureVersion = [string] $status.AntivirusSignatureVersion
        signatureLastUpdatedUtc = $status.AntivirusSignatureLastUpdated.ToUniversalTime().ToString('O')
        amServiceEnabled = [bool] $status.AMServiceEnabled
        antivirusEnabled = [bool] $status.AntivirusEnabled
        realTimeProtectionEnabled = [bool] $status.RealTimeProtectionEnabled
    }
    output = @($scanOutput | ForEach-Object { $_.ToString() })
}

if (-not [string]::IsNullOrWhiteSpace($ReportPath)) {
    $reportFullPath = if ([System.IO.Path]::IsPathRooted($ReportPath)) {
        [System.IO.Path]::GetFullPath($ReportPath)
    }
    else {
        [System.IO.Path]::GetFullPath((Join-Path $ProjectRoot $ReportPath))
    }

    $reportDirectory = Split-Path -Parent $reportFullPath
    if ([string]::IsNullOrWhiteSpace($reportDirectory)) {
        throw "Microsoft Defender report path is invalid: $reportFullPath"
    }

    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
    [System.IO.File]::WriteAllText(
        $reportFullPath,
        ($report | ConvertTo-Json -Depth 8),
        [System.Text.UTF8Encoding]::new($false))
    Write-Host "[INFO] Microsoft Defender scan report: $reportFullPath" -ForegroundColor Cyan
}

if ($scanExitCode -ne 0) {
    throw "Microsoft Defender custom scan failed closed. ExitCode=$scanExitCode Target=$TargetPath"
}

Write-Host "[PASS] Microsoft Defender custom scan passed: $TargetPath" -ForegroundColor Green
