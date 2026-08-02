[CmdletBinding()]
param(
    [string]$Version = '1.2.0',
    [string]$PackageDirectory = (Join-Path $PSScriptRoot '..\Release\packages'),
    [string]$PackageSuffix = 'security-hardened'
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resolvedPackageDirectory = [System.IO.Path]::GetFullPath($PackageDirectory)
$releaseRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'Release'))

if (-not $resolvedPackageDirectory.StartsWith(
        $releaseRoot + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Source package output must remain under Release: $resolvedPackageDirectory"
}

$safeVersion = $Version -replace '[^0-9A-Za-z._-]', '-'
$safeSuffix = $PackageSuffix -replace '[^0-9A-Za-z._-]', '-'
$packageBaseName = "HakamiqChdTool-v$safeVersion-source-2026-08-02-$safeSuffix"
$packagePath = Join-Path $resolvedPackageDirectory ($packageBaseName + '.zip')

if (Test-Path -LiteralPath $packagePath) {
    throw "Source package already exists: $packagePath"
}

$listedFiles = & git -C $repositoryRoot ls-files --cached --others --exclude-standard
if ($LASTEXITCODE -ne 0) {
    throw 'git ls-files failed while collecting source package inputs.'
}

$deletedFiles = @(& git -C $repositoryRoot ls-files --deleted) |
    ForEach-Object { $_.Replace('\', '/').Trim() }
if ($LASTEXITCODE -ne 0) {
    throw 'git ls-files failed while collecting intentionally deleted source paths.'
}

$sourceFiles = @(
    $listedFiles |
        ForEach-Object { $_.Replace('\', '/').Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Where-Object { $deletedFiles -notcontains $_ } |
        Where-Object {
            $_ -notmatch '(^|/)(\.git|\.vs|bin|obj|Release)(/|$)' -and
            $_ -notmatch '(^|/)(TestResults|artifacts)(/|$)'
        } |
        Sort-Object -Unique
)

if ($sourceFiles.Count -eq 0) {
    throw 'No source files were selected for packaging.'
}

New-Item -ItemType Directory -Path $resolvedPackageDirectory -Force | Out-Null

$stageRoot = Join-Path $releaseRoot ('.source-stage-' + [Guid]::NewGuid().ToString('N'))
$packageRoot = Join-Path $stageRoot $packageBaseName

try {
    New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null

    foreach ($relativePath in $sourceFiles) {
        $sourcePath = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $relativePath))
        if (-not $sourcePath.StartsWith(
                $repositoryRoot + [System.IO.Path]::DirectorySeparatorChar,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Source package path escaped the repository: $relativePath"
        }

        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw "Source package input is missing: $relativePath"
        }

        $destinationPath = Join-Path $packageRoot $relativePath
        $destinationDirectory = Split-Path -Parent $destinationPath
        New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
        Copy-Item -LiteralPath $sourcePath -Destination $destinationPath
    }

    $manifestLines = [System.Collections.Generic.List[string]]::new()
    foreach ($file in (Get-ChildItem -LiteralPath $packageRoot -Recurse -File | Sort-Object FullName)) {
        $relative = [System.IO.Path]::GetRelativePath($packageRoot, $file.FullName).Replace('\', '/')
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $manifestLines.Add("$hash  $relative")
    }

    $manifestPath = Join-Path $packageRoot 'SOURCE-MANIFEST.sha256'
    [System.IO.File]::WriteAllLines(
        $manifestPath,
        $manifestLines,
        [System.Text.UTF8Encoding]::new($false))

    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $stageRoot,
        $packagePath,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false)

    $packageHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
    $packageLength = (Get-Item -LiteralPath $packagePath).Length

    [pscustomobject]@{
        Package = $packagePath
        SHA256 = $packageHash
        Bytes = $packageLength
        SourceFiles = $sourceFiles.Count
        ManifestEntries = $manifestLines.Count
    }
}
finally {
    $resolvedStageRoot = [System.IO.Path]::GetFullPath($stageRoot)
    if ($resolvedStageRoot.StartsWith(
            $releaseRoot + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase) -and
        [System.IO.Path]::GetFileName($resolvedStageRoot).StartsWith('.source-stage-', [System.StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedStageRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
