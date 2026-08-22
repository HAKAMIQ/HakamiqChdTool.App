#Requires -Version 5.1
[CmdletBinding()]
param(
    [Alias("OutputPath")]
    [string] $Output = ".\Release",

    [switch] $RequireAuthenticode,

    [string] $ExpectedSignerThumbprint
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = (Resolve-Path -LiteralPath (Join-Path $ScriptDir "..")).Path
$ReleaseRoot = [System.IO.Path]::GetFullPath((Join-Path $ProjectRoot "Release"))

$OutputPath = if ([System.IO.Path]::IsPathRooted($Output)) {
    [System.IO.Path]::GetFullPath($Output)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $ProjectRoot $Output))
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

function Get-NormalizedFullPath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $root = [System.IO.Path]::GetPathRoot($fullPath)

    if ($fullPath.Equals($root, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath.TrimEnd('/')
    }

    return $fullPath.TrimEnd('\', '/')
}

function Test-PathIsSameOrChild {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $Parent
    )

    $fullPath = Get-NormalizedFullPath $Path
    $fullParent = Get-NormalizedFullPath $Parent

    if ($fullPath.Equals($fullParent, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    return $fullPath.StartsWith($fullParent + "\", [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-OutputPathIsSafe {
    if (-not (Test-PathIsSameOrChild -Path $OutputPath -Parent $ReleaseRoot)) {
        throw "End-user release output must be inside the Release directory: $OutputPath"
    }

    if ((Get-NormalizedFullPath $OutputPath).Equals((Get-NormalizedFullPath $ProjectRoot), [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "End-user release output cannot be the project root."
    }

    foreach ($blockedName in @(
        "scripts",
        "Style",
        "Views",
        "Services",
        "Core",
        "Models",
        "ViewModels",
        "Adapters",
        "Localization",
        "Properties",
        "bin",
        "obj",
        ".git",
        ".github",
        ".vs"
    )) {
        $blockedPath = Join-Path $ProjectRoot $blockedName

        if (Test-PathIsSameOrChild -Path $OutputPath -Parent $blockedPath) {
            throw "End-user release output cannot target a source/project directory: $OutputPath"
        }
    }
}

function Assert-FileExists {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RelativePath
    )

    $path = Join-Path $OutputPath $RelativePath

    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required end-user release file is missing: $RelativePath"
    }
}

function Assert-FileIsNotEmpty {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RelativePath
    )

    $path = Join-Path $OutputPath $RelativePath

    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required end-user release file is missing: $RelativePath"
    }

    $file = Get-Item -LiteralPath $path -Force
    if ($file.Length -le 0) {
        throw "Required end-user release file is empty: $RelativePath"
    }
}

function Assert-DirectoryDoesNotExist {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RelativePath
    )

    $path = Join-Path $OutputPath $RelativePath

    if (Test-Path -LiteralPath $path -PathType Container) {
        throw "Developer/source directory must not be included in end-user release: $RelativePath"
    }
}

