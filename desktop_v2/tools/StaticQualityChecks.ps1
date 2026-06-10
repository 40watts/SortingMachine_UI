param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
)

$ErrorActionPreference = "Stop"

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw $Message
    }
}

function Read-Text {
    param([string]$Path)
    return [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
}

$desktop = Join-Path $Root "desktop_v2"
$app = Join-Path $desktop "app"
$web = Join-Path $app "web"
$docs = Join-Path $desktop "docs"

Assert-True (Test-Path $desktop) "desktop_v2 introuvable."
Assert-True (Test-Path $app) "desktop_v2/app introuvable."
Assert-True (Test-Path $web) "desktop_v2/app/web introuvable."
Assert-True (Test-Path $docs) "desktop_v2/docs introuvable."

$requiredDocs = @(
    "README.md",
    "OPERATOR_REQUIREMENTS.md",
    "desktop_v2/docs/code-quality-audit.md",
    "desktop_v2/docs/architecture.md",
    "desktop_v2/docs/quality-interval-routing.md",
    "desktop_v2/docs/ai-context.md",
    "desktop_v2/docs/operator-guide.md",
    "desktop_v2/docs/api-contract.md",
    "desktop_v2/docs/git-hygiene.md",
    "desktop_v2/docs/pre-git-readiness.md",
    "desktop_v2/tools/CiQualityChecks.ps1",
    "desktop_v2/tools/NoMachineQualityChecks.ps1",
    "test_desktop_v2_no_machine.bat",
    ".github/workflows/windows-ci.yml",
    ".gitignore",
    ".gitattributes",
    ".env.example"
)

foreach ($doc in $requiredDocs) {
    Assert-True (Test-Path (Join-Path $Root $doc)) "Documentation manquante: $doc"
}

$readme = Read-Text (Join-Path $Root "README.md")
Assert-True ($readme -match "desktop_v2") "README racine: desktop_v2 non reference."
Assert-True ($readme -match "test_desktop_v2\.bat") "README racine: quality gate non reference."
Assert-True ($readme -match "test_desktop_v2_no_machine\.bat") "README racine: quality gate sans machine non reference."
Assert-True ($readme -match "TRICELL_ALLOW_APP_SMOKE") "README racine: opt-in smoke applicatif non documente."
Assert-True ($readme -match "CiQualityChecks\.ps1") "README racine: CI safe non reference."
Assert-True ($readme -match "fenetre catch-all constructeur") "README racine: voie 11 NG catch-all non documentee."

$runDesktop = Read-Text (Join-Path $Root "run_desktop.bat")
$runApp = Read-Text (Join-Path $Root "run_app.bat")
$testDesktop = Read-Text (Join-Path $Root "test_desktop_v2.bat")
$testDesktopNoMachine = Read-Text (Join-Path $Root "test_desktop_v2_no_machine.bat")
$noMachineQuality = Read-Text (Join-Path $Root "desktop_v2\tools\NoMachineQualityChecks.ps1")
$fieldValidationBatPath = Join-Path $Root "validate_tricell_field.bat"
$fieldValidationRefreshBatPath = Join-Path $Root "refresh_tricell_field_result.bat"
$fieldValidationCheckBatPath = Join-Path $Root "check_tricell_field_result.bat"
Assert-True (Test-Path $fieldValidationBatPath) "Lanceur racine validation terrain manquant."
Assert-True (Test-Path $fieldValidationRefreshBatPath) "Lanceur racine actualisation rapport terrain manquant."
Assert-True (Test-Path $fieldValidationCheckBatPath) "Lanceur racine verification rapport terrain manquant."
$fieldValidationBat = Read-Text $fieldValidationBatPath
$fieldValidationRefreshBat = Read-Text $fieldValidationRefreshBatPath
$fieldValidationCheckBat = Read-Text $fieldValidationCheckBatPath
Assert-True ($runDesktop -match "desktop_v2\\bin\\TriCellPilot\.exe") "run_desktop.bat ne lance pas TriCellPilot."
Assert-True ($runDesktop -match "build_desktop_v2\.bat") "run_desktop.bat ne build pas desktop_v2."
Assert-True ($runApp -match "run_desktop\.bat") "run_app.bat doit rediriger vers l'application active."
Assert-True ($runApp -notmatch "uvicorn") "run_app.bat ne doit plus lancer l'ancien backend."
Assert-True ($testDesktop -match "test_desktop_v2_no_machine\.bat") "test_desktop_v2.bat doit lancer la gate no-machine par defaut."
Assert-True ($testDesktop -match "TRICELL_ALLOW_APP_SMOKE") "test_desktop_v2.bat doit demander un opt-in explicite avant tout smoke test applicatif."
Assert-True ($testDesktop -match "FieldValidationWatcherRegression\.ps1") "test_desktop_v2.bat doit verifier le watcher validation terrain."
Assert-True ($testDesktop -match "ApiSmokeCheck\.ps1") "test_desktop_v2.bat doit garder le smoke API uniquement en opt-in."
Assert-True ($testDesktop -match "can start desktop_v2\\bin\\TriCellPilot\.exe") "test_desktop_v2.bat doit avertir que les smoke tests peuvent lancer l'application."
Assert-True ($testDesktopNoMachine -match "NoMachineQualityChecks\.ps1") "test_desktop_v2_no_machine.bat doit appeler le quality gate sans machine."
Assert-True ($noMachineQuality -match "NgSweepSimulatorRegression") "NoMachineQualityChecks: regression NG sweep simulateur absente."
Assert-True ($noMachineQuality -match "PhysicalRoutingApiRegression") "NoMachineQualityChecks: regression API locale simulateur absente."
Assert-True ($noMachineQuality -match "ScannerHandshakeRegression") "NoMachineQualityChecks: regression handshake scanner absente."
Assert-True ($noMachineQuality -notmatch "ApiSmokeCheck") "NoMachineQualityChecks: ne doit pas lancer le smoke API connecte."
Assert-True ($noMachineQuality -notmatch "FieldValidationWatcherRegression") "NoMachineQualityChecks: ne doit pas lancer le watcher terrain."
Assert-True ($fieldValidationBat -match "Start-FieldValidationWatch\.ps1") "validate_tricell_field.bat doit appeler le lanceur PowerShell terrain."
Assert-True ($fieldValidationBat -match "aucune commande machine") "validate_tricell_field.bat doit annoncer le mode lecture seule."
Assert-True ($fieldValidationBat -notmatch "MinAcceptedTops 3") "validate_tricell_field.bat ne doit plus abaisser le minimum de tops 8230."
Assert-True ($fieldValidationBat -notmatch "MinCounterDelta 3") "validate_tricell_field.bat ne doit plus abaisser le minimum compteur."
Assert-True ($fieldValidationBat -notmatch "MinPhysicalObservations 5") "validate_tricell_field.bat ne doit plus abaisser le minimum observation."
Assert-True ($fieldValidationRefreshBat -match "Update-FieldValidationReportFromCsv\.ps1") "refresh_tricell_field_result.bat doit appeler l'actualiseur CSV."
Assert-True ($fieldValidationRefreshBat -match "aucune commande machine") "refresh_tricell_field_result.bat doit annoncer le mode lecture seule."
Assert-True ($fieldValidationCheckBat -match "Assert-FieldValidationReport\.ps1") "check_tricell_field_result.bat doit appeler le verificateur rapport terrain."
Assert-True ($fieldValidationCheckBat -match "couverture voies GOOD") "check_tricell_field_result.bat doit annoncer la couverture voies GOOD."
Assert-True ($fieldValidationCheckBat -match "lot courant correspond") "check_tricell_field_result.bat doit annoncer le controle du lot courant."

$operator = Read-Text (Join-Path $Root "OPERATOR_REQUIREMENTS.md")
$aiContext = Read-Text (Join-Path $docs "ai-context.md")
Assert-True ($operator -match "ligne 10") "Exigences operateur: ligne 10 non documentee."
Assert-True ($operator -match "9 intervalles") "Exigences operateur: 9 intervalles non documentes."
Assert-True ($operator -match "voie NG en fenetre catch-all") "Exigences operateur: voie NG catch-all non documentee."
Assert-True ($operator -match "RESET=26.*commande operateur explicite") "Exigences operateur: rearmement manuel RESET=26 non documente."
Assert-True ($operator -match 'DEMARRER.*sans envoyer `5978=26`') "Exigences operateur: DEMARRER doit bloquer sans reset automatique."
Assert-True ($operator -match "coil constructeur d.avance convoyeur") "Exigences operateur: avance convoyeur par coil constructeur non documentee."
Assert-True ($operator -match "START_PRELOAD") "Exigences operateur: prechargement seuils avant DEMARRER non documente."
Assert-True ($operator -match "1188..1370") "Exigences operateur: seuils machine de routage non documentes."
Assert-True ($operator -match "poussoir 11/NG.*a chaque avance") "Exigences operateur: balayage NG de fin de ligne non documente."
Assert-True ($operator -match "8408.*4 registres") "Exigences operateur: registre mesure constructeur 8408 longueur 4 non documente."
Assert-True ($operator -match "tests piston directs.*machine arretee") "Exigences operateur: tests piston maintenance machine arretee non documentes."
Assert-True ($operator -match 'resets `28295..28305`') "Exigences operateur: interdiction de pulser les resets piston non documentee."
Assert-True ($operator -match 'test `NG` utilise les registres manuels constructeur') "Exigences operateur: test piston NG par registres constructeur non documente."
Assert-True ($operator -match 'enable `28424` puis sortie `28936`') "Exigences operateur: registres du test piston NG non documentes."
Assert-True ($operator -match "Micro-avance tapis") "Exigences operateur: micro-avance tapis non documentee."
Assert-True ($operator -match "Y11 ON maintenu.*bloque") "Exigences operateur: Y11 ON maintenu doit etre bloque en reel."
Assert-True ($operator -match "liberation automatique du reset NG.*interdite pendant un cycle arme") "Exigences operateur: auto release NG doit etre interdit pendant cycle arme."
Assert-True ($operator -match "1X 5981") "Exigences operateur: micro-avance doit utiliser le coil constructeur 1X 5981."
Assert-True ($operator -match "Ne jamais simuler une micro-avance par une ecriture holding") "Exigences operateur: interdiction holding 5981 non documentee."
Assert-True ($operator -match "Avancer convoyeur") "Exigences operateur: degagement convoyeur avec balayage NG non documente."
Assert-True ($operator -match "validate_tricell_field\.bat") "Exigences operateur: lanceur terrain racine non documente."
Assert-True ($operator -match "refresh_tricell_field_result\.bat") "Exigences operateur: actualisation rapport terrain non documentee."
Assert-True ($operator -match "check_tricell_field_result\.bat") "Exigences operateur: verificateur rapport terrain non documente."
Assert-True ($operator -match "Start-FieldValidationWatch\.ps1") "Exigences operateur: lanceur terrain PowerShell non documente."
Assert-True ($operator -match "Watch-TriCellFieldValidation\.ps1") "Exigences operateur: validation terrain lecture seule non documentee."
Assert-True ($operator -match "Update-FieldValidationReportFromCsv\.ps1") "Exigences operateur: actualiseur CSV non documente."
Assert-True ($operator -match "verdict trace logiciel") "Exigences operateur: verdict trace logiciel non documente."
Assert-True ($operator -match "delta compteurs machine") "Exigences operateur: delta compteurs machine non documente."
Assert-True ($operator -match "couverture voies GOOD") "Exigences operateur: couverture voies GOOD non documentee."
Assert-True ($operator -match "1\.\.9") "Exigences operateur: couverture lignes GOOD 1..9 non documentee."
Assert-True ($operator -match "expected_lane") "Exigences operateur: observation ligne attendue non documentee."
Assert-True ($operator -match "observed_lane") "Exigences operateur: observation ligne vue non documentee."
Assert-True ($operator -match "ng_pulse_seen") "Exigences operateur: observation pulse NG non documentee."
Assert-True ($operator -match "lot courant de l'API") "Exigences operateur: controle rapport terrain vs lot courant non documente."

$operatorGuide = Read-Text (Join-Path $docs "operator-guide.md")
Assert-True ($operatorGuide -match "Validation terrain hors production") "Guide operateur: procedure validation terrain absente."
Assert-True ($operatorGuide -match "validate_tricell_field\.bat") "Guide operateur: lanceur terrain racine absent."
Assert-True ($operatorGuide -match "refresh_tricell_field_result\.bat") "Guide operateur: actualisation rapport terrain absente."
Assert-True ($operatorGuide -match "check_tricell_field_result\.bat") "Guide operateur: verificateur rapport terrain absent."
Assert-True ($operatorGuide -match "Start-FieldValidationWatch\.ps1") "Guide operateur: lanceur terrain PowerShell absent."
Assert-True ($operatorGuide -match "Watch-TriCellFieldValidation\.ps1") "Guide operateur: watcher validation terrain absent."
Assert-True ($operatorGuide -match "Update-FieldValidationReportFromCsv\.ps1") "Guide operateur: actualiseur CSV absent."
Assert-True ($operatorGuide -match "MinAcceptedTops") "Guide operateur: seuil minimum tops 8230 absent."
Assert-True ($operatorGuide -match "MinCounterDelta") "Guide operateur: seuil minimum compteur absent."
Assert-True ($operatorGuide -match "MinPhysicalObservations") "Guide operateur: seuil minimum observations physiques absent."
Assert-True ($operatorGuide -match "MinAcceptedTops=9") "Guide operateur: minimum tops 8230 doit suivre les 9 voies."
Assert-True ($operatorGuide -match "MinCounterDelta=9") "Guide operateur: minimum compteur doit suivre les 9 voies."
Assert-True ($operatorGuide -match "MinPhysicalObservations=9") "Guide operateur: minimum observations doit suivre les 9 voies."
Assert-True ($operatorGuide -match "RequiredGoodLanes=1,2,3,4,5,6,7,8,9") "Guide operateur: couverture voies GOOD 1..9 absente."
Assert-True ($operatorGuide -match "expected_lane") "Guide operateur: colonne expected_lane absente."
Assert-True ($operatorGuide -match "observed_lane") "Guide operateur: colonne observed_lane absente."
Assert-True ($operatorGuide -match "ng_pulse_seen") "Guide operateur: colonne ng_pulse_seen absente."
Assert-True ($operatorGuide -match "lot courant de l'API") "Guide operateur: controle rapport terrain vs lot courant absent."

$preGitReadiness = Read-Text (Join-Path $docs "pre-git-readiness.md")
Assert-True ($preGitReadiness -match "test_desktop_v2_no_machine\.bat") "Pre-git readiness: gate sans machine absent."
Assert-True ($preGitReadiness -match "TRICELL_ALLOW_APP_SMOKE") "Pre-git readiness: opt-in smoke applicatif absent."
Assert-True ($preGitReadiness -match "seuils PLC.*GOOD") "Pre-git readiness: routage GOOD par seuils PLC non documente."
Assert-True ($preGitReadiness -match "voie 11 NG en fenetre catch-all") "Pre-git readiness: voie 11 NG catch-all non documentee."
Assert-True ($preGitReadiness -notmatch "impulsions pistons directes") "Pre-git readiness: ancienne mention pistons directs interdite."

$codeQualityAudit = Read-Text (Join-Path $docs "code-quality-audit.md")
Assert-True ($codeQualityAudit -match "test_desktop_v2_no_machine\.bat") "Audit qualite: gate no-machine absent."
Assert-True ($codeQualityAudit -match "TRICELL_ALLOW_APP_SMOKE") "Audit qualite: opt-in smoke applicatif absent."
Assert-True ($codeQualityAudit -match "PhysicalRoutingApiRegression") "Audit qualite: regression API routage physique absente."
Assert-True ($codeQualityAudit -match "NgSweepSimulatorRegression") "Audit qualite: regression balayage NG absente."
Assert-True ($codeQualityAudit -match "BLOCKED_GOOD_PLC") "Audit qualite: blocage GOOD direct non documente."
Assert-True ($codeQualityAudit -match "registres manuels constructeur") "Audit qualite: test NG par registres constructeur non documente."
Assert-True ($codeQualityAudit -notmatch "smoke test API sur") "Audit qualite: ancien smoke API comme gate sans machine interdit."

$dossierMaitre = "C:\Users\Administrator\Desktop\Analyse_Automate_Tri_Cellule\Dossier_Maitre"
if (Test-Path $dossierMaitre) {
    $dossierReadme = Read-Text (Join-Path $dossierMaitre "README.md")
    $dossierValidation = Read-Text (Join-Path $dossierMaitre "validation_tricell_routage_atelier_2026-06-05.md")
    $dossierSpec = Read-Text (Join-Path $dossierMaitre "spec_tri_intelligent_good_ng.md")
    Assert-True ($dossierReadme -match "validation_tricell_routage_atelier_2026-06-05\.md") "Dossier maitre: journal routage atelier 2026-06-05 non indexe."
    Assert-True ($dossierValidation -match "validate_tricell_field\.bat 180") "Dossier maitre: lanceur validation terrain non documente."
    Assert-True ($dossierValidation -match "refresh_tricell_field_result\.bat") "Dossier maitre: actualisation rapport terrain non documentee."
    Assert-True ($dossierValidation -match "check_tricell_field_result\.bat") "Dossier maitre: verification rapport terrain non documentee."
    Assert-True ($dossierValidation -match "MatchesCurrentLot") "Dossier maitre: verrou lot courant du rapport terrain absent."
    Assert-True ($dossierValidation -match "refuse un rapport.*Lot: #") "Dossier maitre: refus rapport mauvais lot par le verificateur absent."
    Assert-True ($dossierValidation -match "VERDICT_COUVERTURE_VOIES_GOOD: OK") "Dossier maitre: verdict couverture voies GOOD absent du journal."
    Assert-True ($dossierValidation -match "Update-FieldValidationReportFromCsv\.ps1") "Dossier maitre: actualiseur CSV absent du journal."
    Assert-True ($dossierValidation -match "MinNgSweepSent=9") "Dossier maitre: minimum NG sweep 9 absent du journal."
    Assert-True ($dossierValidation -match "MinCounterDelta=9") "Dossier maitre: minimum compteur 9 absent du journal."
    Assert-True ($dossierValidation -match "MinPhysicalObservations=9") "Dossier maitre: minimum observations 9 absent du journal."
    Assert-True ($dossierValidation -match "FieldValidation=NO_REPORT") "Dossier maitre: etat live FieldValidation non trace."
    Assert-True ($dossierValidation -match "test_desktop_v2_no_machine\.bat") "Dossier maitre: preuve PC doit reference le gate sans machine."
    Assert-True ($dossierValidation -notmatch "ApiSmokeCheck OK") "Dossier maitre: ApiSmokeCheck ne doit pas etre presente comme preuve no-machine courante."
    Assert-True ($dossierSpec -match "Verin NG en production") "Dossier maitre: verin NG en production par PLC non documente."
    Assert-True ($dossierSpec -match "FieldValidation") "Dossier maitre: diagnostic validation terrain absent de la spec."
    Assert-True ($dossierSpec -match "VERDICT_TRACE_LOGICIEL: OK") "Dossier maitre: verdict trace logiciel absent de la spec."
    Assert-True ($dossierSpec -match "VERDICT_COUVERTURE_VOIES_GOOD: OK") "Dossier maitre: verdict couverture voies GOOD absent de la spec."
    Assert-True ($dossierSpec -match "PLC pilote physiquement les voies GOOD") "Dossier maitre: GOOD par PLC non documente."
    Assert-True ($dossierSpec -match "8230=0" -and $dossierSpec -match "8230=2" -and $dossierSpec -match "NoBarcodeValue") "Dossier maitre: handshake scanner constructeur non documente."
    Assert-True ($dossierSpec -match "test_desktop_v2_no_machine\.bat") "Dossier maitre: tests obligatoires doivent pointer vers no-machine."
    Assert-True ($dossierSpec -match 'enable `28424` puis sortie `28936`') "Dossier maitre: test NG par registres constructeur non documente."
    Assert-True ($dossierSpec -notmatch "pulse directement le piston GOOD") "Dossier maitre: ancienne production GOOD directe interdite."
    Assert-True ($dossierSpec -notmatch "coupe en defensif") "Dossier maitre: ancienne coupure defensive 28424/28936 interdite."
}

$fieldWatcherPath = Join-Path $desktop "tools\Watch-TriCellFieldValidation.ps1"
Assert-True (Test-Path $fieldWatcherPath) "Watcher validation terrain manquant."
$fieldWatcher = Read-Text $fieldWatcherPath
Assert-True ($fieldWatcher -match "VERDICT_TRACE_LOGICIEL") "Watcher validation terrain: verdict trace logiciel absent."
Assert-True ($fieldWatcher -match "VERDICT_COMPTEURS_MACHINE") "Watcher validation terrain: verdict compteurs absent."
Assert-True ($fieldWatcher -match "VERDICT_OBSERVATION_PHYSIQUE") "Watcher validation terrain: verdict observation physique absent."
Assert-True ($fieldWatcher -match "VERDICT_COUVERTURE_VOIES_GOOD") "Watcher validation terrain: verdict couverture voies GOOD absent."
Assert-True ($fieldWatcher -match "MinAcceptedTops") "Watcher validation terrain: seuil tops 8230 absent."
Assert-True ($fieldWatcher -match "MinCounterDelta") "Watcher validation terrain: seuil compteur absent."
Assert-True ($fieldWatcher -match "MinPhysicalObservations") "Watcher validation terrain: seuil observations physiques absent."
Assert-True ($fieldWatcher -match "LastNgPulse\.OutputPath") "Watcher validation terrain: rapport NG doit afficher le chemin Y11 explicite."
Assert-True ($fieldWatcher -match "effectiveMinAcceptedTops") "Watcher validation terrain: minimum effectif tops absent."
Assert-True ($fieldWatcher -match "effectiveMinCounterDelta") "Watcher validation terrain: minimum effectif compteur absent."
Assert-True ($fieldWatcher -match "effectiveMinPhysicalObservations") "Watcher validation terrain: minimum effectif observation absent."
Assert-True ($fieldWatcher -match "RequiredGoodLanes") "Watcher validation terrain: couverture voies GOOD requises absente."
Assert-True ($fieldWatcher -match "MissingGoodLanes") "Watcher validation terrain: voies GOOD manquantes absentes."
Assert-True ($fieldWatcher -match "ObservationCsvPath") "Watcher validation terrain: chemin CSV observations absent."
Assert-True ($fieldWatcher -match "expected_lane,observed_lane,ng_pulse_seen") "Watcher validation terrain: colonnes observations physiques absentes."
Assert-True ($fieldWatcher -match "Fiche CSV initialisee") "Watcher validation terrain: creation template CSV voies GOOD absente."
Assert-True ($fieldWatcher -match "a completer voie") "Watcher validation terrain: lignes template voie a completer absentes."

$fieldWatcherLauncherPath = Join-Path $desktop "tools\Start-FieldValidationWatch.ps1"
Assert-True (Test-Path $fieldWatcherLauncherPath) "Lanceur PowerShell validation terrain manquant."
$fieldWatcherLauncher = Read-Text $fieldWatcherLauncherPath
Assert-True ($fieldWatcherLauncher -match "Watch-TriCellFieldValidation\.ps1") "Lanceur validation terrain: watcher non appele."
Assert-True ($fieldWatcherLauncher -match "LotControlEnabled") "Lanceur validation terrain: controle cycle deja actif absent."
Assert-True ($fieldWatcherLauncher -match "sans commande machine") "Lanceur validation terrain: message lecture seule absent."
Assert-True ($fieldWatcherLauncher -match '\[int\]\$MinAcceptedTops = 9') "Lanceur validation terrain: minimum tops 9 absent."
Assert-True ($fieldWatcherLauncher -match '\[int\]\$MinCounterDelta = 9') "Lanceur validation terrain: minimum compteur 9 absent."
Assert-True ($fieldWatcherLauncher -match '\[int\]\$MinPhysicalObservations = 9') "Lanceur validation terrain: minimum observation 9 absent."
Assert-True ($fieldWatcherLauncher -match "RequiredGoodLanes") "Lanceur validation terrain: couverture voies GOOD non relayee."

$fieldReportAssertPath = Join-Path $desktop "tools\Assert-FieldValidationReport.ps1"
Assert-True (Test-Path $fieldReportAssertPath) "Verificateur rapport terrain manquant."
$fieldReportAssert = Read-Text $fieldReportAssertPath
Assert-True ($fieldReportAssert -match "VERDICT_TRACE_LOGICIEL: OK") "Verificateur rapport terrain: controle trace absent."
Assert-True ($fieldReportAssert -match "VERDICT_COMPTEURS_MACHINE: OK") "Verificateur rapport terrain: controle compteurs absent."
Assert-True ($fieldReportAssert -match "VERDICT_OBSERVATION_PHYSIQUE: OK") "Verificateur rapport terrain: controle observation absent."
Assert-True ($fieldReportAssert -match "VERDICT_COUVERTURE_VOIES_GOOD: OK") "Verificateur rapport terrain: controle couverture voies GOOD absent."
Assert-True ($fieldReportAssert -match "Assert-LaneCoverageDetails") "Verificateur rapport terrain: controle detail couverture voies GOOD absent."
Assert-True ($fieldReportAssert -match "Couverture voies GOOD requise") "Verificateur rapport terrain: lecture voies requises absente."
Assert-True ($fieldReportAssert -match "Couverture voies GOOD manquante") "Verificateur rapport terrain: lecture voies manquantes absente."
Assert-True ($fieldReportAssert -match "Minimums effectifs") "Verificateur rapport terrain: controle minimums effectifs absent."
Assert-True ($fieldReportAssert -match "Get-ReportLotId") "Verificateur rapport terrain: controle lot absent."
Assert-True ($fieldReportAssert -match "Get-CurrentLotId") "Verificateur rapport terrain: lecture lot courant API absente."
Assert-True ($fieldReportAssert -match "SkipCurrentLotCheck") "Verificateur rapport terrain: option test controle lot absente."
Assert-True ($fieldReportAssert -match "Rapport terrain du mauvais lot") "Verificateur rapport terrain: refus mauvais lot absent."
Assert-True ($fieldReportAssert -match "Preuve terrain complete") "Verificateur rapport terrain: conclusion complete absente."
Assert-True ($fieldReportAssert -match "field_validation_codex") "Verificateur rapport terrain: exclusion smoke Codex absente."

$fieldReportRefreshPath = Join-Path $desktop "tools\Update-FieldValidationReportFromCsv.ps1"
Assert-True (Test-Path $fieldReportRefreshPath) "Actualiseur rapport terrain manquant."
$fieldReportRefresh = Read-Text $fieldReportRefreshPath
Assert-True ($fieldReportRefresh -match "VERDICT_TRACE_LOGICIEL") "Actualiseur rapport terrain: verdict trace absent."
Assert-True ($fieldReportRefresh -match "VERDICT_COMPTEURS_MACHINE") "Actualiseur rapport terrain: verdict compteur absent."
Assert-True ($fieldReportRefresh -match "VERDICT_OBSERVATION_PHYSIQUE") "Actualiseur rapport terrain: verdict observation absent."
Assert-True ($fieldReportRefresh -match "VERDICT_COUVERTURE_VOIES_GOOD") "Actualiseur rapport terrain: verdict couverture absent."
Assert-True ($fieldReportRefresh -match "ObservationCsvPath") "Actualiseur rapport terrain: chemin CSV absent."
Assert-True ($fieldReportRefresh -match "acceptedTopsCount") "Actualiseur rapport terrain: controle tops 8230 source absent."
Assert-True ($fieldReportRefresh -match "deltaTotal") "Actualiseur rapport terrain: controle delta compteur source absent."
Assert-True ($fieldReportRefresh -match "Rapport source") "Actualiseur rapport terrain: lien rapport source absent."
Assert-True ($fieldReportRefresh -match "aucune commande machine envoyee") "Actualiseur rapport terrain: garantie lecture seule absente."

$fieldWatcherRegressionPath = Join-Path $desktop "tools\FieldValidationWatcherRegression.ps1"
Assert-True (Test-Path $fieldWatcherRegressionPath) "Regression watcher validation terrain manquante."
$fieldWatcherRegression = Read-Text $fieldWatcherRegressionPath
Assert-True ($fieldWatcherRegression -match "Start-FieldValidationWatch\.ps1") "Regression watcher: lanceur terrain non teste."
Assert-True ($fieldWatcherRegression -match "Assert-FieldValidationReport\.ps1") "Regression watcher: verificateur rapport non teste."
Assert-True ($fieldWatcherRegression -match "VERDICT_TRACE_LOGICIEL: INCOMPLET") "Regression watcher: controle verdict trace absent."
Assert-True ($fieldWatcherRegression -match "VERDICT_OBSERVATION_PHYSIQUE: OK") "Regression watcher: controle verdict physique absent."
Assert-True ($fieldWatcherRegression -match "VERDICT_COUVERTURE_VOIES_GOOD: INCOMPLET") "Regression watcher: couverture voies GOOD manquante non testee."
Assert-True ($fieldWatcherRegression -match "forgedCoverageExit") "Regression watcher: verdict couverture forge sans detail non teste."
Assert-True ($fieldWatcherRegression -match "template_observations\.csv") "Regression watcher: template CSV observations non teste."
Assert-True ($fieldWatcherRegression -match "template voie 1 absent") "Regression watcher: template voie requise non verifie."
Assert-True ($fieldWatcherRegression -match "-notmatch `"Preuve terrain complete`"") "Regression watcher: controle anti-faux succes absent."
Assert-True ($fieldWatcherRegression -match "wrongLotExit") "Regression watcher: mauvais lot rapport non teste."
Assert-True ($fieldWatcherRegression -match "currentLotReport") "Regression watcher: bon lot courant non teste."
Assert-True ($fieldWatcherRegression -match "Update-FieldValidationReportFromCsv\.ps1") "Regression watcher: actualiseur CSV non teste."
Assert-True ($fieldWatcherRegression -match "weakRefreshExit") "Regression watcher: source actualisee trop faible non testee."
Assert-True ($fieldWatcherRegression -match "Pistons GOOD: pilotes par PLC") "Regression watcher: mode GOOD par seuils PLC non teste."

$physicalRoutingRegressionPath = Join-Path $desktop "tools\PhysicalRoutingApiRegression.cs"
Assert-True (Test-Path $physicalRoutingRegressionPath) "Regression API routage physique manquante."
$physicalRoutingRegression = Read-Text $physicalRoutingRegressionPath

$ngSweepRegressionPath = Join-Path $desktop "tools\NgSweepSimulatorRegression.cs"
Assert-True (Test-Path $ngSweepRegressionPath) "Regression balayage NG simulateur manquante."
$ngSweepRegression = Read-Text $ngSweepRegressionPath
Assert-True ($ngSweepRegression -match "aucun balayage NG PC en production") "Regression NG: l'absence de balayage NG PC doit etre verifiee."
Assert-True ($ngSweepRegression -match "y compris autour de STOP/START/PAUSE") "Regression NG: l'absence de balayage doit etre verifiee autour de STOP/START/PAUSE."
Assert-True ($ngSweepRegression -match "voie NG physique en catch-all constructeur") "Regression NG: la voie 11 catch-all doit etre verifiee."
Assert-True ($ngSweepRegression -match "PLC_THRESHOLDS_NG_CATCHALL") "Regression NG: le mode routage catch-all doit etre verifie."
Assert-True ($ngSweepRegression -match "START arme seul") "Regression NG sweep: test piston doit bloquer si seul START reste arme."
Assert-True ($ngSweepRegression -match "lotControl seul") "Regression NG sweep: test piston doit bloquer si seul le controle lot reste actif."
Assert-True ($ngSweepRegression -match "convoyeur maintenance bloque si START arme seul") "Regression NG sweep: convoyeur maintenance doit bloquer si seul START reste arme."
Assert-True ($ngSweepRegression -match "convoyeur maintenance bloque si lotControl seul") "Regression NG sweep: convoyeur maintenance doit bloquer si seul le controle lot reste actif."
Assert-True ($ngSweepRegression -match "auto release NG bloquee si START arme seul") "Regression NG sweep: auto release NG doit bloquer si seul START reste arme."
Assert-True ($ngSweepRegression -match "auto release NG bloquee si lotControl seul") "Regression NG sweep: auto release NG doit bloquer si seul le controle lot reste actif."
Assert-True ($ngSweepRegression -match "Y11 ON maintenu bloque en reel") "Regression NG sweep: Y11 ON maintenu doit etre bloque en reel."
Assert-True ($ngSweepRegression -match "AssertCycleCommandCodesMatchConstructorButtons") "Regression NG sweep: codes boutons constructeur START/STOP/PAUSE/RESET non testes."
Assert-True ($ngSweepRegression -match '"START", "31"' -and $ngSweepRegression -match '"PAUSE", "32"' -and $ngSweepRegression -match '"STOP", "29"' -and $ngSweepRegression -match '"RESET", "26"') "Regression NG sweep: valeurs cycle 5978 constructeur incompletes."
Assert-True ($ngSweepRegression -match "START sans reset cache") "Regression NG sweep: START doit prouver l'absence de RESET=26 cache."

$scannerHandshakeRegressionPath = Join-Path $desktop "tools\ScannerHandshakeRegression.cs"
Assert-True (Test-Path $scannerHandshakeRegressionPath) "Regression handshake scanner manquante."
$scannerHandshakeRegression = Read-Text $scannerHandshakeRegressionPath
Assert-True ($physicalRoutingRegression -match "field_validation_codex") "Regression API: exclusion rapports smoke Codex non testee."
Assert-True ($physicalRoutingRegression -match '"NO_REPORT"') "Regression API: etat FieldValidation NO_REPORT non teste."
Assert-True ($physicalRoutingRegression -match '"INCOMPLETE"') "Regression API: etat FieldValidation INCOMPLETE non teste."
Assert-True ($physicalRoutingRegression -match '"COMPLETE"') "Regression API: etat FieldValidation COMPLETE non teste."
Assert-True ($physicalRoutingRegression -match "field_validation_wrong_lot_complete") "Regression API: rapport complet mauvais lot non teste."
Assert-True ($physicalRoutingRegression -match "field_validation_forged_coverage_complete") "Regression API: rapport couverture forge non teste."
Assert-True ($physicalRoutingRegression -match "MatchesCurrentLot") "Regression API: coherence lot rapport terrain non testee."
Assert-True ($physicalRoutingRegression -match "LaneCoverageVerdict") "Regression API: verdict couverture voies GOOD non teste."
Assert-True ($physicalRoutingRegression -match "PLC_THRESHOLDS_NG_CATCHALL") "Regression API: mode PLC seuils + NG catch-all non teste."
Assert-True ($physicalRoutingRegression -match "migration registre mesure 8402 vers 8408") "Regression API: migration registre mesure 8402 vers 8408 non testee."
Assert-True ($physicalRoutingRegression -match "START_RAW pas valide") "Regression API: START_RAW ne doit pas etre dans les commandes validees."
Assert-True ($physicalRoutingRegression -match "START_RAW expert") "Regression API: START_RAW doit rester en diagnostic expert."
Assert-True ($scannerHandshakeRegression -match "SIMULATED_0" -and $scannerHandshakeRegression -match "SIMULATED_2") "Regression scanner: reponses 8230=0/2 non testees."
Assert-True ($scannerHandshakeRegression -match "CON ne nourrit pas apprentissage") "Regression scanner: CON doit rester hors apprentissage qualite."
Assert-True ($scannerHandshakeRegression -match "resultat audit CON conserve") "Regression scanner: l'audit doit conserver CON et ne pas l'aplatir en NG."
Assert-True ($scannerHandshakeRegression -match "SCANNER_FALLBACK") "Regression scanner: source fallback scanner non testee."

$gitignore = Read-Text (Join-Path $Root ".gitignore")
foreach ($protected in @("desktop_v2/bin/", "desktop_v2/app/data/", "desktop_app/", "backend/", "frontend/", "webview2_pkg/", "webview2.zip", "webview2.nupkg", "ODOO.txt", "odoo_config.json", ".env.*", "*.corrupt_*")) {
    Assert-True ($gitignore -match [regex]::Escape($protected)) ".gitignore ne protege pas: $protected"
}

$ciWorkflow = Read-Text (Join-Path $Root ".github/workflows/windows-ci.yml")
Assert-True ($ciWorkflow -match [regex]::Escape("CiQualityChecks.ps1")) "CI: workflow Windows doit executer CiQualityChecks.ps1."
Assert-True ($ciWorkflow -notmatch [regex]::Escape("ApiSmokeCheck.ps1")) "CI: ne doit pas lancer ApiSmokeCheck, qui peut demarrer l'application locale."

foreach ($secretName in @("ODOO.txt", "odoo_config.json", ".env")) {
    $matches = Get-ChildItem -Path $Root -Recurse -Force -File -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -eq $secretName -and
            $_.FullName -notmatch "\\desktop_v2\\bin\\" -and
            $_.FullName -notmatch "\\desktop_app\\" -and
            $_.FullName -notmatch "\\backend\\" -and
            $_.FullName -notmatch "\\frontend\\" -and
            $_.FullName -notmatch "\\webview2_pkg\\"
        }
    Assert-True (($matches | Measure-Object).Count -eq 0) "Secret local detecte dans le depot: $secretName"
}

$machineState = Read-Text (Join-Path $app "MachineState.cs")
$program = Read-Text (Join-Path $app "Program.cs")
$models = Read-Text (Join-Path $app "Models.cs")
$apiServer = Read-Text (Join-Path $app "ApiServer.cs")
$apiCatalog = Read-Text (Join-Path $app "ApiContractCatalog.cs")
$apiContract = Read-Text (Join-Path $docs "api-contract.md")
$configStore = Read-Text (Join-Path $app "ConfigStore.cs")
$modbusClient = Read-Text (Join-Path $app "ModbusRtuClient.cs")
$writeHoldingSingleBody = [regex]::Match(
    $machineState,
    "private bool WriteHoldingSingleNoLock[\s\S]*?(?=\r?\n\s*private bool WriteCoilSingleNoLock)"
).Value
$releaseNgPistonBody = [regex]::Match(
    $machineState,
    "private MaintenanceCommandResult ExecuteNgPusherReleaseNoLock[\s\S]*?(?=\r?\n\s*private MaintenanceCommandResult ExecuteNgPusherDiagnosticPulseNoLock)"
).Value
$writePistonIoSingleBody = [regex]::Match(
    $machineState,
    "private bool WritePistonIoSingleNoLock[\s\S]*?(?=\r?\n\s*private bool WritePistonIoMaintenanceSingleNoLock)"
).Value
$startRawBlock = [regex]::Match(
    $machineState,
    "if \(normalized == `"START_RAW`"\)[\s\S]*?(?=\r?\n\s*if \(normalized == `"START`" \|\| normalized == `"STOP`")"
).Value
$maintenanceSnapshotBody = [regex]::Match(
    $machineState,
    "private MaintenanceSnapshot BuildMaintenanceSnapshotNoLock[\s\S]*?(?=\r?\n\s*private static bool IsY11OutputImageBitSet)"
).Value
$validatedMaintenanceBlock = [regex]::Match(
    $maintenanceSnapshotBody,
    "ValidatedCommands = new List<MaintenanceCommandDefinition>[\s\S]*?(?=\r?\n\s*ExpertCommands =)"
).Value
$expertMaintenanceBlock = [regex]::Match(
    $maintenanceSnapshotBody,
    "ExpertCommands = new List<MaintenanceCommandDefinition>[\s\S]*?(?=\r?\n\s*\})"
).Value
Assert-True ($machineState -notmatch "au moins 5 cellules") "MachineState: l'ancien minimum terrain 5 cellules ne doit plus apparaitre."
Assert-True ($machineState -match "au moins 9 cellules") "MachineState: le pre-vol doit rappeler le minimum terrain 9 cellules."
Assert-True ($machineState -match 'case "RESET":') "MachineState: RESET manuel doit etre une commande cycle explicite."
Assert-True ($machineState -match "EnsureStoppedBeforeResetNoLock") "MachineState: REARMER doit envoyer STOP avant RESET si la machine est en marche."
Assert-True ($machineState -match "RESET_AUTO_STOP") "MachineState: trace du STOP prealable au rearmement absente."
Assert-True ($machineState -match "PUSHER_STATIONS_ENABLE") "MachineState: commande armement stations pistons absente."
Assert-True ($machineState -match "PUSHER_STATIONS_AUTO_ARM") "MachineState: armement automatique des stations pistons absent."
Assert-True ($machineState -match "ArmPusherStationsNoLock\(_config, `"START`"\)") "MachineState: DEMARRER doit armer les stations pistons automatiquement."
Assert-True ($machineState -match "ArmPusherStationsNoLock\(cfg, `"CONNECT`"\)") "MachineState: la connexion doit armer les stations pistons automatiquement."
Assert-True ($machineState -match "Stations pistons d") "MachineState: avertissement stations desarmees absent du pre-vol."
Assert-True ($machineState -match "StartCycleCode = 31") "MachineState: code START constructeur 5978=31 absent."
Assert-True ($machineState -match "StopCycleCode = 29") "MachineState: code STOP constructeur 5978=29 absent."
Assert-True ($machineState -match "PauseCycleCode = 32") "MachineState: code PAUSE constructeur 5978=32 absent."
Assert-True ($machineState -match "ResetCycleCode = 26") "MachineState: code RESET constructeur 5978=26 absent."
Assert-True ($machineState -match '"START", "BLOCKED"') "MachineState: START doit etre bloque clairement si le statut 7 reste actif."
Assert-True ($machineState -match 'Statut automate 7: r.?armement requis avant D.?MARRER\.') "MachineState: statut 7 doit imposer un rearmement clair avant DEMARRER."
Assert-True ($machineState -match "Statut automate 7 avec securite bloquante") "MachineState: statut 7 doit continuer a exposer la cause d'alarme si une securite bloque."
Assert-True ($machineState -match [regex]::Escape("return requiresReset;")) "MachineState: START doit etre bloque des que le statut 7 est lu."
Assert-True ($machineState -notmatch "START_AUTO_RESET") "MachineState: START ne doit plus tenter de reset automatique."
Assert-True ($machineState -notmatch "WaitForResetRequiredToClearNoLock") "MachineState: ancienne attente apres reset automatique a retirer."
Assert-True ($machineState -notmatch "START.*ResetCycleCode") "MachineState: START ne doit pas envoyer RESET=26."
Assert-True ($machineState -notmatch "CYCLE_RESET") "MachineState: REARMER/RESET ne doit pas passer par la liberation reset NG ni ecrire un registre piston."
Assert-True ($machineState -notmatch 'result\.Command == "START"\s*\|\|\s*result\.Command == "RESET"') "MachineState: la preparation piston NG avant cycle doit rester limitee a DEMARRER."
Assert-True ($machineState -notmatch "ConfirmStartMotionNoLock") "MachineState: START ne doit pas bloquer le chemin critique par une confirmation synchrone."
Assert-True ($machineState -notmatch '"START_CONFIRM"') "MachineState: START ne doit pas retourner en echec apres avoir envoye 5978=31."
Assert-True ($machineState -notmatch "CycleCommandPulseMs") "MachineState: plus aucune impulsion cycle avec relachement ne doit exister."
Assert-True ($machineState -match "WRITE_ONCE") "MachineState: les commandes cycle doivent etre ecrites une seule fois comme le logiciel chinois."
Assert-True ($machineState -match "ConnectInitCode = 57") "MachineState: initialisation constructeur 5978=57 absente."
Assert-True ($machineState -match "CONNECT_INIT") "MachineState: trace initialisation connexion constructeur absente."
Assert-True ($program -match "TriCellPilot_SingleInstance") "Program: verrou mono-instance absent."
Assert-True ($program -match "Environment\.Exit\(0\)") "Program: garantie anti-zombie absente."
Assert-True ($program -match "state\.Shutdown\(\)") "Program: liberation du port serie a la fermeture absente."
Assert-True ($machineState -match "SendCycleCommandNoLock") "MachineState: les commandes cycle doivent garder un chemin dedie."
Assert-True ($machineState -notmatch "Aucun lot Odoo vérifié associé au lot courant") "MachineState: l'absence de lot Odoo ne doit pas bloquer DEMARRER."
Assert-True ($machineState -match "locale uniquement") "MachineState: l'absence Odoo doit etre signalee comme tracabilite locale."
Assert-True ($machineState -notmatch "SendCyclePulseNoLock") "MachineState: aucun ancien helper de pulse cycle ne doit revenir."
Assert-True ($machineState -match "crite une seule fois comme le logiciel chinois") "MachineState: l'ecriture unique des commandes cycle doit etre tracee."
Assert-True ($machineState -notmatch '"COMMAND",\s*commandName,\s*"WRITE_ONLY"') "MachineState: les commandes cycle ne doivent plus rester en write-only."
Assert-True ($machineState -match '"THRESHOLDS",\s*"SAVE_CHANNEL",\s*writeOk \? "WRITE_ONCE"') "MachineState: Save channel 59 doit etre ecrit une seule fois."
Assert-True ($machineState -match "Save channel 59 appliqu") "MachineState: Save channel 59 doit annoncer l'impulsion appliquee."
Assert-True ($machineState -match "START_RAW") "MachineState: un diagnostic START constructeur brut doit rester disponible."
Assert-True ($machineState -match "brut constructeur") "MachineState: START_RAW doit etre visible et explicite pour l'operateur."
Assert-True ($startRawBlock -match '"BLOCKED"') "MachineState: START_RAW doit etre bloque en reel."
Assert-True ($startRawBlock -notmatch "SendCycleCommandNoLock\(_config, StartCycleCode, normalized\)") "MachineState: START_RAW ne doit pas envoyer 5978=31 en reel."
Assert-True ($validatedMaintenanceBlock -notmatch 'Command = "START_RAW"') "Maintenance: START_RAW ne doit pas etre une commande validee operateur."
Assert-True ($expertMaintenanceBlock -match 'Command = "START_RAW"') "Maintenance: START_RAW doit rester visible seulement en diagnostic expert."
Assert-True ($machineState -match [regex]::Escape('_suspendThresholdSyncUntil = DateTime.Now.AddSeconds(result.Command == "RESET" ? 4 : 2);')) "MachineState: les commandes cycle doivent suspendre brievement la resynchro apres envoi."
Assert-True ($machineState -match [regex]::Escape('ApplyCycleCommandNoLock(result.Command);')) "MachineState: START doit armer le logiciel sans attendre une confirmation bloquante."
Assert-True ($machineState -match "TryPreloadThresholdsBeforeStartNoLock") "MachineState: START doit precharger les seuils machine avant la commande cycle."
Assert-True ($machineState -match "START_PRELOAD") "MachineState: la trace START_PRELOAD doit rester visible pour diagnostiquer le routage automate."
Assert-True ($machineState -match "BuildProgrammableThresholdsNoLock\(cfg, localThresholds, activeLot\)") "MachineState: START_PRELOAD doit programmer le meme modele de seuils que le routage actif."
Assert-True ($machineState -match "CanPreloadThresholdsBeforeStartNoLock") "MachineState: le pre-vol START doit verifier si START_PRELOAD peut preparer les seuils."
Assert-True ($machineState -notmatch 'blockingReasons\.Add\("Mod.le de tri non stable') "MachineState: l'apprentissage ligne 10 ne doit pas bloquer DEMARRER."
Assert-True ($machineState -match 'warnings\.Add\("Mod.le en apprentissage: START_PRELOAD utilisera la ligne 10') "MachineState: l'apprentissage ligne 10 doit etre un avertissement pre-vol."
Assert-True ($machineState -match "START_PRELOAD reprogrammera 1188\.\.1370 avant 5978=31") "MachineState: les seuils non confirmes doivent annoncer le prechargement START."
Assert-True ($machineState -match "Aucun jeu de seuils local programmable") "MachineState: seul l'absence de seuils locaux programmables doit bloquer START_PRELOAD."
Assert-True ($machineState -match "TryPrimeHandshakeGateBeforeStartNoLock") "MachineState: START doit amorcer la porte 8230 avant de lancer le cycle."
Assert-True ($machineState -match "TryPrimeHandshakeGateBeforeStartNoLock\(_config, out handshakeGateMessage\)[\s\S]*?TryPreloadThresholdsBeforeStartNoLock") "MachineState: la base 8230 doit etre lue avant le prechargement START et avant 5978=31."
Assert-True ($machineState -match "TryPrimeHandshakeGateBeforeStartNoLock[\s\S]*?TryReadSingleRegister\(cfg, cfg\.HandshakeRegister[\s\S]*?_lastRecordedHandshake = handshakeValue\.Value") "MachineState: START doit memoriser le 8230 courant pour ne pas ignorer la premiere cellule."
Assert-True ($machineState -match "`"8230_BASELINE`"") "MachineState: l'amorcage 8230 START doit etre trace."
Assert-True ($machineState -match "ResolveScannerFallbackResultNoLock") "MachineState: fallback scanner NoBarcodeValue absent."
Assert-True ($machineState -match "BuildScannerFallbackDecisionNoLock") "MachineState: decision scanner NG/CON absente."
Assert-True ($machineState -match "ApplyScannerFallbackToIntelligentLotNoLock") "MachineState: fallback scanner doit bypasser l'apprentissage intelligent."
Assert-True ($machineState -match "La cellule ne modifie pas l'apprentissage qualite") "MachineState: fallback scanner ne doit pas alimenter l'apprentissage qualite."
Assert-True ($machineState -match "SendScannerHandshakeResponseNoLock") "MachineState: reponse scan constructeur 8230 absente."
Assert-True ($machineState -match "SCAN_RESPONSE") "MachineState: trace reponse scan 8230 absente."
Assert-True ($machineState -match "response = scannerFallbackApplied \? \(ushort\)2 : \(ushort\)0") "MachineState: scan doit repondre 8230=0 si OK et 8230=2 si NG/timeout."
Assert-True ($machineState -match "WriteHoldingSingleNoLock\(cfg, \(ushort\)cfg\.HandshakeRegister, response\)") "MachineState: reponse scanner doit ecrire sur le registre handshake 8230."
Assert-True ($machineState -match "var handshake = cfg\.ScanEnabled \? 1 :") "MachineState: le simulateur scanner doit creer les cellules sur le top constructeur 8230=1."
Assert-True ($machineState -match "SCANNER_FALLBACK") "MachineState: source seuil/decision scanner fallback absente."
Assert-True ($machineState -match 'Result = string\.Equals\(decision, "CON"') "MachineState: l'audit cellule doit conserver CON meme si la voie physique est NG."
Assert-True ($models -match "ScannerNoBarcodeNg" -and $models -match "ScannerNoBarcodeCon") "Models: raisons de rejet scanner NG/CON absentes."
Assert-True ($operator -match "8230=0" -and $operator -match "8230=2" -and $operator -match "NoBarcodeValue") "Exigences operateur: handshake scanner constructeur non documente."
Assert-True ($apiContract -match "8230=0" -and $apiContract -match "8230=2" -and $apiContract -match "NoBarcodeValue") "Documentation API: handshake scanner constructeur non documente."
Assert-True ($machineState -match "BuildFieldValidationDiagnosticNoLock") "MachineState: diagnostic rapport terrain absent."
Assert-True ($machineState -match "field_validation_codex") "MachineState: les rapports smoke Codex doivent etre ignores dans le diagnostic terrain."
Assert-True ($machineState -match "ExtractValidationReportLotId") "MachineState: extraction du lot rapport terrain absente."
Assert-True ($machineState -match "MatchesCurrentLot") "MachineState: controle rapport terrain vs lot courant absent."
Assert-True ($machineState -match "VERDICT_COUVERTURE_VOIES_GOOD") "MachineState: verdict couverture voies GOOD absent du diagnostic terrain."
Assert-True ($machineState -match "LaneCoverageVerdict") "MachineState: champ couverture voies GOOD absent."
Assert-True ($machineState -match "HasValidLaneCoverageDetails") "MachineState: controle detail couverture voies GOOD absent."
Assert-True ($machineState -match "ValidationMinimumsCoverRequiredLaneCount") "MachineState: controle minimums effectifs couverture absent."
Assert-True ($machineState -match "validate_tricell_field\.bat 180") "MachineState: commande validation terrain absente du diagnostic."
Assert-True ($machineState -match "check_tricell_field_result\.bat") "MachineState: commande verification rapport terrain absente du diagnostic."
Assert-True ($machineState -match "HasAlarm\(alarms, 2\)") "MachineState: alarme arret urgence non exposee dans le live."
Assert-True ($machineState -notmatch "PusherAutoModeValue") "MachineState: aucune valeur de repos piston ne doit etre forcee."
Assert-True ($machineState -notmatch "RestorePusherAutoModeNoLock") "MachineState: restauration auto des pistons interdite."
Assert-True ($machineState -notmatch "ReleaseManualPusherOverridesNoLock") "MachineState: aucune liberation globale des banques piston ne doit revenir."
Assert-True ($machineState -notmatch "PUSHER_AUTO_RESTORE") "MachineState: trace de liberation globale piston interdite."
Assert-True ($machineState -notmatch "START_PRELOAD.*28414..28424") "MachineState: START ne doit pas ecrire en bloc sur les enables piston."
Assert-True ($machineState -notmatch "AFTER_TEST.*28926..28936") "MachineState: test piston ne doit pas ecrire en bloc sur les sorties piston."
Assert-True ($machineState -match "PISTON_WRITE_BLOCKED") "MachineState: barriere bas niveau anti-ecriture piston absente."
Assert-True ($machineState -match "WritePistonIoSingleNoLock") "MachineState: chemin d'ecriture piston dedie absent."
Assert-True ($writeHoldingSingleBody -match "return _modbus\.TryWriteSingleHoldingRegister\(") "MachineState: les ecritures holding unitaires doivent garder la primitive terrain validee."
Assert-True ($machineState -match "Y11OutputImageRegister = 3144") "MachineState: registre Y11 3144 absent."
Assert-True ($machineState -match "Y11OutputImageBit = 10") "MachineState: bit Y11 10 absent."
Assert-True ($machineState -match "TrySetY11OutputBitNoLock[\s\S]*?Y11OutputImageRegister[\s\S]*?Y11OutputImageMask") "MachineState: Y11 doit etre pilote par read-modify-write 3144 bit 10."
Assert-True ($machineState -match "PulseY11OutputBitNoLock[\s\S]*?Y11_PRE_RELEASE[\s\S]*?Y11_ON[\s\S]*?Y11_OFF") "MachineState: pulse Y11 doit forcer OFF/ON/OFF."
Assert-True ($machineState -match "WritePistonIoMaintenanceSingleNoLock[\s\S]*?_modbus\.TryWriteHoldingRegisters\(") "MachineState: les tests maintenance piston doivent utiliser le chemin holding 4X constructeur."
Assert-True ($machineState -match "WritePistonIoMaintenanceSingleNoLock") "MachineState: chemin maintenance test piston absent."
Assert-True ($machineState -match "MaintenancePusherPulseMs = 1000") "MachineState: les tests verin maintenance doivent avoir une impulsion visible terrain."
Assert-True ($machineState -match "IsPusherMaintenanceIoRegister") "MachineState: filtre maintenance enable/sortie piston absent."
Assert-True ($machineState -match "DIAG_PULSE_NG") "MachineState: diagnostic cible sortie NG absent."
Assert-True ($machineState -match "MachineSwitchesRegister = 640") "MachineState: registre interrupteurs constructeur 640 absent."
Assert-True ($machineState -notmatch "UNLOADING_MOTOR") "MachineState: aucune commande moteur dechargement mal identifiee ne doit revenir (640 bit 0 = rearmement auto, pas le moteur)."
Assert-True ($machineState -match "MACHINE_SWITCHES") "MachineState: lecture diagnostique des reglages 640 absente."
Assert-True ($machineState -match "ExecuteNgPusherDiagnosticPulseNoLock[\s\S]*?_operatorStartArmed \|\| _lotControlEnabled") "MachineState: diagnostic NG doit etre bloque pendant cycle arme."
Assert-True ($machineState -match "ExecuteNgPusherDiagnosticPulseNoLock[\s\S]*?PulseY11OutputBitNoLock") "MachineState: diagnostic NG doit utiliser Y11 3144.10."
Assert-True ($machineState -match "private bool WritePistonIoSingleNoLock[\s\S]*?PISTON_WRITE_BLOCKED[\s\S]*?return false;") "MachineState: le chemin dedie piston doit bloquer les ecritures directes."
Assert-True ($writePistonIoSingleBody -notmatch "_modbus\.TryWriteSingleHoldingRegister") "MachineState: le chemin direct piston ne doit plus ecrire Modbus en reel."
Assert-True ($writePistonIoSingleBody -notmatch "NgPusherEnableRegister|NgPusherOutputRegister") "MachineState: le chemin direct piston ne doit pas garder d'exception 28424/28936."
Assert-True ($machineState -match "ReleaseNgPusherResetNoLock") "MachineState: liberation ciblee du reset NG absente."
Assert-True ($machineState -match "NgPusherResetRegister") "MachineState: registre cible reset NG absent."
Assert-True ($machineState -match "ReleaseNgPusherResetNoLock[\s\S]*?NgPusherResetRegister[\s\S]*?PusherResetReleasedValue") "MachineState: la liberation NG doit rester ciblee sur 28305=1."
Assert-True ($releaseNgPistonBody -notmatch "TrySetY11OutputBitNoLock") "MachineState: liberation NG ne doit plus toucher la sortie carte Y11."
Assert-True ($releaseNgPistonBody -notmatch "WritePistonIoSingleNoLock") "MachineState: liberation NG ne doit pas ecrire les anciens registres 28424/28936."
Assert-True ($releaseNgPistonBody -notmatch "NgPusherOutputRegister|NgPusherEnableRegister") "MachineState: liberation NG ne doit pas cibler enable/sortie NG anciens."
Assert-True ($releaseNgPistonBody -notmatch "NgPusherResetRegister,\s*0") "MachineState: liberation NG ne doit pas pulser le reset 28305 a 0."
Assert-True ($releaseNgPistonBody -match "Aucun enable/sortie NG 28424/28936") "MachineState: liberation NG doit annoncer qu'aucun ancien registre NG n'est ecrit."
Assert-True ($machineState -match "IsNgAutoReleaseBlockedByRunStateNoLock") "MachineState: helper de blocage auto release NG par etat cycle absent."
Assert-True ($machineState -match "if \(IsNgAutoReleaseBlockedByRunStateNoLock\(\)\)[\s\S]*?AUTO_NG_RELEASE[\s\S]*?else[\s\S]*?ReleaseNgPusherResetNoLock") "MachineState: auto release NG doit verifier l'etat cycle avant 28305=1."
Assert-True ($machineState -match "IsNgAutoReleaseBlockedByRunStateNoLock[\s\S]*?_operatorStartArmed \|\| _lotControlEnabled") "MachineState: auto release NG doit bloquer si START ou le controle lot est arme."
Assert-True ($machineState -match "SendCycleCommandNoLock[\s\S]*?TryWriteHoldingRegisters[\s\S]*?CycleCommandRegister") "MachineState: les commandes 5978 doivent passer par l'ecriture multiple constructeur (fonction 16)."
Assert-True ($machineState -notmatch "WriteHoldingSingleNoLock\(cfg, CycleCommandRegister, 0\)") "MachineState: aucune remise a 0 de 5978 ne doit suivre une commande cycle."
Assert-True ($machineState -match "PusherPulseMs = 1000") "MachineState: les impulsions production doivent etre assez longues pour etre visibles terrain."
Assert-True ($machineState -notmatch "ScheduleRoutingPusherNoLock") "MachineState: l'ancien planificateur piston direct doit rester supprime."
Assert-True ($machineState -notmatch "PulseRoutingPusherNoLock") "MachineState: l'ancienne impulsion piston directe doit rester supprimee."
Assert-True ($machineState -notmatch "ScheduleNgSafetySweepForHandshakeNoLock") "MachineState: aucun balayage NG PC ne doit etre planifie en production."
Assert-True ($machineState -match "NG gere par le PLC via la voie catch-all") "MachineState: un top accepte sans mesure doit annoncer le NG PLC catch-all."
Assert-True ($machineState -match "routing_control=PLC_THRESHOLDS_NG_CATCHALL") "MachineState: le trace decision doit annoncer le controle PLC seuils + NG catch-all."
Assert-True ($machineState -match "PhysicalRoutingMode = `"PLC_THRESHOLDS_NG_CATCHALL`"") "MachineState: le diagnostic doit exposer le mode seuils PLC + NG catch-all."
Assert-True ($machineState -match "GoodPusherDirectControlBlocked = true") "MachineState: les pistons GOOD directs doivent rester bloques en production."
Assert-True ($machineState -notmatch "PulseProductionConveyor") "MachineState: l'avance convoyeur production doit rester geree par le PLC constructeur."
Assert-True ($machineState -notmatch "AdvanceConveyorAfter") "MachineState: aucune avance convoyeur PC ne doit suivre le balayage NG en production."
Assert-True ($machineState -match 'pusherScheduleMode = "PLC_NG_CATCHALL"') "MachineState: le marqueur NG PLC catch-all doit etre trace a chaque top."
Assert-True ($machineState -match "TryAutoResumeLotControlForLiveCycleNoLock") "MachineState: le tri doit se rearmer sur top cellule reel si un lot actif etait reste en pause logicielle."
Assert-True ($machineState -match '"AUTO_RESUME"') "MachineState: le rearmement automatique du tri doit etre trace."
Assert-True ($machineState -match "PISTON_TEST_[\s\S]*?BLOCKED_RUN") "MachineState: test piston manuel doit etre bloque pendant cycle arme."
Assert-True ($machineState -match "IsPistonMaintenanceBlockedByRunStateNoLock") "MachineState: helper de blocage test piston par etat cycle absent."
Assert-True ($machineState -match "IsPistonMaintenanceBlockedByRunStateNoLock[\s\S]*?_operatorStartArmed \|\| _lotControlEnabled") "MachineState: test piston doit bloquer si START ou le controle lot est arme."
Assert-True ($machineState -notmatch "_operatorStartArmed && _lotControlEnabled") "MachineState: test piston ne doit pas attendre les deux drapeaux pour bloquer."
Assert-True ($machineState -match "BLOCKED_LATCHED_ON") "MachineState: Y11 ON maintenu doit etre bloque en reel."
Assert-True ($machineState -match "Y11 ON maintenu bloqu") "MachineState: message blocage Y11 ON maintenu absent."
Assert-True ($machineState -match "BuildPistonSafetyBlockMessageNoLock") "MachineState: blocage securite piston absent."
Assert-True ($machineState -match "urgence actif") "MachineState: test piston doit expliquer l'arret d'urgence actif."
Assert-True ($machineState -match "pression d'air insuffisante") "MachineState: test piston doit expliquer la pression air insuffisante."
Assert-True ($machineState -match "NgPhysicalPusherLane = NgCounterIndex \+ 1") "MachineState: NG doit rester la voie physique 11."
Assert-True ($machineState -match "PusherResetCommandBaseRegister") "MachineState: registre reset constructeur piston absent."
Assert-True ($machineState -match "PusherActiveValue") "MachineState: valeur active piston absente."
Assert-True ($machineState -match "OUTPUT_RELEASE") "MachineState: relachement sortie NG absent."
Assert-True ($machineState -match "ENABLE_RELEASE") "MachineState: relachement enable NG absent."
Assert-True ($machineState -notmatch "PusherConstructorCommandValue") "MachineState: l'ancienne valeur constructeur 0 ne doit plus piloter les pistons."
Assert-True ($machineState -notmatch "RESET_SENT") "MachineState: le routage direct ne doit pas ecrire le reset piston."
Assert-True ($machineState -notmatch "resetOk = WritePistonIoSingleNoLock\(cfg, resetRegister") "MachineState: reset piston 28305 interdit dans le routage direct."
Assert-True ($machineState -notmatch "WritePistonIoMaintenanceSingleNoLock\([^,\)]*,\s*resetRegister") "MachineState: test maintenance ne doit pas ecrire les resets piston."
Assert-True ($machineState -match "register >= PusherResetCommandBaseRegister && register <= resetEnd") "MachineState: reset constructeur NG non autorise par la barriere dediee."
Assert-True ($machineState -match "Test piston manuel bloqu") "MachineState: le test piston direct doit expliquer le blocage pendant cycle."
Assert-True ($machineState -match "CONVEYOR_ONLY_FORWARD") "MachineState: commande convoyeur absente."
Assert-True ($machineState -match "CONVEYOR_FINE_FORWARD") "MachineState: micro-avance tapis absente."
Assert-True ($machineState -match "ConveyorFineForwardPulseMs = 200") "MachineState: duree micro-avance tapis absente ou trop courte."
Assert-True ($machineState -match "ConveyorForwardPulseMs = 1000") "MachineState: duree avance convoyeur absente ou trop courte."
Assert-True ($machineState -match "ConveyorForwardRegister = 5981") "MachineState: avance convoyeur doit utiliser 5981."
Assert-True ($machineState -match "SendCoilPulseNoLock") "MachineState: tapis doit utiliser un pulse coil 1X, pas un holding register."
Assert-True ($machineState -match "TryWriteSingleCoil") "MachineState: ecriture coil convoyeur absente."
Assert-True ($machineState -notmatch "SendHoldingPulseNoLock\(_config, ConveyorForwardRegister") "MachineState: le convoyeur 5981 ne doit plus etre pulse en holding register."
Assert-True ($machineState -match "CONVEYOR_COIL") "MachineState: avance tapis maintenance via coil constructeur absente."
Assert-True ($machineState -notmatch "CONVEYOR_WITH_NG_SWEEP") "MachineState: l'avance tapis ne doit plus inclure de balayage Y11."
Assert-True ($machineState -match "IsMaintenanceConveyorBlockedByRunStateNoLock") "MachineState: helper de blocage convoyeur maintenance par etat cycle absent."
Assert-True ($machineState -match "ExecuteConveyorOnlyForwardNoLock[\s\S]*?IsMaintenanceConveyorBlockedByRunStateNoLock") "MachineState: avance convoyeur maintenance doit verifier l'etat cycle avant Y11/coil."
Assert-True ($machineState -match "IsMaintenanceConveyorBlockedByRunStateNoLock[\s\S]*?_operatorStartArmed \|\| _lotControlEnabled") "MachineState: avance convoyeur maintenance doit bloquer si START ou le controle lot est arme."
Assert-True ($machineState -match "ExecuteConveyorOnlyForwardNoLock[\s\S]*?SendCoilPulseNoLock") "MachineState: avance tapis doit pulser le coil constructeur."
Assert-True ($machineState -notmatch "Commande manuelle inf.r.e") "MachineState: aucun ancien fallback maintenance infere ne doit pouvoir ecrire un registre."
Assert-True ($machineState -notmatch "NON VALID") "MachineState: les anciennes commandes maintenance non validees terrain doivent rester supprimees."
Assert-True ($modbusClient -match "BuildWriteMultipleFrame") "ModbusRtuClient: fonction 0x10 write multiple registers absente."
Assert-True ($modbusClient -match "response\[1\] != 0x10") "ModbusRtuClient: validation reponse 0x10 absente."
Assert-True ($modbusClient -match "TryWriteSingleHoldingRegister") "ModbusRtuClient: ecriture simple holding 0x06 absente."
Assert-True ($modbusClient -match "BuildWriteSingleHoldingFrame") "ModbusRtuClient: trame 0x06 holding absente."
Assert-True ($modbusClient -match "response\[1\] != 0x06") "ModbusRtuClient: validation reponse 0x06 absente."
Assert-True ($machineState -match "ProgramPhysicalNgLaneCatchAllNoLock") "MachineState: la voie NG physique doit etre programmee en catch-all constructeur."
Assert-True ($machineState -notmatch "KeepPhysicalNgLaneOutsideThresholdsNoLock") "MachineState: l'ancienne mise hors seuils de la voie NG ne doit pas revenir."
Assert-True ($machineState -match "ResolvePhysicalNgLaneIdNoLock") "MachineState: resolution voie physique NG absente."
Assert-True ($machineState -match "CreateNgCatchAllChannelThreshold") "MachineState: fenetre catch-all NG constructeur absente."
Assert-True ($machineState -match "ClampThresholdsToConstructorDomainNoLock") "MachineState: garde-fou domaine constructeur des seuils absent."
Assert-True ($machineState -notmatch "VoltageMin = -") "MachineState: aucune fenetre programmable a borne tension negative ne doit exister."
Assert-True ($machineState -notmatch "IrMin = -") "MachineState: aucune fenetre programmable a borne IR negative ne doit exister."
Assert-True ($machineState -match "NG physique voie") "MachineState: trace programmation NG hors seuils absente."
Assert-True ($machineState -match "en fenetre catch-all constructeur: le PLC pousse et ramene le verin NG") "MachineState: justification NG catch-all absente."
Assert-True ($machineState -match "_config.ChannelEnd = _config.Channels") "MachineState: le mode intelligent doit garder la voie physique NG dans la plage de routage."
Assert-True ($machineState -notmatch 'Command = "BACKWARD"') "MachineState: commande brute BACKWARD ne doit pas etre exposee."
Assert-True ($machineState -notmatch 'Command = "STEP"') "MachineState: commande brute STEP ne doit pas etre exposee."

$forbiddenPatterns = @(
    [regex]::Escape("ThreeBand"),
    [regex]::Escape("3 familles"),
    [regex]::Escape("famille 3"),
    "Les\s+[0-9]\s+premi[eè]res cellules valides servent",
    [regex]::Escape("BuildAxisScore"),
    [regex]::Escape("BackfillAuditWindowFromReferenceNoLock"),
    [regex]::Escape("ResolveAuditIntervalsNoLock")
)

$textFiles = Get-ChildItem -Path $app,$docs,(Join-Path $desktop "tools") -Recurse -File |
    Where-Object { $_.Extension -in ".cs", ".js", ".html", ".css", ".md", ".ps1", ".bat" -and $_.Name -ne "StaticQualityChecks.ps1" }

foreach ($file in $textFiles) {
    $content = Read-Text $file.FullName
    foreach ($pattern in $forbiddenPatterns) {
        Assert-True ($content -notmatch $pattern) "Terme obsolete detecte dans $($file.FullName): $pattern"
    }
}

$index = Read-Text (Join-Path $web "index.html")
$appJs = Read-Text (Join-Path $web "app.js")
Assert-True ($index -match 'data-command="RESET"') "UI: bouton REARMER automate absent de la barre de pilotage."
Assert-True ($appJs -notmatch 'command === "RESET"') "UI: RESET ne doit plus etre intercepte comme commande interdite."
Assert-True ($appJs -match "statut 7") "UI: consigne statut 7 / REARMER absente des messages operateur."
Assert-True ($machineState -match "Avancer convoyeur") "UI/API: commande convoyeur absente."
Assert-True ($machineState -match "Micro-avance tapis") "UI/API: commande micro-avance tapis absente."
Assert-True ($machineState -match "1X 5981") "UI/API: micro-avance doit mentionner le coil constructeur 1X 5981."
Assert-True ($appJs -notmatch "reviennent . 1") "UI: ancienne valeur de repos piston a 1 ne doit pas revenir."
Assert-True ($appJs -notmatch "repos START") "UI: START ne doit plus annoncer de remise piston a 0."
Assert-True ($appJs -notmatch "Machine Ã  rÃ©armer") "UI: ne doit plus annoncer une touche de rearmement inexistante."
Assert-True ($appJs -notmatch "RÃ©armement requis") "UI: le blocage automate doit etre libelle depart bloque."

Assert-True ($appJs -match "refresh_tricell_field_result\.bat") "UI: commande actualisation rapport terrain absente du diagnostic."
Assert-True ($appJs -match "captureScrollState") "UI: preservation du scroll pendant le refresh live absente."
Assert-True ($appJs -match "PRESERVED_SCROLL_SELECTORS") "UI: liste des panneaux a scroll preserve absente."

$requiredDomIds = @(
    "machineAlarmBanner",
    "machineStateBlock",
    "bigMetrics",
    "ngCharacteristics",
    "qualityIntervalChart",
    "historyTable",
    "intelligentRecipeForm",
    "diagnosticSummary",
    "runtimeTrace"
)

foreach ($id in $requiredDomIds) {
    Assert-True ($index -match "id=`"$id`"") "Element UI manquant dans index.html: $id"
}

$requiredApiCalls = @(
    "/api/state",
    "/api/cells/audit",
    "/api/lots/history",
    "/api/recipes/intelligent",
    "/api/recipes/legacy"
)

foreach ($api in $requiredApiCalls) {
    Assert-True ($appJs -match [regex]::Escape($api)) "Appel API manquant dans app.js: $api"
}

Assert-True ($appJs -match "escapeHtml") "escapeHtml absent de app.js."
Assert-True ($appJs -match "Selon IR") "UI: libelle Selon IR absent."
Assert-True ($appJs -match "Seuil IR") "UI: seuil IR absent de l'audit."
Assert-True ($appJs -match "StartReadiness") "UI: diagnostic pre-vol START absent."
Assert-True ($appJs -match "HandshakeReady") "UI: diagnostic pre-vol START doit afficher la lecture 8230."
Assert-True ($appJs -match "FieldValidation") "UI: diagnostic validation terrain absent."
Assert-True ($appJs -match "Validation terrain") "UI: carte validation terrain absente."
Assert-True ($appJs -match "MatchesCurrentLot") "UI: coherence lot rapport terrain absente."
Assert-True ($appJs -match "LaneCoverageVerdict") "UI: verdict couverture voies GOOD absent."
Assert-True ($appJs -match "ValidationCommand") "UI: commande de lancement validation terrain absente."
Assert-True ($appJs -match "CheckCommand") "UI: commande de verification rapport terrain absente."
Assert-True ($apiServer -match "/api/diagnostic/start-readiness") "API: endpoint pre-vol START absent."
Assert-True ($models -match "HandshakeReady") "API: champ pre-vol 8230 absent."
Assert-True ($models -match "DefaultMeasurementRegister = 8408") "Config: le registre mesure par defaut doit suivre le binaire constructeur original (8408)."
Assert-True ($models -match "KnownErroneousMeasurementRegister = 8402") "Config: le mauvais defaut mesure 8402 doit rester connu pour migration corrective."
Assert-True ($configStore -match "KnownErroneousMeasurementRegister[\s\S]*?DefaultMeasurementRegister") "ConfigStore: le mauvais defaut mesure 8402 doit migrer vers 8408."
Assert-True ($models -match "OutputPath") "API: diagnostic NG doit exposer le chemin de sortie terrain explicite."
Assert-True ($models -match "OutputImageRegister") "API: diagnostic NG doit exposer le registre image sortie Y11."
Assert-True ($models -match "OutputBit") "API: diagnostic NG doit exposer le bit Y11."
Assert-True ($machineState -match "OutputPath = `"Y11_4X_3144_BIT_10`"") "MachineState: diagnostic NG doit renseigner le chemin Y11 explicite."
Assert-True ($machineState -match "OutputImageRegister = Y11OutputImageRegister") "MachineState: diagnostic NG doit renseigner 3144 comme registre image Y11."
Assert-True ($machineState -match "OutputBit = Y11OutputImageBit") "MachineState: diagnostic NG doit renseigner le bit Y11 10."
Assert-True ($apiCatalog -match "HandshakeReady") "API contracts: champ pre-vol 8230 absent."
Assert-True ($apiCatalog -match "NgPulseDiagnostic[\s\S]*?OutputPath[\s\S]*?OutputImageRegister[\s\S]*?OutputBit") "API contracts: diagnostic NG Y11 explicite absent."
Assert-True ($apiContract -match "HandshakeReady") "Documentation API: champ pre-vol 8230 absent."
Assert-True ($apiContract -match 'registre mesure par defaut est `8408`') "Documentation API: registre mesure constructeur 8408 non documente."
Assert-True ($aiContext -match "8408.*longueur 4") "Contexte IA: registre mesure constructeur 8408 longueur 4 non documente."
Assert-True ($apiContract -match "OutputPath=Y11_4X_3144_BIT_10") "Documentation API: LastNgPulse doit documenter le chemin Y11 explicite."
Assert-True ($apiContract -match 'slot 11: `28424` / `28936`') "Documentation API: test piston NG par registres constructeur non documente."
Assert-True ($apiContract -match "Y11 ON maintenu.*bloque") "Documentation API: Y11 ON maintenu doit etre documente comme bloque."
Assert-True ($apiContract -match "AUTO_NG_RELEASE.*ignoree pendant un cycle arme") "Documentation API: auto release NG doit etre documentee comme ignoree pendant cycle arme."
Assert-True ($apiContract -notmatch "routage production utilise le meme principe") "Documentation API: la production ne doit pas etre decrite comme impulsion piston directe."
Assert-True ($apiServer -match "/api/diagnostic/field-validation") "API: endpoint validation terrain absent."
Assert-True ($appJs.Contains('const startAllowed = !!app?.Connected;')) "UI: DEMARRER doit rester cliquable quand le PLC est connecte, meme si une alarme explique le refus machine."
Assert-True ($appJs.Contains('Cliquer pour tenter le d')) "UI: DEMARRER doit expliquer qu'il tente le demarrage au lieu d'etre bloque par l'UI."
Assert-True ($appJs -match "getMachineAlarmNotice") "UI: les alarmes machine doivent etre derivees en message operateur explicite."
Assert-True ($appJs -match "PRESSION AIR") "UI: l'alarme pression air doit etre exposee explicitement."
Assert-True ($appJs -match "machineAlarmBanner") "UI: banniere d'alarme machine absente du rendu JS."
Assert-True ($appJs -match "data-piston-prepare") "UI: boutons de test piston manuel absents."
Assert-True ($appJs -match "Diag sortie Y11") "UI: bouton diagnostic sortie carte Y11 absent de la grille piston."
Assert-True ($appJs -notmatch "usesY11") "UI: la carte NG ne doit plus etre marquee comme sortie Y11."
Assert-True ($appJs -match "enable: 28414 \+ index") "UI: la carte piston doit afficher l'enable constructeur."
Assert-True ($appJs -match "output: 28926 \+ index") "UI: la carte piston doit afficher la sortie constructeur."
Assert-True ($appJs -notmatch "Sortie terrain Y11") "UI: le test NG ne doit plus presenter Y11 comme sortie NG."
Assert-True ($appJs -match "PLC_THRESHOLDS_NG_CATCHALL") "UI: mode routage catch-all absent."
Assert-True ($appJs -match "voie 11 catch-all") "UI: la carte NG production doit expliquer la voie 11 catch-all."
Assert-True ($appJs -match "lastNgPulse\.Status") "UI: le dernier pulse Y11 maintenance doit rester visible."
Assert-True ($appJs -match "const ioSummary = ") "UI: resume I/O du test piston absent."
Assert-True ($appJs -match "cycle est arm") "UI: aide test piston machine arretee absente."

$styles = Read-Text (Join-Path $web "styles.css")
Assert-True ($styles -match "machine-alarm-banner") "CSS: style de banniere d'alarme machine absent."
Assert-True ($styles -match "machine-alarm-active") "CSS: etat visuel d'alarme machine absent."

Write-Host "StaticQualityChecks OK"
