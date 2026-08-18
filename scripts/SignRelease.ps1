#Requires -Version 5.1
[CmdletBinding()]
param(
    [string] $Output = ".\Release",

    [string] $CertificateThumbprint,

    [string] $PfxPath,

    [string] $PfxPasswordEnvironmentVariable = "HAKAMIQ_SIGNING_PFX_PASSWORD",

    [ValidatePattern('^https?://')]
    [string] $TimestampUrl = "http://timestamp.digicert.com",

    [string] $ReportPath
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = (Resolve-Path -LiteralPath (Join-Path $ScriptDir "..")).Path
$ReleaseRoot = [System.IO.Path]::GetFullPath((Join-Path $ProjectRoot "Release"))
$ManifestScript = Join-Path $ScriptDir "GenManifest.ps1"

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

function Resolve-SignTool {
    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (Test-Path -LiteralPath $kitsRoot -PathType Container) {
        $candidate = Get-ChildItem -LiteralPath $kitsRoot -Filter signtool.exe -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '[\\/]x64[\\/]signtool\.exe$' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($null -ne $candidate) {
            return $candidate.FullName
        }
    }

    throw "signtool.exe was not found. Install the Windows SDK signing tools."
}

function Invoke-SignTool {
    param([Parameter(Mandatory = $true)][string[]] $Arguments)

    & $script:SignTool @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "SignTool failed with exit code $LASTEXITCODE."
    }
}

function Assert-CodeSigningCertificate {
    param([Parameter(Mandatory = $true)][System.Security.Cryptography.X509Certificates.X509Certificate2] $Certificate)

    if (-not $Certificate.HasPrivateKey) {
        throw "The selected Authenticode certificate does not have an accessible private key."
    }

    $now = [System.DateTime]::UtcNow
    if ($Certificate.NotBefore.ToUniversalTime() -gt $now -or $Certificate.NotAfter.ToUniversalTime() -lt $now) {
        throw "The selected Authenticode certificate is not currently valid."
    }

    $codeSigningOid = "1.3.6.1.5.5.7.3.3"
    $hasCodeSigningEku = $false
    foreach ($extension in $Certificate.Extensions) {
        if ($extension -is [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]) {
            foreach ($oid in $extension.EnhancedKeyUsages) {
                if ($oid.Value -eq $codeSigningOid) {
                    $hasCodeSigningEku = $true
                }
            }
        }
    }

    if (-not $hasCodeSigningEku) {
        throw "The selected certificate does not contain the Code Signing EKU."
    }
}

$OutputPath = if ([System.IO.Path]::IsPathRooted($Output)) {
    [System.IO.Path]::GetFullPath($Output)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $ProjectRoot $Output))
}

if (-not (Test-PathIsSameOrChild -Path $OutputPath -Parent $ReleaseRoot)) {
    throw "Signing output must be inside the repository Release directory: $OutputPath"
}

if (-not (Test-Path -LiteralPath $OutputPath -PathType Container)) {
    throw "Release output was not found: $OutputPath"
}

if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint) -and
    -not [string]::IsNullOrWhiteSpace($PfxPath)) {
    throw "Specify either CertificateThumbprint or PfxPath, not both."
}

if ([string]::IsNullOrWhiteSpace($CertificateThumbprint) -and
    [string]::IsNullOrWhiteSpace($PfxPath)) {
    throw "Authenticode signing requires CertificateThumbprint or PfxPath."
}