function Assert-RootIsClean {
    param(
        [Parameter(Mandatory = $true)]
        [string] $OutputPath
    )

    $approvedRootDirectories = @(
        "Tools",
        "docs",
        "runtimes"
    )

    $forbiddenRootNames = @(
        "README.md",
        "LICENSE",
        "LEGAL.md",
        "3P_NOTICE.txt",
        "CHDMAN_NOTICE.md",
        "docs\legal\7ZIP.md",
        "MAME_COPYING.txt",
        "MAME_GPL-2.0.txt",
        "SECURITY.md",
        "CHANGELOG.md"
    )

    $approvedRootFiles = @(
        "HakamiqChdTool.exe",
        "HakamiqChdTool.dll",
        "HakamiqChdTool.deps.json",
        "HakamiqChdTool.runtimeconfig.json",
        "release-manifest.json"
    )

    $rootFiles = Get-ChildItem -LiteralPath $OutputPath -File -Force -ErrorAction Stop
    foreach ($file in $rootFiles) {
        if ($forbiddenRootNames -contains $file.Name) {
            throw "Documentation/legal file must not be in release root: $($file.Name). Move it under docs or docs/legal."
        }

        $isApprovedRootFile = $approvedRootFiles -contains $file.Name
        $isDependencyDll = $file.Extension.Equals(".dll", [System.StringComparison]::OrdinalIgnoreCase)

        if (-not ($isApprovedRootFile -or $isDependencyDll)) {
            throw "Root release file is not approved: $($file.Name). Keep only .NET publish dependencies in root and move documentation/legal files under docs."
        }

        if ($file.Extension.Equals(".pdb", [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Debug symbol file must not be shipped: $($file.Name)"
        }
    }

    $rootDirectories = Get-ChildItem -LiteralPath $OutputPath -Directory -Force -ErrorAction Stop
    foreach ($directory in $rootDirectories) {
        if ($approvedRootDirectories -notcontains $directory.Name) {
            throw "Root release directory is not approved: $($directory.Name)."
        }
    }
}

function Assert-NoDirectoriesByName {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Names,

        [Parameter(Mandatory = $true)]
        [string] $Message
    )

    $lowerNames = $Names | ForEach-Object { $_.ToLowerInvariant() }

    $matches = Get-ChildItem -LiteralPath $OutputPath -Directory -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object { $lowerNames -contains $_.Name.ToLowerInvariant() }

    if ($matches) {
        foreach ($match in $matches) {
            Write-Err "$Message $($match.FullName)"
        }

        throw $Message
    }
}

function Assert-NoFilesByExtension {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Extensions,

        [Parameter(Mandatory = $true)]
        [string] $Message
    )

    $lowerExtensions = $Extensions | ForEach-Object { $_.ToLowerInvariant() }

    $matches = Get-ChildItem -LiteralPath $OutputPath -File -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object { $lowerExtensions -contains $_.Extension.ToLowerInvariant() }

    if ($matches) {
        foreach ($match in $matches) {
            Write-Err "$Message $($match.FullName)"
        }

        throw $Message
    }
}

function Assert-NoFilesByNamePattern {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Patterns,

        [Parameter(Mandatory = $true)]
        [string] $Message
    )

    $matches = Get-ChildItem -LiteralPath $OutputPath -File -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object {
            $matched = $false

            foreach ($pattern in $Patterns) {
                if ($_.Name -like $pattern) {
                    $matched = $true
                    break
                }
            }

            $matched
        }

    if ($matches) {
        foreach ($match in $matches) {
            Write-Err "$Message $($match.FullName)"
        }

        throw $Message
    }
}

function Assert-NoDirectoriesByNamePattern {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Patterns,

        [Parameter(Mandatory = $true)]
        [string] $Message
    )

    $matches = Get-ChildItem -LiteralPath $OutputPath -Directory -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object {
            $matched = $false

            foreach ($pattern in $Patterns) {
                if ($_.Name -like $pattern) {
                    $matched = $true
                    break
                }
            }

            $matched
        }

    if ($matches) {
        foreach ($match in $matches) {
            Write-Err "$Message $($match.FullName)"
        }

        throw $Message
    }
}

