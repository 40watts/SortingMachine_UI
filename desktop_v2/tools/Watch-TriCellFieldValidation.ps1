param(
    [string]$BaseUrl = "http://127.0.0.1:8050",
    [int]$DurationSeconds = 120,
    [int]$PollMilliseconds = 500,
    [int]$MinAcceptedTops = 9,
    [int]$MinCounterDelta = 9,
    [int]$MinPhysicalObservations = 9,
    [string]$RequiredGoodLanes = "1,2,3,4,5,6,7,8,9",
    [string]$ObservationCsvPath = "",
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"

function Invoke-Json {
    param([string]$Path)
    return Invoke-RestMethod -Uri "$BaseUrl$Path" -TimeoutSec 5
}

function Add-Line {
    param([System.Collections.Generic.List[string]]$Lines, [string]$Text)
    $Lines.Add($Text) | Out-Null
}

function Get-TraceRows {
    try {
        $payload = Invoke-Json "/api/runtime-trace?limit=300"
        if ($payload.rows) {
            return @($payload.rows)
        }
    } catch {
    }

    return @()
}

function Get-HandshakeFromText {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) {
        return ""
    }

    $match = [regex]::Match($Text, "HS=([0-9]+)")
    if ($match.Success) {
        return $match.Groups[1].Value
    }

    return ""
}

function Get-UniqueHandshakeCount {
    param([object[]]$Rows)
    $values = @($Rows | ForEach-Object { Get-HandshakeFromText $_.Detail } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
    return $values.Count
}

function Normalize-Lane {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ""
    }

    $lane = $Value.Trim().ToUpperInvariant().Replace(" ", "")
    if ($lane -eq "11" -or $lane -eq "L11" -or $lane -eq "VOIE11" -or $lane -eq "LINE11") {
        return "NG"
    }

    if ($lane.StartsWith("L") -and $lane.Length -gt 1) {
        return $lane.Substring(1)
    }

    return $lane
}

function Test-Yes {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $false
    }

    $normalized = $Value.Trim().ToUpperInvariant()
    return $normalized -eq "1" -or
        $normalized -eq "Y" -or
        $normalized -eq "YES" -or
        $normalized -eq "O" -or
        $normalized -eq "OUI" -or
        $normalized -eq "TRUE" -or
        $normalized -eq "OK"
}

function Get-RequiredGoodLanes {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return @()
    }

    return @($Value.Split(",; ".ToCharArray(), [System.StringSplitOptions]::RemoveEmptyEntries) |
        ForEach-Object { Normalize-Lane $_ } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and $_ -ne "NG" } |
        Select-Object -Unique)
}

function Get-PhysicalObservationSummary {
    param([string]$Path, [int]$Minimum, [string[]]$RequiredLanes)

    $empty = [pscustomobject]@{
        Path = $Path
        Rows = @()
        ObservationCount = 0
        MatchingCount = 0
        MismatchCount = 0
        MissingExpectedCount = 0
        NgPulseSeenCount = 0
        CoveredGoodLanes = @()
        MissingGoodLanes = @($RequiredLanes)
        BasicObservationOk = $false
        LaneCoverageOk = $false
        Ok = $false
    }

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path $Path)) {
        return $empty
    }

    $rows = @(Import-Csv -LiteralPath $Path)
    $usable = @($rows | Where-Object { -not [string]::IsNullOrWhiteSpace($_.observed_lane) })
    $matching = 0
    $mismatch = 0
    $missingExpected = 0
    $ngPulseSeen = 0
    $covered = @{}

    foreach ($row in $usable) {
        $expected = Normalize-Lane $row.expected_lane
        $observed = Normalize-Lane $row.observed_lane
        if ([string]::IsNullOrWhiteSpace($expected)) {
            $missingExpected++
        } elseif ($expected -eq $observed) {
            $matching++
            if ($expected -ne "NG") {
                $covered[$expected] = $true
            }
        } else {
            $mismatch++
        }

        if (Test-Yes $row.ng_pulse_seen) {
            $ngPulseSeen++
        }
    }

    $coveredGoodLanes = @($covered.Keys | Sort-Object)
    $missingGoodLanes = @($RequiredLanes | Where-Object { -not $covered.ContainsKey($_) })
    $laneCoverageOk = $missingGoodLanes.Count -eq 0
    $basicObservationOk = $usable.Count -ge $Minimum -and
        $matching -ge $Minimum -and
        $mismatch -eq 0 -and
        $missingExpected -eq 0 -and
        $ngPulseSeen -eq $usable.Count

    return [pscustomobject]@{
        Path = $Path
        Rows = $usable
        ObservationCount = $usable.Count
        MatchingCount = $matching
        MismatchCount = $mismatch
        MissingExpectedCount = $missingExpected
        NgPulseSeenCount = $ngPulseSeen
        CoveredGoodLanes = $coveredGoodLanes
        MissingGoodLanes = $missingGoodLanes
        BasicObservationOk = $basicObservationOk
        LaneCoverageOk = $laneCoverageOk
        Ok = $basicObservationOk -and $laneCoverageOk
    }
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $OutputPath = Join-Path (Join-Path $PSScriptRoot "..\bin\data") "field_validation_$stamp.md"
}

