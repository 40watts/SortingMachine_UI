param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [string]$ReportPath = "",
    [switch]$IncludeCodexSmoke,
    [int]$MaxAgeMinutes = 0,
    [string]$BaseUrl = "http://127.0.0.1:8050",
    [switch]$SkipCurrentLotCheck
)

$ErrorActionPreference = "Stop"

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw $Message
    }
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
    Assert-True ($latest.Count -gt 0) "Aucun rapport terrain operateur trouve. Lancer validate_tricell_field.bat avant de verifier."
    return $latest[0].FullName
}

function Get-ReportLotId {
    param([string]$Text)

    $match = [regex]::Match($Text, "(?m)^\s*-\s*Lot:\s*#([0-9]+)\b")
    if (-not $match.Success) {
        $match = [regex]::Match($Text, "Lot:\s*#([0-9]+)\b")
    }

    if (-not $match.Success) {
        return $null
    }

    return [int]$match.Groups[1].Value
}

function Get-CurrentLotId {
    param([string]$ApiBaseUrl)

    $state = Invoke-RestMethod -Uri "$ApiBaseUrl/api/state" -TimeoutSec 5
    Assert-True ($null -ne $state.Production) "API locale: bloc Production absent."
    Assert-True ($null -ne $state.Production.CurrentLotId) "API locale: CurrentLotId absent."
    $currentLotId = [int]$state.Production.CurrentLotId
    Assert-True ($currentLotId -gt 0) "API locale: aucun lot courant actif."
    return $currentLotId
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

function Get-LaneList {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return @()
    }

    $trimmed = $Value.Trim()
    if ($trimmed -eq "aucune") {
        return @()
    }

    return @($trimmed.Split(",; ".ToCharArray(), [System.StringSplitOptions]::RemoveEmptyEntries) |
        ForEach-Object { $_.Trim().ToUpperInvariant().Replace("L", "") } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and $_ -ne "NG" } |
        Select-Object -Unique)
}

function Assert-LaneCoverageDetails {
    param([string]$Text)

    $required = Get-LaneList (Get-ReportLineValue $Text "Couverture voies GOOD requise")
    $observed = Get-LaneList (Get-ReportLineValue $Text "Couverture voies GOOD observee")
    $missing = Get-LaneList (Get-ReportLineValue $Text "Couverture voies GOOD manquante")

    Assert-True ($required.Count -gt 0) "Detail couverture absent: aucune voie GOOD requise n'est lisible dans le rapport."
    Assert-True ($observed.Count -gt 0) "Detail couverture absent: aucune voie GOOD observee n'est lisible dans le rapport."
    Assert-True ($missing.Count -eq 0) ("Couverture voies GOOD incomplete: voies manquantes={0}." -f ($missing -join ","))

    $observedSet = @{}
    foreach ($lane in $observed) {
        $observedSet[$lane] = $true
    }

    $notObserved = @($required | Where-Object { -not $observedSet.ContainsKey($_) })
    Assert-True ($notObserved.Count -eq 0) ("Couverture voies GOOD incoherente: requises non observees={0}." -f ($notObserved -join ","))

    $minimums = [regex]::Match($Text, "Minimums effectifs:\s*tops=([0-9]+),\s*compteurs=([0-9]+),\s*observations=([0-9]+),\s*voies GOOD=([0-9,]+|aucune)")
    Assert-True ($minimums.Success) "Detail couverture absent: minimums effectifs introuvables."

    $requiredCount = $required.Count
    Assert-True ([int]$minimums.Groups[1].Value -ge $requiredCount) "Minimum tops 8230 inferieur au nombre de voies GOOD requises."
    Assert-True ([int]$minimums.Groups[2].Value -ge $requiredCount) "Minimum compteurs inferieur au nombre de voies GOOD requises."
    Assert-True ([int]$minimums.Groups[3].Value -ge $requiredCount) "Minimum observations inferieur au nombre de voies GOOD requises."

}

$rootPath = (Resolve-Path $Root).Path
$resolvedReport = Resolve-ReportPath $rootPath $ReportPath ([bool]$IncludeCodexSmoke)
$reportFile = Get-Item -LiteralPath $resolvedReport

if ($MaxAgeMinutes -gt 0) {
    $age = (Get-Date) - $reportFile.LastWriteTime
    Assert-True ($age.TotalMinutes -le $MaxAgeMinutes) ("Rapport terrain trop ancien: {0:0.0} min, maximum {1} min." -f $age.TotalMinutes, $MaxAgeMinutes)
}

$text = [System.IO.File]::ReadAllText($resolvedReport, [System.Text.Encoding]::UTF8)

Assert-True ($text -match "VERDICT_TRACE_LOGICIEL: OK") "Trace logiciel incomplete: START_PRELOAD/START/tops 8230 ne sont pas tous OK."
Assert-True ($text -match "VERDICT_COMPTEURS_MACHINE: OK") "Compteurs machine incomplets: aucun delta compteur suffisant dans le rapport."
Assert-True ($text -match "VERDICT_OBSERVATION_PHYSIQUE: OK") "Observation physique incomplete: lignes vues ou pulse NG non valides."
Assert-True ($text -match "VERDICT_COUVERTURE_VOIES_GOOD: OK") "Couverture voies GOOD incomplete: les lignes GOOD requises ne sont pas toutes observees."
Assert-LaneCoverageDetails $text
$reportLotId = Get-ReportLotId $text
Assert-True ($null -ne $reportLotId) "Lot du rapport terrain absent."
Assert-True ($text -match "Preuve terrain complete") "Conclusion complete absente du rapport terrain."
Assert-True ($text -notmatch "Aucun START envoye") "Rapport invalide: aucun START observe."
Assert-True ($text -notmatch "preuve.*incomplete|VERDICT_[^\r\n]*INCOMPLET|\|\s*[^|]+\|\s*(MANQUANT|A_COMPLETER)\s*\|") "Rapport invalide: il reste un point incomplet, manquant ou a completer."

if (-not $SkipCurrentLotCheck) {
    try {
        $currentLotId = Get-CurrentLotId $BaseUrl
    } catch {
        throw "Impossible de verifier le lot courant via $BaseUrl/api/state: $($_.Exception.Message)"
    }

    Assert-True ($reportLotId -eq $currentLotId) ("Rapport terrain du mauvais lot: rapport #{0}, lot courant #{1}." -f $reportLotId, $currentLotId)
}

Write-Host ("Field validation report OK: {0} (lot #{1})" -f $resolvedReport, $reportLotId)
