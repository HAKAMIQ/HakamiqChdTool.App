#Requires -Version 7.0
[CmdletBinding()]
param()

# Fetches the GPL-3.0 Corresponding Source for the bundled igir v5.7.2 executable.
# PackRel.ps1 publishes this archive next to any release ZIP that contains igir.exe.
$ErrorActionPreference = 'Stop'
$ProjectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$Destination = Join-Path $ProjectRoot 'Tools\igir\source\igir-5.7.2-source.tar.gz'
$SourceUrl = 'https://github.com/emmercm/igir/archive/refs/tags/v5.7.2.tar.gz'
$ExpectedHash = 'EA5341ED3A40054BFBF2F6D26F6C76902C6D5EC949D66D38692218656245875B'

if (Test-Path -LiteralPath $Destination) { throw "Refusing to overwrite existing source archive: $Destination" }
$Scratch = Join-Path ([System.IO.Path]::GetTempPath()) ('hakamiq-igir-src-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $Scratch | Out-Null
try {
    $ArchivePath = Join-Path $Scratch 'igir-5.7.2-source.tar.gz'
    Invoke-WebRequest -Uri $SourceUrl -OutFile $ArchivePath
    if ((Get-Item -LiteralPath $ArchivePath).Length -gt 150MB) { throw 'igir source archive exceeds the bounded download size.' }
    if ((Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash -ne $ExpectedHash) {
        throw 'igir v5.7.2 source archive SHA-256 does not match the pinned digest.'
    }

    New-Item -ItemType Directory -Path (Split-Path -Parent $Destination) -Force | Out-Null
    if (Test-Path -LiteralPath $Destination) { throw "Refusing to overwrite existing source archive: $Destination" }
    [System.IO.File]::Move($ArchivePath, $Destination)
    Write-Host "Verified igir v5.7.2 source archive installed at $Destination"
} finally {
    Remove-Item -LiteralPath $Scratch -Recurse -Force
}
