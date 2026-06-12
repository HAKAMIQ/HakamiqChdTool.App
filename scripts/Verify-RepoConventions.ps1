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

function Test-NoAppTextAlignmentReferencesOutsideAppXaml {
    Get-AllFiles @('*.xaml') | Where-Object {
        $_.Name -ne 'App.xaml'
    } | ForEach-Object {
        $content = Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8

        if ($content -match 'App\.TextAlignment') {
            Add-Failure "App.TextAlignment reference is not allowed outside App.xaml: $($_.FullName)"
        }
    }
}

function Test-NoApplyFixScripts {
    Get-RepositoryFiles | Where-Object {
        $_.Name -like 'Apply-Fix*.ps1'
    } | ForEach-Object {
        Add-Failure "Patch-style Apply-Fix script is not allowed: $($_.FullName)"
    }
}

function Test-PublishPackagingPolicy {
    $publishConfigurationFiles = Get-RepositoryFiles | Where-Object {
        $_.Extension -in @('.csproj', '.props', '.targets', '.pubxml')
    }

    foreach ($file in $publishConfigurationFiles) {
        $text = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8

        if ($text -match '<PublishSingleFile>\s*true\s*</PublishSingleFile>') {
            Add-Failure "PublishSingleFile=true is not allowed for the approved portable .NET publish policy: $($file.FullName)"
        }

        if ($text -match '<SelfContained>\s*true\s*</SelfContained>') {
            Add-Failure "SelfContained=true is not allowed for the approved portable .NET publish policy: $($file.FullName)"
        }

        if ($text -match '<IncludeNativeLibrariesForSelfExtract>\s*true\s*</IncludeNativeLibrariesForSelfExtract>') {
            Add-Failure "IncludeNativeLibrariesForSelfExtract is not allowed for the approved portable .NET publish policy: $($file.FullName)"
        }
    }

    $publishScripts = Get-RepositoryFiles | Where-Object {
        $_.Extension -eq '.ps1' -and
        $_.Name -ne 'Verify-RepoConventions.ps1' -and
        ($_.Name -like '*Publish*' -or $_.Name -like '*Release*')
    }

    foreach ($file in $publishScripts) {
        $text = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8

        if ($text -match 'PublishSingleFile\s*=\s*true') {
            Add-Failure "PublishSingleFile=true is not allowed in publish/release scripts: $($file.FullName)"
        }

        if ($text -match 'SelfContained\s*=\s*true') {
            Add-Failure "SelfContained=true is not allowed in publish/release scripts: $($file.FullName)"
        }

        if ($text -match 'IncludeNativeLibrariesForSelfExtract') {
            Add-Failure "IncludeNativeLibrariesForSelfExtract is not allowed in publish/release scripts: $($file.FullName)"
        }
    }

    $releasePath = Join-Path $root 'Release'
    if (-not (Test-Path -LiteralPath $releasePath -PathType Container)) {
        return
    }

    Get-ChildItem -LiteralPath $releasePath -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object {
        $relative = $_.FullName.Substring($releasePath.Length).TrimStart([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)

        if ($_.Extension -in @('.cs', '.xaml', '.csproj', '.sln', '.ps1')) {
            Add-Failure "Source/build script file is not allowed inside Release output: $($_.FullName)"
        }

        $segments = $relative -split '[\\/]'
        if ($segments.Count -eq 1 -and $segments[0] -in @('LEGAL.md', 'THIRD_PARTY_NOTICES.txt', 'CHDMAN_NOTICE.md', 'SEVENZIP_NOTICE.md', 'MAME_COPYING.txt', 'MAME_GPL-2.0.txt')) {
            Add-Failure "Legal document is not allowed in Release root; it must remain under docs/legal: $($_.FullName)"
        }
    }
}

function Test-RefactoredFileSizeThresholds {
    $limits = @{
        'Views\RedumpDetailsDialog.xaml.cs' = 120
        'Views\OptionsWindow.xaml.cs' = 180
        'Services\Conversion\ChdConversionService.cs' = 700
        'Core\Workflow\Paths\WorkflowPathUtilities.cs' = 180
        'Core\Queue\QueueManager.cs' = 180
        'Resources\Style\QueueItemTemplate.xaml' = 80
    }

    foreach ($entry in $limits.GetEnumerator()) {
        $path = Join-Path $root $entry.Key
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            Add-Failure "Expected refactored file is missing: $path"
            continue
        }

        $lineCount = (Get-FileLines $path).Count
        if ($lineCount -gt $entry.Value) {
            Add-Failure "Refactored file exceeds limit ($($entry.Value)): $path => $lineCount lines"
        }
    }
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
        Join-Path $root 'Resources\Themes\Light.xaml'
        Join-Path $root 'Resources\Themes\Dark.xaml'
        Join-Path $root 'Resources\Themes\Hakamiq.xaml'
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
        Join-Path $root 'Resources\Tokens\Light.xaml'
        Join-Path $root 'Resources\Tokens\Dark.xaml'
        Join-Path $root 'Resources\Tokens\Hakamiq.xaml'
    )

    foreach ($file in $deadPaletteFiles) {
        if (Test-Path $file) {
            Add-Failure "Dead theme token palette dictionary is not allowed: $file. Use Resources/Themes/Light.xaml, Resources/Themes/Dark.xaml, and Resources/Themes/Hakamiq.xaml as the live swappable theme palettes."
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

function Test-RefactorCompositionCompletion {
    $queueWorkspace = Join-Path $root 'Views\Main\QueueWorkspaceView.xaml'
    if (Test-Path -LiteralPath $queueWorkspace -PathType Leaf) {
        $content = Get-Content -LiteralPath $queueWorkspace -Raw -Encoding UTF8
        foreach ($viewName in @('QueueToolbarView', 'QueueSummaryView', 'QueueEmptyStateView', 'QueueListView')) {
            if ($content -notmatch ('main:' + [regex]::Escape($viewName) + '\b')) {
                Add-Failure "QueueWorkspaceView.xaml does not compose required subview: $viewName"
            }
        }
    }

    $queueItemTemplate = Join-Path $root 'Resources\Style\Queue\QueueItemTemplate.xaml'
    if (Test-Path -LiteralPath $queueItemTemplate -PathType Leaf) {
        $content = Get-Content -LiteralPath $queueItemTemplate -Raw -Encoding UTF8
        foreach ($templateKey in @(
            'QueueItemHeaderFieldsTemplate',
            'QueueItemStatusFieldsTemplate',
            'QueueItemProgressFieldsTemplate',
            'QueueItemActionsFieldsTemplate',
            'QueueItemTechnicalFieldsTemplate')) {
            if ($content -notmatch [regex]::Escape($templateKey)) {
                Add-Failure "QueueItemTemplate.xaml does not compose required subtemplate: $templateKey"
            }
        }
    }

    foreach ($subTemplate in @(
        'Resources\Style\Queue\QueueItemHeaderTemplate.xaml',
        'Resources\Style\Queue\QueueItemStatusTemplate.xaml',
        'Resources\Style\Queue\QueueItemProgressTemplate.xaml',
        'Resources\Style\Queue\QueueItemActionsTemplate.xaml',
        'Resources\Style\Queue\QueueItemTechnicalFieldsTemplate.xaml')) {
        $templatePath = Join-Path $root $subTemplate
        if (Test-Path -LiteralPath $templatePath -PathType Leaf) {
            $templateContent = Get-Content -LiteralPath $templatePath -Raw -Encoding UTF8
            if ($templateContent -notmatch 'QueueItemBaseStyles\.xaml') {
                Add-Failure "$subTemplate must merge QueueItemBaseStyles.xaml so subtemplate StaticResource lookups are self-contained."
            }

            if ($templateContent -notmatch 'QueueWorkspaceSharedStyles\.xaml') {
                Add-Failure "$subTemplate must merge QueueWorkspaceSharedStyles.xaml so subtemplate StaticResource lookups are self-contained."
            }
        }
    }
}

function Test-NoPartialRefactorSlicing {
    $blockedPatterns = @(
        'partial\s+class\s+QueueManager',
        'partial\s+class\s+ChdConversionService',
        'static\s+partial\s+class\s+WorkflowPathUtilities',
        'partial\s+class\s+WorkflowPathUtilities')

    Get-AllFiles @('*.cs') | ForEach-Object {
        $content = Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8
        foreach ($pattern in $blockedPatterns) {
            if ($content -match $pattern) {
                Add-Failure "Partial-slicing refactor pattern is not allowed in $($_.FullName): $pattern"
            }
        }
    }
}

function Test-OptionsCoordinatorPlacement {
    $applicationLayerCoordinator = Join-Path $root 'Application\Options\HqOptionsShell.cs'
    if (Test-Path -LiteralPath $applicationLayerCoordinator -PathType Leaf) {
        Add-Failure "HqOptionsShell is WPF-heavy and must not live under Application: $applicationLayerCoordinator"
    }

    Get-AllFiles @('*.cs') | ForEach-Object {
        $content = Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8
        if ($content -match 'ApplicationLayer\.Options') {
            Add-Failure "Stale Options ApplicationLayer namespace reference: $($_.FullName)"
        }
    }
}


function Test-NoWpfShellUnderServices {
    $servicesWpfShell = Join-Path $root 'Services\WpfShell'
    if (Test-Path -LiteralPath $servicesWpfShell) {
        Add-Failure "WPF shell files must live under Ui\Shell, not Services\WpfShell."
    }

    Get-AllFiles @('*.cs') | Where-Object {
        $_.FullName -like (Join-Path $root 'Services\*')
    } | ForEach-Object {
        $content = Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8
        if ($content -match 'HakamiqChdTool\.App\.Views' -or
            $content -match 'HakamiqChdTool\.App\.ViewModels') {
            Add-Failure "Services layer must not reference Views/ViewModels directly: $($_.FullName)"
        }
    }

    Get-AllFiles @('*.cs') | ForEach-Object {
        $content = Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8
        if ($content -match 'HakamiqChdTool\.App\.Services\.WpfShell') {
            Add-Failure "Stale Services.WpfShell namespace reference: $($_.FullName)"
        }
    }
}


function Test-RedumpDetailsViewModelPurity {
    $viewModel = Join-Path $root 'ViewModels\Dialogs\RedumpDetailsDialogViewModel.cs'
    if (-not (Test-Path -LiteralPath $viewModel -PathType Leaf)) {
        return
    }

    $content = Get-Content -LiteralPath $viewModel -Raw -Encoding UTF8
    foreach ($pattern in @(
        'Clipboard',
        'DispatcherTimer',
        'Application\.Current',
        '\bBrush\b',
        '\bVisibility\b',
        'System\.Windows')) {
        if ($content -match $pattern) {
            Add-Failure "RedumpDetailsDialogViewModel must not depend on WPF primitives: $pattern"
        }
    }
}


function Test-ChdProgressParserImplementation {
    $parser = Join-Path $root 'Services\Conversion\ChdProgressParser.cs'
    if (-not (Test-Path -LiteralPath $parser -PathType Leaf)) {
        Add-Failure "ChdProgressParser.cs is missing."
        return
    }

    $content = Get-Content -LiteralPath $parser -Raw -Encoding UTF8
    foreach ($method in @(
        'ParseSnapshot',
        'TryParseLastPercent',
        'TryParseActiveProgressSnapshot',
        'StripPercentTokensForNarrative',
        'ToCleanLogLine')) {
        if ($content -notmatch "\b$method\s*\(") {
            Add-Failure "ChdProgressParser must implement $method."
        }
    }

    if ($content -match 'placeholder' -or $content -match 'delegated to ChdmanCliRunner') {
        Add-Failure "ChdProgressParser must not be a placeholder."
    }

    $runner = Join-Path $root 'Services\ChdmanProcessRunner.cs'
    if (Test-Path -LiteralPath $runner -PathType Leaf) {
        $runnerContent = Get-Content -LiteralPath $runner -Raw -Encoding UTF8
        if ($runnerContent -notmatch 'IChdProgressParser') {
            Add-Failure "ChdmanProcessRunner must use IChdProgressParser."
        }
    }
}




function Test-ConversionRuntimeReliabilityPolicy {
    $progressPolicy = Join-Path $root 'Services\Conversion\ChdProgressPolicy.cs'
    if (-not (Test-Path -LiteralPath $progressPolicy -PathType Leaf)) {
        Add-Failure 'ChdProgressPolicy.cs is required to prevent unreliable extractcd raw percent from driving UI progress.'
    }
    else {
        $content = Get-Content -LiteralPath $progressPolicy -Raw -Encoding UTF8
        if ($content -notmatch 'StartsWith\("extract"' -or $content -notmatch 'return\s+!command\.StartsWith') {
            Add-Failure 'ChdProgressPolicy must reject raw chdman percent parsing for extractcd/extractdvd commands.'
        }
    }

    $conversionService = Join-Path $root 'Services\Conversion\ChdConversionService.cs'
    if (Test-Path -LiteralPath $conversionService -PathType Leaf) {
        $content = Get-Content -LiteralPath $conversionService -Raw -Encoding UTF8
        if ($content -notmatch 'ChdProgressPolicy\.ShouldParseRawPercent\(arguments\)') {
            Add-Failure 'ChdConversionService must route chdman raw progress through ChdProgressPolicy.'
        }
        if ($content -notmatch 'extractCdBinOutputPathForArgument' -or $content -notmatch 'monitoredOutputPath:\s*monitoredOutputPath') {
            Add-Failure 'extractcd performance monitoring must track the produced BIN/ISO payload output, not the tiny CUE file.'
        }
        if ($content -notmatch 'ConversionMetricsResolver\.TryParseLogicalSizeBytes') {
            Add-Failure 'ChdConversionService must parse chdman Logical size for reliable conversion metrics.'
        }
    }

    $metricsResolver = Join-Path $root 'Services\Conversion\ConversionMetricsResolver.cs'
    if (-not (Test-Path -LiteralPath $metricsResolver -PathType Leaf)) {
        Add-Failure 'ConversionMetricsResolver.cs is required for logical media-size based conversion reports.'
    }

    $resultModel = Join-Path $root 'Models\Chd\ChdConversionResult.cs'
    if (Test-Path -LiteralPath $resultModel -PathType Leaf) {
        $content = Get-Content -LiteralPath $resultModel -Raw -Encoding UTF8
        if ($content -notmatch 'LogicalInputBytes') {
            Add-Failure 'ChdConversionResult must carry LogicalInputBytes so reports do not use descriptor text file size.'
        }
    }

    $conversionStage = Join-Path $root 'Core\Workflow\WorkflowConversionStage.cs'
    if (Test-Path -LiteralPath $conversionStage -PathType Leaf) {
        $content = Get-Content -LiteralPath $conversionStage -Raw -Encoding UTF8
        if ($content -notmatch 'ResolveConversionInputBytes' -or $content -notmatch 'conversionResult\.LogicalInputBytes') {
            Add-Failure 'WorkflowConversionStage must prefer logical input bytes over descriptor file size for performance reports.'
        }
    }

    $extractionStage = Join-Path $root 'Core\Workflow\WorkflowExtractionStage.cs'
    if (Test-Path -LiteralPath $extractionStage -PathType Leaf) {
        $content = Get-Content -LiteralPath $extractionStage -Raw -Encoding UTF8
        if ($content -notmatch 'ReportReliableExtractionPercent' -or $content -notmatch 'sample\.OutputBytes \* 100d / logicalBytes') {
            Add-Failure 'WorkflowExtractionStage must derive extract progress from output growth and logical bytes.'
        }
    }

    $safePathValidator = Join-Path $root 'Core\Workflow\Paths\WorkflowSafePathValidator.cs'
    if (Test-Path -LiteralPath $safePathValidator -PathType Leaf) {
        $content = Get-Content -LiteralPath $safePathValidator -Raw -Encoding UTF8
        if ($content -notmatch 'Path\.ChangeExtension\(finalCueFullPath, "\.bin"\)' -or $content -notmatch 'RelativeReference\s*=\s*finalBinName') {
            Add-Failure 'Extracted single-bin CUE output must be renamed to the final game stem, not left as output.bin.'
        }
    }
}



function Test-CompressionPresetTruthLayer {
    $commandPreparation = Join-Path $root 'Services\Conversion\ChdCommandPreparationService.cs'
    if (Test-Path -LiteralPath $commandPreparation -PathType Leaf) {
        $content = Get-Content -LiteralPath $commandPreparation -Raw -Encoding UTF8
        foreach ($required in @(
            'ResolveCompressionSettingWithTruth',
            'MameCreateCdDefaultCompression',
            'EffectiveCompression',
            'SameAsMameDefault')) {
            if ($content -notmatch $required) {
                Add-Failure "Compression preset truth layer missing command-preparation marker: $required"
            }
        }
    }
    else {
        Add-Failure 'ChdCommandPreparationService is required for compression preset truth resolution.'
    }

    $resolutionModel = Join-Path $root 'Models\Chd\ChdCompressionResolution.cs'
    if (-not (Test-Path -LiteralPath $resolutionModel -PathType Leaf)) {
        Add-Failure 'ChdCompressionResolution model is required to report requested/resolved/effective compression truth.'
    }

    $resultModel = Join-Path $root 'Models\Chd\ChdConversionResult.cs'
    if (Test-Path -LiteralPath $resultModel -PathType Leaf) {
        $content = Get-Content -LiteralPath $resultModel -Raw -Encoding UTF8
        foreach ($required in @(
            'RequestedCompressionPreset',
            'ResolvedCompressionCodecs',
            'EffectiveCompressionCodecs',
            'EffectiveCompressionSameAsMameDefault')) {
            if ($content -notmatch $required) {
                Add-Failure "ChdConversionResult missing compression truth field: $required"
            }
        }
    }

    $conversionService = Join-Path $root 'Services\Conversion\ChdConversionService.cs'
    if (Test-Path -LiteralPath $conversionService -PathType Leaf) {
        $content = Get-Content -LiteralPath $conversionService -Raw -Encoding UTF8
        foreach ($required in @(
            'RequestedPreset:',
            'ResolvedCompression:',
            'SameAsMameDefault:',
            'CHD compression preset resolved')) {
            if ($content -notmatch [Regex]::Escape($required)) {
                Add-Failure "Conversion log must include compression truth marker: $required"
            }
        }
    }

    $performanceReport = Join-Path $root 'Models\Chd\ConversionPerformanceReport.cs'
    if (Test-Path -LiteralPath $performanceReport -PathType Leaf) {
        $content = Get-Content -LiteralPath $performanceReport -Raw -Encoding UTF8
        foreach ($required in @(
            'RequestedCompressionPreset',
            'ResolvedCompressionCodecs',
            'EffectiveCompressionCodecs',
            'EffectiveCompressionSameAsMameDefault')) {
            if ($content -notmatch $required) {
                Add-Failure "ConversionPerformanceReport missing compression truth field: $required"
            }
        }
    }

    $arabic = Join-Path $root 'Resources\ArabicStrings.xaml'
    $english = Join-Path $root 'Resources\EnglishStrings.xaml'
    foreach ($resourceFile in @($arabic, $english)) {
        if (Test-Path -LiteralPath $resourceFile -PathType Leaf) {
            $content = Get-Content -LiteralPath $resourceFile -Raw -Encoding UTF8
            if ($content -match 'أعلى ضغط — LZMA' -or $content -match 'Maximum compression') {
                Add-Failure "Compression preset UI must not imply LZMA always produces the smallest file: $resourceFile"
            }
            if ($content -notmatch 'cdlz,cdzl,cdfl') {
                Add-Failure "Compression preset UI must disclose the actual CD max/default codec set: $resourceFile"
            }
        }
    }
}


function Test-CoreServicesDependencyReduction {
    $coreRoot = Join-Path $root 'Core'
    if (-not (Test-Path -LiteralPath $coreRoot)) {
        return
    }

    $coreServiceReferences = @(Get-ChildItem -LiteralPath $coreRoot -Recurse -File -Filter '*.cs' | Select-String -Pattern 'using HakamiqChdTool\.App\.Services')
    if ($coreServiceReferences.Count -gt 30) {
        Add-Failure "Core -> Services dependency count is too high after v1.0.5 P4 refactor: $($coreServiceReferences.Count)."
    }

    foreach ($relativePath in @(
        'Services\ChdInfoResult.cs',
        'Services\ChdVerificationResult.cs',
        'Services\ChdConversionResult.cs',
        'Services\PlatformDetectionResult.cs',
        'Services\ChdmanExtractionKind.cs')) {
        $candidate = Join-Path $root $relativePath
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            Add-Failure "Neutral DTO/model must not remain under Services: $relativePath"
        }
    }

    foreach ($requiredPath in @(
        'Models\Chd\ChdInfoResult.cs',
        'Models\Chd\ChdVerificationResult.cs',
        'Models\Chd\ChdConversionResult.cs',
        'Models\PlatformDetectionResult.cs',
        'Models\Chd\PerformanceSample.cs',
        'Models\Chd\ConversionPerformanceReport.cs',
        'Models\Chd\ChdmanExtractionKind.cs',
        'Core\Contracts\IChdInfoService.cs',
        'Core\Contracts\IChdVerificationService.cs',
        'Core\Contracts\IChdConversionService.cs')) {
        $candidate = Join-Path $root $requiredPath
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            Add-Failure "Expected v1.0.5 P4 refactor file is missing: $requiredPath"
        }
    }
}