function Assert-RequiredReleaseFiles {
    foreach ($required in @(
        "HakamiqChdTool.exe",
        "HakamiqChdTool.dll",
        "HakamiqChdTool.deps.json",
        "HakamiqChdTool.runtimeconfig.json",
        "release-manifest.json",
        "Tools\chdman.exe",
        "Tools\7zip\7z.exe",
        "Tools\7zip\7z.dll",
        "Tools\7zip\License.txt",
        "Tools\hakamiq-cso\win-x64\csokit.exe",
        "Tools\hakamiq-cso\win-x64\CsoKit.Native.dll",
        "Tools\hakamiq-cso\win-x64\LICENSE.txt",
        "Tools\hakamiq-cso\win-x64\README.md",
        "Tools\hakamiq-cso\win-x64\RELEASE_NOTES.md",
        "Tools\hakamiq-cso\win-x64\SHA256SUMS.txt",
        "Tools\hakamiq-cso\win-x64\THIRD_PARTY_NOTICES.md",
        "docs\README.md",
        "docs\CHANGELOG.md",
        "docs\SECURITY.md",
        "docs\sbom.cdx.json",
        "docs\legal\LICENSE",
        "docs\legal\LEGAL.md",
        "docs\legal\3P_NOTICE.txt",
        "docs\legal\CHDMAN_NOTICE.md",
        "docs\legal\MAME_COPYING.txt",
        "docs\legal\MAME_GPL-2.0.txt",
        "docs\legal\7ZIP.md",
        "docs\legal\CSOKIT_NOTICE.md"
    )) {
        Assert-FileExists $required
    }

    foreach ($required in @(
        "HakamiqChdTool.exe",
        "HakamiqChdTool.dll",
        "HakamiqChdTool.deps.json",
        "HakamiqChdTool.runtimeconfig.json",
        "release-manifest.json",
        "Tools\chdman.exe",
        "Tools\7zip\7z.exe",
        "Tools\7zip\7z.dll",
        "Tools\7zip\License.txt",
        "Tools\hakamiq-cso\win-x64\csokit.exe",
        "Tools\hakamiq-cso\win-x64\CsoKit.Native.dll",
        "Tools\hakamiq-cso\win-x64\LICENSE.txt",
        "Tools\hakamiq-cso\win-x64\README.md",
        "Tools\hakamiq-cso\win-x64\RELEASE_NOTES.md",
        "Tools\hakamiq-cso\win-x64\SHA256SUMS.txt",
        "Tools\hakamiq-cso\win-x64\THIRD_PARTY_NOTICES.md",
        "docs\README.md",
        "docs\CHANGELOG.md",
        "docs\SECURITY.md",
        "docs\sbom.cdx.json",
        "docs\legal\LICENSE",
        "docs\legal\LEGAL.md",
        "docs\legal\3P_NOTICE.txt",
        "docs\legal\CHDMAN_NOTICE.md",
        "docs\legal\MAME_COPYING.txt",
        "docs\legal\MAME_GPL-2.0.txt",
        "docs\legal\7ZIP.md",
        "docs\legal\CSOKIT_NOTICE.md"
    )) {
        Assert-FileIsNotEmpty $required
    }
}

function Assert-NoDeveloperArtifacts {
    foreach ($directory in @(
        ".git",
        ".github",
        ".vs",
        "scripts",
        "bin",
        "obj",
        "Style",
        "Views",
        "Services",
        "Core",
        "Models",
        "ViewModels",
        "Adapters",
        "Localization",
        "Properties"
    )) {
        Assert-DirectoryDoesNotExist $directory
    }

    Assert-NoDirectoriesByName `
        -Names @(".git", ".github", ".vs", ".runtime", "scripts", "bin", "obj", "TestResults") `
        -Message "Developer/build directory must not be included in end-user release:"

    Assert-NoFilesByExtension `
        -Extensions @(".cs", ".xaml", ".csproj", ".sln", ".props", ".targets", ".user", ".pubxml", ".ps1", ".cmd", ".bat") `
        -Message "Developer/source file must not be included in end-user release:"

    Assert-NoFilesByExtension `
        -Extensions @(".pdb") `
        -Message "Debug symbol file must not be included in public end-user release:"

    Assert-NoFilesByExtension `
        -Extensions @(".tmp", ".bak", ".log", ".zip", ".7z", ".rar") `
        -Message "Temporary/archive/log file must not be included in end-user release:"

    Assert-NoFilesByNamePattern `
        -Patterns @("owner.pid", "*.user", "*.suo", "*.cache", "*Development*.json", "*.deps.dev.json") `
        -Message "Development-only file must not be included in end-user release:"
}

function Assert-NoUnsupportedMameTools {
    $toolsRoot = Join-Path $OutputPath "Tools"

    if (-not (Test-Path -LiteralPath $toolsRoot -PathType Container)) {
        return
    }

    $unsupported = @(
        "castool.exe",
        "floptool.exe",
        "imgtool.exe",
        "romcmp.exe",
        "unidasm.exe",
        "jedutil.exe",
        "nltool.exe",
        "nlwav.exe",
        "pngcmp.exe",
        "ledutil.exe",
        "ldverify.exe",
        "ldresample.exe"
    )

    $matches = Get-ChildItem -LiteralPath $toolsRoot -File -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object { $unsupported -contains $_.Name.ToLowerInvariant() }

    if ($matches) {
        foreach ($match in $matches) {
            Write-Err "Unsupported MAME external tool must not be included: $($match.FullName)"
        }

        throw "Unsupported MAME tools detected in end-user release."
    }
}

