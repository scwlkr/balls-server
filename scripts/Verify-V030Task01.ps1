param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

$threatModelPath = Join-Path $RepositoryRoot 'docs\security\v0.3.0-threat-model.md'
$operationMatrixPath = Join-Path $RepositoryRoot 'docs\security\v0.3.0-operation-matrix.md'
$traceabilityPath = Join-Path $RepositoryRoot 'docs\security\v0.3.0-traceability.md'
$specPath = Join-Path $RepositoryRoot '.scratch\v0.3.0\spec.md'
$roadmapPath = Join-Path $RepositoryRoot 'docs\roadmap\v0.3.0.md'

$requiredThreatHeadings = @(
    '## Trust zones',
    '## Protected assets',
    '## Data flows',
    '## Attacker capabilities',
    '## Assumptions and non-goals',
    '## Residual risks',
    '## Permanent refusal invariants',
    '## Research reconciliation'
)

$requiredColumns = @(
    'Operation ID',
    'Operation',
    'Consent',
    'Authorization',
    'Least privilege',
    'Idempotency',
    'Ownership proof',
    'Verification',
    'Partial failure',
    'Rollback',
    'Manual recovery',
    'Audit / redaction',
    'Refusal behavior'
)

$requiredOperationNames = [ordered]@{
    'OP-01' = 'Draft provisional preview'
    'OP-02' = 'Authoritative consent'
    'OP-03' = 'Initialize protected ledger'
    'OP-04' = 'Recover protected ledger'
    'OP-05' = 'Validate host prerequisites'
    'OP-06' = 'Validate managed folder'
    'OP-07' = 'Create product access group'
    'OP-08' = 'Apply managed-folder ACE'
    'OP-09' = 'Create managed share'
    'OP-10' = 'Create private LAN firewall rule'
    'OP-11' = 'Create private Tailscale firewall rule'
    'OP-12' = 'Create disabled access grant'
    'OP-13' = 'Activate transferred access grant'
    'OP-14' = 'Rotate access grant'
    'OP-15' = 'Revoke access grant'
    'OP-16' = 'Close attributable SMB sessions'
    'OP-17' = 'Return one setup-code secret response'
    'OP-18' = 'Display setup code once'
    'OP-19' = 'Destroy setup code on Hide or timeout'
    'OP-20' = 'Write setup code to clipboard'
    'OP-21' = 'Conditionally clear product clipboard value'
    'OP-22' = 'Render setup-code QR in memory'
    'OP-23' = 'Repair owned host configuration'
    'OP-24' = 'Remove owned host configuration'
    'OP-25' = 'Hand off Tailscale installation or sign-in'
    'OP-26' = 'Parse initial setup code'
    'OP-27' = 'Import endpoint-update bundle'
    'OP-28' = 'Inspect selected endpoint and collisions'
    'OP-29' = 'Switch selected endpoint'
    'OP-30' = 'Run advanced IP transport diagnostic'
    'OP-31' = 'Perform one-shot authentication'
    'OP-32' = 'Verify managed-folder access'
    'OP-33' = 'Save exact provider credential'
    'OP-34' = 'Delete exact provider credential'
    'OP-35' = 'Map selected drive'
    'OP-36' = 'Unmap selected drive'
    'OP-37' = 'Persist reconnect profile choice'
    'OP-38' = 'Clean up verification file'
    'OP-39' = 'Purge expired non-secret retention records'
}
$requiredOperations = @($requiredOperationNames.Keys)

$requiredOperationPhrases = @{
    'OP-05' = @('guest, anonymous, or blank-password SMB')
    'OP-09' = @('no guest, anonymous, blank-password, or Everyone access')
    'OP-12' = @('at least 32 cryptographically random bytes', 'exactly one policy attempt per explicit owner action')
    'OP-31' = @('guest, anonymous, or blank-password authentication')
}

