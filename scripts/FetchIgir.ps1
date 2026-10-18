#Requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ProjectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$Destination = Join-Path $ProjectRoot 'Tools\igir\win-x64\igir.exe'
$ZipUrl = 'https://github.com/emmercm/igir/releases/download/v5.7.2/igir-5.7.2-Windows-x64.zip'
$ExpectedZipHash = '7E448AE8E34377A8829E33C949B07A09613CB3A0DAD2B4325CA9BB52CF8D7584'
$ExpectedExeHash = '716ECBD7033081FC000A51FBD0588FEE6C1F1E95DAB1EFD6443324BC73597511'

if (Test-Path -LiteralPath $Destination) { throw "Refusing to overwrite existing tool: $Destination" }
$Scratch = Join-Path ([System.IO.Path]::GetTempPath()) ('hakamiq-igir-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $Scratch | Out-Null
try {
    $ZipPath = Join-Path $Scratch 'igir.zip'
    Invoke-WebRequest -Uri $ZipUrl -OutFile $ZipPath
    if ((Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash -ne $ExpectedZipHash) {
        throw 'Official igir ZIP SHA-256 does not match the pinned digest.'
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $Archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $Entry = $Archive.Entries | Where-Object FullName -eq 'igir.exe'
        if ($null -eq $Entry -or $Entry.Length -gt 150MB) { throw 'igir.exe is missing or exceeds the bounded download size.' }
        $Input = $Entry.Open()
        try {
            $Digest = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($Input))
            if ($Digest -ne $ExpectedExeHash) { throw 'igir.exe SHA-256 does not match the pinned digest.' }
        } finally { $Input.Dispose() }
        $Input = $Entry.Open()
        try {
            $Output = [System.IO.File]::Open((Join-Path $Scratch 'igir.exe'), [System.IO.FileMode]::CreateNew)
            try { $Input.CopyTo($Output) } finally { $Output.Dispose() }
        } finally { $Input.Dispose() }
    } finally { $Archive.Dispose() }

    $DestinationDirectory = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Path $DestinationDirectory -Force | Out-Null
    if (Test-Path -LiteralPath $Destination) { throw "Refusing to overwrite existing tool: $Destination" }
    [System.IO.File]::Move((Join-Path $Scratch 'igir.exe'), $Destination)
    Write-Host "Verified igir v5.7.2 installed at $Destination"
} finally {
    Remove-Item -LiteralPath $Scratch -Recurse -Force
}