function Test-OptionsWindowEarlyEventSafety {
    $windowCodeBehind = Join-Path $root 'Views\OptionsWindow.xaml.cs'
    if (-not (Test-Path -LiteralPath $windowCodeBehind -PathType Leaf)) {
        return
    }

    $content = Get-Content -LiteralPath $windowCodeBehind -Raw -Encoding UTF8
    if ($content -match 'private\s+void\s+OnTabButtonChecked\s*\([^)]*\)\s*=>\s*_coordinator\.OnTabButtonChecked') {
        Add-Failure "OptionsWindow.OnTabButtonChecked must be null-safe because XAML Checked can fire during InitializeComponent."
    }

    if ($content -notmatch '_coordinator\?\.OnTabButtonChecked') {
        Add-Failure "OptionsWindow.OnTabButtonChecked must guard coordinator access during InitializeComponent."
    }

    if ($content -match 'AddHandler\s*\(') {
        Add-Failure "OptionsWindow code-behind must not attach tooltip handlers directly; use HqOptionsShell.Attach()."
    }

    if ($content -notmatch '_coordinator\.Attach\s*\(\s*\)') {
        Add-Failure "OptionsWindow must call HqOptionsShell.Attach() after creating the coordinator."
    }

    $coordinatorPath = Join-Path $root 'Ui\Shell\HqOptionsShell.cs'
    if (Test-Path -LiteralPath $coordinatorPath -PathType Leaf) {
        $coordinator = Get-Content -LiteralPath $coordinatorPath -Raw -Encoding UTF8
        if ($coordinator -notmatch 'private\s+readonly\s+ToolTipEventHandler\s+_toolTipOpeningHandler') {
            Add-Failure "HqOptionsShell must keep a typed ToolTipEventHandler delegate for Attach/Dispose symmetry."
        }

        if ($coordinator -notmatch 'public\s+void\s+Attach\s*\(\s*\)' -or $coordinator -notmatch 'AddHandler\s*\(\s*ToolTipService\.ToolTipOpeningEvent\s*,\s*_toolTipOpeningHandler') {
            Add-Failure "HqOptionsShell.Attach() must attach ToolTipOpening with the typed delegate."
        }

        if ($coordinator -notmatch 'RemoveHandler\s*\(\s*ToolTipService\.ToolTipOpeningEvent\s*,\s*_toolTipOpeningHandler\s*\)') {
            Add-Failure "HqOptionsShell.Dispose() must remove ToolTipOpening with the same typed delegate."
        }
    }
}


