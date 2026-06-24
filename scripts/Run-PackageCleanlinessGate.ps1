#Requires -Version 5.1
[CmdletBinding()]
param(
    [string] $ReleaseOutput = '',

    [switch] $KeepTemp
)

$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = (Resolve-Path -LiteralPath (Join-Path $ScriptDir '..')).Path
$EndUserReleaseGateScript = Join-Path $ScriptDir 'Verify-EndUserRelease.ps1'
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

function Write-Err {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Message
    )

    Write-Host "[ERROR] $Message" -ForegroundColor Red
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
        '-NoProfile',
        '-ExecutionPolicy',
        'Bypass',
        '-File',
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

    $baseFullPath = [System.IO.Path]::GetFullPath($BasePath).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $itemFullPath = [System.IO.Path]::GetFullPath($FullPath)

    $baseUri = [System.Uri]::new($baseFullPath)
    $itemUri = [System.Uri]::new($itemFullPath)
    $relativeUri = $baseUri.MakeRelativeUri($itemUri)
    $relativePath = [System.Uri]::UnescapeDataString($relativeUri.ToString())

    return $relativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
}

function Assert-RequiredFile {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RootPath,

        [Parameter(Mandatory = $true)]
        [string] $RelativePath
    )

    $path = Join-Path $RootPath $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required package file is missing: $RelativePath"
    }

    $file = Get-Item -LiteralPath $path -Force
    if ($file.Length -le 0) {
        throw "Required package file is empty: $RelativePath"
    }
}

function Test-BlockedPathSegment {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RelativePath,

        [Parameter(Mandatory = $true)]
        [string[]] $BlockedNames
    )

    foreach ($segment in ($RelativePath -split '[\\/]')) {
        if ($BlockedNames -contains $segment) {
            return $true
        }
    }

    return $false
}

function Assert-NoBlockedDirectorySegments {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RootPath,

        [Parameter(Mandatory = $true)]
        [string] $Context
    )

    $blocked = @(
        '.git',
        '.vs',
        'bin',
        'obj',
        'Release',
        'artifacts',
        'TestResults',
        'publish',
        '_release'
    )

    $matches = Get-ChildItem -LiteralPath $RootPath -Directory -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object {
            $relative = Get-RelativePathCompat -BasePath $RootPath -FullPath $_.FullName
            Test-BlockedPathSegment -RelativePath $relative -BlockedNames $blocked
        }

    if ($matches) {
        foreach ($match in $matches) {
            Write-Err "Blocked directory found in ${Context}: $($match.FullName)"
        }

        throw "Blocked directory detected in ${Context}."
    }
}

function Assert-NoBlockedFiles {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RootPath,

        [Parameter(Mandatory = $true)]
        [string] $Context
    )

    $blockedExtensions = @(
        '.7z',
        '.bak',
        '.bin',
        '.chd',
        '.cso',
        '.cue',
        '.dax',
        '.dmp',
        '.etl',
        '.gdi',
        '.img',
        '.iso',
        '.log',
        '.mdmp',
        '.nrg',
        '.orig',
        '.pdb',
        '.pkg',
        '.rar',
        '.tmp',
        '.user',
        '.suo',
        '.rsuser',
        '.zso',
        '.zip'
    )

    $blockedNames = @(
        'createdump.exe',
        '.suo',
        'premium-license.json',
        'premium-license.txt'
    )

    $matches = Get-ChildItem -LiteralPath $RootPath -File -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object {
            $extension = $_.Extension.ToLowerInvariant()
            $name = $_.Name.ToLowerInvariant()

            ($blockedExtensions -contains $extension) -or ($blockedNames -contains $name)
        }

    if ($matches) {
        foreach ($match in $matches) {
            Write-Err "Blocked file found in ${Context}: $($match.FullName)"
        }

        throw "Blocked file detected in ${Context}."
    }
}

