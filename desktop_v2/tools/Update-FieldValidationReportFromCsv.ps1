param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [string]$ReportPath = "",
    [string]$ObservationCsvPath = "",
    [string]$OutputPath = "",
    [int]$MinPhysicalObservations = 9,
    [string]$RequiredGoodLanes = "1,2,3,4,5,6,7,8,9",
    [switch]$IncludeCodexSmoke
)

$ErrorActionPreference = "Stop"

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw $Message
    }
}

function Add-Line {
    param([System.Collections.Generic.List[string]]$Lines, [string]$Text)
    $Lines.Add($Text) | Out-Null
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

function Get-LaneList {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) {
        return @()
    }

    return @($Value.Split(",; ".ToCharArray(), [System.StringSplitOptions]::RemoveEmptyEntries) |
        ForEach-Object { Normalize-Lane $_ } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and $_ -ne "NG" } |
        Select-Object -Unique)
}

function Resolve-ReportPath {
    param([string]$RootPath, [string]$ExplicitPath, [bool]$IncludeSmoke)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        Assert-True (Test-Path $ExplicitPath) "Rapport terrain introuvable: $ExplicitPath"
        return (Resolve-Path $ExplicitPath).Path
    }

    $dataDir = Join-Path $RootPath "desktop_v2\bin\data"
    Assert-True (Test-Path $dataDir) "Dossier rapports terrain introuvable: $dataDir"

    $candidates = Get-ChildItem -LiteralPath $dataDir -Filter "field_validation*.md" -File -ErrorAction SilentlyContinue |
        Where-Object { $IncludeSmoke -or $_.Name -notlike "field_validation_codex*" } |
        Sort-Object LastWriteTime -Descending

    $latest = @($candidates | Select-Object -First 1)
    Assert-True ($latest.Count -gt 0) "Aucun rapport terrain operateur trouve. Lancer validate_tricell_field.bat avant d'actualiser."
    return $latest[0].FullName
}

function Resolve-ObservationCsvPath {
    param([string]$ResolvedReportPath, [string]$ExplicitPath)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        Assert-True (Test-Path $ExplicitPath) "CSV observations introuvable: $ExplicitPath"
        return (Resolve-Path $ExplicitPath).Path
    }

    $reportDir = Split-Path $ResolvedReportPath
    $reportBase = [System.IO.Path]::GetFileNameWithoutExtension($ResolvedReportPath)
    $direct = Join-Path $reportDir ($reportBase + "_observations.csv")
    if (Test-Path $direct) {
        return (Resolve-Path $direct).Path
    }

    $sourceBase = $reportBase -replace "_refreshed_[0-9]{8}_[0-9]{6}$", ""
    $sourceDirect = Join-Path $reportDir ($sourceBase + "_observations.csv")
    if (Test-Path $sourceDirect) {
        return (Resolve-Path $sourceDirect).Path
    }

    $latest = @(Get-ChildItem -LiteralPath $reportDir -Filter "field_validation*_observations.csv" -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1)
    Assert-True ($latest.Count -gt 0) "CSV observations introuvable. Passer -ObservationCsvPath ou conserver le CSV genere avec le rapport."
    return $latest[0].FullName
}

function Get-ReportLineValue {
    param([string]$Text, [string]$Label)

    $pattern = "(?m)^\s*-\s*" + [regex]::Escape($Label) + ":\s*(.+?)\s*$"
    $match = [regex]::Match($Text, $pattern)
    if (-not $match.Success) {
        return ""
    }

    return $match.Groups[1].Value.Trim()
}

function Get-ReportLotLine {
    param([string]$Text)

    $match = [regex]::Match($Text, "(?m)^\s*-\s*Lot:\s*#([0-9]+)\s*(.*?)\s*$")
    Assert-True $match.Success "Lot du rapport terrain absent."
    return [pscustomobject]@{
        Id = [int]$match.Groups[1].Value
        Suffix = $match.Groups[2].Value.Trim()
    }
}

function Get-Verdict {
    param([string]$Text, [string]$Name)

    $pattern = "(?m)^\s*-\s*" + [regex]::Escape($Name) + ":\s*([A-Z_]+)\s*$"
    $match = [regex]::Match($Text, $pattern)
    if (-not $match.Success) {
        return "UNKNOWN"
    }

    return $match.Groups[1].Value.Trim().ToUpperInvariant()
}