function Test-RedumpAutoSyncStartupPolicy {
    $settingsPath = Join-Path $root 'Services\Configuration\AppSettings.cs'
    $startupPath = Join-Path $root 'Startup\MainWindowStartupCoordinator.cs'
    $autoSyncPath = Join-Path $root 'Services\RedumpAutoSyncStartupService.cs'

    if (Test-Path -LiteralPath $settingsPath -PathType Leaf) {
        $settings = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8
        if ($settings -notmatch 'EnableRedumpAutoSync\s*\{\s*get;\s*set;\s*\}\s*=\s*false\s*;') {
            Add-Failure 'EnableRedumpAutoSync must remain false by default.'
        }

        if ($settings -notmatch 'RedumpAutoSyncBackoffUntilUtc') {
            Add-Failure 'AppSettings must persist Redump auto-sync failure backoff.'
        }
    }

    if (Test-Path -LiteralPath $startupPath -PathType Leaf) {
        $startup = Get-Content -LiteralPath $startupPath -Raw -Encoding UTF8
        if ($startup -match 'Task\s+redumpAutoSyncTask\s*=\s*StartRedumpAutoSyncIfConfiguredAsync\(cancellationToken\)\s*;\s*ObserveDeferredTask\(redumpAutoSyncTask,\s*"Redump startup auto-sync"\)\s*;\s*return\s+RunStartupUpdateCheckAsync') {
            Add-Failure 'Redump auto-sync must not start directly in the MainWindow startup path; queue it after MainWindow is rendered.'
        }

        if ($startup -notmatch 'QueueRedumpAutoSyncAfterMainWindowShown') {
            Add-Failure 'MainWindow startup must queue Redump auto-sync after the MainWindow is shown.'
        }

        if ($startup -notmatch 'ContentRendered') {
            Add-Failure 'Redump auto-sync must be gated by MainWindow ContentRendered before it starts.'
        }

        if ($startup -notmatch 'UiPriority\.ApplicationIdle') {
            Add-Failure 'Redump auto-sync must be dispatched at ApplicationIdle so it does not delay startup UI.'
        }

        if ($startup -notmatch 'ObserveDeferredTask\(redumpAutoSyncTask,\s*"Redump startup auto-sync"\)') {
            Add-Failure 'Redump auto-sync background task must remain observed.'
        }
    }

    if (Test-Path -LiteralPath $autoSyncPath -PathType Leaf) {
        $autoSync = Get-Content -LiteralPath $autoSyncPath -Raw -Encoding UTF8
        if ($autoSync -notmatch 'StartupSyncTimeout') {
            Add-Failure 'Redump auto-sync must use a short startup timeout.'
        }

        if ($autoSync -notmatch 'CancelAfter\(StartupSyncTimeout\)') {
            Add-Failure 'Redump auto-sync must cancel using the short startup timeout.'
        }

        if ($autoSync -notmatch 'Timeout\s*=\s*StartupSyncTimeout') {
            Add-Failure 'Redump auto-sync HttpClient must use the short startup timeout.'
        }

        if ($autoSync -notmatch 'FailureBackoffHours' -or $autoSync -notmatch 'BackoffUntilUtc') {
            Add-Failure 'Redump auto-sync must apply a failure backoff after startup sync failures.'
        }

        if ($autoSync -notmatch 'RedumpAutoSyncBackoffUntilUtc') {
            Add-Failure 'Redump auto-sync ShouldRun must honor persisted failure backoff.'
        }
    }
}


