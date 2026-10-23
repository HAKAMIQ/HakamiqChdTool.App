[CmdletBinding()]
param(
    [string]$LockFile,
    [string]$OutputPath
)

if ($PSVersionTable.PSVersion.Major -lt 7) {
    $pwshCommand = Get-Command pwsh.exe -ErrorAction SilentlyContinue
    if ($null -eq $pwshCommand) {
        throw "SBOM generation requires PowerShell 7 (pwsh)."
    }

    $forwardArguments = @(
        '-NoProfile',
        '-File',
        $PSCommandPath
    )

    if ($PSBoundParameters.ContainsKey('LockFile')) {
        $forwardArguments += @('-LockFile', $LockFile)
    }

    if ($PSBoundParameters.ContainsKey('OutputPath')) {
        $forwardArguments += @('-OutputPath', $OutputPath)
    }

    & $pwshCommand.Source @forwardArguments
    $pwshExitCode = $LASTEXITCODE

    if ($pwshExitCode -ne 0) {
        throw "PowerShell 7 SBOM generation failed with exit code $pwshExitCode."
    }

    return
}


$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($LockFile)) {
    $LockFile = Join-Path $PSScriptRoot '..\packages.lock.json'
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $PSScriptRoot '..\docs\sbom.cdx.json'
}

$resolvedLockFile = [System.IO.Path]::GetFullPath($LockFile)
$resolvedOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

$applicationProject = Join-Path $repositoryRoot 'HakamiqChdTool.App.csproj'

if (-not (Test-Path -LiteralPath $applicationProject -PathType Leaf)) {
    throw "Application project file was not found: $applicationProject"
}

[xml] $applicationProjectXml = Get-Content -LiteralPath $applicationProject -Raw
$applicationVersionNodes = @($applicationProjectXml.SelectNodes('/Project/PropertyGroup/Version'))

if ($applicationVersionNodes.Count -ne 1) {
    throw "Application project must declare exactly one Version property."
}

$applicationVersion = $applicationVersionNodes[0].InnerText.Trim()

if ([string]::IsNullOrWhiteSpace($applicationVersion)) {
    throw "Application Version property must not be empty."
}

if (-not (Test-Path -LiteralPath $resolvedLockFile -PathType Leaf)) {
    throw "NuGet lock file was not found: $resolvedLockFile"
}

$lock = Get-Content -LiteralPath $resolvedLockFile -Raw | ConvertFrom-Json
$target = $lock.dependencies.PSObject.Properties | Select-Object -First 1
if ($null -eq $target) {
    throw 'NuGet lock file does not contain a target framework.'
}

