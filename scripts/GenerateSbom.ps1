[CmdletBinding()]
param(
    [string]$LockFile = (Join-Path $PSScriptRoot '..\packages.lock.json'),
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\docs\sbom.cdx.json')
)

$ErrorActionPreference = 'Stop'

$resolvedLockFile = [System.IO.Path]::GetFullPath($LockFile)
$resolvedOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

if (-not (Test-Path -LiteralPath $resolvedLockFile -PathType Leaf)) {
    throw "NuGet lock file was not found: $resolvedLockFile"
}

$lock = Get-Content -LiteralPath $resolvedLockFile -Raw | ConvertFrom-Json -Depth 100
$target = $lock.dependencies.PSObject.Properties | Select-Object -First 1
if ($null -eq $target) {
    throw 'NuGet lock file does not contain a target framework.'
}

function Convert-Base64Sha512ToHex([string]$Value) {
    $bytes = [Convert]::FromBase64String($Value)
    return [Convert]::ToHexString($bytes)
}

function New-PackageUrl([string]$Name, [string]$Version) {
    $escapedName = [Uri]::EscapeDataString($Name)
    $escapedVersion = [Uri]::EscapeDataString($Version)
    return "pkg:nuget/$escapedName@$escapedVersion"
}

$components = [System.Collections.Generic.List[object]]::new()
$dependencyGraph = [System.Collections.Generic.List[object]]::new()
$rootDependencies = [System.Collections.Generic.List[string]]::new()

foreach ($packageProperty in ($target.Value.PSObject.Properties | Sort-Object Name)) {
    $package = $packageProperty.Value
    $name = $packageProperty.Name
    $version = [string]$package.resolved
    $bomRef = New-PackageUrl $name $version

    $component = [ordered]@{
        type = 'library'
        'bom-ref' = $bomRef
        name = $name
        version = $version
        hashes = @(
            [ordered]@{
                alg = 'SHA-512'
                content = Convert-Base64Sha512ToHex ([string]$package.contentHash)
            }
        )
        purl = $bomRef
        properties = @(
            [ordered]@{
                name = 'hakamiq:nuget-dependency-type'
                value = [string]$package.type
            }
        )
    }

    $components.Add($component)

    if ([string]$package.type -eq 'Direct') {
        $rootDependencies.Add($bomRef)
    }

    $dependsOn = [System.Collections.Generic.List[string]]::new()
    if ($null -ne $package.dependencies) {
        foreach ($dependencyProperty in ($package.dependencies.PSObject.Properties | Sort-Object Name)) {
            $resolvedDependency = $target.Value.PSObject.Properties[$dependencyProperty.Name]
            if ($null -ne $resolvedDependency) {
                $dependsOn.Add((New-PackageUrl $dependencyProperty.Name ([string]$resolvedDependency.Value.resolved)))
            }
        }
    }

    $dependencyGraph.Add([ordered]@{
        ref = $bomRef
        dependsOn = @($dependsOn)
    })
}