function Assert-OnlyApprovedToolFiles {
    $toolsRoot = Join-Path $OutputPath "Tools"

    if (-not (Test-Path -LiteralPath $toolsRoot -PathType Container)) {
        throw "Required Tools directory is missing from end-user release."
    }

    $approved = @(
        "chdman.exe",
        "7zip\7z.exe",
        "7zip\7z.dll",
        "7zip\license.txt",
        "hakamiq-cso\win-x64\csokit.exe",
        "hakamiq-cso\win-x64\csokit.native.dll",
        "hakamiq-cso\win-x64\license.txt",
        "hakamiq-cso\win-x64\readme.md",
        "hakamiq-cso\win-x64\release_notes.md",
        "hakamiq-cso\win-x64\sha256sums.txt",
        "hakamiq-cso\win-x64\third_party_notices.md"
    )

    $matches = Get-ChildItem -LiteralPath $toolsRoot -File -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object {
            $relativePath = $_.FullName.Substring($toolsRoot.Length).TrimStart('\', '/').Replace('/', '\').ToLowerInvariant()
            -not ($approved -contains $relativePath)
        }

    if ($matches) {
        foreach ($match in $matches) {
            Write-Err "Unapproved tool/runtime file must not be included: $($match.FullName)"
        }

        throw "Unapproved files detected in Tools directory."
    }
}

function Assert-OnlyApprovedToolDirectories {
    $toolsRoot = Join-Path $OutputPath "Tools"

    if (-not (Test-Path -LiteralPath $toolsRoot -PathType Container)) {
        throw "Required Tools directory is missing from end-user release."
    }

    $approved = @(
        "7zip",
        "hakamiq-cso",
        "hakamiq-cso\win-x64"
    )

    $matches = Get-ChildItem -LiteralPath $toolsRoot -Directory -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object {
            $relativePath = $_.FullName.Substring($toolsRoot.Length).TrimStart('\', '/').Replace('/', '\').ToLowerInvariant()
            -not ($approved -contains $relativePath)
        }

    if ($matches) {
        foreach ($match in $matches) {
            Write-Err "Unapproved tool directory must not be included: $($match.FullName)"
        }

        throw "Unapproved directories detected in Tools directory."
    }
}

function Assert-BundledChdmanContract {
    $expectedPath = [System.IO.Path]::GetFullPath((Join-Path $OutputPath "Tools\chdman.exe"))
    $matches = @(
        Get-ChildItem -LiteralPath $OutputPath -File -Recurse -Force -ErrorAction SilentlyContinue |
            Where-Object { $_.Name.Equals("chdman.exe", [System.StringComparison]::OrdinalIgnoreCase) }
    )

    if ($matches.Count -ne 1 -or -not $matches[0].FullName.Equals($expectedPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        foreach ($match in $matches) {
            Write-Err "Unexpected chdman.exe location in end-user release: $($match.FullName)"
        }

        throw "End-user release must contain exactly one chdman.exe at Tools\chdman.exe."
    }

    $expectedSha256 = "8A74468E3B0879698835B57C3B58E88E5A51E4DE73BEE6EF755C28530B5B040F"
    $actualSha256 = (Get-FileHash -LiteralPath $expectedPath -Algorithm SHA256).Hash

    if (-not $actualSha256.Equals($expectedSha256, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Bundled Tools\chdman.exe SHA-256 mismatch. Expected=$expectedSha256 Actual=$actualSha256"
    }
}

function Assert-NoLibchdrReleaseArtifacts {
    $blockedNames = @(
        "libchdr.dll",
        "libchdr.pdb",
        "chdr.dll",
        "chdr.pdb"
    )

    $matches = Get-ChildItem -LiteralPath $OutputPath -File -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object { $blockedNames -contains $_.Name.ToLowerInvariant() }

    if ($matches) {
        foreach ($match in $matches) {
            Write-Err "libchdr/native CHD inspection artifact must not be included in the P1 end-user release: $($match.FullName)"
        }

        throw "libchdr/native CHD inspection artifacts detected in end-user release."
    }
}

function Assert-CsoKitBundledToolContract {
    foreach ($required in @(
        "Tools\hakamiq-cso\win-x64\csokit.exe",
        "Tools\hakamiq-cso\win-x64\CsoKit.Native.dll",
        "Tools\hakamiq-cso\win-x64\LICENSE.txt",
        "Tools\hakamiq-cso\win-x64\README.md",
        "Tools\hakamiq-cso\win-x64\RELEASE_NOTES.md",
        "Tools\hakamiq-cso\win-x64\SHA256SUMS.txt",
        "Tools\hakamiq-cso\win-x64\THIRD_PARTY_NOTICES.md",
        "docs\legal\CSOKIT_NOTICE.md"
    )) {
        Assert-FileExists $required
        Assert-FileIsNotEmpty $required
    }
}

function Assert-NoOldCsoToolArtifacts {
    $blocked = Get-ChildItem -LiteralPath $OutputPath -Recurse -File -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match 'disallowed-cso-helper' -or $_.FullName -match '[\\/]disallowed-cso-helper([\\/]|$)' }

    if ($blocked) {
        foreach ($match in $blocked) {
            Write-Err "disallowed-cso-helper must not be included in the release package: $($match.FullName)"
        }

        throw "disallowed-cso-helper must not be included in the release package."
    }
}

function Assert-NoForbiddenExternalArtifacts {
    $blockedNames = @(
        "ZArchive*",
        "ymir*"
    )

    $matches = Get-ChildItem -LiteralPath $OutputPath -File -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object {
            $fileName = $_.Name
            $isBlocked = $false
            foreach ($pattern in $blockedNames) {
                if ($fileName -like $pattern) {
                    $isBlocked = $true
                    break
                }
            }

            $isBlocked
        }

    if ($matches) {
        foreach ($match in $matches) {
            Write-Err "Forbidden external tool artifact must not be included in the end-user release: $($match.FullName)"
        }

        throw "Forbidden external tool artifacts detected in end-user release."
    }
}



function Assert-NoSquashFsArtifacts {
    $patterns = @(
        "*squashfs*",
        "*mksquashfs*",
        "*unsquashfs*"
    )

    Assert-NoFilesByNamePattern `
        -Patterns $patterns `
        -Message "SquashFS artifact must not be included in end-user release:"

    Assert-NoDirectoriesByNamePattern `
        -Patterns $patterns `
        -Message "SquashFS directory must not be included in end-user release:"
}

function Assert-NoCrashDumpHelper {
    Assert-NoFilesByNamePattern `
        -Patterns @("createdump.exe") `
        -Message "Crash dump helper must not be included in end-user release:"
}

function Assert-NoSuspiciousNestedRelease {
    $matches = Get-ChildItem -LiteralPath $OutputPath -Directory -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -in @("Release", "Debug", "publish") -and
            -not $_.FullName.Equals($OutputPath, [System.StringComparison]::OrdinalIgnoreCase)
        }

    if ($matches) {
        foreach ($match in $matches) {
            Write-Err "Suspicious nested release/build directory: $($match.FullName)"
        }

        throw "Suspicious nested release/build directory detected."
    }
}

function Assert-ExecutableFilesAreNotEmpty {
    $executables = Get-ChildItem -LiteralPath $OutputPath -File -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension.Equals(".exe", [System.StringComparison]::OrdinalIgnoreCase) }

    if (-not $executables) {
        throw "No executable files were found in end-user release."
    }

    foreach ($exe in $executables) {
        if ($exe.Length -le 0) {
            throw "Executable file is empty: $($exe.FullName)"
        }
    }
}

function Assert-AuthenticodeSignature {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RelativePath
    )

    $path = Join-Path $OutputPath $RelativePath
    $signature = Get-AuthenticodeSignature -LiteralPath $path

    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "Authenticode signature is not valid for ${RelativePath}: $($signature.Status) $($signature.StatusMessage)"
    }

    if ($null -eq $signature.SignerCertificate) {
        throw "Authenticode signer certificate is missing for: $RelativePath"
    }

    if ($null -eq $signature.TimeStamperCertificate) {
        throw "RFC 3161 timestamp/countersigner certificate is missing for: $RelativePath"
    }

    $rsaPublicKey = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey(
        $signature.SignerCertificate)
    if ($null -eq $rsaPublicKey) {
        throw "Authenticode signer certificate must use an RSA public key for: $RelativePath"
    }
    $rsaPublicKey.Dispose()

    if (-not [string]::IsNullOrWhiteSpace($ExpectedSignerThumbprint)) {
        $expected = ($ExpectedSignerThumbprint -replace '\s', '').ToUpperInvariant()
        $actual = ($signature.SignerCertificate.Thumbprint -replace '\s', '').ToUpperInvariant()
        if ($expected -ne $actual) {
            throw "Authenticode signer thumbprint mismatch for ${RelativePath}. Expected $expected, actual $actual."
        }
    }

    return $signature
}

