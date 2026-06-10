param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [string]$BaseUrl = "http://127.0.0.1:8050"
)

$ErrorActionPreference = "Stop"

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw $Message
    }
}

function Invoke-Json {
    param([string]$Path)
    return Invoke-RestMethod -Uri "$BaseUrl$Path" -TimeoutSec 5
}

function Wait-Api {
    param([int]$Seconds = 20)
    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        try {
            return Invoke-Json "/api/state"
        } catch {
            Start-Sleep -Milliseconds 500
        }
    } while ((Get-Date) -lt $deadline)

    throw "API locale indisponible apres $Seconds secondes."
}

function Invoke-ReportAssert {
    param([string]$ScriptPath, [string]$RootPath, [string]$Path, [switch]$SkipCurrentLotCheck)

    try {
        if ($SkipCurrentLotCheck) {
            & powershell -NoProfile -ExecutionPolicy Bypass -File $ScriptPath -Root $RootPath -ReportPath $Path -BaseUrl $BaseUrl -SkipCurrentLotCheck *> $null
        } else {
            & powershell -NoProfile -ExecutionPolicy Bypass -File $ScriptPath -Root $RootPath -ReportPath $Path -BaseUrl $BaseUrl *> $null
        }
        return $LASTEXITCODE
    } catch {
        if ($LASTEXITCODE -ne $null) {
            return $LASTEXITCODE
        }

        return 1
    }
}

$rootPath = (Resolve-Path $Root).Path
$exe = Join-Path $rootPath "desktop_v2\bin\TriCellPilot.exe"
$watcher = Join-Path $rootPath "desktop_v2\tools\Watch-TriCellFieldValidation.ps1"
$launcher = Join-Path $rootPath "desktop_v2\tools\Start-FieldValidationWatch.ps1"
$reportAssert = Join-Path $rootPath "desktop_v2\tools\Assert-FieldValidationReport.ps1"
$refreshReport = Join-Path $rootPath "desktop_v2\tools\Update-FieldValidationReportFromCsv.ps1"

Assert-True (Test-Path $exe) "Executable introuvable: $exe"
Assert-True (Test-Path $watcher) "Watcher validation terrain introuvable: $watcher"
Assert-True (Test-Path $launcher) "Lanceur validation terrain introuvable: $launcher"
Assert-True (Test-Path $reportAssert) "Verificateur rapport terrain introuvable: $reportAssert"
Assert-True (Test-Path $refreshReport) "Actualiseur rapport terrain introuvable: $refreshReport"

$startedProcess = $null
$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("TriCellFieldValidationRegression_" + [guid]::NewGuid().ToString("N"))

