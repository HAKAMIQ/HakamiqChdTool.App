[CmdletBinding()]
param(
    [ValidateRange(1, 168)]
    [int]$Hours = 24,

    [ValidateRange(1, 256)]
    [int]$SampleMebibytes = 4,

    [string]$Output = ".\TestResults\Security",

    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repo "HakamiqChdTool.App.sln"
$appAssembly = Join-Path $repo `
    "bin\Release\net10.0-windows10.0.17763.0\win-x64\HakamiqChdTool.dll"
$testAssembly = Join-Path $repo `
    "HakamiqChdTool.App.Tests\bin\Release\net10.0-windows10.0.17763.0\HakamiqChdTool.App.Tests.dll"
$outputPath = [IO.Path]::GetFullPath((Join-Path $repo $Output))
$durationMinutes = [int]($Hours * 60)

if ($null -eq (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK is not installed or is not available in PATH."
}

Push-Location $repo
try {
    if (-not $SkipBuild) {
        & dotnet restore $solution --locked-mode
        if ($LASTEXITCODE -ne 0) {
            throw "Locked restore failed."
        }

        & dotnet build $solution -c Release --no-restore
        if ($LASTEXITCODE -ne 0) {
            throw "Release build failed."
        }
    }

    foreach ($path in @($appAssembly, $testAssembly)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Required Release assembly was not found: $path"
        }
    }

    Write-Host "Starting $Hours-hour CsoKit/application soak campaign." -ForegroundColor Cyan
    Write-Host "Checkpoint: $outputPath\soak\checkpoint.json" -ForegroundColor Yellow

    & dotnet $testAssembly `
        --app-assembly $appAssembly `
        --security-mode soak `
        --duration-minutes $durationMinutes `
        --sample-mebibytes $SampleMebibytes `
        --security-output $outputPath

    if ($LASTEXITCODE -ne 0) {
        throw "Long soak campaign failed. Review the checkpoint and campaign-failure reports."
    }
}
finally {
    Pop-Location
}