$allOperationTargets = @(1..39 | ForEach-Object { 'OP-{0:D2}' -f $_ })
$requiredTraceTargets = [ordered]@{
    'Story 01' = @('OP-01')
    'Story 02' = @('OP-02')
    'Story 03' = @('OP-02')
    'Story 04' = @('OP-01', 'OP-02')
    'Story 05' = @('OP-02')
    'Story 06' = @('OP-02', 'OP-23')
    'Story 07' = @('NO-11')
    'Story 08' = @('OP-02')
    'Story 09' = @('OP-02')
    'Story 10' = @('OP-02')
    'Story 11' = @('OP-02')
    'Story 12' = @('OP-03', 'OP-04')
    'Story 13' = @('OP-02', 'NO-08')
    'Story 14' = @('OP-06')
    'Story 15' = @('OP-06')
    'Story 16' = @('NO-06', 'OP-08')
    'Story 17' = @('OP-08')
    'Story 18' = @('OP-09')
    'Story 19' = @('OP-09', 'NO-12')
    'Story 20' = @('OP-09', 'OP-32')
    'Story 21' = @('NO-06', 'OP-24')
    'Story 22' = @('NO-01', 'OP-05')
    'Story 23' = @('OP-05')
    'Story 24' = @('NO-01', 'OP-05')
    'Story 25' = @('NO-03', 'OP-10', 'OP-11')
    'Story 26' = @('OP-10')
    'Story 27' = @('OP-11')
    'Story 28' = @('OP-05', 'OP-23')
    'Story 29' = @('NO-01', 'NO-03')
    'Story 30' = @('NO-04', 'OP-25')
    'Story 31' = @('OP-23')
    'Story 32' = @('OP-03')
    'Story 33' = @('OP-03')
    'Story 34' = @('OP-23')
    'Story 35' = @('OP-23')
    'Story 36' = @('OP-02')
    'Story 37' = @('OP-23')
    'Story 38' = @('OP-23')
    'Story 39' = @('OP-24', 'OP-39')
    'Story 40' = @('OP-12')
    'Story 41' = @('OP-12')
    'Story 42' = @('NO-02', 'OP-05')
    'Story 43' = @('OP-12')
    'Story 44' = @('OP-12')
    'Story 45' = @('OP-05', 'OP-14')
    'Story 46' = @('OP-15')
    'Story 47' = @('OP-14')
    'Story 48' = @('OP-16')
    'Story 49' = @('OP-17', 'OP-18', 'OP-26')
    'Story 50' = @('OP-18')
    'Story 51' = @('OP-18', 'OP-19')
    'Story 52' = @('OP-14', 'OP-19')
    'Story 53' = @('OP-17')
    'Story 54' = @('NO-08')
    'Story 55' = @('OP-20', 'OP-22')
    'Story 56' = @('OP-21')
    'Story 57' = @('OP-17', 'OP-19')
    'Story 58' = @('OP-03', 'OP-29')
    'Story 59' = @('OP-05', 'OP-28')
    'Story 60' = @('OP-28', 'OP-29')
    'Story 61' = @('OP-28', 'OP-31')
    'Story 62' = @('NO-07', 'OP-27', 'OP-29')
    'Story 63' = @('OP-27', 'OP-29')
    'Story 64' = @('NO-07', 'OP-30')
    'Story 65' = @('NO-07', 'NO-04')
    'Story 66' = @('OP-26', 'OP-28')
    'Story 67' = @('OP-31', 'NO-12')
    'Story 68' = @('OP-31')
    'Story 69' = @('OP-33')
    'Story 70' = @('OP-37')
    'Story 71' = @('OP-28', 'OP-35')
    'Story 72' = @('OP-36', 'OP-34')
    'Story 73' = @('OP-32', 'OP-38')
    'Story 74' = @('OP-15', 'OP-34', 'OP-36')
    'Story 75' = @('OP-01', 'OP-02', 'OP-03', 'OP-32', 'OP-23', 'OP-18', 'OP-35', 'OP-14', 'OP-15', 'OP-24')
    'Story 76' = @('NO-08')
    'Story 77' = @('NO-08')
    'Story 78' = @('NO-08')
    'Story 79' = @('TB-06') + $allOperationTargets
    'Story 80' = @('OP-23', 'OP-24')
    'Story 81' = @('TB-01')
    'Story 82' = @('TB-02')
    'Story 83' = @('TB-03')
    'Story 84' = @('TB-04')
    'Story 85' = @('TB-05')
    'Story 86' = @('NO-09', 'OA-01')
    'Exit 01' = $allOperationTargets
    'Exit 02' = @('NO-08')
    'Exit 03' = @('OP-23', 'OP-24', 'OP-35', 'NO-12')
    'Exit 04' = $allOperationTargets
    'Exit 05' = @('TB-07')
    'Exit 06' = @('OA-02')
}

$requiredTraceSummaries = @{
    'Story 07' = 'Host Dashboard remains unelevated for ordinary viewing and diagnostics in every later milestone.'
    'Story 13' = 'Helper returns typed bounded results while redaction prevents raw command and secret disclosure.'
    'Story 43' = 'Each password uses at least 32 cryptographically random bytes and exactly one policy attempt per explicit owner action.'
    'Story 75' = 'Audit covers preview, consent, authorization, mutation, verification, rollback, repair, credential-display acknowledgement, mapping, rotation, revocation, and removal.'
    'Story 79' = 'Every operation''s partial-failure path has a specific automated rollback rule or exact manual recovery instruction.'
    'Exit 01' = 'Every proposed operation and mutation is accounted for with its least required privilege.'
    'Exit 04' = 'Every operation defines consent, idempotency, ownership, verification, and recovery.'
}