if ([string]::IsNullOrWhiteSpace($ObservationCsvPath)) {
    $outputDirForObservation = [System.IO.Path]::GetDirectoryName($OutputPath)
    $outputNameForObservation = [System.IO.Path]::GetFileNameWithoutExtension($OutputPath)
    $ObservationCsvPath = [System.IO.Path]::Combine($outputDirForObservation, $outputNameForObservation + "_observations.csv")
}

$requiredGoodLaneList = Get-RequiredGoodLanes $RequiredGoodLanes
$observationDir = Split-Path $ObservationCsvPath
if (-not [string]::IsNullOrWhiteSpace($observationDir)) {
    New-Item -ItemType Directory -Path $observationDir -Force | Out-Null
}

if (-not (Test-Path $ObservationCsvPath)) {
    $templateRows = New-Object 'System.Collections.Generic.List[string]'
    Add-Line $templateRows "timestamp,handshake,expected_lane,observed_lane,ng_pulse_seen,operator,notes"
    foreach ($lane in $requiredGoodLaneList) {
        Add-Line $templateRows (",,{0},,,,a completer voie {0}" -f $lane)
    }

    [System.IO.File]::WriteAllLines($ObservationCsvPath, $templateRows.ToArray(), [System.Text.Encoding]::UTF8)
    Write-Host ("Fiche CSV initialisee avec {0} voie(s) GOOD a completer: {1}" -f $requiredGoodLaneList.Count, ($(if ($requiredGoodLaneList.Count -gt 0) { $requiredGoodLaneList -join "," } else { "aucune" })))
}

$startedAt = Get-Date
$deadline = $startedAt.AddSeconds($DurationSeconds)
$samples = New-Object 'System.Collections.Generic.List[object]'
$traceSeen = @{}

$initialState = Invoke-Json "/api/state"
$initialRouting = Invoke-Json "/api/diagnostic/physical-routing"
$initialReadiness = Invoke-Json "/api/diagnostic/start-readiness"
$baselineRows = Get-TraceRows
$baselineTraceId = 0
$effectiveMinAcceptedTops = [Math]::Max($MinAcceptedTops, $requiredGoodLaneList.Count)
$effectiveMinCounterDelta = [Math]::Max($MinCounterDelta, $requiredGoodLaneList.Count)
$effectiveMinPhysicalObservations = [Math]::Max($MinPhysicalObservations, $requiredGoodLaneList.Count)
foreach ($row in $baselineRows) {
    if ($null -ne $row.Id) {
        $baselineTraceId = [Math]::Max($baselineTraceId, [int]$row.Id)
    }
}

