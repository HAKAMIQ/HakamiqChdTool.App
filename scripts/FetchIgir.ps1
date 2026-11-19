#Requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ProjectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$Destination = Join-Path $ProjectRoot 'Tools\igir\win-x64\igir.exe'
$ZipUrl = 'https://github.com/emmercm/igir/releases/download/v5.8.1/igir-5.8.1-Windows-x64.zip'
$ExpectedZipHash = '48025BACCB4EE564B6DDA4B3AA65C9B90AB01340C2A43773FA506DC880D013F2'
$ExpectedExeHash = 'F3BCD0173C5E79A3DCEC8CC65707007035849920AB362B51F09E133BDBB63DD1'

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
    Write-Host "Verified igir v5.8.1 installed at $Destination"
} finally {
    Remove-Item -LiteralPath $Scratch -Recurse -Force
}