try {
    try {
        $state = Invoke-Json "/api/state"
    } catch {
        $startedProcess = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -WindowStyle Hidden -PassThru
        $state = Wait-Api 20
    }

    Assert-True ($null -ne $state.Diagnostic) "API state: diagnostic absent."
    Assert-True ($null -ne $state.Diagnostic.PhysicalRouting) "API state: diagnostic routage physique absent."
    Assert-True ($null -ne $state.Diagnostic.StartReadiness) "API state: pre-vol START absent."
    $currentLotId = [int]$state.Production.CurrentLotId
    Assert-True ($currentLotId -gt 0) "API state: lot courant absent."

    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
    $observationCsv = Join-Path $tempDir "observations.csv"
    $report = Join-Path $tempDir "field_validation_report.md"

    @(
        "timestamp,handshake,expected_lane,observed_lane,ng_pulse_seen,operator,notes",
        "2026-06-04 20:10:01,1,4,4,oui,Regression,observation conforme 1",
        "2026-06-04 20:10:02,2,L4,4,yes,Regression,observation conforme 2",
        "2026-06-04 20:10:03,3,NG,11,1,Regression,observation conforme NG"
    ) | Set-Content -LiteralPath $observationCsv -Encoding UTF8

    & powershell -NoProfile -ExecutionPolicy Bypass -File $launcher `
        -Root $rootPath `
        -BaseUrl $BaseUrl `
        -DurationSeconds 1 `
        -PollMilliseconds 250 `
        -MinAcceptedTops 1 `
        -MinCounterDelta 1 `
        -MinPhysicalObservations 3 `
        -RequiredGoodLanes "4" `
        -ObservationCsvPath $observationCsv `
        -OutputPath $report

    if ($LASTEXITCODE -ne 0) {
        throw "Watcher validation terrain termine avec code $LASTEXITCODE."
    }

    Assert-True (Test-Path $report) "Rapport watcher non cree."
    $text = [System.IO.File]::ReadAllText($report, [System.Text.Encoding]::UTF8)

    Assert-True ($text -match "Mode: lecture seule") "Rapport watcher: mode lecture seule absent."
    Assert-True ($text -match "VERDICT_TRACE_LOGICIEL: INCOMPLET") "Rapport watcher: trace logiciel devrait etre incomplete sans START."
    Assert-True ($text -match "VERDICT_COMPTEURS_MACHINE: INCOMPLET") "Rapport watcher: compteurs devraient etre incomplets sans cellule."
    Assert-True ($text -match "VERDICT_OBSERVATION_PHYSIQUE: OK") "Rapport watcher: observation physique CSV conforme non reconnue."
    Assert-True ($text -match "VERDICT_COUVERTURE_VOIES_GOOD: OK") "Rapport watcher: couverture voie 4 non reconnue."
    Assert-True ($text -match "Aucun START envoye") "Rapport watcher: absence de START non detectee."
    Assert-True ($text -match "expected_lane, observed_lane, ng_pulse_seen") "Rapport watcher: colonnes observation non documentees."
    Assert-True ($text -notmatch "Preuve terrain complete") "Rapport watcher: ne doit pas conclure terrain complet sans START ni compteurs."

    $incompleteExit = Invoke-ReportAssert $reportAssert $rootPath $report
    Assert-True ($incompleteExit -ne 0) "Verificateur rapport: un rapport incomplet ne doit pas etre accepte."

    $missingCoverageReport = Join-Path $tempDir "field_validation_missing_coverage_report.md"
    & powershell -NoProfile -ExecutionPolicy Bypass -File $launcher `
        -Root $rootPath `
        -BaseUrl $BaseUrl `
        -DurationSeconds 1 `
        -PollMilliseconds 250 `
        -MinAcceptedTops 1 `
        -MinCounterDelta 1 `
        -MinPhysicalObservations 3 `
        -RequiredGoodLanes "1,4" `
        -ObservationCsvPath $observationCsv `
        -OutputPath $missingCoverageReport

    if ($LASTEXITCODE -ne 0) {
        throw "Watcher validation terrain couverture termine avec code $LASTEXITCODE."
    }

    $missingCoverageText = [System.IO.File]::ReadAllText($missingCoverageReport, [System.Text.Encoding]::UTF8)
    Assert-True ($missingCoverageText -match "VERDICT_COUVERTURE_VOIES_GOOD: INCOMPLET") "Rapport watcher: voie GOOD manquante non detectee."
    Assert-True ($missingCoverageText -match "Couverture voies GOOD manquante: 1") "Rapport watcher: liste des voies manquantes absente."

    $templateCsv = Join-Path $tempDir "template_observations.csv"
    $templateReport = Join-Path $tempDir "field_validation_template_report.md"
    & powershell -NoProfile -ExecutionPolicy Bypass -File $launcher `
        -Root $rootPath `
        -BaseUrl $BaseUrl `
        -DurationSeconds 1 `
        -PollMilliseconds 250 `
        -MinAcceptedTops 1 `
        -MinCounterDelta 1 `
        -MinPhysicalObservations 1 `
        -RequiredGoodLanes "1,4" `
        -ObservationCsvPath $templateCsv `
        -OutputPath $templateReport

    if ($LASTEXITCODE -ne 0) {
        throw "Watcher validation terrain template termine avec code $LASTEXITCODE."
    }

    $templateRows = @(Import-Csv -LiteralPath $templateCsv)
    Assert-True ($templateRows.Count -eq 2) "Watcher validation terrain: template CSV doit contenir les voies requises."
    Assert-True (($templateRows | Where-Object { $_.expected_lane -eq "1" } | Measure-Object).Count -eq 1) "Watcher validation terrain: template voie 1 absent."
    Assert-True (($templateRows | Where-Object { $_.expected_lane -eq "4" } | Measure-Object).Count -eq 1) "Watcher validation terrain: template voie 4 absent."
    Assert-True (($templateRows | Where-Object { -not [string]::IsNullOrWhiteSpace($_.observed_lane) } | Measure-Object).Count -eq 0) "Watcher validation terrain: template ne doit pas pre-remplir observed_lane."

    $forgedCoverageReport = Join-Path $tempDir "field_validation_forged_coverage.md"
    @(
        "# TriCell Pilot - rapport surveillance terrain",
        "",
        "- Lot: #$currentLotId PAUSED",
        "",
        "## Verdict structure",
        "",
        "- VERDICT_TRACE_LOGICIEL: OK",
        "- VERDICT_COMPTEURS_MACHINE: OK",
        "- VERDICT_OBSERVATION_PHYSIQUE: OK",
        "- VERDICT_COUVERTURE_VOIES_GOOD: OK",
        "",
        "## Conclusion automatique",
        "",
        "Preuve terrain complete: traces logiciel, compteurs machine et observations physiques sont coherents. Conserver rapport, CSV et video si disponible."
    ) | Set-Content -LiteralPath $forgedCoverageReport -Encoding UTF8

    $forgedCoverageExit = Invoke-ReportAssert $reportAssert $rootPath $forgedCoverageReport -SkipCurrentLotCheck
    Assert-True ($forgedCoverageExit -ne 0) "Verificateur rapport: un verdict couverture OK sans detail ne doit pas etre accepte."

    $completeReport = Join-Path $tempDir "field_validation_complete.md"
    @(
        "# TriCell Pilot - rapport surveillance terrain",
        "",
        "- Lot: #$($currentLotId + 1000) PAUSED",
        "",
        "## Verdict structure",
        "",
        "- VERDICT_TRACE_LOGICIEL: OK",
        "- VERDICT_COMPTEURS_MACHINE: OK",
        "- VERDICT_OBSERVATION_PHYSIQUE: OK",
        "- VERDICT_COUVERTURE_VOIES_GOOD: OK",
        "",
        "- Minimums effectifs: tops=1, compteurs=1, observations=1, voies GOOD=4",
        "- Tops 8230 acceptes: True lignes=1",
        "- Couverture voies GOOD requise: 4",
        "- Couverture voies GOOD observee: 4",
        "- Couverture voies GOOD manquante: aucune",
        "",
        "## Conclusion automatique",
        "",
        "Preuve terrain complete: traces logiciel, compteurs machine et observations physiques sont coherents. Conserver rapport, CSV et video si disponible."
    ) | Set-Content -LiteralPath $completeReport -Encoding UTF8

    $wrongLotExit = Invoke-ReportAssert $reportAssert $rootPath $completeReport
    Assert-True ($wrongLotExit -ne 0) "Verificateur rapport: un rapport complet d'un autre lot ne doit pas etre accepte."

    $currentLotReport = Join-Path $tempDir "field_validation_current_lot_complete.md"
    @(
        "# TriCell Pilot - rapport surveillance terrain",
        "",
        "- Lot: #$currentLotId PAUSED",
        "",
        "## Verdict structure",
        "",
        "- VERDICT_TRACE_LOGICIEL: OK",
        "- VERDICT_COMPTEURS_MACHINE: OK",
        "- VERDICT_OBSERVATION_PHYSIQUE: OK",
        "- VERDICT_COUVERTURE_VOIES_GOOD: OK",
        "",
        "- Minimums effectifs: tops=1, compteurs=1, observations=1, voies GOOD=4",
        "- Tops 8230 acceptes: True lignes=1",
        "- Couverture voies GOOD requise: 4",
        "- Couverture voies GOOD observee: 4",
        "- Couverture voies GOOD manquante: aucune",
        "",
        "## Conclusion automatique",
        "",
        "Preuve terrain complete: traces logiciel, compteurs machine et observations physiques sont coherents. Conserver rapport, CSV et video si disponible."
    ) | Set-Content -LiteralPath $currentLotReport -Encoding UTF8

    $completeExit = Invoke-ReportAssert $reportAssert $rootPath $currentLotReport
    Assert-True ($completeExit -eq 0) "Verificateur rapport: un rapport complet du lot courant doit etre accepte."

    $offlineCompleteExit = Invoke-ReportAssert $reportAssert $rootPath $completeReport -SkipCurrentLotCheck
    Assert-True ($offlineCompleteExit -eq 0) "Verificateur rapport: l'option test -SkipCurrentLotCheck doit accepter un rapport complet synthetique."

    $refreshCsv = Join-Path $tempDir "field_validation_refresh_observations.csv"
    @(
        "timestamp,handshake,expected_lane,observed_lane,ng_pulse_seen,operator,notes",
        "2026-06-04 20:20:01,10,1,1,oui,Regression,voie 1 conforme",
        "2026-06-04 20:20:02,11,4,4,oui,Regression,voie 4 conforme"
    ) | Set-Content -LiteralPath $refreshCsv -Encoding UTF8

    $refreshSource = Join-Path $tempDir "field_validation_refresh_source.md"
    @(
        "# TriCell Pilot - rapport surveillance terrain",
        "",
        "- Mode: lecture seule, aucune commande machine envoyee par ce script",
        "- Lot: #$currentLotId PAUSED",
        "",
        "## Etat final",
        "",
        "- Delta compteurs: total=2 good=2 ng=0",
        "",
        "## Preuves trace runtime",
        "",
        "- START_PRELOAD observe: True lignes=1",
        "- START envoye: True lignes=1",
        "- Tops 8230 acceptes: True lignes=2",
        "- Pistons GOOD: pilotes par PLC via seuils 1188..1370, aucune impulsion directe PC attendue en production.",
        "- Minimums effectifs: tops=1, compteurs=1, observations=1, voies GOOD=4",
        "",
        "## Verdict structure",
        "",
        "- VERDICT_TRACE_LOGICIEL: OK",
        "- VERDICT_COMPTEURS_MACHINE: OK",
        "- VERDICT_OBSERVATION_PHYSIQUE: INCOMPLET",
        "- VERDICT_COUVERTURE_VOIES_GOOD: INCOMPLET",
        "",
        "## Observations physiques operateur",
        "",
        "- Couverture voies GOOD requise: 4",
        "- Couverture voies GOOD observee: aucune",
        "- Couverture voies GOOD manquante: 4"
    ) | Set-Content -LiteralPath $refreshSource -Encoding UTF8

    $refreshOutput = Join-Path $tempDir "field_validation_refresh_output.md"
    & powershell -NoProfile -ExecutionPolicy Bypass -File $refreshReport `
        -Root $rootPath `
        -ReportPath $refreshSource `
        -ObservationCsvPath $refreshCsv `
        -OutputPath $refreshOutput `
        -RequiredGoodLanes "1,4" `
        -MinPhysicalObservations 1

    if ($LASTEXITCODE -ne 0) {
        throw "Actualisation rapport terrain terminee avec code $LASTEXITCODE."
    }

    $refreshText = [System.IO.File]::ReadAllText($refreshOutput, [System.Text.Encoding]::UTF8)
    Assert-True ($refreshText -match "rapport surveillance terrain actualise") "Actualiseur rapport: titre actualise absent."
    Assert-True ($refreshText -match "VERDICT_TRACE_LOGICIEL: OK") "Actualiseur rapport: trace source OK non conservee."
    Assert-True ($refreshText -match "VERDICT_COMPTEURS_MACHINE: OK") "Actualiseur rapport: compteur source OK non conserve."
    Assert-True ($refreshText -match "VERDICT_OBSERVATION_PHYSIQUE: OK") "Actualiseur rapport: CSV conforme non reconnu."
    Assert-True ($refreshText -match "VERDICT_COUVERTURE_VOIES_GOOD: OK") "Actualiseur rapport: couverture CSV conforme non reconnue."
    Assert-True ($refreshText -match "Minimums effectifs: tops=2, compteurs=2, observations=2, voies GOOD=1,4") "Actualiseur rapport: minimums effectifs rafraichis absents."
    Assert-True ($refreshText -match "Preuve terrain complete") "Actualiseur rapport: conclusion complete absente apres CSV conforme."

    $refreshAssertExit = Invoke-ReportAssert $reportAssert $rootPath $refreshOutput
    Assert-True ($refreshAssertExit -eq 0) "Verificateur rapport: un rapport actualise complet du lot courant doit etre accepte."

    $weakRefreshSource = Join-Path $tempDir "field_validation_refresh_weak_source.md"
    $weakText = $refreshText `
        -replace "rapport surveillance terrain actualise", "rapport surveillance terrain" `
        -replace "Mode: lecture seule fichier", "Mode: lecture seule" `
        -replace "Delta compteurs: total=2", "Delta compteurs: total=1" `
        -replace "Tops 8230 acceptes: True lignes=2", "Tops 8230 acceptes: True lignes=1" `
        -replace "Minimums effectifs: tops=2, compteurs=2, observations=2, voies GOOD=1,4", "Minimums effectifs: tops=1, compteurs=1, observations=1, voies GOOD=4"
    [System.IO.File]::WriteAllText($weakRefreshSource, $weakText, [System.Text.Encoding]::UTF8)

    $weakRefreshOutput = Join-Path $tempDir "field_validation_refresh_weak_output.md"
    & powershell -NoProfile -ExecutionPolicy Bypass -File $refreshReport `
        -Root $rootPath `
        -ReportPath $weakRefreshSource `
        -ObservationCsvPath $refreshCsv `
        -OutputPath $weakRefreshOutput `
        -RequiredGoodLanes "1,4" `
        -MinPhysicalObservations 1

    if ($LASTEXITCODE -ne 0) {
        throw "Actualisation rapport terrain source faible terminee avec code $LASTEXITCODE."
    }

    $weakRefreshText = [System.IO.File]::ReadAllText($weakRefreshOutput, [System.Text.Encoding]::UTF8)
    Assert-True ($weakRefreshText -match "VERDICT_TRACE_LOGICIEL: INCOMPLET") "Actualiseur rapport: source avec un seul handshake ne doit pas rester OK pour deux voies."
    Assert-True ($weakRefreshText -match "VERDICT_COMPTEURS_MACHINE: INCOMPLET") "Actualiseur rapport: source avec un seul compteur ne doit pas rester OK pour deux voies."
    $weakRefreshExit = Invoke-ReportAssert $reportAssert $rootPath $weakRefreshOutput
    Assert-True ($weakRefreshExit -ne 0) "Verificateur rapport: un rapport actualise depuis une preuve source trop faible ne doit pas etre accepte."

    Write-Host "FieldValidationWatcherRegression OK"
}
finally {
    if ($startedProcess -and -not $startedProcess.HasExited) {
        Stop-Process -Id $startedProcess.Id -Force -ErrorAction SilentlyContinue
    }

    if (Test-Path $tempDir) {
        Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