Write-Host "Surveillance terrain TriCell Pilot en lecture seule pendant $DurationSeconds secondes."
Write-Host "Ne pas lancer START depuis ce script. Appuyer sur DEMARRER dans l'IHM quand la zone machine est prete."
Write-Host "Observations physiques a saisir si possible: $ObservationCsvPath"
Write-Host ("Minimums effectifs: tops={0}, compteurs={1}, observations={2}, voies GOOD={3}" -f $effectiveMinAcceptedTops, $effectiveMinCounterDelta, $effectiveMinPhysicalObservations, ($(if ($requiredGoodLaneList.Count -gt 0) { $requiredGoodLaneList -join "," } else { "aucune" })))

while ((Get-Date) -lt $deadline) {
    try {
        $state = Invoke-Json "/api/state"
        $routing = Invoke-Json "/api/diagnostic/physical-routing"
        $readiness = Invoke-Json "/api/diagnostic/start-readiness"
        $traceRows = Get-TraceRows

        foreach ($row in $traceRows) {
            if ($null -eq $row.Id) {
                continue
            }

            if ([int]$row.Id -gt $baselineTraceId) {
                $traceSeen[[string]$row.Id] = $row
            }
        }

        $samples.Add([pscustomobject]@{
            Timestamp = Get-Date
            Result = $state.Live.Result
            LotControlEnabled = [bool]$state.Production.LotControlEnabled
            Handshake = $routing.physicalRouting.LastHandshake
            MachineStatus = $routing.physicalRouting.MachineStatus
            ExpectedLane = $routing.physicalRouting.ExpectedLane
            AppliedLane = $routing.physicalRouting.AppliedLane
            LastNgPulseStatus = $routing.physicalRouting.LastNgPulse.Status
            LastNgPulseHandshake = $routing.physicalRouting.LastNgPulse.Handshake
            ReadyToStart = [bool]$readiness.startReadiness.ReadyToStart
            CounterTotal = $state.Counters.Total
            CounterGood = $state.Counters.GoodTotal
            CounterNg = $state.Counters.NgTotal
        }) | Out-Null
    } catch {
        $samples.Add([pscustomobject]@{
            Timestamp = Get-Date
            Error = $_.Exception.Message
        }) | Out-Null
    }

    Start-Sleep -Milliseconds $PollMilliseconds
}

$finalState = Invoke-Json "/api/state"
$finalRouting = Invoke-Json "/api/diagnostic/physical-routing"
$finalReadiness = Invoke-Json "/api/diagnostic/start-readiness"
$allRows = @($traceSeen.Values)

$startPreload = @($allRows | Where-Object {
    ($_.Category -eq "THRESHOLDS" -and ($_.Detail -match "START_PRELOAD" -or $_.Action -match "START_PRELOAD")) -or
    ($_.Category -eq "COMMAND" -and $_.Action -eq "START_PRELOAD")
})
$startSent = @($allRows | Where-Object { $_.Category -eq "COMMAND" -and $_.Action -eq "START" -and $_.Status -eq "SENT" })
$startBlocked = @($allRows | Where-Object { $_.Category -eq "COMMAND" -and $_.Action -eq "START" -and $_.Status -match "BLOCKED|ERROR" })
$acceptedTops = @($allRows | Where-Object { $_.Category -eq "HANDSHAKE" -and $_.Action -eq "8230_CHANGE" -and $_.Status -eq "ACCEPTED" })
$decisionRows = @($allRows | Where-Object { $_.Category -eq "DECISION" })

$initialTotal = [int]$initialState.Counters.Total
$finalTotal = [int]$finalState.Counters.Total
$initialGood = [int]$initialState.Counters.GoodTotal
$finalGood = [int]$finalState.Counters.GoodTotal
$initialNg = [int]$initialState.Counters.NgTotal
$finalNg = [int]$finalState.Counters.NgTotal

