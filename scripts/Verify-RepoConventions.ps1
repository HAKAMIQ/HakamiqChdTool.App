#Requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$failures = New-Object System.Collections.Generic.List[string]

$script:ExcludedRepositoryDirectories = @(
    '.git',
    '.vs',
    '.vscode',
    '.idea',
    'bin',
    'obj',
    'packages',
    'publish',
    'Release',
    'artifacts',
    'node_modules'
)

function Add-Failure([string]$message) {
    $script:failures.Add($message)
}

function Get-RepositoryRelativePath([string]$fullPath) {
    $normalizedRoot = [System.IO.Path]::GetFullPath($root).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)

    $normalizedPath = [System.IO.Path]::GetFullPath($fullPath)

    if ($normalizedPath.Length -le $normalizedRoot.Length) {
        return ''
    }

    return $normalizedPath.Substring($normalizedRoot.Length).TrimStart(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
}

function Test-IsExcludedRepositoryPath([string]$fullPath) {
    $relativePath = Get-RepositoryRelativePath $fullPath

    if ([string]::IsNullOrWhiteSpace($relativePath)) {
        return $false
    }

    foreach ($segment in ($relativePath -split '[\\/]')) {
        if ($script:ExcludedRepositoryDirectories -contains $segment) {
            return $true
        }
    }

    return $false
}

function Get-RepositoryFiles {
    Get-ChildItem -Path $root -Recurse -File -ErrorAction SilentlyContinue | Where-Object {
        -not (Test-IsExcludedRepositoryPath $_.FullName)
    }
}

function Get-FileLines([string]$path) {
    Get-Content -LiteralPath $path -Encoding UTF8
}

function Get-AllFiles([string[]]$patterns) {
    Get-RepositoryFiles | Where-Object {
        $matched = $false

        foreach ($pattern in $patterns) {
            if ($_.Name -like $pattern) {
                $matched = $true
                break
            }
        }

        $matched
    }
}

function Get-XamlResourceKeys {
    $keys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

    Get-AllFiles @('*.xaml') | ForEach-Object {
        $content = Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8

        foreach ($match in [regex]::Matches($content, 'x:Key\s*=\s*"([^"]+)"')) {
            [void]$keys.Add($match.Groups[1].Value)
        }
    }

    return ,$keys
}

function Test-DuplicateXamlResourceKeys {
    Get-AllFiles @('*.xaml') | ForEach-Object {
        $file = $_
        $lines = Get-Content -LiteralPath $file.FullName -Encoding UTF8
        $seen = [System.Collections.Generic.Dictionary[string, int]]::new([System.StringComparer]::Ordinal)

        for ($i = 0; $i -lt $lines.Count; $i++) {
            foreach ($match in [regex]::Matches($lines[$i], 'x:Key\s*=\s*"([^"]+)"')) {
                $key = $match.Groups[1].Value
                $lineNumber = $i + 1

                if ($seen.ContainsKey($key)) {
                    Add-Failure "Duplicate XAML resource key: '$key' in $($file.FullName) first declared near line $($seen[$key]), duplicated near line $lineNumber"
                    continue
                }

                $seen[$key] = $lineNumber
            }
        }
    }
}

function Test-MissingResourceKeys {
    $xamlFiles = Get-AllFiles @('*.xaml')
    $declared = Get-XamlResourceKeys
    $used = New-Object System.Collections.Generic.List[object]

    foreach ($file in $xamlFiles) {
        $content = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8

        foreach ($pattern in @('\{DynamicResource\s+([^\}\s,]+)', '\{StaticResource\s+([^\}\s,]+)')) {
            foreach ($match in [regex]::Matches($content, $pattern)) {
                $key = $match.Groups[1].Value.Trim()

                if (-not $key) { continue }
                if ($key.StartsWith('x:')) { continue }
                if ($key.StartsWith('{x:Type')) { continue }
                if ($key.StartsWith('System')) { continue }

                $used.Add([pscustomobject]@{
                    File = $file.FullName
                    Key = $key
                })
            }
        }
    }

    foreach ($entry in $used) {
        if (-not $declared.Contains($entry.Key)) {
            Add-Failure "Resource key not declared: '$($entry.Key)' used in $($entry.File)"
        }
    }
}