$explicitNonOperationTraceIds = @(
    'Story 07', 'Story 13', 'Story 16', 'Story 21', 'Story 22', 'Story 24', 'Story 25', 'Story 29',
    'Story 30', 'Story 42', 'Story 54', 'Story 62', 'Story 64', 'Story 65', 'Story 76', 'Story 77',
    'Story 78', 'Story 86', 'Exit 02'
)
$testBoundaryTraceIds = @('Story 79', 'Story 81', 'Story 82', 'Story 83', 'Story 84', 'Story 85', 'Exit 05')

$errors = [System.Collections.Generic.List[string]]::new()

function Read-RequiredFile {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        $errors.Add("Missing required artifact: $Path")
        return $null
    }

    return Get-Content -LiteralPath $Path -Raw
}

$threatModel = Read-RequiredFile $threatModelPath
$operationMatrix = Read-RequiredFile $operationMatrixPath
$traceability = Read-RequiredFile $traceabilityPath
$spec = Read-RequiredFile $specPath
$roadmap = Read-RequiredFile $roadmapPath

if ($null -ne $threatModel) {
    foreach ($heading in $requiredThreatHeadings) {
        if ($threatModel -notmatch "(?m)^$([regex]::Escape($heading))\s*$") {
            $errors.Add("Threat model is missing required heading: $heading")
        }
    }

    if ($threatModel -notmatch [regex]::Escape('guest, anonymous, and blank-password SMB')) {
        $errors.Add('Threat model must explicitly refuse guest, anonymous, and blank-password SMB.')
    }
}

$matrixRows = @{}
if ($null -ne $operationMatrix) {
    $headerLine = @($operationMatrix -split "`r?`n" | Where-Object { $_ -match '^\|\s*Operation ID\s*\|' })
    if ($headerLine.Count -ne 1) {
        $errors.Add('Operation matrix must contain exactly one canonical header row.')
    }
    else {
        $actualColumns = @($headerLine[0].Trim().Trim('|').Split('|') | ForEach-Object { $_.Trim() })
        if (($actualColumns -join '|') -ne ($requiredColumns -join '|')) {
            $errors.Add('Operation matrix columns do not match the required completion properties.')
        }
    }

    foreach ($line in ($operationMatrix -split "`r?`n")) {
        if ($line -notmatch '^\|\s*(OP-\d{2})\s*\|') {
            continue
        }

        $cells = @($line.Trim().Trim('|').Split('|') | ForEach-Object { $_.Trim() })
        $operationId = $cells[0]
        if ($matrixRows.ContainsKey($operationId)) {
            $errors.Add("Duplicate operation matrix row: $operationId")
            continue
        }

        $matrixRows[$operationId] = $cells
        if ($cells.Count -ne $requiredColumns.Count) {
            $errors.Add("$operationId has $($cells.Count) cells; expected $($requiredColumns.Count).")
            continue
        }

        for ($columnIndex = 1; $columnIndex -lt $cells.Count; $columnIndex++) {
            $cell = $cells[$columnIndex]
            if ($cell.Length -lt 18 -or $cell -match '^(?:-|—|N/?A|None|See\b.*)$') {
                $errors.Add("$operationId has a non-substantive $($requiredColumns[$columnIndex]) cell.")
            }
        }

        if ($cells.Count -eq $requiredColumns.Count -and $cells[1] -ne $requiredOperationNames[$operationId]) {
            $errors.Add("$operationId is named '$($cells[1])'; expected '$($requiredOperationNames[$operationId])'.")
        }

        if ($requiredOperationPhrases.ContainsKey($operationId)) {
            $rowText = $cells -join ' '
            foreach ($requiredPhrase in $requiredOperationPhrases[$operationId]) {
                if ($rowText -notmatch [regex]::Escape($requiredPhrase)) {
                    $errors.Add("$operationId is missing required semantic contract: $requiredPhrase")
                }
            }
        }
    }

    foreach ($operationId in $requiredOperations) {
        if (-not $matrixRows.ContainsKey($operationId)) {
            $errors.Add("Missing operation matrix row: $operationId")
        }
    }

    foreach ($operationId in $matrixRows.Keys) {
        if ($operationId -notin $requiredOperations) {
            $errors.Add("Unexpected operation matrix row: $operationId")
        }
    }
}

$specStoryIds = @()
if ($null -ne $spec) {
    $specStoryIds = @([regex]::Matches($spec, '(?m)^(\d{1,2})\. As an? ') | ForEach-Object { [int]$_.Groups[1].Value })
    if (($specStoryIds -join ',') -ne ((1..86) -join ',')) {
        $errors.Add('Specification stories are not the exact ordered set 1 through 86.')
    }
}