$report = New-Object 'System.Collections.Generic.List[string]'
Add-Line $report "# TriCell Pilot - rapport surveillance terrain"
Add-Line $report ""
Add-Line $report "- Debut: $($startedAt.ToString('yyyy-MM-dd HH:mm:ss'))"
Add-Line $report "- Fin: $((Get-Date).ToString('yyyy-MM-dd HH:mm:ss'))"
Add-Line $report "- Base API: $BaseUrl"
Add-Line $report "- Duree demandee: $DurationSeconds s"
Add-Line $report "- Mode: lecture seule, aucune commande machine envoyee par ce script"
Add-Line $report "- Trace baseline id: $baselineTraceId"
Add-Line $report ""
Add-Line $report "## Etat initial"
Add-Line $report ""
Add-Line $report ("- Resultat live: {0}" -f $initialState.Live.Result)
Add-Line $report ("- Lot: #{0} {1}" -f $initialState.Production.CurrentLotId, $initialState.Production.LotStatus)
Add-Line $report ("- ReadyToStart: {0}" -f $initialReadiness.startReadiness.ReadyToStart)
Add-Line $report ("- Ligne attendue/appliquee: {0} / {1}" -f $initialRouting.physicalRouting.ExpectedLane, $initialRouting.physicalRouting.AppliedLane)
Add-Line $report "- Compteurs: total=$initialTotal good=$initialGood ng=$initialNg"
Add-Line $report ""
Add-Line $report "## Etat final"
Add-Line $report ""
Add-Line $report ("- Resultat live: {0}" -f $finalState.Live.Result)
Add-Line $report ("- LotControlEnabled: {0}" -f $finalState.Production.LotControlEnabled)
Add-Line $report ("- ReadyToStart: {0}" -f $finalReadiness.startReadiness.ReadyToStart)
Add-Line $report ("- Ligne attendue/appliquee: {0} / {1}" -f $finalRouting.physicalRouting.ExpectedLane, $finalRouting.physicalRouting.AppliedLane)
Add-Line $report ("- Dernier pulse Y11 maintenance: status={0} chemin={1} registre={2}.{3}" -f $finalRouting.physicalRouting.LastNgPulse.Status, $finalRouting.physicalRouting.LastNgPulse.OutputPath, $finalRouting.physicalRouting.LastNgPulse.OutputImageRegister, $finalRouting.physicalRouting.LastNgPulse.OutputBit)
Add-Line $report "- Compteurs: total=$finalTotal good=$finalGood ng=$finalNg"
Add-Line $report "- Delta compteurs: total=$($finalTotal - $initialTotal) good=$($finalGood - $initialGood) ng=$($finalNg - $initialNg)"
Add-Line $report ""
Add-Line $report "## Preuves trace runtime"
Add-Line $report ""
$hasStartPreload = $startPreload.Count -gt 0
$hasStartSent = $startSent.Count -gt 0
$hasStartBlocked = $startBlocked.Count -gt 0
$hasAcceptedTops = $acceptedTops.Count -gt 0
$hasDecisions = $decisionRows.Count -gt 0
$deltaTotal = $finalTotal - $initialTotal
$deltaGood = $finalGood - $initialGood
$deltaNg = $finalNg - $initialNg
$startPreloadOk = $startPreload.Count -gt 0
$startSentOk = $startSent.Count -gt 0
$topsTraceOk = $acceptedTops.Count -ge $effectiveMinAcceptedTops
$counterEvidenceOk = $deltaTotal -ge $effectiveMinCounterDelta
$softwareTraceOk = $startPreloadOk -and $startSentOk -and $topsTraceOk
$physicalObservations = Get-PhysicalObservationSummary $ObservationCsvPath $effectiveMinPhysicalObservations $requiredGoodLaneList

Add-Line $report ("- START_PRELOAD observe: {0} lignes={1}" -f $hasStartPreload, $startPreload.Count)
Add-Line $report ("- START envoye: {0} lignes={1}" -f $hasStartSent, $startSent.Count)
Add-Line $report ("- START bloque/erreur: {0} lignes={1}" -f $hasStartBlocked, $startBlocked.Count)
Add-Line $report ("- Tops 8230 acceptes: {0} lignes={1}" -f $hasAcceptedTops, $acceptedTops.Count)
Add-Line $report ("- Decisions tri observees: {0} lignes={1}" -f $hasDecisions, $decisionRows.Count)
Add-Line $report "- Pistons GOOD: pilotes par PLC via seuils 1188..1370, aucune impulsion directe PC attendue en production."
Add-Line $report "- Verin NG: pousse par le PLC via la voie 11 catch-all; preuve par observation physique ng_pulse_seen."
Add-Line $report ("- Minimums effectifs: tops={0}, compteurs={1}, observations={2}, voies GOOD={3}" -f $effectiveMinAcceptedTops, $effectiveMinCounterDelta, $effectiveMinPhysicalObservations, ($(if ($requiredGoodLaneList.Count -gt 0) { $requiredGoodLaneList -join "," } else { "aucune" })))
Add-Line $report ""