function Test-MissingCSharpLocalizationKeys {
    $declared = Get-XamlResourceKeys
    $codeFiles = Get-AllFiles @('*.cs')
    $localizationKeyPattern = '(?<![A-Za-z0-9_])Loc[A-Za-z0-9]*_[A-Za-z0-9_]+(?![A-Za-z0-9_])'

    foreach ($file in $codeFiles) {
        $content = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8

        foreach ($match in [regex]::Matches($content, $localizationKeyPattern)) {
            $key = $match.Value

            if (-not $declared.Contains($key)) {
                Add-Failure "Localization key not declared in XAML resources: '$key' used in $($file.FullName)"
            }
        }
    }
}

function Test-UnreferencedHandlers {
    $xamlFiles = Get-AllFiles @('*.xaml', '*.axaml')
    $xamlText = ($xamlFiles | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 }) -join "`n"

    $codeFiles = Get-AllFiles @('*.xaml.cs', '*.axaml.cs', 'MainWindow*.cs')
    $allCodeText = ($codeFiles | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 }) -join "`n"

    $eventSignature = 'void\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(\s*object\??\s+[A-Za-z_][A-Za-z0-9_]*\s*,\s*[^\)]*\)'

    foreach ($file in $codeFiles) {
        $content = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8

        foreach ($match in [regex]::Matches($content, $eventSignature)) {
            $name = $match.Groups[1].Value

            if ($name -in @('OnPropertyChanged', 'OnErrorsChanged')) {
                continue
            }

            $escaped = [regex]::Escape($name)
            $referencedInXaml = $xamlText -match ('"' + $escaped + '"')
            $referencedAsHandler = $allCodeText -match ('\+=\s*' + $escaped + '\b')
            $declarationPattern = 'void\s+' + $escaped + '\s*\('
            $references = [regex]::Matches($allCodeText, '\b' + $escaped + '\b')
            $nonDeclarationReference = $false

            foreach ($reference in $references) {
                $start = [Math]::Max(0, $reference.Index - 6)
                $window = $allCodeText.Substring($start, [Math]::Min(40, $allCodeText.Length - $start))

                if ($window -match $declarationPattern) {
                    continue
                }

                $nonDeclarationReference = $true
                break
            }

            if (-not $referencedInXaml -and -not $referencedAsHandler -and -not $nonDeclarationReference) {
                Add-Failure "Handler without reference: $name in $($file.FullName)"
            }
        }
    }
}

function Test-LocalStylesWithoutGlobalEquivalent {
    $globalKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $viewsPath = Join-Path $root 'Views'
    $mainWindowPath = Join-Path $root 'MainWindow.xaml'

    $globalFiles = Get-AllFiles @('*.xaml') | Where-Object {
        $fullName = $_.FullName
        $isView = (Test-Path $viewsPath) -and $fullName.StartsWith($viewsPath, [System.StringComparison]::OrdinalIgnoreCase)
        $isMainWindow = $fullName.Equals($mainWindowPath, [System.StringComparison]::OrdinalIgnoreCase)

        -not $isView -and -not $isMainWindow
    }

    foreach ($file in $globalFiles) {
        $content = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8

        foreach ($match in [regex]::Matches($content, '<Style\s+x:Key\s*=\s*"([^"]+)"')) {
            [void]$globalKeys.Add($match.Groups[1].Value)
        }
    }

    $viewFiles = New-Object System.Collections.Generic.List[System.IO.FileInfo]

    if (Test-Path $viewsPath) {
        Get-ChildItem -Path $viewsPath -Recurse -File -Filter *.xaml | Where-Object {
            -not (Test-IsExcludedRepositoryPath $_.FullName)
        } | ForEach-Object {
            [void]$viewFiles.Add($_)
        }
    }

    if (Test-Path $mainWindowPath) {
        [void]$viewFiles.Add((Get-Item $mainWindowPath))
    }

    foreach ($file in $viewFiles) {
        $content = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8

        foreach ($match in [regex]::Matches($content, '<Style\s+x:Key\s*=\s*"([^"]+)"')) {
            $key = $match.Groups[1].Value

            if ($globalKeys.Contains($key)) {
                Add-Failure "Local style duplicates global style '$key' in $($file.FullName)"
            }
        }
    }
}