function Test-DispatcherUnhandledExceptionPolicy {
    $app = Join-Path $root 'App.xaml.cs'
    if (-not (Test-Path -LiteralPath $app -PathType Leaf)) {
        Add-Failure 'App.xaml.cs is missing.'
        return
    }

    $content = Get-Content -LiteralPath $app -Raw -Encoding UTF8
    $methodMatch = [regex]::Match(
        $content,
        'private\s+static\s+void\s+OnDispatcherUnhandledException\s*\([^)]*\)\s*\{([\s\S]*?)\n\s*\}\s*\n\s*private\s+static\s+void\s+RequestFatalShutdown',
        [System.Text.RegularExpressions.RegexOptions]::Singleline)

    if (-not $methodMatch.Success) {
        Add-Failure 'DispatcherUnhandledException policy method could not be inspected.'
        return
    }

    $method = $methodMatch.Groups[1].Value
    $expectedIndex = $method.IndexOf('DiagnosticLogPolicy.IsExpectedCancellation(ex)', [System.StringComparison]::Ordinal)
    $firstHandledTrueIndex = $method.IndexOf('e.Handled = true', [System.StringComparison]::Ordinal)

    if ($expectedIndex -lt 0) {
        Add-Failure 'DispatcherUnhandledException must classify expected cancellation before handling.'
    }

    if ($firstHandledTrueIndex -lt 0) {
        Add-Failure 'Expected dispatcher cancellation should be marked handled.'
    }
    elseif ($expectedIndex -lt 0 -or $firstHandledTrueIndex -lt $expectedIndex) {
        Add-Failure 'DispatcherUnhandledException must not mark exceptions handled before expected-cancellation classification.'
    }

    if ($method -notmatch 'IsUiConstructionFault') {
        Add-Failure 'DispatcherUnhandledException must classify UI construction/XAML faults for fatal logging.'
    }

    if ($method -notmatch 'RequestFatalShutdown\s*\(\s*\)') {
        Add-Failure 'Unexpected dispatcher faults must request a clean fatal shutdown.'
    }

    if ($content -notmatch 'private\s+static\s+void\s+RequestFatalShutdown\s*\(' -or $content -notmatch 'Shutdown\s*\(\s*1\s*\)') {
        Add-Failure 'Fatal dispatcher faults must shut down the application with a non-zero exit code.'
    }

    if ($method -match 'BuildCrashDialogMessage\(ex\)' -or $method -match 'ShowApplicationNotice\([\s\S]*RuntimeDiagnosticFormatter\.BuildCrashDialogMessage') {
        Add-Failure 'DispatcherUnhandledException must not try to show a UI crash dialog for fatal UI faults.'
    }
}

