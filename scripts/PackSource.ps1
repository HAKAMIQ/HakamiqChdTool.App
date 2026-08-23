#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Version = '1.2.1',
    [string]$PackageDirectory,
    [string]$PackageSuffix = 'security-hardened'
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression.FileSystem

$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..'))

if ([string]::IsNullOrWhiteSpace($PackageDirectory)) {
    $PackageDirectory = Join-Path $repositoryRoot 'Release\packages'
}

$resolvedPackageDirectory = [System.IO.Path]::GetFullPath($PackageDirectory)
$releaseRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot 'Release'))

function Get-RelativePathCompat {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BasePath,

        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $resolvedBase = [System.IO.Path]::GetFullPath($BasePath).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)

    if ([string]::Equals(
            $resolvedBase,
            $resolvedPath,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        return ''
    }

    $prefix =
        $resolvedBase +
        [System.IO.Path]::DirectorySeparatorChar

    if (-not $resolvedPath.StartsWith(
            $prefix,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside the expected base directory: $resolvedPath"
    }

    return $resolvedPath.Substring($prefix.Length)
}

if (-not $resolvedPackageDirectory.StartsWith(
        $releaseRoot + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Source package output must remain under Release: $resolvedPackageDirectory"
}

$safeVersion =
    $Version -replace '[^0-9A-Za-z._-]', '-'

$safeSuffix =
    $PackageSuffix -replace '[^0-9A-Za-z._-]', '-'

$packageBaseName =
    "HakamiqChdTool-v$safeVersion-source-2026-08-02-$safeSuffix"

$packagePath =
    Join-Path $resolvedPackageDirectory ($packageBaseName + '.zip')

if (Test-Path -LiteralPath $packagePath) {
    throw "Source package already exists: $packagePath"
}

$listedFiles =
    & git -C $repositoryRoot ls-files --cached

if ($LASTEXITCODE -ne 0) {
    throw 'git ls-files failed while collecting tracked source package inputs.'
}

$deletedFiles = @(
    & git -C $repositoryRoot ls-files --deleted
) | ForEach-Object {
    $_.Replace('\', '/').Trim()
}

if ($LASTEXITCODE -ne 0) {
    throw 'git ls-files failed while collecting intentionally deleted source paths.'
}

$sourceFiles = @(
    $listedFiles |
        ForEach-Object {
            $_.Replace('\', '/').Trim()
        } |
        Where-Object {
            -not [string]::IsNullOrWhiteSpace($_)
        } |
        Where-Object {
            $deletedFiles -notcontains $_
        } |
        Where-Object {
            $_ -notmatch '(^|/)(\.git|\.vs|bin|obj|Release)(/|$)' -and
            $_ -notmatch '(^|/)(TestResults|artifacts)(/|$)'
        } |
        Sort-Object -Unique
)

if ($sourceFiles.Count -eq 0) {
    throw 'No source files were selected for packaging.'
}

New-Item `
    -ItemType Directory `
    -Path $resolvedPackageDirectory `
    -Force |
    Out-Null

$stageRoot =
    Join-Path $releaseRoot (
        '.source-stage-' +
        [Guid]::NewGuid().ToString('N'))

$packageRoot =
    Join-Path $stageRoot $packageBaseName

try {
    New-Item `
        -ItemType Directory `
        -Path $packageRoot `
        -Force |
        Out-Null

    foreach ($relativePath in $sourceFiles) {
        $sourcePath =
            [System.IO.Path]::GetFullPath(
                (Join-Path $repositoryRoot $relativePath))

        if (-not $sourcePath.StartsWith(
                $repositoryRoot + [System.IO.Path]::DirectorySeparatorChar,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Source package path escaped the repository: $relativePath"
        }

        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw "Source package input is missing: $relativePath"
        }

        $destinationPath =
            Join-Path $packageRoot $relativePath

        $destinationDirectory =
            Split-Path -Parent $destinationPath

        New-Item `
            -ItemType Directory `
            -Path $destinationDirectory `
            -Force |
            Out-Null

        Copy-Item `
            -LiteralPath $sourcePath `
            -Destination $destinationPath
    }

    $manifestLines =
        New-Object System.Collections.Generic.List[string]

    foreach ($file in (
        Get-ChildItem `
            -LiteralPath $packageRoot `
            -Recurse `
            -File |
            Sort-Object FullName)) {

        $relative = (
            Get-RelativePathCompat `
                -BasePath $packageRoot `
                -Path $file.FullName
        ).Replace('\', '/')

        $hash = (
            Get-FileHash `
                -LiteralPath $file.FullName `
                -Algorithm SHA256
        ).Hash.ToLowerInvariant()

        $manifestLines.Add(
            "$hash  $relative")
    }

    $manifestPath =
        Join-Path $packageRoot 'SOURCE-MANIFEST.sha256'

    [System.IO.File]::WriteAllLines(
        $manifestPath,
        $manifestLines,
        (New-Object System.Text.UTF8Encoding($false)))

    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $stageRoot,
        $packagePath,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false)

    $packageHash = (
        Get-FileHash `
            -LiteralPath $packagePath `
            -Algorithm SHA256
    ).Hash

    $packageLength = (
        Get-Item -LiteralPath $packagePath
    ).Length

    [pscustomobject]@{
        Package         = $packagePath
        SHA256          = $packageHash
        Bytes           = $packageLength
        SourceFiles     = $sourceFiles.Count
        ManifestEntries = $manifestLines.Count
    }
}
finally {
    if (-not [string]::IsNullOrWhiteSpace($stageRoot)) {
        $resolvedStageRoot =
            [System.IO.Path]::GetFullPath($stageRoot)

        if ($resolvedStageRoot.StartsWith(
                $releaseRoot + [System.IO.Path]::DirectorySeparatorChar,
                [System.StringComparison]::OrdinalIgnoreCase) -and
            [System.IO.Path]::GetFileName(
                $resolvedStageRoot).StartsWith(
                    '.source-stage-',
                    [System.StringComparison]::Ordinal)) {

            Remove-Item `
                -LiteralPath $resolvedStageRoot `
                -Recurse `
                -Force `
                -ErrorAction SilentlyContinue
        }
    }
}