function Test-NoServiceConstructionInWindows {
    $windowFiles = Get-AllFiles @('*.xaml.cs', 'MainWindow*.cs') | Where-Object {
        $relativePath = Get-RepositoryRelativePath $_.FullName

        (($_.Name -like 'MainWindow*') -and $_.Name -ne 'MainWindowBootstrap.cs') -or
        ($relativePath -match '(^|[\\/])Views[\\/]')
    }

    $allowed = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    [void]$allowed.Add('QueueViewportService')

    foreach ($file in $windowFiles) {
        $content = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8

        foreach ($match in [regex]::Matches($content, 'new\s+([A-Za-z_][A-Za-z0-9_]*)Service\s*\(')) {
            $typeName = $match.Groups[1].Value + 'Service'

            if ($allowed.Contains($typeName)) {
                continue
            }

            Add-Failure "Window code-behind constructs service directly: $typeName in $($file.FullName)"
        }
    }
}

function Test-CodeBehindManualUiThreshold {
    $xamlFiles = Get-AllFiles @('*.xaml') | Where-Object {
        $relativePath = Get-RepositoryRelativePath $_.FullName

        $_.Name -eq 'MainWindow.xaml' -or $relativePath -match '(^|[\\/])Views[\\/]'
    }

    foreach ($xaml in $xamlFiles) {
        $codeBehind = [System.IO.Path]::ChangeExtension($xaml.FullName, '.xaml.cs')

        if (-not (Test-Path $codeBehind)) {
            continue
        }

        $names = [regex]::Matches((Get-Content -LiteralPath $xaml.FullName -Raw -Encoding UTF8), 'x:Name\s*=\s*"([^"]+)"') |
            ForEach-Object { $_.Groups[1].Value } |
            Select-Object -Unique

        $lines = Get-Content -LiteralPath $codeBehind -Encoding UTF8
        $maxHitsInMethod = 0
        $methodStartLine = 0

        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match '^\s*(private|internal|protected|public)\s+(async\s+)?void\s+[A-Za-z_][A-Za-z0-9_]*\s*\(') {
                $start = $i
                $braceDepth = 0
                $opened = $false
                $j = $i
                $methodLines = New-Object System.Collections.Generic.List[string]

                for (; $j -lt $lines.Count; $j++) {
                    $line = $lines[$j]
                    [void]$methodLines.Add($line)

                    $openCount = ([regex]::Matches($line, '\{')).Count
                    $closeCount = ([regex]::Matches($line, '\}')).Count

                    if ($openCount -gt 0) {
                        $opened = $true
                    }

                    $braceDepth += $openCount
                    $braceDepth -= $closeCount

                    if ($opened -and $braceDepth -le 0) {
                        break
                    }
                }

                $methodText = $methodLines -join "`n"
                $methodHits = 0

                foreach ($name in $names) {
                    if ($methodText -match ('\b' + [regex]::Escape($name) + '\.(Text|IsChecked|IsEnabled|Visibility|Value|SelectedItem|SelectedIndex|ItemsSource|ToolTip|Content|DataContext)\b')) {
                        $methodHits++
                    }
                }

                if ($methodHits -gt $maxHitsInMethod) {
                    $maxHitsInMethod = $methodHits
                    $methodStartLine = $start + 1
                }

                $i = $j
            }
        }

        if ($maxHitsInMethod -ge 10) {
            Add-Failure "Code-behind manually touches $maxHitsInMethod named UI elements in a single method in $codeBehind (starting near line $methodStartLine)"
        }
    }
}

function Test-FileLengthThresholds {
    $viewModelLimit = 1000
    $orchestratorLimit = 250
    $viewModelsPath = Join-Path $root 'ViewModels'

    if (Test-Path $viewModelsPath) {
        Get-ChildItem -Path $viewModelsPath -Filter *.cs -File | Where-Object {
            -not (Test-IsExcludedRepositoryPath $_.FullName)
        } | ForEach-Object {
            $lineCount = (Get-FileLines $_.FullName).Count

            if ($_.Name -like '*ViewModel*.cs' -and $lineCount -gt $viewModelLimit) {
                Add-Failure "ViewModel exceeds limit ($viewModelLimit): $($_.FullName) => $lineCount lines"
            }
        }
    }

    $workflowPath = Join-Path $root 'Core\Workflow'

    if (Test-Path $workflowPath) {
        Get-ChildItem -Path $workflowPath -Filter *Orchestrator*.cs -File | Where-Object {
            -not (Test-IsExcludedRepositoryPath $_.FullName)
        } | ForEach-Object {
            $lineCount = (Get-FileLines $_.FullName).Count

            if ($lineCount -gt $orchestratorLimit) {
                Add-Failure "Orchestrator exceeds limit ($orchestratorLimit): $($_.FullName) => $lineCount lines"
            }
        }
    }
}