function Test-SingleInstanceGuard {
    $app = Join-Path $root 'App.xaml.cs'
    if (-not (Test-Path -LiteralPath $app -PathType Leaf)) {
        return
    }

    $content = Get-Content -LiteralPath $app -Raw -Encoding UTF8
    if ($content -notmatch 'Local\\HakamiqChdTool\.App\.SingleInstance') {
        Add-Failure 'App must use the fixed single-instance mutex name Local\HakamiqChdTool.App.SingleInstance.'
    }

    if ($content -notmatch 'new\s+Mutex\s*\(\s*initiallyOwned:\s*true\s*,\s*name:\s*SingleInstanceMutexName\s*,\s*createdNew:\s*out\s+bool\s+createdNew') {
        Add-Failure 'App must create a named Mutex with createdNew for single-instance guarding.'
    }

    if ($content -notmatch '!createdNew') {
        Add-Failure 'Duplicate app instances must be detected through createdNew.'
    }

    if ($content -notmatch 'Shutdown\s*\(\s*0\s*\)') {
        Add-Failure 'Duplicate app instances must exit immediately with Shutdown(0).'
    }

    if ($content -notmatch 'ReleaseMutex\s*\(\s*\)') {
        Add-Failure 'Single-instance mutex must be released on application exit.'
    }
}

function Test-OptionsConstructorGuard {
    $modals = Join-Path $root 'Views\MainWindow\MainWindow.Modals.cs'
    if (-not (Test-Path -LiteralPath $modals -PathType Leaf)) {
        return
    }

    $content = Get-Content -LiteralPath $modals -Raw -Encoding UTF8
    if ($content -notmatch 'try[\s\S]*new\s+OptionsWindow') {
        Add-Failure 'OpenOptionsDialog must wrap OptionsWindow construction in try/catch.'
    }

    if ($content -notmatch 'catch\s*\(\s*Exception\s+ex\s*\)[\s\S]*Options window construction failed') {
        Add-Failure 'OpenOptionsDialog must log constructor failures safely.'
    }

    if ($content -notmatch 'ShowNoticeDialog') {
        Add-Failure 'OpenOptionsDialog must show a safe user-facing error if construction fails.'
    }
}

function Test-ShutdownBackgroundTimeouts {
    $lifecycle = Join-Path $root 'Views\MainWindow\MainWindow.Lifecycle.cs'
    if (-not (Test-Path -LiteralPath $lifecycle -PathType Leaf)) {
        return
    }

    $content = Get-Content -LiteralPath $lifecycle -Raw -Encoding UTF8
    foreach ($name in @(
        'StartupUpdateCheckShutdownTimeout',
        'RuntimeDeferredCleanupShutdownTimeout',
        'QueueDisposeShutdownTimeout',
        'PendingWorkspaceCleanupShutdownTimeout',
        'RuntimeSessionCleanupShutdownTimeout')) {
        if ($content -notmatch $name) {
            Add-Failure "Shutdown timeout is missing: $name"
        }
    }

    if ($content -notmatch 'WaitAsync\s*\(\s*timeout\.Value\s*\)' -or $content -notmatch 'TimeoutException') {
        Add-Failure 'Background shutdown steps must enforce timeout using WaitAsync and handle TimeoutException.'
    }
}

function Test-ShowRedumpDetailsReturnsTask {
    $commands = Join-Path $root 'Views\MainWindow\MainWindow.Commands.cs'
    $session = Join-Path $root 'Views\MainWindow\MainWindow.Session.cs'
    $sessionInterface = Join-Path $root 'ViewModels\IMainWindowSession.cs'
    $mainVm = Join-Path $root 'ViewModels\MainWindowViewModel.cs'

    if (Test-Path -LiteralPath $commands -PathType Leaf) {
        $content = Get-Content -LiteralPath $commands -Raw -Encoding UTF8
        if ($content -match 'public\s+async\s+void\s+ShowRedumpDetails') {
            Add-Failure 'ShowRedumpDetails must not be public async void.'
        }
        if ($content -notmatch 'public\s+async\s+Task\s+ShowRedumpDetails') {
            Add-Failure 'ShowRedumpDetails must return Task.'
        }
    }

    if (Test-Path -LiteralPath $sessionInterface -PathType Leaf) {
        $content = Get-Content -LiteralPath $sessionInterface -Raw -Encoding UTF8
        if ($content -notmatch 'Task\s+ShowRedumpDetails\s*\(') {
            Add-Failure 'IMainWindowSession.ShowRedumpDetails must return Task.'
        }
    }

    if (Test-Path -LiteralPath $session -PathType Leaf) {
        $content = Get-Content -LiteralPath $session -Raw -Encoding UTF8
        if ($content -notmatch 'public\s+Task\s+ShowRedumpDetails\s*\(') {
            Add-Failure 'MainWindow.Session ShowRedumpDetails adapter must return Task.'
        }
    }

    if (Test-Path -LiteralPath $mainVm -PathType Leaf) {
        $content = Get-Content -LiteralPath $mainVm -Raw -Encoding UTF8
        if ($content -notmatch 'new\s+AsyncRelayCommand<TaskQueueItemViewModel\?>\s*\([\s\S]*ShowRedumpDetails') {
            Add-Failure 'ShowRedumpDetailsCommand must use AsyncRelayCommand.'
        }
    }
}