function Convert-Base64Sha512ToHex([string]$Value) {
    $bytes = [Convert]::FromBase64String($Value)
    return ([BitConverter]::ToString($bytes)).Replace('-', '')
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
    $dependenciesProperty = $package.PSObject.Properties['dependencies']
    if ($null -ne $dependenciesProperty -and $null -ne $dependenciesProperty.Value) {
        foreach ($dependencyProperty in ($dependenciesProperty.Value.PSObject.Properties | Sort-Object Name)) {
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
        'bom-ref' = 'pkg:generic/csokit@0.6.3'
        name = 'CsoKit CLI'
        version = '0.6.3'
        hashes = @([ordered]@{ alg = 'SHA-256'; content = '77F0662A408BD5DA2241898E41C074B4E600DE9FA58D115C8DBBDF6FFF3EEFD9' })
        properties = @(
            [ordered]@{ name = 'hakamiq:source-commit'; value = 'e1a4bca67207e5bb6c2c4b1dd043418a1ee2a31d' },
            [ordered]@{ name = 'hakamiq:runtime-file'; value = 'Tools/hakamiq-cso/win-x64/csokit.exe' }
        )
        externalReferences = @([ordered]@{ type = 'vcs'; url = 'https://github.com/HAKAMIQ/CsoKit/tree/e1a4bca67207e5bb6c2c4b1dd043418a1ee2a31d' })
    },
    [ordered]@{
        type = 'library'
        'bom-ref' = 'pkg:generic/csokit-native@0.6.3'
        name = 'CsoKit Native'
        version = '0.6.3'
        hashes = @([ordered]@{ alg = 'SHA-256'; content = 'E9991ADDF7FAFAEC400D124CEF7D0FC2B276CE7624261369A1EA1F8B7ECE57E0' })
        properties = @(
            [ordered]@{ name = 'hakamiq:source-commit'; value = 'e1a4bca67207e5bb6c2c4b1dd043418a1ee2a31d' },
            [ordered]@{ name = 'hakamiq:native-abi'; value = '2' },
            [ordered]@{ name = 'hakamiq:runtime-file'; value = 'Tools/hakamiq-cso/win-x64/CsoKit.Native.dll' }
        )
        externalReferences = @([ordered]@{ type = 'vcs'; url = 'https://github.com/HAKAMIQ/CsoKit/tree/e1a4bca67207e5bb6c2c4b1dd043418a1ee2a31d' })
    },
    [ordered]@{
        type = 'application'
        'bom-ref' = 'pkg:generic/igir@5.7.2'
        authors = @([ordered]@{ name = 'Christian Emmer and contributors' })
        name = 'igir'
        version = '5.7.2'
        licenses = @([ordered]@{ license = [ordered]@{ id = 'GPL-3.0-or-later' } })
        hashes = @([ordered]@{ alg = 'SHA-256'; content = '716ECBD7033081FC000A51FBD0588FEE6C1F1E95DAB1EFD6443324BC73597511' })
        purl = 'pkg:github/emmercm/igir@v5.7.2'
        properties = @(
            [ordered]@{ name = 'hakamiq:optional'; value = 'true' },
            [ordered]@{ name = 'hakamiq:modified'; value = 'false' },
            [ordered]@{ name = 'hakamiq:invocation'; value = 'igir report' },
            [ordered]@{ name = 'hakamiq:runtime-file'; value = 'Tools/igir/win-x64/igir.exe' },
            [ordered]@{ name = 'hakamiq:corresponding-source-file'; value = 'igir-5.7.2-source.tar.gz' }
        )
        externalReferences = @(
            [ordered]@{
                type = 'distribution'
                url = 'https://github.com/emmercm/igir/releases/download/v5.7.2/igir-5.7.2-Windows-x64.zip'
                hashes = @([ordered]@{ alg = 'SHA-256'; content = '7E448AE8E34377A8829E33C949B07A09613CB3A0DAD2B4325CA9BB52CF8D7584' })
            },
            [ordered]@{
                type = 'source-distribution'
                url = 'https://github.com/emmercm/igir/archive/refs/tags/v5.7.2.tar.gz'
                hashes = @([ordered]@{ alg = 'SHA-256'; content = 'EA5341ED3A40054BFBF2F6D26F6C76902C6D5EC949D66D38692218656245875B' })
            },
            [ordered]@{ type = 'vcs'; url = 'https://github.com/emmercm/igir/tree/v5.7.2' },
            [ordered]@{ type = 'license'; url = 'https://github.com/emmercm/igir/blob/v5.7.2/LICENSE' },
            [ordered]@{ type = 'website'; url = 'https://igir.io/' }
        )
    }
)

foreach ($tool in $bundledTools) {
    $components.Add($tool)
    $rootDependencies.Add([string]$tool.'bom-ref')
    [string[]]$toolDependencies = @()
    if ([string]$tool.'bom-ref' -eq 'pkg:generic/csokit@0.6.3') {
        $toolDependencies = @('pkg:generic/csokit-native@0.6.3')
    }

    $dependencyGraph.Add([ordered]@{
        ref = [string]$tool.'bom-ref'
        dependsOn = @($toolDependencies)
    })
}

$applicationRef = "pkg:generic/hakamiq-chd-tool@$applicationVersion"
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
            version = $applicationVersion
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

$json = $bom | ConvertTo-Json -Depth 20 -Compress
[System.IO.File]::WriteAllText(
    $resolvedOutputPath,
    $json + [Environment]::NewLine,
    [System.Text.UTF8Encoding]::new($false))

Write-Host "CycloneDX 1.7 SBOM generated: $resolvedOutputPath"