function Test-NoTemporaryRepositoryFiles {
    $blocked = Get-RepositoryFiles | Where-Object {
        $_.Name -like '*.tmp' -or
        $_.Name -like '*.bak' -or
        $_.Name -like '*.backup' -or
        $_.Name -like '*.orig' -or
        $_.Name -like '*.user' -or
        $_.Name -like 'testwrite*' -or
        $_.Name -eq 'testroot.tmp'
    }

    foreach ($file in $blocked) {
        Add-Failure "Temporary/scratch file committed: $($file.FullName)"
    }
}

function Test-NoForbiddenUserMediaArtifacts {
    $blockedExtensions = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

    foreach ($extension in @(
        '.iso',
        '.cue',
        '.gdi',
        '.chd',
        '.cso',
        '.pbp',
        '.rvz',
        '.wua',
        '.bin',
        '.img',
        '.mdf',
        '.nrg',
        '.raw',
        '.rom',
        '.sbi',
        '.lsd',
        '.dat',
        '.key',
        '.dkey',
        '.keys',
        '.sqlite',
        '.sqlite-shm',
        '.sqlite-wal')) {
        [void]$blockedExtensions.Add($extension)
    }

    Get-RepositoryFiles | Where-Object {
        $blockedExtensions.Contains($_.Extension)
    } | ForEach-Object {
        Add-Failure "Forbidden user-media or verification artifact committed: $($_.FullName)"
    }
}

function Test-NoDropShadowEffectInXaml {
    Get-AllFiles @('*.xaml') | ForEach-Object {
        $content = Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8

        if ($content -match '<\s*DropShadowEffect\b|\bDropShadowEffect\b') {
            Add-Failure "DropShadowEffect is not allowed in production XAML: $($_.FullName)"
        }
    }
}

