#Requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = (Resolve-Path -LiteralPath (Join-Path $ScriptDir '..')).Path
$Project = Join-Path $ProjectRoot 'HakamiqChdTool.App.csproj'
$RepoCheck = Join-Path $ScriptDir 'Verify-RepoConventions.ps1'
$Ps2AdvisoryTests = Join-Path $ScriptDir 'Run-Ps2AdvisoryValidationTests.ps1'
$Checklist = Join-Path $ProjectRoot 'docs\SMOKE_TEST_CHECKLIST.md'
$PowerShellExe = Join-Path $PSHOME 'powershell.exe'

if (-not (Test-Path -LiteralPath $PowerShellExe -PathType Leaf)) {
    $PowerShellExe = 'powershell.exe'
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
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $Message
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Message $Path"
    }
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

    Assert-FileExists -Path $ScriptPath -Message 'PowerShell script was not found:'

    $powerShellArguments = @(
        '-NoProfile',
        '-ExecutionPolicy',
        'Bypass',
        '-File',
        $ScriptPath
    ) + $Arguments

    Invoke-NativeCommand -FilePath $PowerShellExe -Arguments $powerShellArguments
}

Push-Location $ProjectRoot
try {
    Assert-CommandExists 'dotnet'
    Assert-FileExists -Path $Project -Message 'Project file was not found:'
    Assert-FileExists -Path $RepoCheck -Message 'Repository convention script was not found:'
    Assert-FileExists -Path $Ps2AdvisoryTests -Message 'PS2 advisory validation script was not found:'

    Write-Host 'Repository conventions ...' -ForegroundColor Cyan
    Invoke-PowerShellFile -ScriptPath $RepoCheck

    Write-Host 'dotnet restore ...' -ForegroundColor Cyan
    Invoke-NativeCommand -FilePath 'dotnet' -Arguments @(
        'restore',
        $Project,
        '-r',
        'win-x64'
    )

    Write-Host 'Build Debug ...' -ForegroundColor Cyan
    Invoke-NativeCommand -FilePath 'dotnet' -Arguments @(
        'build',
        $Project,
        '-c',
        'Debug',
        '--no-restore'
    )

    Write-Host 'Build Release ...' -ForegroundColor Cyan
    Invoke-NativeCommand -FilePath 'dotnet' -Arguments @(
        'build',
        $Project,
        '-c',
        'Release',
        '-r',
        'win-x64',
        '--no-restore'
    )

    Write-Host 'PS2 advisory validation tests ...' -ForegroundColor Cyan
    Invoke-PowerShellFile -ScriptPath $Ps2AdvisoryTests -Arguments @('-SkipAppBuild')

    Write-Host ''
    Write-Host 'Manual smoke checklist:' -ForegroundColor Yellow
    Write-Host '  1) Launch app in Light theme.'
    Write-Host '  2) Launch app in Dark theme.'
    Write-Host '  3) Launch app in Hakamiq theme.'
    Write-Host '  4) Open MainWindow.'
    Write-Host '  5) Open OptionsWindow.'
    Write-Host '  6) Open AboutWindow.'
    Write-Host '  7) Switch Arabic and English once, then restart.'
    Write-Host '  8) Confirm no XAML parse errors.'
    Write-Host '  9) Confirm no resource lookup failures in logs/output.'
    Write-Host ''

    if (Test-Path -LiteralPath $Checklist -PathType Leaf) {
        Write-Host "Checklist: $Checklist" -ForegroundColor Yellow
    }
    else {
        Write-Host "Checklist file was not found: $Checklist" -ForegroundColor DarkYellow
    }

    Write-Host '[PASS] Local verification completed.' -ForegroundColor Green

}
finally {
    Pop-Location
}