function Test-BinCueConsoleIdentityArchitecture {
    $serviceRoot = Join-Path $root 'Services\ConsoleMedia'
    $requiredFiles = @(
        'ConsoleDiscIdentityResult.cs',
        'ConsoleDiscScanContext.cs',
        'IConsoleDiscIdentityProbe.cs',
        'ConsoleDiscIdentityService.cs'
    )

    foreach ($file in $requiredFiles) {
        $path = Join-Path $serviceRoot $file
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            Add-Failure "BIN/CUE console identity layer file is missing: Services\ConsoleMedia\$file"
        }
    }

    $identityService = Join-Path $serviceRoot 'ConsoleDiscIdentityService.cs'
    if (Test-Path -LiteralPath $identityService -PathType Leaf) {
        $content = Get-Content -LiteralPath $identityService -Raw -Encoding UTF8
        foreach ($probe in @(
            'PlayStationConsoleDiscProbe',
            'SegaSaturnConsoleDiscProbe',
            'SegaMegaCdConsoleDiscProbe',
            'DreamcastConsoleDiscProbe',
            'NeoGeoCdConsoleDiscProbe',
            'ThreeDoConsoleDiscProbe',
            'PcEngineCdConsoleDiscProbe')) {
            if ($content -notmatch $probe) {
                Add-Failure "BIN/CUE console identity probe is missing: $probe"
            }
        }
    }

    $platformDetection = Join-Path $root 'Services\PlatformDetectionService.cs'
    if (Test-Path -LiteralPath $platformDetection -PathType Leaf) {
        $content = Get-Content -LiteralPath $platformDetection -Raw -Encoding UTF8
        if ($content -notmatch 'ConsoleDiscIdentityService\.Shared\.Detect') {
            Add-Failure 'PlatformDetectionService must use ConsoleDiscIdentityService for standalone BIN platform detection.'
        }
    }

    $assembler = Join-Path $root 'Services\BinCueRescue\MultiBinDiscAssembler.cs'
    if (Test-Path -LiteralPath $assembler -PathType Leaf) {
        $content = Get-Content -LiteralPath $assembler -Raw -Encoding UTF8
        if ($content -notmatch 'ConsoleDiscIdentityService\.Shared\.Detect') {
            Add-Failure 'BIN/CUE rescue must verify console identity before generating a temporary CUE.'
        }
        if ($content -notmatch 'BinCueRescueRefusalReason\.UnsupportedPlatform') {
            Add-Failure 'BIN/CUE rescue must refuse standalone BIN when console identity is unknown.'
        }
    }

    $mediaPolicy = Join-Path $root 'Services\MediaInputPolicy\MediaInputPolicy.cs'
    if (-not (Test-Path -LiteralPath $mediaPolicy -PathType Leaf)) {
        Add-Failure 'MediaInputPolicy is required to centralize standalone BIN/CUE intake decisions.'
    }
    else {
        $content = Get-Content -LiteralPath $mediaPolicy -Raw -Encoding UTF8
        foreach ($required in @(
            'FindAdjacentCue',
            'ConsoleDiscIdentityService\.Shared\.Detect',
            'MultiBinDiscAssembler\.AssembleForBin',
            'AcceptTemporaryCue',
            'Block')) {
            if ($content -notmatch $required) {
                Add-Failure "MediaInputPolicy missing required standalone BIN policy marker: $required"
            }
        }
    }

    $planner = Join-Path $root 'Services\ChdWorkflowProfilePlanner.cs'
    if (Test-Path -LiteralPath $planner -PathType Leaf) {
        $content = Get-Content -LiteralPath $planner -Raw -Encoding UTF8
        if ($content -match '"\.bin"\s*=>\s*ChdWorkflowProfilePlan\.Unsupported') {
            Add-Failure 'ChdWorkflowProfilePlanner must not hard-block .bin before MediaInputPolicy/CueRescue can evaluate it.'
        }
        if ($content -notmatch 'PlanStandaloneBinCreate' -or $content -notmatch 'MediaInputPolicy\.Evaluate') {
            Add-Failure 'ChdWorkflowProfilePlanner must route .bin through MediaInputPolicy.'
        }
    }

    $queueIntake = Join-Path $root 'ViewModels\MainWindowViewModel.QueueIntake.cs'
    if (Test-Path -LiteralPath $queueIntake -PathType Leaf) {
        $content = Get-Content -LiteralPath $queueIntake -Raw -Encoding UTF8
        if ($content -notmatch 'MediaInputPolicy\.Evaluate' -or $content -notmatch 'mediaDecision\.EffectivePath') {
            Add-Failure 'Queue intake must apply MediaInputPolicy and use EffectivePath for BIN->CUE redirects.'
        }
    }

    $capability = Join-Path $root 'Services\QueueOperationCapabilityService.cs'
    if (Test-Path -LiteralPath $capability -PathType Leaf) {
        $content = Get-Content -LiteralPath $capability -Raw -Encoding UTF8
        if ($content -notmatch 'MediaInputPolicy\.Evaluate') {
            Add-Failure 'QueueOperationCapabilityService must use MediaInputPolicy before exposing BIN conversion operations.'
        }
    }
}