$bundledTools = @(
    [ordered]@{
        type = 'application'
        'bom-ref' = 'pkg:generic/mame-chdman@0.289'
        name = 'MAME chdman'
        version = '0.289'
        hashes = @([ordered]@{ alg = 'SHA-256'; content = '8A74468E3B0879698835B57C3B58E88E5A51E4DE73BEE6EF755C28530B5B040F' })
        externalReferences = @([ordered]@{ type = 'distribution'; url = 'https://github.com/mamedev/mame/releases/tag/mame0289' })
    },
    [ordered]@{
        type = 'application'
        'bom-ref' = 'pkg:generic/7-zip@26.02'
        name = '7-Zip'
        version = '26.02'
        hashes = @(
            [ordered]@{ alg = 'SHA-256'; content = '83967F1B02B43C4EFEDA302795722C809E0E81B8307DE73558D10484D5676A7D' },
            [ordered]@{ alg = 'SHA-256'; content = '69FD4DF057985C40E510E2FAC182881C7F85E90AA13EC703F763A8FDB2CE61F8' }
        )
        externalReferences = @([ordered]@{ type = 'distribution'; url = 'https://github.com/ip7z/7zip/releases/tag/26.02' })
    },
    [ordered]@{
        type = 'application'
        'bom-ref' = 'pkg:generic/csokit@0.6.1'
        name = 'CsoKit CLI'
        version = '0.6.1'
        hashes = @([ordered]@{ alg = 'SHA-256'; content = 'FB1BF1E6BD0C51CAB54F505E7E44404F1E5CBFBFF3CB0FFC7EEC159D7D9254C0' })
        properties = @(
            [ordered]@{ name = 'hakamiq:source-commit'; value = '9e2a93d5502fa651f9a21d9dd97269e7c4912c48' },
            [ordered]@{ name = 'hakamiq:runtime-file'; value = 'Tools/hakamiq-cso/win-x64/csokit.exe' }
        )
        externalReferences = @([ordered]@{ type = 'vcs'; url = 'https://github.com/HAKAMIQ/CsoKit/tree/9e2a93d5502fa651f9a21d9dd97269e7c4912c48' })
    },
    [ordered]@{
        type = 'library'
        'bom-ref' = 'pkg:generic/csokit-native@0.6.1'
        name = 'CsoKit Native'
        version = '0.6.1'
        hashes = @([ordered]@{ alg = 'SHA-256'; content = 'B396B0CA41BE7F905E8EA73C285C1F5089C8DA4FB1E4C157775BF198B1F70589' })
        properties = @(
            [ordered]@{ name = 'hakamiq:source-commit'; value = '9e2a93d5502fa651f9a21d9dd97269e7c4912c48' },
            [ordered]@{ name = 'hakamiq:native-abi'; value = '2' },
            [ordered]@{ name = 'hakamiq:runtime-file'; value = 'Tools/hakamiq-cso/win-x64/CsoKit.Native.dll' }
        )
        externalReferences = @([ordered]@{ type = 'vcs'; url = 'https://github.com/HAKAMIQ/CsoKit/tree/9e2a93d5502fa651f9a21d9dd97269e7c4912c48' })
    }
)

foreach ($tool in $bundledTools) {
    $components.Add($tool)
    $rootDependencies.Add([string]$tool.'bom-ref')
    $toolDependencies = if ([string]$tool.'bom-ref' -eq 'pkg:generic/csokit@0.6.1') {
        @('pkg:generic/csokit-native@0.6.1')
    }
    else {
        @()
    }
    $dependencyGraph.Add([ordered]@{ ref = [string]$tool.'bom-ref'; dependsOn = $toolDependencies })
}

$applicationRef = 'pkg:generic/hakamiq-chd-tool@1.2.0'
$dependencyGraph.Insert(0, [ordered]@{
    ref = $applicationRef
    dependsOn = @($rootDependencies | Sort-Object -Unique)
})

$bom = [ordered]@{
    '$schema' = 'https://cyclonedx.org/schema/bom-1.7.schema.json'
    bomFormat = 'CycloneDX'
    specVersion = '1.7'
    version = 1
    metadata = [ordered]@{
        lifecycles = @([ordered]@{ phase = 'build' })
        tools = [ordered]@{
            components = @(
                [ordered]@{
                    type = 'application'
                    name = 'Hakamiq deterministic SBOM generator'
                    version = '1.0'
                }
            )
        }
        component = [ordered]@{
            type = 'application'
            'bom-ref' = $applicationRef
            name = 'Hakamiq CHD Tool'
            version = '1.2.0'
            licenses = @([ordered]@{ license = [ordered]@{ id = 'MIT' } })
        }
    }
    components = @($components)
    dependencies = @($dependencyGraph)
}

$outputDirectory = Split-Path -Parent $resolvedOutputPath
if (-not (Test-Path -LiteralPath $outputDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $outputDirectory | Out-Null
}

$json = $bom | ConvertTo-Json -Depth 20
[System.IO.File]::WriteAllText(
    $resolvedOutputPath,
    $json + [Environment]::NewLine,
    [System.Text.UTF8Encoding]::new($false))

Write-Host "CycloneDX 1.7 SBOM generated: $resolvedOutputPath"