function Assert-BundledToolHashes {
    $expectedHashes = [ordered]@{
        "Tools\chdman.exe" = "8A74468E3B0879698835B57C3B58E88E5A51E4DE73BEE6EF755C28530B5B040F"
        "Tools\7zip\7z.exe" = "83967F1B02B43C4EFEDA302795722C809E0E81B8307DE73558D10484D5676A7D"
        "Tools\7zip\7z.dll" = "69FD4DF057985C40E510E2FAC182881C7F85E90AA13EC703F763A8FDB2CE61F8"
        "Tools\hakamiq-cso\win-x64\csokit.exe" = "FB1BF1E6BD0C51CAB54F505E7E44404F1E5CBFBFF3CB0FFC7EEC159D7D9254C0"
        "Tools\hakamiq-cso\win-x64\CsoKit.Native.dll" = "B396B0CA41BE7F905E8EA73C285C1F5089C8DA4FB1E4C157775BF198B1F70589"
    }

    foreach ($relativePath in $expectedHashes.Keys) {
        $path = Join-Path $OutputPath $relativePath
        $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        $expectedHash = $expectedHashes[$relativePath]

        if (-not $actualHash.Equals($expectedHash, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Bundled tool SHA-256 mismatch for ${relativePath}. Expected=$expectedHash Actual=$actualHash"
        }
    }
}

function Assert-OnlyDeclaredRuntimeBinaries {
    $depsPath = Join-Path $OutputPath "HakamiqChdTool.deps.json"
    try {
        $deps = Get-Content -LiteralPath $depsPath -Raw -Encoding UTF8 | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw "Could not parse HakamiqChdTool.deps.json for runtime binary allowlisting: $($_.Exception.Message)"
    }

    $targetName = [string] $deps.runtimeTarget.name
    $targetProperty = $deps.targets.PSObject.Properties |
        Where-Object { $_.Name -eq $targetName } |
        Select-Object -First 1
    if ($null -eq $targetProperty) {
        throw "Release dependency manifest does not contain runtime target: $targetName"
    }

    $allowedRootNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    [void] $allowedRootNames.Add("HakamiqChdTool.exe")
    $allowedNestedPaths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

    foreach ($libraryProperty in $targetProperty.Value.PSObject.Properties) {
        $library = $libraryProperty.Value

        foreach ($runtimeAsset in @($library.runtime.PSObject.Properties)) {
            $extension = [System.IO.Path]::GetExtension($runtimeAsset.Name)
            if ($extension -in @(".dll", ".exe")) {
                [void] $allowedRootNames.Add([System.IO.Path]::GetFileName($runtimeAsset.Name))
            }
        }

        foreach ($sectionName in @("native", "runtimeTargets")) {
            $sectionProperty = $library.PSObject.Properties |
                Where-Object { $_.Name -eq $sectionName } |
                Select-Object -First 1
            if ($null -eq $sectionProperty) {
                continue
            }

            foreach ($asset in $sectionProperty.Value.PSObject.Properties) {
                $extension = [System.IO.Path]::GetExtension($asset.Name)
                if ($extension -in @(".dll", ".exe")) {
                    [void] $allowedRootNames.Add([System.IO.Path]::GetFileName($asset.Name))
                    [void] $allowedNestedPaths.Add($asset.Name.Replace('/', '\'))
                }
            }
        }
    }

    $binaries = Get-ChildItem -LiteralPath $OutputPath -File -Recurse -Force -ErrorAction Stop |
        Where-Object {
            $_.Extension -in @(".dll", ".exe") -and
            -not $_.FullName.StartsWith((Join-Path $OutputPath "Tools") + "\", [System.StringComparison]::OrdinalIgnoreCase)
        }

    foreach ($binary in $binaries) {
        $relativePath = Get-RelativeReleasePath $binary.FullName
        $directory = [System.IO.Path]::GetDirectoryName($relativePath)

        if ([string]::IsNullOrEmpty($directory)) {
            if (-not $allowedRootNames.Contains($binary.Name)) {
                throw "Unknown root executable/library is not declared by HakamiqChdTool.deps.json: $relativePath"
            }
        }
        elseif (-not $allowedNestedPaths.Contains($relativePath)) {
            throw "Unknown nested executable/library is not declared by HakamiqChdTool.deps.json: $relativePath"
        }
    }
}

function Assert-AuthenticodeReport {
    param(
        [Parameter(Mandatory = $true)]
        [System.Management.Automation.Signature[]] $Signatures
    )

    $reportPath = Join-Path $OutputPath "docs\authenticode-report.json"
    if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
        throw "Authenticode signing report is missing: docs\authenticode-report.json"
    }

    try {
        $report = Get-Content -LiteralPath $reportPath -Raw -Encoding UTF8 | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw "Authenticode signing report is not valid JSON: $($_.Exception.Message)"
    }

    if ($report.format -ne "HakamiqAuthenticodeReport.v1") {
        throw "Authenticode signing report format is invalid."
    }

    $signerThumbprints = @(
        $Signatures |
            ForEach-Object { ($_.SignerCertificate.Thumbprint -replace '\s', '').ToUpperInvariant() } |
            Select-Object -Unique
    )
    if ($signerThumbprints.Count -ne 1) {
        throw "Owned Hakamiq release files must use the same Authenticode signer certificate."
    }

    $signerCertificate = $Signatures[0].SignerCertificate
    $reportThumbprint = ([string] $report.certificateThumbprint -replace '\s', '').ToUpperInvariant()
    if ($reportThumbprint -ne $signerThumbprints[0]) {
        throw "Authenticode report signer thumbprint does not match the signed release files."
    }

    if ([string] $report.certificateSubject -ne $signerCertificate.Subject) {
        throw "Authenticode report signer subject does not match the signed release files."
    }

    $expectedNotAfterUtc = $signerCertificate.NotAfter.ToUniversalTime().ToString("O")
    if ([string] $report.certificateNotAfterUtc -ne $expectedNotAfterUtc) {
        throw "Authenticode report certificate expiration does not match the signed release files."
    }

    if ($report.publicKeyAlgorithm -ne "RSA" -or
        $report.fileDigestAlgorithm -ne "SHA256" -or
        $report.timestampProtocol -ne "RFC3161" -or
        $report.timestampDigestAlgorithm -ne "SHA256") {
        throw "Authenticode report signing algorithms do not match the required RSA/SHA256/RFC3161 policy."
    }

    $timestampUri = $null
    if (-not [System.Uri]::TryCreate([string] $report.timestampUrl, [System.UriKind]::Absolute, [ref] $timestampUri) -or
        $timestampUri.Scheme -notin @("http", "https")) {
        throw "Authenticode report timestamp URL is invalid."
    }

    $expectedFiles = @("HakamiqChdTool.dll", "HakamiqChdTool.exe")
    $reportedFiles = @($report.files | ForEach-Object { [string] $_ } | Sort-Object -Unique)
    $fileDifferences = @(Compare-Object -ReferenceObject $expectedFiles -DifferenceObject $reportedFiles)
    if ($reportedFiles.Count -ne $expectedFiles.Count -or $fileDifferences.Count -ne 0) {
        throw "Authenticode report file list does not match the owned Hakamiq release files."
    }
}

function Assert-AuthenticodePolicy {
    if (-not $RequireAuthenticode) {
        return
    }

    $signatures = @(
        Assert-AuthenticodeSignature "HakamiqChdTool.exe"
        Assert-AuthenticodeSignature "HakamiqChdTool.dll"
    )

    Assert-AuthenticodeReport -Signatures $signatures
}


function Get-RelativeReleasePath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $FilePath
    )

    $fullPath = [System.IO.Path]::GetFullPath($FilePath)
    if (-not (Test-PathIsSameOrChild -Path $fullPath -Parent $OutputPath)) {
        throw "File is outside end-user release output: $fullPath"
    }

    return $fullPath.Substring((Get-NormalizedFullPath $OutputPath).Length).TrimStart('\', '/').Replace('/', '\')
}

function Get-Sha256Hex {
    param(
        [Parameter(Mandatory = $true)]
        [string] $FilePath
    )

    $stream = [System.IO.File]::OpenRead($FilePath)
    try {
        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        try {
            $hash = $sha256.ComputeHash($stream)
            return ([System.BitConverter]::ToString($hash)).Replace('-', '').ToUpperInvariant()
        }
        finally {
            $sha256.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Assert-ReleaseManifest {
    $manifestRelativePath = "release-manifest.json"
    $manifestPath = Join-Path $OutputPath $manifestRelativePath

    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "End-user release manifest is missing: $manifestRelativePath"
    }

    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        throw "End-user release manifest is not valid JSON: $manifestPath"
    }

    if ($null -eq $manifest -or $manifest.format -ne "HakamiqReleaseManifest.v1") {
        throw "End-user release manifest format is invalid."
    }

    if ($manifest.app -ne "HakamiqChdTool") {
        throw "End-user release manifest app value is invalid."
    }

    if ($null -eq $manifest.files) {
        throw "End-user release manifest does not contain files."
    }

    $manifestEntries = @($manifest.files)
    if ([int] $manifest.fileCount -ne $manifestEntries.Count) {
        throw "End-user release manifest fileCount does not match files array."
    }

    $entryByPath = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::OrdinalIgnoreCase)

    foreach ($entry in $manifestEntries) {
        $relativePath = ([string] $entry.path).Trim().Replace('/', '\')

        if ([string]::IsNullOrWhiteSpace($relativePath)) {
            throw "End-user release manifest contains an empty path."
        }

        if ([System.IO.Path]::IsPathRooted($relativePath) -or $relativePath.Contains('..')) {
            throw "End-user release manifest contains an unsafe path: $relativePath"
        }

        if ($relativePath.Equals($manifestRelativePath, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "End-user release manifest must not list itself."
        }

        if ($entryByPath.ContainsKey($relativePath)) {
            throw "End-user release manifest contains a duplicate path: $relativePath"
        }

        $hash = ([string] $entry.sha256).Trim().ToUpperInvariant()
        if ($hash -notmatch '^[0-9A-F]{64}$') {
            throw "End-user release manifest contains an invalid SHA-256 hash for: $relativePath"
        }

        [void]$entryByPath.Add($relativePath, $entry)
    }

    $actualFiles = Get-ChildItem -LiteralPath $OutputPath -File -Recurse -Force -ErrorAction Stop |
        Where-Object {
            $relativePath = Get-RelativeReleasePath $_.FullName
            -not $relativePath.Equals($manifestRelativePath, [System.StringComparison]::OrdinalIgnoreCase)
        }

    foreach ($file in $actualFiles) {
        $relativePath = Get-RelativeReleasePath $file.FullName

        if (-not $entryByPath.ContainsKey($relativePath)) {
            throw "End-user release manifest is missing file entry: $relativePath"
        }

        $entry = $entryByPath[$relativePath]
        $expectedSize = [Int64] $entry.sizeBytes
        if ($expectedSize -ne $file.Length) {
            throw "End-user release manifest size mismatch for: $relativePath"
        }

        $expectedHash = ([string] $entry.sha256).Trim().ToUpperInvariant()
        $actualHash = Get-Sha256Hex $file.FullName

        if ($expectedHash -ne $actualHash) {
            throw "End-user release manifest hash mismatch for: $relativePath"
        }
    }

    $actualPathSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $actualFiles) {
        [void]$actualPathSet.Add((Get-RelativeReleasePath $file.FullName))
    }

    foreach ($relativePath in $entryByPath.Keys) {
        if (-not $actualPathSet.Contains($relativePath)) {
            throw "End-user release manifest lists a file that does not exist: $relativePath"
        }
    }
}

Assert-OutputPathIsSafe

if (-not (Test-Path -LiteralPath $OutputPath -PathType Container)) {
    throw "End-user release output directory was not found: $OutputPath"
}

Write-Info "Verifying end-user release output: $OutputPath"

Assert-RequiredReleaseFiles
Assert-RootIsClean -OutputPath $OutputPath
Assert-NoDeveloperArtifacts
Assert-NoUnsupportedMameTools
Assert-BundledChdmanContract
Assert-NoLibchdrReleaseArtifacts
Assert-NoOldCsoToolArtifacts
Assert-NoForbiddenExternalArtifacts
Assert-OnlyApprovedToolFiles
Assert-OnlyApprovedToolDirectories
Assert-CsoKitBundledToolContract
Assert-BundledToolHashes
Assert-OnlyDeclaredRuntimeBinaries
Assert-NoSquashFsArtifacts
Assert-NoCrashDumpHelper
Assert-ExecutableFilesAreNotEmpty
Assert-AuthenticodePolicy
Assert-ReleaseManifest
Assert-NoSuspiciousNestedRelease

Write-Host "[PASS] End-user release security gate passed: $OutputPath" -ForegroundColor Green