function Get-SourceMinimums {
    param([string]$Text)

    $match = [regex]::Match($Text, "Minimums effectifs:\s*tops=([0-9]+),\s*compteurs=([0-9]+),\s*observations=([0-9]+),\s*voies GOOD=(.+)")
    Assert-True $match.Success "Minimums effectifs introuvables dans le rapport source."
    return [pscustomobject]@{
        Tops = [int]$match.Groups[1].Value
        Counters = [int]$match.Groups[2].Value
        Observations = [int]$match.Groups[3].Value
        RequiredGoodLanes = $match.Groups[4].Value.Trim()
    }
}

function Get-FirstInt {
    param([string]$Text, [string]$Pattern)

    $match = [regex]::Match($Text, $Pattern)
    if (-not $match.Success) {
        return $null
    }

    return [int]$match.Groups[1].Value
}

function Get-PhysicalObservationSummary {
    param([string]$Path, [int]$Minimum, [string[]]$RequiredLanes)

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

$rootPath = (Resolve-Path $Root).Path
$resolvedReport = Resolve-ReportPath $rootPath $ReportPath ([bool]$IncludeCodexSmoke)
$resolvedObservationCsv = Resolve-ObservationCsvPath $resolvedReport $ObservationCsvPath

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $reportDir = Split-Path $resolvedReport
    $reportBase = [System.IO.Path]::GetFileNameWithoutExtension($resolvedReport)
    $OutputPath = Join-Path $reportDir ($reportBase + "_refreshed_" + $stamp + ".md")
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
Assert-True (-not ([string]::Equals($resolvedOutput, $resolvedReport, [System.StringComparison]::OrdinalIgnoreCase))) "Le rapport source ne doit pas etre ecrase; choisir un OutputPath distinct."

$sourceText = [System.IO.File]::ReadAllText($resolvedReport, [System.Text.Encoding]::UTF8)
Assert-True ($sourceText -match "TriCell Pilot - rapport surveillance terrain") "Le rapport source ne ressemble pas a un rapport terrain TriCell Pilot."
Assert-True ($sourceText -match "Mode: lecture seule") "Le rapport source doit provenir d'une surveillance lecture seule."

$lot = Get-ReportLotLine $sourceText
$sourceMinimums = Get-SourceMinimums $sourceText
$traceVerdictSource = Get-Verdict $sourceText "VERDICT_TRACE_LOGICIEL"
$counterVerdictSource = Get-Verdict $sourceText "VERDICT_COMPTEURS_MACHINE"

$requiredGoodLaneList = Get-LaneList $RequiredGoodLanes
if ($requiredGoodLaneList.Count -eq 0) {
    $requiredGoodLaneList = Get-LaneList (Get-ReportLineValue $sourceText "Couverture voies GOOD requise")
}
Assert-True ($requiredGoodLaneList.Count -gt 0) "Aucune voie GOOD requise lisible. Renseigner -RequiredGoodLanes."

$effectiveMinAcceptedTops = [Math]::Max($sourceMinimums.Tops, $requiredGoodLaneList.Count)
$effectiveMinCounterDelta = [Math]::Max($sourceMinimums.Counters, $requiredGoodLaneList.Count)
$effectiveMinPhysicalObservations = [Math]::Max([Math]::Max($sourceMinimums.Observations, $MinPhysicalObservations), $requiredGoodLaneList.Count)
$acceptedTopsCount = Get-FirstInt $sourceText "Tops 8230 acceptes:\s*\w+\s+lignes=([0-9]+)"
if ($null -eq $acceptedTopsCount) {
    $acceptedTopsCount = $sourceMinimums.Tops
}

$deltaTotal = Get-FirstInt $sourceText "Delta compteurs:\s*total=([-0-9]+)"
if ($null -eq $deltaTotal) {
    $deltaTotal = $sourceMinimums.Counters
}

$traceVerdict = if ($traceVerdictSource -eq "OK" -and $acceptedTopsCount -ge $effectiveMinAcceptedTops) { "OK" } else { "INCOMPLET" }
$counterVerdict = if ($counterVerdictSource -eq "OK" -and $deltaTotal -ge $effectiveMinCounterDelta) { "OK" } else { "INCOMPLET" }
$physicalObservations = Get-PhysicalObservationSummary $resolvedObservationCsv $effectiveMinPhysicalObservations $requiredGoodLaneList

$report = New-Object 'System.Collections.Generic.List[string]'
Add-Line $report "# TriCell Pilot - rapport surveillance terrain actualise"
Add-Line $report ""
Add-Line $report "- Actualisation: $((Get-Date).ToString('yyyy-MM-dd HH:mm:ss'))"
Add-Line $report "- Rapport source: $resolvedReport"
Add-Line $report "- CSV observations: $resolvedObservationCsv"
Add-Line $report "- Mode: lecture seule fichier, aucune commande machine envoyee par ce script"
Add-Line $report ("- Lot: #{0} {1}" -f $lot.Id, $lot.Suffix)
Add-Line $report ""
Add-Line $report "## Preuves source conservees"
Add-Line $report ""
Add-Line $report ("- START_PRELOAD observe: {0}" -f ($(if ([string]::IsNullOrWhiteSpace((Get-ReportLineValue $sourceText "START_PRELOAD observe"))) { "source non detaillee" } else { Get-ReportLineValue $sourceText "START_PRELOAD observe" })))
Add-Line $report ("- START envoye: {0}" -f ($(if ([string]::IsNullOrWhiteSpace((Get-ReportLineValue $sourceText "START envoye"))) { "source non detaillee" } else { Get-ReportLineValue $sourceText "START envoye" })))
Add-Line $report ("- Tops 8230 acceptes: {0} lignes={1}" -f ($(if ($acceptedTopsCount -gt 0) { "True" } else { "False" })), $acceptedTopsCount)
Add-Line $report "- Pistons GOOD: pilotes par PLC via seuils 1188..1370, aucune impulsion directe PC attendue en production."
Add-Line $report "- Verin NG: pousse par le PLC via la voie 11 catch-all; preuve par observation physique ng_pulse_seen."
Add-Line $report ("- Delta compteurs: {0}" -f ($(if ([string]::IsNullOrWhiteSpace((Get-ReportLineValue $sourceText "Delta compteurs"))) { "total=$deltaTotal" } else { Get-ReportLineValue $sourceText "Delta compteurs" })))
Add-Line $report ("- Minimums effectifs: tops={0}, compteurs={1}, observations={2}, voies GOOD={3}" -f $effectiveMinAcceptedTops, $effectiveMinCounterDelta, $effectiveMinPhysicalObservations, ($requiredGoodLaneList -join ","))
Add-Line $report ""
Add-Line $report "## Verdict structure"
Add-Line $report ""
Add-Line $report ("- VERDICT_TRACE_LOGICIEL: {0}" -f $traceVerdict)
Add-Line $report ("- VERDICT_COMPTEURS_MACHINE: {0}" -f $counterVerdict)
Add-Line $report ("- VERDICT_OBSERVATION_PHYSIQUE: {0}" -f ($(if ($physicalObservations.BasicObservationOk) { "OK" } else { "INCOMPLET" })))
Add-Line $report ("- VERDICT_COUVERTURE_VOIES_GOOD: {0}" -f ($(if ($physicalObservations.LaneCoverageOk) { "OK" } else { "INCOMPLET" })))
Add-Line $report ""
Add-Line $report "| Point | Etat | Preuve |"
Add-Line $report "| --- | --- | --- |"
Add-Line $report ("| Trace logiciel source | {0} | source={1}; tops={2}/{3}; pistons GOOD=PLC seuils 1188..1370 |" -f ($(if ($traceVerdict -eq "OK") { "OK" } else { "MANQUANT" })), $traceVerdictSource, $acceptedTopsCount, $effectiveMinAcceptedTops)
Add-Line $report ("| Compteurs machine source | {0} | source={1}; delta total={2}; minimum={3} |" -f ($(if ($counterVerdict -eq "OK") { "OK" } else { "MANQUANT" })), $counterVerdictSource, $deltaTotal, $effectiveMinCounterDelta)
Add-Line $report ("| Observation lignes GOOD | {0} | {1}/{2} ligne(s) conformes, {3} mismatch, {4} attendu manquant |" -f ($(if ($physicalObservations.BasicObservationOk) { "OK" } else { "A_COMPLETER" })), $physicalObservations.MatchingCount, $effectiveMinPhysicalObservations, $physicalObservations.MismatchCount, $physicalObservations.MissingExpectedCount)
Add-Line $report ("| Observation poussoir NG | {0} | {1}/{2} observation(s) avec ng_pulse_seen=oui |" -f ($(if ($physicalObservations.BasicObservationOk) { "OK" } else { "A_COMPLETER" })), $physicalObservations.NgPulseSeenCount, $physicalObservations.ObservationCount)
Add-Line $report ("| Couverture voies GOOD | {0} | requises={1}; couvertes={2}; manquantes={3} |" -f ($(if ($physicalObservations.LaneCoverageOk) { "OK" } else { "A_COMPLETER" })), ($requiredGoodLaneList -join ","), ($(if ($physicalObservations.CoveredGoodLanes.Count -gt 0) { $physicalObservations.CoveredGoodLanes -join "," } else { "aucune" })), ($(if ($physicalObservations.MissingGoodLanes.Count -gt 0) { $physicalObservations.MissingGoodLanes -join "," } else { "aucune" })))
Add-Line $report ""
Add-Line $report "## Observations physiques operateur"
Add-Line $report ""
Add-Line $report "- Fichier CSV: $resolvedObservationCsv"
Add-Line $report "- Colonnes: timestamp, handshake, expected_lane, observed_lane, ng_pulse_seen, operator, notes"
Add-Line $report "- Minimum attendu: $effectiveMinPhysicalObservations cellule(s) observee(s), aucune divergence, NG pulse vu a chaque ligne observee."
Add-Line $report ("- Couverture voies GOOD requise: {0}" -f ($requiredGoodLaneList -join ","))
Add-Line $report ("- Couverture voies GOOD observee: {0}" -f ($(if ($physicalObservations.CoveredGoodLanes.Count -gt 0) { $physicalObservations.CoveredGoodLanes -join "," } else { "aucune" })))
Add-Line $report ("- Couverture voies GOOD manquante: {0}" -f ($(if ($physicalObservations.MissingGoodLanes.Count -gt 0) { $physicalObservations.MissingGoodLanes -join "," } else { "aucune" })))
Add-Line $report ""
if ($physicalObservations.ObservationCount -gt 0) {
    Add-Line $report "| Handshake | Attendue | Vue | NG pulse | Operateur | Notes |"
    Add-Line $report "| --- | --- | --- | --- | --- | --- |"
    foreach ($row in @($physicalObservations.Rows | Select-Object -First 30)) {
        Add-Line $report ("| {0} | {1} | {2} | {3} | {4} | {5} |" -f $row.handshake, $row.expected_lane, $row.observed_lane, $row.ng_pulse_seen, $row.operator, $row.notes)
    }
} else {
    Add-Line $report "Aucune observation physique complete dans le CSV."
}
Add-Line $report ""
Add-Line $report "## Conclusion automatique"
Add-Line $report ""
if ($traceVerdict -eq "OK" -and $counterVerdict -eq "OK" -and $physicalObservations.Ok) {
    Add-Line $report "Preuve terrain complete: traces logiciel, compteurs machine, observations physiques et couverture des voies GOOD sont coherents. Conserver rapport source, rapport actualise, CSV et video si disponible."
} elseif ($traceVerdict -eq "OK" -and $counterVerdict -eq "OK") {
    Add-Line $report "Preuve logicielle et compteurs OK. Observation terrain incomplete: completer le CSV avec toutes les lignes GOOD requises et le poussoir 11/NG visible."
} else {
    Add-Line $report "Preuve terrain incomplete: le rapport source ne couvre pas encore les tops 8230 acceptes ou le delta compteurs requis pour cette couverture voies GOOD. Les pistons GOOD et le verin NG se prouvent par observation/couverture car ils sont pilotes par le PLC."
}

$dir = Split-Path $resolvedOutput
if (-not [string]::IsNullOrWhiteSpace($dir)) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}

[System.IO.File]::WriteAllLines($resolvedOutput, $report.ToArray(), [System.Text.Encoding]::UTF8)
Write-Host "Rapport actualise: $resolvedOutput"