$roadmapExitChecks = @()
if ($null -ne $roadmap) {
    $exitSection = [regex]::Match($roadmap, '(?ms)^## Exit checks\s*(.*?)^## Evidence')
    if (-not $exitSection.Success) {
        $errors.Add('Roadmap Exit checks section could not be parsed.')
    }
    else {
        $roadmapExitChecks = @([regex]::Matches($exitSection.Groups[1].Value, '(?m)^- \[[ xX]\] (.+)$') | ForEach-Object { $_.Groups[1].Value.Trim() })
        if ($roadmapExitChecks.Count -ne 6) {
            $errors.Add("Roadmap has $($roadmapExitChecks.Count) exit checks; expected 6.")
        }
    }
}

if ($null -ne $traceability) {
    $allAnchors = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($document in @($threatModel, $operationMatrix, $traceability)) {
        if ($null -eq $document) {
            continue
        }

        foreach ($anchorMatch in [regex]::Matches($document, '<a id="([a-z]{2}-\d{2})"></a>', 'IgnoreCase')) {
            [void]$allAnchors.Add($anchorMatch.Groups[1].Value.ToUpperInvariant())
        }
    }

    $traceRows = @{}
    foreach ($line in ($traceability -split "`r?`n")) {
        if ($line -notmatch '^\|\s*((?:Story|Exit) \d{2})\s*\|') {
            continue
        }

        $cells = @($line.Trim().Trim('|').Split('|') | ForEach-Object { $_.Trim() })
        if ($cells.Count -ne 4) {
            $errors.Add("Traceability row $($Matches[1]) must contain exactly four cells.")
            continue
        }

        $sourceId = $cells[0]
        if ($traceRows.ContainsKey($sourceId)) {
            $errors.Add("Duplicate traceability row: $sourceId")
            continue
        }

        $traceRows[$sourceId] = $cells
        if ($cells[1].Length -lt 18) {
            $errors.Add("$sourceId has a non-substantive source summary.")
        }

        $expectedDisposition = if ($sourceId -eq 'Exit 06') {
            'Later owner acceptance'
        }
        elseif ($sourceId -in $explicitNonOperationTraceIds) {
            'Explicit non-operation'
        }
        elseif ($sourceId -in $testBoundaryTraceIds) {
            'Test boundary'
        }
        else {
            'Operation'
        }
        if ($cells[2] -ne $expectedDisposition) {
            $errors.Add("$sourceId has disposition '$($cells[2])'; expected '$expectedDisposition'.")
        }

        $targetIds = @([regex]::Matches($cells[3], '\b(?:OP|NO|TB|OA)-\d{2}\b') | ForEach-Object { $_.Value })
        if ($targetIds.Count -eq 0) {
            $errors.Add("$sourceId has no explicit target ID.")
        }
        foreach ($targetId in $targetIds) {
            if (-not $allAnchors.Contains($targetId)) {
                $errors.Add("$sourceId references missing target anchor: $targetId")
            }
        }


        if ($requiredTraceTargets.Contains($sourceId)) {
            $expectedTargets = @($requiredTraceTargets[$sourceId])
            if (($targetIds -join ',') -ne ($expectedTargets -join ',')) {
                $errors.Add("$sourceId targets '$($targetIds -join ',')'; expected '$($expectedTargets -join ',')'.")
            }
        }

        if ($requiredTraceSummaries.ContainsKey($sourceId) -and $cells[1] -ne $requiredTraceSummaries[$sourceId]) {
            $errors.Add("$sourceId summary does not match its canonical semantic contract.")
        }
    }

    foreach ($storyNumber in 1..86) {
        $storyId = 'Story {0:D2}' -f $storyNumber
        if (-not $traceRows.ContainsKey($storyId)) {
            $errors.Add("Missing traceability row: $storyId")
        }
    }

    foreach ($exitNumber in 1..6) {
        $exitId = 'Exit {0:D2}' -f $exitNumber
        if (-not $traceRows.ContainsKey($exitId)) {
            $errors.Add("Missing traceability row: $exitId")
        }
    }


    foreach ($requiredTraceId in $requiredTraceTargets.Keys) {
        if (-not $traceRows.ContainsKey($requiredTraceId)) {
            $errors.Add("Missing canonical traceability contract row: $requiredTraceId")
        }
    }
}

if ($errors.Count -gt 0) {
    Write-Host "FAIL: v0.3.0 Task 01 design verification found $($errors.Count) problem(s)."
    foreach ($errorMessage in $errors) {
        Write-Host " - $errorMessage"
    }
    exit 1
}

Write-Host "PASS: 39 operation rows, 13 completion columns, 86 stories, 6 exit checks, and the threat-model contract verified."
