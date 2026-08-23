#Requires -Version 5.1
[CmdletBinding()]
param(
    [string] $Output = ".\Release\audit\HakamiqChdTool-independent-audit-bundle.zip",
    [switch] $IncludeSecurityEvidence
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = (Resolve-Path -LiteralPath (Join-Path $ScriptDir "..")).Path
$ReleaseRoot = [System.IO.Path]::GetFullPath((Join-Path $ProjectRoot "Release"))
$OutputPath = if ([System.IO.Path]::IsPathRooted($Output)) {
    [System.IO.Path]::GetFullPath($Output)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $ProjectRoot $Output))
}
$StagingRoot = Join-Path $ReleaseRoot ("_audit-staging-" + [Guid]::NewGuid().ToString("N"))

function Get-NormalizedFullPath {
    param([Parameter(Mandatory = $true)][string] $Path)
    return [System.IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
}

function Test-PathIsSameOrChild {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Parent
    )

    $candidate = Get-NormalizedFullPath $Path
    $root = Get-NormalizedFullPath $Parent
    return $candidate.Equals($root, [System.StringComparison]::OrdinalIgnoreCase) -or
        $candidate.StartsWith($root + "\", [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-RelativePathCompat {
    param(
        [Parameter(Mandatory = $true)][string] $BasePath,
        [Parameter(Mandatory = $true)][string] $FullPath
    )

    $base = [System.IO.Path]::GetFullPath($BasePath).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $baseUri = [System.Uri]::new($base)
    $itemUri = [System.Uri]::new([System.IO.Path]::GetFullPath($FullPath))
    return [System.Uri]::UnescapeDataString($baseUri.MakeRelativeUri($itemUri).ToString()).Replace('/', '\')
}

function Invoke-GitText {
    param([Parameter(Mandatory = $true)][string[]] $Arguments)

    $output = @(& git -C $ProjectRoot @Arguments)
    if ($LASTEXITCODE -ne 0) {
        throw "git command failed: git $($Arguments -join ' ')"
    }
    return @($output)
}

if (-not (Test-PathIsSameOrChild -Path $OutputPath -Parent $ReleaseRoot)) {
    throw "Audit bundle must be written inside the Release directory: $OutputPath"
}
if (-not (Test-PathIsSameOrChild -Path $StagingRoot -Parent $ReleaseRoot) -or
    -not ([System.IO.Path]::GetFileName($StagingRoot).StartsWith("_audit-staging-", [System.StringComparison]::Ordinal))) {
    throw "Unsafe audit staging root: $StagingRoot"
}

try {
    New-Item -ItemType Directory -Path $StagingRoot -Force | Out-Null

    $commitLines = @(Invoke-GitText -Arguments @('rev-parse', '--verify', 'HEAD'))
    if ($commitLines.Count -ne 1) {
        throw "Expected one Git commit identifier, got $($commitLines.Count)."
    }
    $commit = ([string]$commitLines[0]).Trim()
    if ($commit -notmatch '^[0-9a-fA-F]{40}$') {
        throw "Unexpected Git commit identifier: $commit"
    }

    $sourceRoot = Join-Path $StagingRoot "source"
    $sourceArchive = Join-Path $StagingRoot "_source-head.zip"
    New-Item -ItemType Directory -Path $sourceRoot -Force | Out-Null

    & git -C $ProjectRoot archive --format=zip --output=$sourceArchive $commit
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $sourceArchive -PathType Leaf)) {
        throw "git archive failed while capturing immutable source for commit $commit."
    }

    [System.IO.Compression.ZipFile]::ExtractToDirectory($sourceArchive, $sourceRoot)
    Remove-Item -LiteralPath $sourceArchive -Force

    $evidenceIncluded = $false
    if ($IncludeSecurityEvidence) {
        $evidenceRoot = Join-Path $ProjectRoot "TestResults\Security"
        if (Test-Path -LiteralPath $evidenceRoot -PathType Container) {
            $evidenceDestination = Join-Path $StagingRoot "evidence\security-campaigns"
            New-Item -ItemType Directory -Path $evidenceDestination -Force | Out-Null

            foreach ($evidenceFile in Get-ChildItem -LiteralPath $evidenceRoot -File -Recurse -Force) {
                if (($evidenceFile.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                    continue
                }
                if (-not (Test-PathIsSameOrChild -Path $evidenceFile.FullName -Parent $evidenceRoot)) {
                    throw "Evidence file escaped the expected root: $($evidenceFile.FullName)"
                }

                $relativeEvidencePath = Get-RelativePathCompat -BasePath $evidenceRoot -FullPath $evidenceFile.FullName
                $evidenceTarget = Join-Path $evidenceDestination $relativeEvidencePath
                New-Item -ItemType Directory -Path (Split-Path -Parent $evidenceTarget) -Force | Out-Null
                Copy-Item -LiteralPath $evidenceFile.FullName -Destination $evidenceTarget -Force
                $evidenceIncluded = $true
            }
        }
    }

    $trackedStatus = @(Invoke-GitText -Arguments @('status', '--porcelain=v1', '--untracked-files=no'))
    $status = [ordered]@{
        format = "HakamiqIndependentAuditStatus.v1"
        preparedAtUtc = [System.DateTimeOffset]::UtcNow.ToString("O")
        commit = $commit
        sourceMode = "git-archive-head"
        trackedWorkingTreeDirty = ($trackedStatus.Count -gt 0)
        securityEvidenceIncluded = $evidenceIncluded
        independentAuditPerformed = $false
        independentReproducibleBuildPerformed = $false
        note = "This is an audit-readiness evidence bundle, not an independent audit report. Source content is captured from the immutable Git commit listed above."
    }
    [System.IO.File]::WriteAllText(
        (Join-Path $StagingRoot "AUDIT-STATUS.json"),
        ($status | ConvertTo-Json -Depth 6),
        [System.Text.UTF8Encoding]::new($false))

    $manifestLines = [System.Collections.Generic.List[string]]::new()
    foreach ($file in Get-ChildItem -LiteralPath $StagingRoot -File -Recurse -Force | Sort-Object FullName) {
        if ($file.Name -eq "AUDIT-BUNDLE-MANIFEST.sha256") {
            continue
        }
        $relativePath = (Get-RelativePathCompat -BasePath $StagingRoot -FullPath $file.FullName).Replace('\', '/')
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
        $manifestLines.Add("$hash  $relativePath")
    }
    [System.IO.File]::WriteAllLines(
        (Join-Path $StagingRoot "AUDIT-BUNDLE-MANIFEST.sha256"),
        $manifestLines,
        [System.Text.UTF8Encoding]::new($false))

    $outputDirectory = Split-Path -Parent $OutputPath
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    if (Test-Path -LiteralPath $OutputPath -PathType Leaf) {
        Remove-Item -LiteralPath $OutputPath -Force
    }

    $zipStream = [System.IO.File]::Open($OutputPath, [System.IO.FileMode]::CreateNew)
    try {
        $zip = [System.IO.Compression.ZipArchive]::new($zipStream, [System.IO.Compression.ZipArchiveMode]::Create)
        try {
            $fixedTimestamp = [System.DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [System.TimeSpan]::Zero)
            foreach ($file in Get-ChildItem -LiteralPath $StagingRoot -File -Recurse -Force | Sort-Object FullName) {
                $relativePath = (Get-RelativePathCompat -BasePath $StagingRoot -FullPath $file.FullName).Replace('\', '/')
                $entry = $zip.CreateEntry($relativePath, [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $fixedTimestamp
                $entryStream = $entry.Open()
                try {
                    $sourceStream = [System.IO.File]::OpenRead($file.FullName)
                    try { $sourceStream.CopyTo($entryStream) }
                    finally { $sourceStream.Dispose() }
                }
                finally { $entryStream.Dispose() }
            }
        }
        finally { $zip.Dispose() }
    }
    finally { $zipStream.Dispose() }

    $zipHash = (Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash.ToUpperInvariant()
    [System.IO.File]::WriteAllText(
        "$OutputPath.sha256",
        "$zipHash  $([System.IO.Path]::GetFileName($OutputPath))`r`n",
        [System.Text.UTF8Encoding]::new($false))

    Write-Host "[PASS] Independent-audit readiness bundle created: $OutputPath" -ForegroundColor Green
    Write-Host "[INFO] Source captured from commit: $commit" -ForegroundColor Cyan
    if (-not $IncludeSecurityEvidence) {
        Write-Host "[INFO] Security-campaign evidence was excluded. Review it before using -IncludeSecurityEvidence." -ForegroundColor Yellow
    }
    Write-Host "[INFO] Audit status remains NOT PERFORMED until an external reviewer delivers a report." -ForegroundColor Yellow
}
finally {
    if (Test-Path -LiteralPath $StagingRoot -PathType Container) {
        if (-not (Test-PathIsSameOrChild -Path $StagingRoot -Parent $ReleaseRoot) -or
            -not ([System.IO.Path]::GetFileName($StagingRoot).StartsWith("_audit-staging-", [System.StringComparison]::Ordinal))) {
            throw "Refusing to remove unsafe audit staging root: $StagingRoot"
        }
        Remove-Item -LiteralPath $StagingRoot -Recurse -Force
    }
}
