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
        'bom-ref' = 'pkg:generic/7-zip@26.06'
        name = '7-Zip'
        version = '26.06'
        hashes = @(
            [ordered]@{ alg = 'SHA-256'; content = 'DC0493429FE28136FCB177A0E77C9875CBB3B0BFF5F2F8EEBA4DF302E98CEF76' },
            [ordered]@{ alg = 'SHA-256'; content = '500DB9EB17F3C21B729BA41DD02ECFE1823DA05028C7E4BA129084CC74CC364E' }
        )
        externalReferences = @([ordered]@{ type = 'distribution'; url = 'https://github.com/ip7z/7zip/releases/tag/26.06' })
    },
    [ordered]@{
        type = 'application'
        'bom-ref' = 'pkg:generic/csokit@0.6.6'
        name = 'CsoKit CLI'
        version = '0.6.6'
        hashes = @([ordered]@{ alg = 'SHA-256'; content = 'AE8404614FC44B4917F0662FB7FABEC603351086F4BB2FF89FD4B53E589A0F36' })
        properties = @(
            [ordered]@{ name = 'hakamiq:source-commit'; value = '2f4da904e1420bbb9da2e508415841108414ab0c' },
            [ordered]@{ name = 'hakamiq:runtime-file'; value = 'Tools/hakamiq-cso/win-x64/csokit.exe' }
        )
        externalReferences = @([ordered]@{ type = 'vcs'; url = 'https://github.com/HAKAMIQ/CsoKit/tree/2f4da904e1420bbb9da2e508415841108414ab0c' })
    },
    [ordered]@{
        type = 'library'
        'bom-ref' = 'pkg:generic/csokit-native@0.6.6'
        name = 'CsoKit Native'
        version = '0.6.6'
        hashes = @([ordered]@{ alg = 'SHA-256'; content = 'F37AED7918AA1CFFE68BEDF04128292EFF6607F0E4E0386801C700707F2F388E' })
        properties = @(
            [ordered]@{ name = 'hakamiq:source-commit'; value = '2f4da904e1420bbb9da2e508415841108414ab0c' },
            [ordered]@{ name = 'hakamiq:native-abi'; value = '2' },
            [ordered]@{ name = 'hakamiq:runtime-file'; value = 'Tools/hakamiq-cso/win-x64/CsoKit.Native.dll' }
        )
        externalReferences = @([ordered]@{ type = 'vcs'; url = 'https://github.com/HAKAMIQ/CsoKit/tree/2f4da904e1420bbb9da2e508415841108414ab0c' })
    },
    [ordered]@{
        type = 'application'
        'bom-ref' = 'pkg:generic/igir@5.8.1'
        authors = @([ordered]@{ name = 'Christian Emmer and contributors' })
        name = 'igir'
        version = '5.8.1'
        licenses = @([ordered]@{ license = [ordered]@{ id = 'GPL-3.0-or-later' } })
        hashes = @([ordered]@{ alg = 'SHA-256'; content = 'F3BCD0173C5E79A3DCEC8CC65707007035849920AB362B51F09E133BDBB63DD1' })
        purl = 'pkg:github/emmercm/igir@v5.8.1'
        properties = @(
            [ordered]@{ name = 'hakamiq:optional'; value = 'true' },
            [ordered]@{ name = 'hakamiq:modified'; value = 'false' },
            [ordered]@{ name = 'hakamiq:invocation'; value = 'igir report' },
            [ordered]@{ name = 'hakamiq:runtime-file'; value = 'Tools/igir/win-x64/igir.exe' },
            [ordered]@{ name = 'hakamiq:corresponding-source-file'; value = 'igir-5.8.1-source.tar.gz' }
        )
        externalReferences = @(
            [ordered]@{
                type = 'distribution'
                url = 'https://github.com/emmercm/igir/releases/download/v5.8.1/igir-5.8.1-Windows-x64.zip'
                hashes = @([ordered]@{ alg = 'SHA-256'; content = '48025BACCB4EE564B6DDA4B3AA65C9B90AB01340C2A43773FA506DC880D013F2' })
            },
            [ordered]@{
                type = 'source-distribution'
                url = 'https://github.com/emmercm/igir/archive/refs/tags/v5.8.1.tar.gz'
                hashes = @([ordered]@{ alg = 'SHA-256'; content = '18742ACA59AAD2D562B25645EC6DAE728A9BF234AE597E2BD162ABCC992BEF46' })
            },
            [ordered]@{ type = 'vcs'; url = 'https://github.com/emmercm/igir/tree/v5.8.1' },
            [ordered]@{ type = 'license'; url = 'https://github.com/emmercm/igir/blob/v5.8.1/LICENSE' },
            [ordered]@{ type = 'website'; url = 'https://igir.io/' }
        )
    },
    [ordered]@{
        type = 'application'
        'bom-ref' = 'pkg:generic/xdvdfs@0.8.3'
        authors = @([ordered]@{ name = 'antangelo' })
        name = 'xdvdfs'
        version = '0.8.3'
        licenses = @([ordered]@{ license = [ordered]@{ id = 'MIT' } })
        hashes = @([ordered]@{ alg = 'SHA-256'; content = 'DF5F19954EF706C130C256546BF67CF7725611BDB30D1815577187007A6F907A' })
        purl = 'pkg:github/antangelo/xdvdfs@v0.8.3'
        properties = @(
            [ordered]@{ name = 'hakamiq:modified'; value = 'false' },
            [ordered]@{ name = 'hakamiq:runtime-file'; value = 'Tools/xdvdfs/win-x64/xdvdfs.exe' }
        )
        externalReferences = @(
            [ordered]@{
                type = 'distribution'
                url = 'https://github.com/antangelo/xdvdfs/releases/download/v0.8.3/xdvdfs-windows-1cc850bf1b3487fad7ec7c9eed01d83e8fc75ba4.zip'
                hashes = @([ordered]@{ alg = 'SHA-256'; content = '008B1B3A63F1ECB00D1ED51AB154053FD0F7C41C68E973455BBE77AB989A7CC5' })
            },
            [ordered]@{ type = 'vcs'; url = 'https://github.com/antangelo/xdvdfs/tree/v0.8.3' }
        )
    },
    [ordered]@{
        type = 'application'
        'bom-ref' = 'pkg:generic/iso2god-rs@1.8.1'
        authors = @([ordered]@{ name = 'Ilia Pozdnyakov' })
        name = 'iso2god-rs'
        version = '1.8.1'
        licenses = @([ordered]@{ license = [ordered]@{ id = 'MIT' } })
        hashes = @([ordered]@{ alg = 'SHA-256'; content = '1459FCF8FF5A89E039B02C68C06B006B7A036F3B1CA237C2D7E03D61BD211D47' })
        purl = 'pkg:github/iliazeus/iso2god-rs@v1.8.1'
        properties = @(
            [ordered]@{ name = 'hakamiq:modified'; value = 'false' },
            [ordered]@{ name = 'hakamiq:runtime-file'; value = 'Tools/iso2god/win-x64/iso2god.exe' }
        )
        externalReferences = @(
            [ordered]@{ type = 'distribution'; url = 'https://github.com/iliazeus/iso2god-rs/releases/download/v1.8.1/iso2god-x86_64-windows.exe' },
            [ordered]@{ type = 'vcs'; url = 'https://github.com/iliazeus/iso2god-rs/tree/v1.8.1' }
        )
    },
    [ordered]@{
        type = 'application'
        'bom-ref' = 'pkg:generic/zarchive@0.1.2'
        authors = @([ordered]@{ name = 'Exzap' })
        name = 'ZArchive'
        version = '0.1.2'
        licenses = @([ordered]@{ license = [ordered]@{ id = 'MIT-0' } })
        hashes = @([ordered]@{ alg = 'SHA-256'; content = 'F9BBA11DF7EE120186E013214C14C210690F72AB2940ECC158FC40BF36E78B6C' })
        purl = 'pkg:github/Exzap/ZArchive@v0.1.2'
        properties = @(
            [ordered]@{ name = 'hakamiq:modified'; value = 'false' },
            [ordered]@{ name = 'hakamiq:runtime-file'; value = 'Tools/zarchive/win-x64/zarchive.exe' }
        )
        externalReferences = @(
            [ordered]@{ type = 'distribution'; url = 'https://github.com/Exzap/ZArchive/releases/download/v0.1.2/zarchive.exe' },
            [ordered]@{ type = 'vcs'; url = 'https://github.com/Exzap/ZArchive/tree/v0.1.2' }
        )
    }
)

foreach ($tool in $bundledTools) {
    $components.Add($tool)
    $rootDependencies.Add([string]$tool.'bom-ref')
    [string[]]$toolDependencies = @()
    if ([string]$tool.'bom-ref' -eq 'pkg:generic/csokit@0.6.6') {
        $toolDependencies = @('pkg:generic/csokit-native@0.6.6')
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