function Test-ThemeResourceParity {
    $themeFiles = @(
        Join-Path $root 'Themes\Light.xaml'
        Join-Path $root 'Themes\Dark.xaml'
        Join-Path $root 'Themes\Hakamiq.xaml'
    )

    foreach ($themeFile in $themeFiles) {
        if (-not (Test-Path -LiteralPath $themeFile -PathType Leaf)) {
            Add-Failure "Theme resource file is missing: $themeFile"
        }
    }

    $existingThemeFiles = @($themeFiles | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
    if ($existingThemeFiles.Count -ne $themeFiles.Count) {
        return
    }

    Test-KeySetParity -Files $themeFiles -Label 'Theme'
}

function Test-LocalizationResourceParity {
    $arabicStrings = Join-Path $root 'Resources\ArabicStrings.xaml'
    $englishStrings = Join-Path $root 'Resources\EnglishStrings.xaml'

    if (-not (Test-Path -LiteralPath $arabicStrings -PathType Leaf)) {
        Add-Failure "Arabic localization resource file is missing: $arabicStrings"
        return
    }

    if (-not (Test-Path -LiteralPath $englishStrings -PathType Leaf)) {
        Add-Failure "English localization resource file is missing: $englishStrings"
        return
    }

    Test-KeySetParity -Files @($arabicStrings, $englishStrings) -Label 'Localization'
}

function Test-NoDeadThemeTokenPalettes {
    $deadPaletteFiles = @(
        Join-Path $root 'Tokens\Light.xaml'
        Join-Path $root 'Tokens\Dark.xaml'
        Join-Path $root 'Tokens\Hakamiq.xaml'
    )

    foreach ($file in $deadPaletteFiles) {
        if (Test-Path $file) {
            Add-Failure "Dead theme token palette dictionary is not allowed: $file. Use Themes/Light.xaml, Themes/Dark.xaml, and Themes/Hakamiq.xaml as the live swappable theme palettes."
        }
    }
}

function Test-KeySetParity([string[]]$Files, [string]$Label) {
    if ($Files.Count -lt 2) {
        return
    }

    $keySets = @{}

    foreach ($file in $Files) {
        $content = Get-Content -LiteralPath $file -Raw -Encoding UTF8
        $keys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

        foreach ($match in [regex]::Matches($content, 'x:Key\s*=\s*"([^"]+)"')) {
            [void]$keys.Add($match.Groups[1].Value)
        }

        $keySets[$file] = $keys
    }

    $baselineFile = $Files[0]
    $baseline = $keySets[$baselineFile]

    foreach ($file in $Files | Select-Object -Skip 1) {
        $keys = $keySets[$file]

        foreach ($key in $baseline) {
            if (-not $keys.Contains($key)) {
                Add-Failure "$Label resource key missing from ${file}: $key"
            }
        }

        foreach ($key in $keys) {
            if (-not $baseline.Contains($key)) {
                Add-Failure "$Label resource key missing from ${baselineFile} but present in ${file}: $key"
            }
        }
    }
}

function Test-FluentTokenEnforcementInViews {
    $viewFiles = Get-AllFiles @('*.xaml') | Where-Object {
        $relativePath = Get-RepositoryRelativePath $_.FullName

        $_.Name -eq 'MainWindow.xaml' -or $relativePath -match '(^|[\\/])Views[\\/]'
    }

    $blockedPatterns = @(
        @{ Name = 'Hardcoded color'; Pattern = '#[0-9A-Fa-f]{6,8}' },
        @{ Name = 'Inline DropShadowEffect'; Pattern = 'DropShadowEffect' },
        @{ Name = 'Hardcoded Margin'; Pattern = '\bMargin="[0-9]' },
        @{ Name = 'Hardcoded Padding'; Pattern = '\bPadding="[0-9]' },
        @{ Name = 'Hardcoded CornerRadius'; Pattern = '\bCornerRadius="[0-9]' },
        @{ Name = 'Hardcoded FontSize'; Pattern = '\bFontSize="[0-9]' }
    )

    foreach ($file in $viewFiles) {
        $content = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8

        foreach ($entry in $blockedPatterns) {
            if ($content -match $entry.Pattern) {
                Add-Failure "$($entry.Name) is not allowed in view XAML: $($file.FullName)"
            }
        }
    }
}

$script:UiResourceFolderNames = @(
    'Themes',
    'Tokens',
    'Views',
    'ViewModels',
    'Style',
    'Resources'
)

function Test-IsUnderUiResourceFolder([string]$fullPath) {
    $relativePath = Get-RepositoryRelativePath $fullPath

    if ([string]::IsNullOrWhiteSpace($relativePath)) {
        return $false
    }

    $segments = $relativePath -split '[\\/]'
    return $segments.Count -gt 0 -and $script:UiResourceFolderNames -contains $segments[0]
}

function Test-ReleaseScriptConventions {
    $productionReleaseMatches = Get-RepositoryFiles | Where-Object {
        $_.Name -like '*ProductionRelease*'
    }

    foreach ($match in $productionReleaseMatches) {
        Add-Failure "Legacy production release script is not allowed: $($match.FullName)"
    }

    $toolsPublishScript = Join-Path $root 'Tools\publish-release.ps1'
    if (Test-Path -LiteralPath $toolsPublishScript -PathType Leaf) {
        Add-Failure "publish-release.ps1 must live under scripts, not Tools: $toolsPublishScript"
    }

    $projectFile = Join-Path $root 'HakamiqChdTool.App.csproj'
    if (Test-Path -LiteralPath $projectFile -PathType Leaf) {
        $projectText = Get-Content -LiteralPath $projectFile -Raw -Encoding UTF8

        if ($projectText -match 'Content\s+Include="Tools\\7zip\\\*\*\\\*"') {
            Add-Failure "7-Zip publish content must be an explicit allowlist, not Tools\7zip\**\*."
        }

        if ($projectText -notmatch 'EmbeddedResource\s+Include="Tools\\chdman\.exe"') {
            Add-Failure "chdman.exe must remain embedded as a runtime resource in the project file."
        }
    }

    $releaseManifestScript = Join-Path $root 'scripts\Generate-ReleaseManifest.ps1'
    if (-not (Test-Path -LiteralPath $releaseManifestScript -PathType Leaf)) {
        Add-Failure "Generate-ReleaseManifest.ps1 is required for end-user release hardening."
    }

    $endUserGate = Join-Path $root 'scripts\Verify-EndUserRelease.ps1'
    if (-not (Test-Path -LiteralPath $endUserGate -PathType Leaf)) {
        Add-Failure "Verify-EndUserRelease.ps1 is required for end-user release hardening."
    }
    else {
        $gateText = Get-Content -LiteralPath $endUserGate -Raw -Encoding UTF8

        if ($gateText -match '"Tools\\chdman\.exe"') {
            Add-Failure "End-user release gate must not require standalone Tools\chdman.exe because chdman is embedded and extracted at runtime."
        }

        if ($gateText -notmatch 'Assert-NoStandaloneChdmanExecutable') {
            Add-Failure "End-user release gate must reject standalone chdman.exe in Release output."
        }

        if ($gateText -notmatch 'Assert-ReleaseManifest') {
            Add-Failure "End-user release gate must validate release-manifest.json for final package integrity."
        }
    }

    $currentPublishScript = Join-Path $root 'scripts\Publish-EndUserRelease.ps1'
    if (-not (Test-Path -LiteralPath $currentPublishScript -PathType Leaf)) {
        Add-Failure "Publish-EndUserRelease.ps1 is required as the canonical end-user release script."
    }
    else {
        $publishText = Get-Content -LiteralPath $currentPublishScript -Raw -Encoding UTF8

        if ($publishText -notmatch 'Generate-ReleaseManifest\.ps1') {
            Add-Failure "Publish-EndUserRelease.ps1 must generate release-manifest.json before the end-user release gate."
        }

        if ($publishText -notmatch 'Verify-EndUserRelease\.ps1') {
            Add-Failure "Publish-EndUserRelease.ps1 must run Verify-EndUserRelease.ps1 after publish."
        }

        if ($publishText -match '\$LASTEXITCODE:') {
            Add-Failure "Publish-EndUserRelease.ps1 must use `${LASTEXITCODE}` before ':' inside interpolated strings."
        }

        if ($publishText -match '"Tools\\chdman\.exe"') {
            Add-Failure "Publish-EndUserRelease.ps1 must not require standalone Tools\chdman.exe in the end-user Release output."
        }
    }

    $legacyPublishScript = Join-Path $root 'scripts\publish-release.ps1'
    if (Test-Path -LiteralPath $legacyPublishScript -PathType Leaf) {
        Add-Failure "Legacy scripts\publish-release.ps1 is not allowed. Use scripts\Publish-EndUserRelease.ps1."
    }
}

function Test-NoVisualBasicSources {
    Get-RepositoryFiles | Where-Object {
        $_.Extension -eq '.vb'
    } | ForEach-Object {
        Add-Failure "Visual Basic source is not allowed in this C# WPF project: $($_.FullName)"
    }
}

function Test-NoEmptyExtensionlessUiResourceFiles {
    Get-RepositoryFiles | Where-Object {
        ($_.Extension -eq '' -or $null -eq $_.Extension) -and
        $_.Length -eq 0 -and
        (Test-IsUnderUiResourceFolder $_.FullName)
    } | ForEach-Object {
        Add-Failure "Empty extensionless stray file under UI/resource folder: $($_.FullName)"
    }
}

function Test-NoSedScratchUnderUiResources {
    Get-RepositoryFiles | Where-Object {
        $_.Name -like 'sed*' -and (Test-IsUnderUiResourceFolder $_.FullName)
    } | ForEach-Object {
        Add-Failure "sed* scratch file under UI/resource folder: $($_.FullName)"
    }
}

Test-DuplicateXamlResourceKeys
Test-MissingResourceKeys
Test-MissingCSharpLocalizationKeys
Test-UnreferencedHandlers
Test-LocalStylesWithoutGlobalEquivalent
Test-NoServiceConstructionInWindows
Test-CodeBehindManualUiThreshold
Test-FileLengthThresholds
Test-NoTemporaryRepositoryFiles
Test-NoForbiddenUserMediaArtifacts
Test-NoDropShadowEffectInXaml
Test-ThemeResourceParity
Test-LocalizationResourceParity
Test-NoDeadThemeTokenPalettes
Test-FluentTokenEnforcementInViews
Test-ReleaseScriptConventions
Test-NoVisualBasicSources
Test-NoEmptyExtensionlessUiResourceFiles
Test-NoSedScratchUnderUiResources

if ($failures.Count -gt 0) {
    Write-Host 'Repository convention verification failed:' -ForegroundColor Red
    $failures | Sort-Object -Unique | ForEach-Object {
        Write-Host " - $_" -ForegroundColor Red
    }

    exit 1
}

Write-Host 'Repository convention verification passed.' -ForegroundColor Green
exit 0