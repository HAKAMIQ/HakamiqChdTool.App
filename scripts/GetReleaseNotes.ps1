#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Version,

    [string] $ChangelogPath = ""
)

# Returns the body of the "## v<Version>" section of the changelog.
# Fails when the section is missing, duplicated, or empty, so a release can
# never be published with notes that belong to another version.

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

$body = New-Object System.Collections.Generic.List[string]
for ($index = $headingIndexes[0] + 1; $index -lt $lines.Count; $index++) {
    if ($lines[$index] -match '^## ') {
        break
    }

    $body.Add($lines[$index].TrimEnd())
}

$text = ($body -join "`n").Trim()
if ([string]::IsNullOrWhiteSpace($text)) {
    throw "Changelog section for v$Version is empty: $ChangelogPath"
}

return $text
