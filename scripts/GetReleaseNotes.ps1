#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Version,

    [string] $ChangelogPath = ""
)

# Returns the short "### Release notes" list of the "## v<Version>" section of
# the changelog; the detailed subsections after it are not published.
# Fails when the version section or its release notes are missing, duplicated,
# or empty, so a release can never be published with notes that belong to
# another version.

$ErrorActionPreference = "Stop"

if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') {
    throw "Version must contain exactly three numeric components: $Version"
}

if ([string]::IsNullOrWhiteSpace($ChangelogPath)) {
    $ChangelogPath = Join-Path (Split-Path -Parent $PSScriptRoot) "docs\release-notes\CHANGELOG.md"
}

if (-not (Test-Path -LiteralPath $ChangelogPath -PathType Leaf)) {
    throw "Changelog not found: $ChangelogPath"
}

$lines = @(Get-Content -LiteralPath $ChangelogPath -Encoding UTF8)
$headingPattern = '^## v' + [regex]::Escape($Version) + '(\s+-\s+.*)?\s*$'
$headingIndexes = @(for ($index = 0; $index -lt $lines.Count; $index++) {
    if ($lines[$index] -match $headingPattern) { $index }
})

if ($headingIndexes.Count -eq 0) {
    throw "Changelog has no section for v${Version}: $ChangelogPath"
}

if ($headingIndexes.Count -gt 1) {
    throw "Changelog has more than one section for v${Version}: $ChangelogPath"
}

$sectionEnd = $lines.Count
for ($index = $headingIndexes[0] + 1; $index -lt $lines.Count; $index++) {
    if ($lines[$index] -match '^## ') {
        $sectionEnd = $index
        break
    }
}

$notesIndexes = @(for ($index = $headingIndexes[0] + 1; $index -lt $sectionEnd; $index++) {
    if ($lines[$index] -match '^### Release notes\s*$') { $index }
})

if ($notesIndexes.Count -eq 0) {
    throw "Changelog section for v$Version has no '### Release notes' subsection: $ChangelogPath"
}

if ($notesIndexes.Count -gt 1) {
    throw "Changelog section for v$Version has more than one '### Release notes' subsection: $ChangelogPath"
}

$body = New-Object System.Collections.Generic.List[string]
for ($index = $notesIndexes[0] + 1; $index -lt $sectionEnd; $index++) {
    if ($lines[$index] -match '^#{1,3} ') {
        break
    }

    $body.Add($lines[$index].TrimEnd())
}

$text = ($body -join "`n").Trim()
if ([string]::IsNullOrWhiteSpace($text)) {
    throw "Release notes for v$Version are empty: $ChangelogPath"
}

return $text
