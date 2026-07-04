#Requires -Version 5.1
[CmdletBinding()]
param(
    [switch] $SkipAppBuild
)

$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = (Resolve-Path -LiteralPath (Join-Path $ScriptDir '..')).Path
$AppProject = Join-Path $ProjectRoot 'HakamiqChdTool.App.csproj'
$TestProject = Join-Path $ProjectRoot 'HakamiqChdTool.App.Tests\HakamiqChdTool.App.Tests.csproj'
$AppAssembly = Join-Path $ProjectRoot 'bin\Debug\net8.0-windows10.0.17763.0\HakamiqChdTool.dll'

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

Push-Location $ProjectRoot
try {
    Assert-CommandExists 'dotnet'
    Assert-FileExists -Path $AppProject -Message 'App project was not found:'
    Assert-FileExists -Path $TestProject -Message 'PS2 advisory test project was not found:'

    if (-not $SkipAppBuild) {
        Write-Host 'Build app Debug for PS2 advisory tests ...' -ForegroundColor Cyan
        Invoke-NativeCommand -FilePath 'dotnet' -Arguments @(
            'build',
            $AppProject,
            '-c',
            'Debug',
            '--nologo'
        )
    }

    Assert-FileExists -Path $AppAssembly -Message 'App assembly was not found. Build Debug first:'

    Write-Host 'Run PS2 advisory validation tests ...' -ForegroundColor Cyan
    Invoke-NativeCommand -FilePath 'dotnet' -Arguments @(
        'run',
        '--project',
        $TestProject,
        '-c',
        'Debug',
        '--nologo',
        '--',
        '--app-assembly',
        $AppAssembly
    )

    Write-Host '[PASS] PS2 advisory validation tests completed.' -ForegroundColor Green
}
finally {
    Pop-Location
}
