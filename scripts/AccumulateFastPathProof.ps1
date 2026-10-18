#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string[]]$ReportPaths,

    [Parameter(Mandatory = $true)]
    [string]$LedgerPath
)

$ErrorActionPreference = 'Stop'

function Assert-Condition([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

function Normalize-Hex([object]$value, [int]$length, [string]$name) {
    $text = [string]$value
    Assert-Condition ($text -match "^[0-9a-fA-F]{$length}$") "Invalid $name."
    return $text.ToLowerInvariant()
}

function Get-CaseObservation($row, [string]$group, [string]$reportHash) {
    $source = $row.source
    Assert-Condition ($null -ne $source) 'A case has no source hashes.'
    $sha1 = Normalize-Hex $source.sha1 40 'source SHA1'
    $md5 = Normalize-Hex $source.md5 32 'source MD5'
    $crc32 = Normalize-Hex $source.crc32 8 'source CRC32'
    $size = [long]$source.sizeBytes
    Assert-Condition ($size -gt 0) 'A case has no exact source size.'
    $sourcePath = [IO.Path]::GetFullPath([string]$row.sourcePath)
    $chd = $row.chd
    $info = if ($null -ne $chd) { $chd.info } else { $null }
    $rawSha1 = if ($null -ne $info -and $info.rawSha1) {
        Normalize-Hex $info.rawSha1 40 'CHD Raw SHA1'
    } else { $null }
    $tags = @()
    if ($null -ne $info) { $tags = @($info.metadataTags) }
    $dvdMetadata = $tags.Count -eq 1 -and $tags[0] -ceq 'DVD '
    $verifyPass = $null -ne $chd -and $null -ne $chd.verify -and
        $chd.verify.exitCode -eq 0 -and
        ([string]$chd.verify.stdout).Contains('Raw SHA1 verification successful!')
    $proofPass = $null -ne $chd -and $chd.created -eq $true -and
        $dvdMetadata -and $verifyPass -and $rawSha1 -ceq $sha1 -and
        [long]$info.logicalSizeBytes -eq $size -and
        $row.comparisons.sha1EqualsRawSha1 -eq $true -and
        $row.comparisons.sizeEqualsLogicalSize -eq $true

    $redump = $row.redump
    Assert-Condition ($null -ne $redump) 'A case has no Redump lookup result.'
    $hitCount = [int]$redump.ambiguityCount
    Assert-Condition ($hitCount -ge 0) 'Negative Redump ambiguity count.'
    $redumpAgreement = $hitCount -eq 0
    if ($hitCount -eq 1) {
        $redumpAgreement = $redump.sha1SizeUniqueMatch -eq $true -and
            $redump.fullHashMatch -eq $true -and
            [long]$redump.exactSizeBytes -eq $size -and
            ([string]$redump.sha1) -ieq $sha1 -and
            ([string]$redump.md5) -ieq $md5 -and
            ([string]$redump.crc32) -ieq $crc32 -and
            ([string]$redump.system) -ceq $(if ($group -eq 'psp') {
                'Sony - PlayStation Portable'
            } else { 'Sony - PlayStation 2' })
    }

    $classification = $row.classificationEvidence
    $pspSerialPrefix = $null
    $pspStructure = $false
    if ($group -eq 'psp' -and $null -ne $classification) {
        $umd = [string]$classification.umdData
        $pspStructure = $classification.pvd -ceq 'ISO9660' -and
            @($classification.rootEntries) -ccontains 'PSP_GAME' -and
            @($classification.rootEntries) -ccontains 'UMD_DATA.BIN' -and
            $umd -cmatch '^([A-Z]{4})-[0-9]{5}\|'
        if ($pspStructure) { $pspSerialPrefix = $Matches[1] }
    }

    # A PS2 BOOT2 marker establishes platform only. This ledger never treats
    # filename, image size, or createdvd's output tag as source DVD evidence.
    $qualified = $group -eq 'psp' -and $pspStructure -and $proofPass -and $redumpAgreement
    return [pscustomobject]@{
        reportSha256 = $reportHash
        sourcePath = $sourcePath
        platform = $group
        sourceSizeBytes = $size
        sourceSha1 = $sha1
        sourceMd5 = $md5
        sourceCrc32 = $crc32
        pspSerialPrefix = $pspSerialPrefix
        chdRawSha1 = $rawSha1
        chdLogicalSizeBytes = if ($null -ne $info) { $info.logicalSizeBytes } else { $null }
        chdVerifyPass = [bool]$verifyPass
        chdDvdMetadata = [bool]$dvdMetadata
        rawSha1AndSizeProofPass = [bool]$proofPass
        redumpMatchCount = $hitCount
        redumpCanonicalIsoAgreement = [bool]$redumpAgreement
        qualified = [bool]$qualified
        classificationStatus = if ($group -eq 'psp') {
            if ($pspStructure) { 'PSP_UMD_STRUCTURE' } else { 'UNCLASSIFIED' }
        } elseif ($group -eq 'ps2Candidate') {
            if ($row.platform -ceq 'PS2-DVD') { 'PENDING_AUTHORITATIVE_DVD_EVIDENCE' }
            else { 'CD_DVD_AMBIGUOUS' }
        }
        else { 'UNCLASSIFIED' }
    }
}

$ledgerFullPath = [IO.Path]::GetFullPath($LedgerPath)
$ledger = if (Test-Path -LiteralPath $ledgerFullPath) {
    Get-Content -LiteralPath $ledgerFullPath -Raw -Encoding UTF8 | ConvertFrom-Json
} else {
    [pscustomobject]@{
        format = 'HakamiqFastPathEvidenceLedger.v1'
        sourceReports = @()
        cases = @()
    }
}
Assert-Condition ($ledger.format -ceq 'HakamiqFastPathEvidenceLedger.v1') 'Unsupported ledger format.'
$reports = [Collections.Generic.List[object]]::new()
$cases = [Collections.Generic.List[object]]::new()
foreach ($existing in @($ledger.sourceReports)) { $reports.Add($existing) }
foreach ($existing in @($ledger.cases)) {
    if ($existing.platform -ceq 'ps2Dvd' -or $existing.platform -ceq 'ps2Unknown') {
        $existing.platform = 'ps2Candidate'
    }
    $cases.Add($existing)
}

foreach ($reportPath in $ReportPaths) {
    $fullPath = [IO.Path]::GetFullPath($reportPath)
    Assert-Condition ($fullPath -ine $ledgerFullPath) 'The ledger cannot overwrite an input report.'
    Assert-Condition (Test-Path -LiteralPath $fullPath -PathType Leaf) "Missing report: $fullPath"
    $hash = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if (@($reports | Where-Object { $_.sha256 -ceq $hash }).Count -gt 0) { continue }
    $report = Get-Content -LiteralPath $fullPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Condition ($null -ne $report.tooling -and $null -ne $report.psp -and
        $null -ne $report.ps2Dvd -and $null -ne $report.excluded) 'Unsupported report schema.'
    $corpus = [IO.Path]::GetFullPath([string]$report.tooling.corpusRoot).TrimEnd('\', '/') + '\'
    Assert-Condition (-not $ledgerFullPath.StartsWith($corpus, [StringComparison]::OrdinalIgnoreCase)) 'The ledger must be outside the corpus.'
    Assert-Condition (-not $fullPath.StartsWith($corpus, [StringComparison]::OrdinalIgnoreCase)) 'A report must be outside the corpus.'

    foreach ($group in @('psp', 'ps2Dvd', 'excluded')) {
        $rows = if ($group -eq 'excluded') { @($report.excluded) } else { @($report.$group.files) }
        foreach ($row in $rows) {
            $caseGroup = if ($group -eq 'excluded') {
                if ($row.platform -ceq 'PS2-unknown-media') { 'ps2Candidate' } else { 'unclassified' }
            } elseif ($group -eq 'ps2Dvd') { 'ps2Candidate' }
            else { $group }
            $observation = Get-CaseObservation $row $caseGroup $hash
            Assert-Condition ($observation.sourcePath.StartsWith($corpus, [StringComparison]::OrdinalIgnoreCase)) "A report case is outside its corpus: $($observation.sourcePath)"
            $key = $observation.sourceSha1 + ':' + $observation.sourceSizeBytes
            $existingCase = $cases | Where-Object { $_.key -ceq $key } | Select-Object -First 1
            foreach ($prior in $cases) {
                if (@($prior.observations | Where-Object {
                    $_.sourcePath -ieq $observation.sourcePath -and
                    ($_.sourceSha1 -cne $observation.sourceSha1 -or
                     $_.sourceSizeBytes -ne $observation.sourceSizeBytes)
                }).Count -gt 0) {
                    throw "Source path changed identity between runs: $($observation.sourcePath)"
                }
            }
            if ($null -eq $existingCase) {
                $cases.Add([pscustomobject]@{
                    key = $key
                    platform = $caseGroup
                    observations = @($observation)
                })
            } else {
                Assert-Condition ($existingCase.platform -ceq $caseGroup) "Conflicting platform identity for $key"
                $first = @($existingCase.observations)[0]
                Assert-Condition ($first.sourceMd5 -ceq $observation.sourceMd5 -and
                    $first.sourceCrc32 -ceq $observation.sourceCrc32) "Conflicting source hashes for $key"
                $existingCase.observations = @($existingCase.observations) + @($observation)
            }
        }
    }
    $reports.Add([pscustomobject]@{
        path = $fullPath
        sha256 = $hash
        campaignDate = $report.tooling.campaignDate
        corpusRoot = $report.tooling.corpusRoot
    })
}

$pspValid = @($cases | Where-Object {
    $_.platform -ceq 'psp' -and
    @($_.observations | Where-Object { -not $_.qualified }).Count -eq 0
})
$pspOther = @($cases | Where-Object {
    $_.platform -ceq 'psp' -and
    @($_.observations | Where-Object { -not $_.qualified }).Count -gt 0
})
$ps2Pending = @($cases | Where-Object {
    $_.platform -ceq 'ps2Candidate'
})
$prefixes = @($pspValid | ForEach-Object { @($_.observations)[0].pspSerialPrefix } |
    Where-Object { $_ } | Sort-Object -Unique)
$sizes = @($pspValid | ForEach-Object { [long]@($_.observations)[0].sourceSizeBytes })
$ledger | Add-Member -NotePropertyName updatedAtUtc -NotePropertyValue ([DateTimeOffset]::UtcNow.ToString('O')) -Force
$ledger | Add-Member -NotePropertyName sourceReports -NotePropertyValue @($reports.ToArray()) -Force
$ledger | Add-Member -NotePropertyName cases -NotePropertyValue @($cases.ToArray()) -Force
$ledger | Add-Member -NotePropertyName psp -NotePropertyValue ([pscustomobject]@{
    evidenceStatus = if ($pspOther.Count -gt 0) { 'CONTRADICTED OR UNCLASSIFIED' }
        elseif ($pspValid.Count -gt 0) { 'CORPUS_PROVEN / CANDIDATE' }
        else { 'INSUFFICIENT' }
    runtimeFastPathEnabled = $false
    proofLevel = 'RedumpVerification.RawSha1AndSize'
    distinctValidIsoCount = $pspValid.Count
    targetValidIsoCount = 10
    remainingValidIsos = [Math]::Max(0, 10 - $pspValid.Count)
    minimumCountMet = $pspValid.Count -ge 10
    serialRegionPrefixes = $prefixes
    minimumImageBytes = if ($sizes.Count) { ($sizes | Measure-Object -Minimum).Minimum } else { $null }
    maximumImageBytes = if ($sizes.Count) { ($sizes | Measure-Object -Maximum).Maximum } else { $null }
    globalEnablementEligible = $false
}) -Force
$ledger | Add-Member -NotePropertyName ps2Dvd -NotePropertyValue ([pscustomobject]@{
    evidenceStatus = 'INSUFFICIENT'
    runtimeFastPathEnabled = $false
    independentlyClassifiedDvdIsoCount = 0
    pendingMediaClassificationCount = $ps2Pending.Count
    targetValidIsoCount = 10
    remainingValidIsos = 10
    globalEnablementEligible = $false
}) -Force
$ledger | Add-Member -NotePropertyName ps2Cd -NotePropertyValue ([pscustomobject]@{
    dvdRawSha1FastPathAllowed = $false
}) -Force

$directory = Split-Path -Parent $ledgerFullPath
if (-not (Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory | Out-Null }
$temporary = $ledgerFullPath + '.tmp-' + [guid]::NewGuid().ToString('N')
try {
    $json = ($ledger | ConvertTo-Json -Depth 100).Replace("`r`n", "`n")
    [IO.File]::WriteAllText($temporary, $json + "`n", [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporary -Destination $ledgerFullPath -Force
} finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
}
Write-Output "PSP: $($ledger.psp.evidenceStatus), $($pspValid.Count)/10 distinct valid ISOs."
Write-Output "PS2-DVD: INSUFFICIENT, 0/10 independently classified DVD ISOs; $($ps2Pending.Count) pending."
Write-Output "Ledger: $ledgerFullPath"