function Assert-SourcePackageLayout {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RootPath
    )

    foreach ($required in @(
        'HakamiqChdTool.App.csproj',
        'HakamiqChdTool.App.sln',
        'README.md',
        'LICENSE',
        'SECURITY.md',
        'scripts\Verify-Local.ps1',
        'scripts\Verify-RepoConventions.ps1',
        'scripts\Verify-EndUserRelease.ps1',
        'scripts\Generate-ReleaseManifest.ps1',
        'docs\legal\LEGAL.md',
        'docs\legal\THIRD_PARTY_NOTICES.txt'
    )) {
        Assert-RequiredFile -RootPath $RootPath -RelativePath $required
    }

    Assert-NoBlockedDirectorySegments -RootPath $RootPath -Context 'source archive'
    Assert-NoBlockedFiles -RootPath $RootPath -Context 'source archive'
}

function Invoke-SourceArchiveGate {
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('HakamiqChdTool.PackageCleanliness.' + [System.Guid]::NewGuid().ToString('N'))
    $sourceCandidatePath = Join-Path $tempRoot 'source-candidate'

    New-Item -ItemType Directory -Path $sourceCandidatePath -Force | Out-Null

    try {
        Write-Info 'Creating temporary source package candidate from Git-managed files ...'

        $fileList = & git ls-files --cached --others --exclude-standard
        if ($LASTEXITCODE -ne 0) {
            throw 'git ls-files failed while preparing source package candidate.'
        }

        $relativeFiles = @($fileList | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
        if ($relativeFiles.Count -eq 0) {
            throw 'No Git-managed source files were found.'
        }

        foreach ($relativePath in $relativeFiles) {
            if ([System.IO.Path]::IsPathRooted($relativePath)) {
                throw "Git returned an absolute source path: $relativePath"
            }

            $normalizedRelativePath = $relativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
            $sourcePath = Join-Path $ProjectRoot $normalizedRelativePath
            $destinationPath = Join-Path $sourceCandidatePath $normalizedRelativePath
            $destinationDirectory = Split-Path -Parent $destinationPath

            if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
                throw "Git-managed source file was not found on disk: $normalizedRelativePath"
            }

            New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
            Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force
        }

        $manifestPath = Join-Path $sourceCandidatePath 'SOURCE_FILES.txt'
        $relativeFiles |
            ForEach-Object { $_.Replace('\\', '/') } |
            Set-Content -LiteralPath $manifestPath -Encoding UTF8

        Assert-SourcePackageLayout -RootPath $sourceCandidatePath

        Write-Host "[PASS] Source package cleanliness gate passed: $($relativeFiles.Count) file(s)." -ForegroundColor Green
    }
    finally {
        if ($KeepTemp) {
            Write-Info "Temporary source package candidate kept: $tempRoot"
        }
        elseif (Test-Path -LiteralPath $tempRoot -PathType Container) {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Invoke-ReleaseOutputGateIfRequested {
    if ([string]::IsNullOrWhiteSpace($ReleaseOutput)) {
        Write-Info 'Release output check skipped. Pass -ReleaseOutput to verify a built release folder.'
        return
    }

    $resolvedReleaseOutput = if ([System.IO.Path]::IsPathRooted($ReleaseOutput)) {
        [System.IO.Path]::GetFullPath($ReleaseOutput)
    }
    else {
        [System.IO.Path]::GetFullPath((Join-Path $ProjectRoot $ReleaseOutput))
    }

    if (-not (Test-Path -LiteralPath $resolvedReleaseOutput -PathType Container)) {
        throw "Release output directory was not found: $resolvedReleaseOutput"
    }

    Write-Info "Running end-user release gate for: $resolvedReleaseOutput"
    Invoke-PowerShellFile -ScriptPath $EndUserReleaseGateScript -Arguments @('-Output', $resolvedReleaseOutput)
}

Push-Location $ProjectRoot
try {
    Assert-CommandExists 'git'

    Invoke-SourceArchiveGate
    Invoke-ReleaseOutputGateIfRequested

    Write-Host '[PASS] Package cleanliness gate completed.' -ForegroundColor Green
}
finally {
    Pop-Location
}