function Test-ChdmanCapabilityPolicyGates {
    $capabilityService = Join-Path $root 'Services\Conversion\ChdmanCapabilityService.cs'
    if (-not (Test-Path -LiteralPath $capabilityService -PathType Leaf)) {
        Add-Failure 'ChdmanCapabilityService is required to inspect the actual chdman binary before CHD execution.'
    }
    else {
        $content = Get-Content -LiteralPath $capabilityService -Raw -Encoding UTF8
        foreach ($required in @(
            'Version',
            'supportsCreateDvd',
            'supportsExtractDvd',
            'supportsZstd',
            'SupportsRequestedCompression',
            'SupportsRequestedHunkSize',
            'WaitForExitAsync')) {
            if ($content -notmatch $required) {
                Add-Failure "ChdmanCapabilityService missing required capability marker: $required"
            }
        }
    }

    $capabilitySnapshot = Join-Path $root 'Models\Chd\ChdmanCapabilitySnapshot.cs'
    if (-not (Test-Path -LiteralPath $capabilitySnapshot -PathType Leaf)) {
        Add-Failure 'ChdmanCapabilitySnapshot model is required for centralized CHD capability decisions.'
    }
    else {
        $content = Get-Content -LiteralPath $capabilitySnapshot -Raw -Encoding UTF8
        foreach ($required in @(
            'SupportsCreateDvd',
            'SupportsExtractDvd',
            'SupportsZstd',
            'SupportsRequestedCompression',
            'SupportsRequestedHunkSize')) {
            if ($content -notmatch $required) {
                Add-Failure "ChdmanCapabilitySnapshot missing required marker: $required"
            }
        }
    }

    $policyGate = Join-Path $root 'Services\Conversion\ChdOperationPolicyGate.cs'
    if (-not (Test-Path -LiteralPath $policyGate -PathType Leaf)) {
        Add-Failure 'ChdOperationPolicyGate is required to centralize CHD command decisions.'
    }
    else {
        $content = Get-Content -LiteralPath $policyGate -Raw -Encoding UTF8
        foreach ($required in @(
            'PspIsoCreateCdBlocked',
            'Ps2DvdIsoCreateCdBlocked',
            'CreateDvdUnsupported',
            'ExtractDvdUnsupported',
            'ExtractionMetadataRequired',
            'SupportsRequestedCompression',
            'SupportsRequestedHunkSize',
            'UnknownIsoMediaKindRequired')) {
            if ($content -notmatch $required) {
                Add-Failure "ChdOperationPolicyGate missing required safety marker: $required"
            }
        }
    }



    $profilePolicy = Join-Path $root 'Services\Conversion\PlatformAwareChdProfilePolicy.cs'
    if (-not (Test-Path -LiteralPath $profilePolicy -PathType Leaf)) {
        Add-Failure 'PlatformAwareChdProfilePolicy is required to centralize platform-aware CHD profile selection.'
    }
    else {
        $content = Get-Content -LiteralPath $profilePolicy -Raw -Encoding UTF8
        foreach ($required in @(
            'PlatformAwareChdProfileRequest',
            'PlatformAwareChdProfileDecision',
            'ChdProfileMediaKind',
            'TargetEmulatorProfile',
            'ChdProfileUserGoal',
            'PspPpssppIsoCreatedvd2048',
            'Ps2DvdIsoCreatedvd',
            'UnknownIsoMediaKindRequired',
            'ResolveCdProfileSettings',
            'ResolveDvdProfileSettings',
            'ChdDiscHunkIntent')) {
            if ($content -notmatch $required) {
                Add-Failure "PlatformAwareChdProfilePolicy missing required marker: $required"
            }
        }

        if ($content -match 'PreserveRequestedCdHunk' -or $content -match 'PreserveRequestedAdvancedDvdHunk') {
            Add-Failure 'PlatformAwareChdProfilePolicy must use separate CD/DVD profile policies instead of generic hunk preservation helpers.'
        }

        if ($content -match 'Gdi[\s\S]{0,500}2048' -or $content -match 'Cue[\s\S]{0,500}2048') {
            Add-Failure 'PlatformAwareChdProfilePolicy must not apply 2048-byte hunk policy to CD/GDI/BIN+CUE paths.'
        }
    }

    $mediaSpecificPolicy = Join-Path $root 'Services\Conversion\ChdMediaSpecificProfilePolicies.cs'
    if (-not (Test-Path -LiteralPath $mediaSpecificPolicy -PathType Leaf)) {
        Add-Failure 'ChdMediaSpecificProfilePolicies is required to separate CHD CD/DVD compression and hunk decisions internally.'
    }
    else {
        $content = Get-Content -LiteralPath $mediaSpecificPolicy -Raw -Encoding UTF8
        foreach ($required in @(
            'ChdCdCompressionPolicy',
            'ChdDvdCompressionPolicy',
            'ChdCdHunkPolicy',
            'ChdDvdHunkPolicy',
            'CHD CD compression policy',
            'CHD DVD compression policy',
            'CHD CD hunk policy',
            'CHD DVD hunk policy',
            'PspPpssppDvd',
            'Ps2Dvd',
            'requestedHunkSizeBytes == 2048')) {
            if ($content -notmatch [regex]::Escape($required)) {
                Add-Failure "ChdMediaSpecificProfilePolicies missing required marker: $required"
            }
        }

        if ($content -match 'ChdDiscHunkIntent\.Gdi[\s\S]{0,500}PspPpssppCompatibilityHunkSizeBytes' -or
            $content -match 'ChdDiscHunkIntent\.CdDescriptor[\s\S]{0,500}PspPpssppCompatibilityHunkSizeBytes' -or
            $content -match 'ChdDiscHunkIntent\.IsoCd[\s\S]{0,500}PspPpssppCompatibilityHunkSizeBytes') {
            Add-Failure 'DVD hunk policy must not leak PSP/2048 DVD hunk settings into CD/GDI/BIN+CUE paths.'
        }
    }

    $conversion = Join-Path $root 'Services\Conversion\ChdConversionService.cs'
    if (Test-Path -LiteralPath $conversion -PathType Leaf) {
        $content = Get-Content -LiteralPath $conversion -Raw -Encoding UTF8
        foreach ($required in @(
            '_operationPolicyGate',
            'ChdOperationPolicyRequest',
            'extractionMetadataDecisionConfirmed',
            'policyDecision\.IsAllowed',
            '_profilePolicy',
            'PlatformAwareChdProfileRequest')) {
            if ($content -notmatch $required) {
                Add-Failure "ChdConversionService missing centralized CHD policy gate marker: $required"
            }
        }
    }

    $extractionStage = Join-Path $root 'Core\Workflow\WorkflowExtractionStage.cs'
    if (Test-Path -LiteralPath $extractionStage -PathType Leaf) {
        $content = Get-Content -LiteralPath $extractionStage -Raw -Encoding UTF8
        foreach ($required in @(
            'MetadataAwareChdExtractionPolicy',
            'RestoreTargetPolicy',
            'extractionMetadataDecisionConfirmed:\s*true',
            'extractCdCueOutputPath:\s*restoreTarget\.ExtractCdCueOutputPath',
            'verifyExtractCdCueBinContract:\s*restoreTarget\.VerifyExtractCdCueBinContract')) {
            if ($content -notmatch $required) {
                Add-Failure "CHD extraction metadata/restore policy missing workflow marker: $required"
            }
        }
    }

    $metadataExtractionPolicy = Join-Path $root 'Services\Conversion\MetadataAwareChdExtractionPolicy.cs'
    if (-not (Test-Path -LiteralPath $metadataExtractionPolicy -PathType Leaf)) {
        Add-Failure 'MetadataAwareChdExtractionPolicy is required for metadata-based CHD extraction routing.'
    }
    else {
        $content = Get-Content -LiteralPath $metadataExtractionPolicy -Raw -Encoding UTF8
        foreach ($required in @(
            'DvdMetadataExtractDvd',
            'CdMetadataExtractCd',
            'HdMetadataExtractHd',
            'UnknownMetadataBlocked',
            'LegacyCdMetadataToIsoRestore',
            'Wrong-profile / Legacy CHD',
            'ChdRestoreTargetMode\.LegacyCdProfileToIso')) {
            if ($content -notmatch $required) {
                Add-Failure "MetadataAwareChdExtractionPolicy missing required marker: $required"
            }
        }
    }

    $restoreTargetPolicy = Join-Path $root 'Services\Conversion\RestoreTargetPolicy.cs'
    if (-not (Test-Path -LiteralPath $restoreTargetPolicy -PathType Leaf)) {
        Add-Failure 'RestoreTargetPolicy is required for Legacy CD-profile CHD restore targets.'
    }
    else {
        $content = Get-Content -LiteralPath $restoreTargetPolicy -Raw -Encoding UTF8
        foreach ($required in @(
            'LegacyCdProfileToIso',
            'ExtractCdCueOutputPath',
            'ExtractCdBinOutputPath',
            'VerifyExtractCdCueBinContract: false',
            'FinalizationKind: ChdmanExtractionKind.ExtractDvd')) {
            if ($content -notmatch $required) {
                Add-Failure "RestoreTargetPolicy missing required marker: $required"
            }
        }
    }

    $cleanupStage = Join-Path $root 'Core\Workflow\WorkflowCleanupStage.cs'
    if (Test-Path -LiteralPath $cleanupStage -PathType Leaf) {
        $content = Get-Content -LiteralPath $cleanupStage -Raw -Encoding UTF8
        foreach ($required in @(
            'sourceDeletionWasVerified',
            'QueueItemTerminalOutcome\.Healthy',
            'QueueItemTerminalOutcome\.Extracted')) {
            if ($content -notmatch $required) {
                Add-Failure "Source cleanup must remain gated by verified success marker: $required"
            }
        }
    }

    $cleanupPipeline = Join-Path $root 'Core\Workflow\WorkflowSourceCleanupPipeline.cs'
    if (Test-Path -LiteralPath $cleanupPipeline -PathType Leaf) {
        $content = Get-Content -LiteralPath $cleanupPipeline -Raw -Encoding UTF8
        foreach ($required in @(
            '!request\.IsEnabled\s*\|\|\s*!request\.IsVerified',
            'IsVerifiedOutputReadyForCleanup',
            'File\.Delete')) {
            if ($content -notmatch $required) {
                Add-Failure "Source cleanup pipeline must not delete originals before verified success marker: $required"
            }
        }
    }
}