$script:SignTool = Resolve-SignTool
$store = [System.Security.Cryptography.X509Certificates.X509Store]::new(
    [System.Security.Cryptography.X509Certificates.StoreName]::My,
    [System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
$importedCertificate = $null
$removeImportedCertificate = $false

try {
    $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)

    if (-not [string]::IsNullOrWhiteSpace($PfxPath)) {
        $resolvedPfxPath = (Resolve-Path -LiteralPath $PfxPath).Path
        $password = [System.Environment]::GetEnvironmentVariable($PfxPasswordEnvironmentVariable)
        if ([string]::IsNullOrEmpty($password)) {
            throw "The PFX password environment variable is missing or empty: $PfxPasswordEnvironmentVariable"
        }

        $flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::UserKeySet -bor
            [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::PersistKeySet
        $importedCertificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
            $resolvedPfxPath,
            $password,
            $flags)
        $password = $null
        Assert-CodeSigningCertificate $importedCertificate

        $existing = $store.Certificates.Find(
            [System.Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint,
            $importedCertificate.Thumbprint,
            $false)
        if ($existing.Count -eq 0) {
            $store.Add($importedCertificate)
            $removeImportedCertificate = $true
        }
        $CertificateThumbprint = $importedCertificate.Thumbprint
    }

    $normalizedThumbprint = ($CertificateThumbprint -replace '\s', '').ToUpperInvariant()
    if ($normalizedThumbprint -notmatch '^[0-9A-F]{40,128}$') {
        throw "Certificate thumbprint has an invalid format."
    }

    $matches = $store.Certificates.Find(
        [System.Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint,
        $normalizedThumbprint,
        $false)
    if ($matches.Count -ne 1) {
        throw "Expected exactly one matching certificate in CurrentUser/My; found $($matches.Count)."
    }

    $certificate = $matches[0]
    Assert-CodeSigningCertificate $certificate

    $targets = @(
        Join-Path $OutputPath "HakamiqChdTool.exe"
        Join-Path $OutputPath "HakamiqChdTool.dll"
    )

    foreach ($target in $targets) {
        if (-not (Test-Path -LiteralPath $target -PathType Leaf)) {
            throw "Authenticode target was not found: $target"
        }

        Invoke-SignTool @(
            "sign",
            "/sha1", $normalizedThumbprint,
            "/s", "My",
            "/fd", "SHA256",
            "/tr", $TimestampUrl,
            "/td", "SHA256",
            "/d", "Hakamiq CHD Tool",
            $target
        )
        Invoke-SignTool @("verify", "/pa", "/all", "/tw", $target)
    }

    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $ManifestScript -Output $OutputPath
    if ($LASTEXITCODE -ne 0) {
        throw "Release manifest regeneration failed after Authenticode signing."
    }

    if ([string]::IsNullOrWhiteSpace($ReportPath)) {
        $ReportPath = Join-Path $OutputPath "docs\authenticode-report.json"
    }
    $reportFullPath = [System.IO.Path]::GetFullPath($ReportPath)
    if (-not (Test-PathIsSameOrChild -Path $reportFullPath -Parent $OutputPath)) {
        throw "Authenticode report must be inside the signed release output."
    }

    $report = [ordered]@{
        format = "HakamiqAuthenticodeReport.v1"
        signedAtUtc = [System.DateTimeOffset]::UtcNow.ToString("O")
        certificateThumbprint = $normalizedThumbprint
        certificateSubject = $certificate.Subject
        certificateNotAfterUtc = $certificate.NotAfter.ToUniversalTime().ToString("O")
        fileDigestAlgorithm = "SHA256"
        timestampProtocol = "RFC3161"
        timestampDigestAlgorithm = "SHA256"
        timestampUrl = $TimestampUrl
        files = @($targets | ForEach-Object { [System.IO.Path]::GetFileName($_) })
    }
    [System.IO.File]::WriteAllText(
        $reportFullPath,
        ($report | ConvertTo-Json -Depth 6),
        [System.Text.UTF8Encoding]::new($false))

    # The report changes the release contents, so include it in the final manifest too.
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $ManifestScript -Output $OutputPath
    if ($LASTEXITCODE -ne 0) {
        throw "Final release manifest regeneration failed after writing the Authenticode report."
    }

    Write-Host "[PASS] Authenticode signing and RFC 3161 timestamp verification passed: $OutputPath" -ForegroundColor Green
}
finally {
    if ($removeImportedCertificate -and $null -ne $importedCertificate) {
        try {
            $store.Remove($importedCertificate)
        }
        catch {
            Write-Warning "Temporary signing certificate could not be removed from CurrentUser/My."
        }
    }

    $store.Close()
    if ($null -ne $importedCertificate) {
        $importedCertificate.Dispose()
    }
}