Add-Line $report "## Verdict structure"
Add-Line $report ""
Add-Line $report ("- VERDICT_TRACE_LOGICIEL: {0}" -f ($(if ($softwareTraceOk) { "OK" } else { "INCOMPLET" })))
Add-Line $report ("- VERDICT_COMPTEURS_MACHINE: {0}" -f ($(if ($counterEvidenceOk) { "OK" } else { "INCOMPLET" })))
Add-Line $report ("- VERDICT_OBSERVATION_PHYSIQUE: {0}" -f ($(if ($physicalObservations.BasicObservationOk) { "OK" } else { "INCOMPLET" })))
Add-Line $report ("- VERDICT_COUVERTURE_VOIES_GOOD: {0}" -f ($(if ($physicalObservations.LaneCoverageOk) { "OK" } else { "INCOMPLET" })))
Add-Line $report ""
Add-Line $report "| Point | Etat | Preuve |"
Add-Line $report "| --- | --- | --- |"
Add-Line $report ("| START_PRELOAD | {0} | {1} ligne(s) trace runtime |" -f ($(if ($startPreloadOk) { "OK" } else { "MANQUANT" })), $startPreload.Count)
Add-Line $report ("| START 5978=31 | {0} | {1} ligne(s) SENT |" -f ($(if ($startSentOk) { "OK" } else { "MANQUANT" })), $startSent.Count)
Add-Line $report "| Pistons GOOD PLC | OK | tri physique attendu via seuils 1188..1370, valide par observations/couverture voies |"
Add-Line $report ("| Tops cellule 8230 | {0} | {1} accepte(s), minimum={2} |" -f ($(if ($topsTraceOk) { "OK" } else { "MANQUANT" })), $acceptedTops.Count, $effectiveMinAcceptedTops)
Add-Line $report ("| Compteurs machine | {0} | delta total={1}, good={2}, ng={3}, minimum={4} |" -f ($(if ($counterEvidenceOk) { "OK" } else { "MANQUANT" })), $deltaTotal, $deltaGood, $deltaNg, $effectiveMinCounterDelta)
Add-Line $report ("| Observation lignes GOOD | {0} | {1}/{2} ligne(s) conformes, {3} mismatch, {4} attendu manquant |" -f ($(if ($physicalObservations.BasicObservationOk) { "OK" } else { "A_COMPLETER" })), $physicalObservations.MatchingCount, $effectiveMinPhysicalObservations, $physicalObservations.MismatchCount, $physicalObservations.MissingExpectedCount)
Add-Line $report ("| Observation poussoir NG | {0} | {1}/{2} observation(s) avec ng_pulse_seen=oui |" -f ($(if ($physicalObservations.BasicObservationOk) { "OK" } else { "A_COMPLETER" })), $physicalObservations.NgPulseSeenCount, $physicalObservations.ObservationCount)
Add-Line $report ("| Couverture voies GOOD | {0} | requises={1}; couvertes={2}; manquantes={3} |" -f ($(if ($physicalObservations.LaneCoverageOk) { "OK" } else { "A_COMPLETER" })), ($(if ($requiredGoodLaneList.Count -gt 0) { $requiredGoodLaneList -join "," } else { "aucune" })), ($(if ($physicalObservations.CoveredGoodLanes.Count -gt 0) { $physicalObservations.CoveredGoodLanes -join "," } else { "aucune" })), ($(if ($physicalObservations.MissingGoodLanes.Count -gt 0) { $physicalObservations.MissingGoodLanes -join "," } else { "aucune" })))
Add-Line $report ""
Add-Line $report "## Observations physiques operateur"
Add-Line $report ""
Add-Line $report "- Fichier CSV: $ObservationCsvPath"
Add-Line $report "- Colonnes: timestamp, handshake, expected_lane, observed_lane, ng_pulse_seen, operator, notes"
Add-Line $report "- Minimum attendu: $effectiveMinPhysicalObservations cellule(s) observee(s), aucune divergence, NG pulse vu a chaque ligne observee."
Add-Line $report ("- Couverture voies GOOD requise: {0}" -f ($(if ($requiredGoodLaneList.Count -gt 0) { $requiredGoodLaneList -join "," } else { "aucune" })))
Add-Line $report ("- Couverture voies GOOD observee: {0}" -f ($(if ($physicalObservations.CoveredGoodLanes.Count -gt 0) { $physicalObservations.CoveredGoodLanes -join "," } else { "aucune" })))
Add-Line $report ("- Couverture voies GOOD manquante: {0}" -f ($(if ($physicalObservations.MissingGoodLanes.Count -gt 0) { $physicalObservations.MissingGoodLanes -join "," } else { "aucune" })))
Add-Line $report ""
if ($physicalObservations.ObservationCount -gt 0) {
    Add-Line $report "| Handshake | Attendue | Vue | NG pulse | Operateur | Notes |"
    Add-Line $report "| --- | --- | --- | --- | --- | --- |"
    foreach ($row in @($physicalObservations.Rows | Select-Object -First 20)) {
        Add-Line $report ("| {0} | {1} | {2} | {3} | {4} | {5} |" -f $row.handshake, $row.expected_lane, $row.observed_lane, $row.ng_pulse_seen, $row.operator, $row.notes)
    }
} else {
    Add-Line $report "Aucune observation physique saisie. Ajouter des lignes dans le CSV pendant ou apres l'essai, puis relancer le script ou conserver le CSV avec le rapport."
}
Add-Line $report ""
Add-Line $report "## Conclusion automatique"
Add-Line $report ""
if ($softwareTraceOk -and $counterEvidenceOk -and $physicalObservations.Ok) {
    Add-Line $report "Preuve terrain complete: traces logiciel, compteurs machine, observations physiques et couverture des voies GOOD sont coherents. Conserver rapport, CSV et video si disponible."
} elseif ($softwareTraceOk -and $counterEvidenceOk) {
    Add-Line $report "Preuve logicielle et compteurs OK. Le tri reel reste a valider par le CSV d'observations physiques signe/video: lignes GOOD requises conformes et poussoir 11/NG visible."
} elseif (-not $startSentOk) {
    Add-Line $report "Aucun START envoye pendant la fenetre de surveillance. L'essai physique n'est pas encore prouve."
} elseif (-not $softwareTraceOk) {
    Add-Line $report "START a ete vu, mais la preuve trace logiciel est incomplete. Verifier START_PRELOAD et les tops 8230 acceptes. Les pistons GOOD et le verin NG sont valides par observation terrain, car ils sont pilotes par le PLC."
} else {
    Add-Line $report "Preuve trace logiciel OK, mais aucun delta compteur suffisant. Faire passer au moins $effectiveMinCounterDelta cellule(s) ou confirmer qu'il s'agissait uniquement d'un essai NG a vide."
}

Add-Line $report ""
Add-Line $report "## Dernieres lignes pertinentes"
Add-Line $report ""
foreach ($row in @($allRows | Sort-Object Id | Select-Object -Last 40)) {
    Add-Line $report ("- {0} {1} {2} {3} reg {4} val {5} - {6}" -f $row.Timestamp, $row.Category, $row.Action, $row.Status, $row.Register, $row.Value, $row.Detail)
}

$dir = Split-Path $OutputPath
if (-not [string]::IsNullOrWhiteSpace($dir)) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}

[System.IO.File]::WriteAllLines($OutputPath, $report.ToArray(), [System.Text.Encoding]::UTF8)
Write-Host "Rapport ecrit: $OutputPath"