function Test-SafeRecompressPipelinePolicy {
    $pipeline = Join-Path $root 'Services\Conversion\SafeRecompressPipeline.cs'
    if (-not (Test-Path -LiteralPath $pipeline -PathType Leaf)) {
        Add-Failure 'SafeRecompressPipeline is required so CHD recompression uses CHD -> original-like extraction -> platform-aware rebuild.'
    }
    else {
        $content = Get-Content -LiteralPath $pipeline -Raw -Encoding UTF8
        foreach ($required in @(
            'ReadInfoAsync',
            'MetadataAwareChdExtractionPolicy',
            'RestoreTargetPolicy',
            'extractionMetadataDecisionConfirmed:\s*true',
            'extractCdCueOutputPath:\s*restoreTarget\.ExtractCdCueOutputPath',
            'ChdmanExtractionKind\.None',
            'PlatformAwareChdProfilePolicy')) {
            if ($content -notmatch $required) {
                Add-Failure "SafeRecompressPipeline missing required safe recompress marker: $required"
            }
        }
    }

    $conversion = Join-Path $root 'Services\Conversion\ChdConversionService.cs'
    if (Test-Path -LiteralPath $conversion -PathType Leaf) {
        $content = Get-Content -LiteralPath $conversion -Raw -Encoding UTF8
        foreach ($required in @(
            'Direct CHD to CHD recompression was blocked',
            'DirectChdRecompressBlockedMessageKey')) {
            if ($content -notmatch [Regex]::Escape($required)) {
                Add-Failure "ChdConversionService must block direct CHD-to-CHD recompression marker: $required"
            }
        }
    }

    $resultModel = Join-Path $root 'Models\Chd\ChdConversionResult.cs'
    if (Test-Path -LiteralPath $resultModel -PathType Leaf) {
        $content = Get-Content -LiteralPath $resultModel -Raw -Encoding UTF8
        foreach ($required in @(
            'RequestedProfile',
            'ResolvedCommand',
            'ResolvedCompression',
            'ResolvedHunkSize',
            'EffectiveCompression',
            'EffectiveHunkSize',
            'SameAsMameDefault',
            'CompatibilityNotes',
            'ChdmanVersion')) {
            if ($content -notmatch $required) {
                Add-Failure "ChdConversionResult missing phase 5 final report field: $required"
            }
        }
    }

    $reportModel = Join-Path $root 'Models\Chd\ConversionPerformanceReport.cs'
    if (Test-Path -LiteralPath $reportModel -PathType Leaf) {
        $content = Get-Content -LiteralPath $reportModel -Raw -Encoding UTF8
        foreach ($required in @(
            'RequestedProfile',
            'ResolvedCommand',
            'ResolvedCompression',
            'ResolvedHunkSize',
            'EffectiveCompression',
            'EffectiveHunkSize',
            'SameAsMameDefault',
            'CompatibilityNotes',
            'ChdmanVersion')) {
            if ($content -notmatch $required) {
                Add-Failure "ConversionPerformanceReport missing phase 5 final report field: $required"
            }
        }
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

Test-NoAppTextAlignmentReferencesOutsideAppXaml
Test-NoApplyFixScripts
Test-PublishPackagingPolicy
Test-RefactoredFileSizeThresholds
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
Test-DispatcherUnhandledExceptionPolicy
Test-SingleInstanceGuard
Test-OptionsConstructorGuard
Test-ShutdownBackgroundTimeouts
Test-ShowRedumpDetailsReturnsTask
Test-BinCueConsoleIdentityArchitecture
Test-ChdmanCapabilityPolicyGates
Test-SafeRecompressPipelinePolicy
Test-NoVisualBasicSources
Test-NoEmptyExtensionlessUiResourceFiles
Test-NoSedScratchUnderUiResources
Test-RefactorCompositionCompletion
Test-NoPartialRefactorSlicing
Test-OptionsCoordinatorPlacement
Test-NoWpfShellUnderServices
Test-RedumpDetailsViewModelPurity
Test-ChdProgressParserImplementation
Test-ConversionRuntimeReliabilityPolicy
Test-CompressionPresetTruthLayer
Test-CoreServicesDependencyReduction
Test-OptionsWindowEarlyEventSafety
Test-RedumpAutoSyncStartupPolicy

if ($failures.Count -gt 0) {
    Write-Host 'Repository convention verification failed:' -ForegroundColor Red
    $failures | Sort-Object -Unique | ForEach-Object {
        Write-Host " - $_" -ForegroundColor Red
    }

    exit 1
}

Write-Host 'Repository convention verification passed.' -ForegroundColor Green
exit 0
