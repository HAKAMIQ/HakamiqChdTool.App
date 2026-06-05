#Requires -Version 5.1
[CmdletBinding()]
param(
    [string] $Output = ".\Release"
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
        "Tools\chd_reader_tool.exe",
        "Tools\7zip\7z.exe",
        "Tools\7zip\7z.dll",
        "Tools\7zip\License.txt",
        "LICENSE",
        "LEGAL.md",
        "THIRD_PARTY_NOTICES.txt",
        "CHDMAN_NOTICE.md",
        "MAME_COPYING.txt",
        "MAME_GPL-2.0.txt",
        "SEVENZIP_NOTICE.md"
    )) {
        Assert-FileExists $required
    }

    foreach ($required in @(
        "HakamiqChdTool.exe",
        "Tools\chd_reader_tool.exe",
        "Tools\7zip\7z.exe",
        "Tools\7zip\7z.dll",
        "Tools\7zip\License.txt",
        "LICENSE",
        "LEGAL.md",
        "THIRD_PARTY_NOTICES.txt",
        "CHDMAN_NOTICE.md",
        "MAME_COPYING.txt",
        "MAME_GPL-2.0.txt",
        "SEVENZIP_NOTICE.md"
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
        -Names @(".git", ".github", ".vs", "scripts", "bin", "obj") `
        -Message "Developer/build directory must not be included in end-user release:"

    Assert-NoFilesByExtension `
        -Extensions @(".cs", ".xaml", ".csproj", ".sln", ".props", ".targets", ".user", ".pubxml", ".ps1", ".cmd", ".bat") `
        -Message "Developer/source file must not be included in end-user release:"

    Assert-NoFilesByExtension `
        -Extensions @(".pdb") `
        -Message "Debug symbol file must not be included in public end-user release:"

    Assert-NoFilesByExtension `
        -Extensions @(".tmp", ".log", ".zip", ".7z", ".rar") `
        -Message "Temporary/archive/log file must not be included in end-user release:"

    Assert-NoFilesByNamePattern `
        -Patterns @("*.user", "*.suo", "*.cache", "*Development*.json", "*.deps.dev.json") `
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
        "chd_reader_tool.exe",
        "7zip\7z.exe",
        "7zip\7z.dll",
        "7zip\license.txt"
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
        "7zip"
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

function Assert-NoStandaloneChdmanExecutable {
    $matches = Get-ChildItem -LiteralPath $OutputPath -File -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name.Equals("chdman.exe", [System.StringComparison]::OrdinalIgnoreCase) }

    if ($matches) {
        foreach ($match in $matches) {
            Write-Err "Standalone chdman.exe must not be included in end-user release because chdman is embedded and extracted at runtime: $($match.FullName)"
        }

        throw "Standalone chdman.exe detected in end-user release."
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
Assert-NoDeveloperArtifacts
Assert-NoUnsupportedMameTools
Assert-NoStandaloneChdmanExecutable
Assert-OnlyApprovedToolFiles
Assert-OnlyApprovedToolDirectories
Assert-NoSquashFsArtifacts
Assert-ExecutableFilesAreNotEmpty
Assert-ReleaseManifest
Assert-NoSuspiciousNestedRelease

Write-Host "[PASS] End-user release security gate passed: $OutputPath" -ForegroundColor Green
exit 0
