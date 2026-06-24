#Requires -Version 5.1
[CmdletBinding()]
param(
    [string] $Output = '',

    [switch] $VerifyOnly,

    [switch] $KeepOutput
)

$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = (Resolve-Path -LiteralPath (Join-Path $ScriptDir '..')).Path
$ReleaseRoot = [System.IO.Path]::GetFullPath((Join-Path $ProjectRoot 'Release'))
$PublishScript = Join-Path $ScriptDir 'Publish-EndUserRelease.ps1'
$PackageCleanlinessGateScript = Join-Path $ScriptDir 'Run-PackageCleanlinessGate.ps1'
$PowerShellExe = Join-Path $PSHOME 'powershell.exe'

if (-not (Test-Path -LiteralPath $PowerShellExe -PathType Leaf)) {
    $PowerShellExe = 'powershell.exe'
}

function Write-Info {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Message
    )

    Write-Host "[INFO] $Message" -ForegroundColor Cyan
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

    return $fullPath.StartsWith($fullParent + '\', [System.StringComparison]::OrdinalIgnoreCase)
}

function Resolve-ReleaseOutputPath {
    if ([string]::IsNullOrWhiteSpace($Output)) {
        if ($VerifyOnly) {
            throw 'Pass -Output when using -VerifyOnly.'
        }

        return [System.IO.Path]::GetFullPath((Join-Path $ReleaseRoot '_output-gate'))
    }

    if ([System.IO.Path]::IsPathRooted($Output)) {
        return [System.IO.Path]::GetFullPath($Output)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $ProjectRoot $Output))
}

function Assert-OutputPathIsSafe {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    if (-not (Test-PathIsSameOrChild -Path $Path -Parent $ReleaseRoot)) {
        throw "Release output gate path must be inside the Release directory: $Path"
    }

    if ((Get-NormalizedFullPath $Path).Equals((Get-NormalizedFullPath $ProjectRoot), [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'Release output gate path cannot be the project root.'
    }

    foreach ($blockedName in @(
        'scripts',
        'Style',
        'Views',
        'Services',
        'Core',
        'Models',
        'ViewModels',
        'Adapters',
        'Localization',
        'Properties',
        'bin',
        'obj',
        '.git',
        '.github',
        '.vs'
    )) {
        $blockedPath = Join-Path $ProjectRoot $blockedName

        if (Test-PathIsSameOrChild -Path $Path -Parent $blockedPath) {
            throw "Release output gate path cannot target a source/project directory: $Path"
        }
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
    Assert-FileExists -Path $PublishScript -Message 'Publish script was not found:'
    Assert-FileExists -Path $PackageCleanlinessGateScript -Message 'Package cleanliness gate script was not found:'

    $outputPath = Resolve-ReleaseOutputPath
    Assert-OutputPathIsSafe -Path $outputPath

    if ($VerifyOnly) {
        if (-not (Test-Path -LiteralPath $outputPath -PathType Container)) {
            throw "Release output directory was not found: $outputPath"
        }

        Write-Info "Verifying existing end-user release output: $outputPath"
        Invoke-PowerShellFile -ScriptPath $PackageCleanlinessGateScript -Arguments @('-ReleaseOutput', $outputPath)
        Write-Host "[PASS] Release output gate completed: $outputPath" -ForegroundColor Green
        return
    }

    $completed = $false
    try {
        if (Test-Path -LiteralPath $outputPath -PathType Container) {
            Write-Info "Removing previous disposable release output: $outputPath"
            Remove-Item -LiteralPath $outputPath -Recurse -Force -ErrorAction Stop
        }

        Write-Info "Publishing disposable end-user release output: $outputPath"
        Invoke-PowerShellFile -ScriptPath $PublishScript -Arguments @('-Output', $outputPath)

        Write-Info 'Running package cleanliness gate against disposable release output ...'
        Invoke-PowerShellFile -ScriptPath $PackageCleanlinessGateScript -Arguments @('-ReleaseOutput', $outputPath)

        $completed = $true
        Write-Host "[PASS] Release output gate completed: $outputPath" -ForegroundColor Green
    }
    finally {
        if ($completed -and -not $KeepOutput -and (Test-Path -LiteralPath $outputPath -PathType Container)) {
            Remove-Item -LiteralPath $outputPath -Recurse -Force -ErrorAction SilentlyContinue
            Write-Info "Removed disposable release output: $outputPath"
        }
        elseif (-not $completed -and -not $KeepOutput -and (Test-Path -LiteralPath $outputPath -PathType Container)) {
            Write-Info "Release output was kept for failure inspection: $outputPath"
        }
    }
}
finally {
    Pop-Location
}
