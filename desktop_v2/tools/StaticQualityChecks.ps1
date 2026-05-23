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
    "desktop_v2/docs/quality-interval-routing.md",
    "desktop_v2/docs/ai-context.md",
    "desktop_v2/docs/operator-guide.md",
    "desktop_v2/docs/api-contract.md",
    "desktop_v2/docs/git-hygiene.md",
    "desktop_v2/docs/pre-git-readiness.md",
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

$runDesktop = Read-Text (Join-Path $Root "run_desktop.bat")
$runApp = Read-Text (Join-Path $Root "run_app.bat")
Assert-True ($runDesktop -match "desktop_v2\\bin\\TriCellPilot\.exe") "run_desktop.bat ne lance pas TriCellPilot."
Assert-True ($runDesktop -match "build_desktop_v2\.bat") "run_desktop.bat ne build pas desktop_v2."
Assert-True ($runApp -match "run_desktop\.bat") "run_app.bat doit rediriger vers l'application active."
Assert-True ($runApp -notmatch "uvicorn") "run_app.bat ne doit plus lancer l'ancien backend."

$operator = Read-Text (Join-Path $Root "OPERATOR_REQUIREMENTS.md")
Assert-True ($operator -match "ligne 10") "Exigences operateur: ligne 10 non documentee."
Assert-True ($operator -match "9 intervalles") "Exigences operateur: 9 intervalles non documentes."
Assert-True ($operator -match "voie NG hors seuils") "Exigences operateur: voie NG hors seuils non documentee."
Assert-True ($operator -match "RESET=26.*commande operateur explicite") "Exigences operateur: rearmement manuel RESET=26 non documente."
Assert-True ($operator -match 'DEMARRER.*sans envoyer `5978=26`') "Exigences operateur: DEMARRER doit bloquer sans reset automatique."
Assert-True ($operator -match 'aucun signal piston') "Exigences operateur: convoyeur seul sans signal piston non documente."
Assert-True ($operator -match "START_PRELOAD") "Exigences operateur: prechargement seuils avant DEMARRER non documente."
Assert-True ($operator -match "1188..1370") "Exigences operateur: seuils machine de routage non documentes."
Assert-True ($operator -match "ne pulse plus NG ni les lignes GOOD en direct") "Exigences operateur: interdiction pilotage direct pistons non documentee."
Assert-True ($operator -match "28295..28314") "Exigences operateur: banques I/O piston non validees non documentees."
Assert-True ($operator -match "Micro-avance tapis") "Exigences operateur: micro-avance tapis non documentee."
Assert-True ($operator -match "1X 5981") "Exigences operateur: micro-avance doit utiliser le coil constructeur 1X 5981."
Assert-True ($operator -match "Ne jamais simuler une micro-avance par une ecriture holding") "Exigences operateur: interdiction holding 5981 non documentee."
Assert-True ($operator -match "Avancer convoyeur seul") "Exigences operateur: degagement convoyeur seul non documente."

$gitignore = Read-Text (Join-Path $Root ".gitignore")
foreach ($protected in @("desktop_v2/bin/", "desktop_app/", "backend/", "frontend/", "webview2_pkg/", "webview2.zip", "webview2.nupkg", "ODOO.txt", "odoo_config.json", ".env.*", "*.corrupt_*")) {
    Assert-True ($gitignore -match [regex]::Escape($protected)) ".gitignore ne protege pas: $protected"
}

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
$modbusClient = Read-Text (Join-Path $app "ModbusRtuClient.cs")
Assert-True ($machineState -match 'case "RESET":') "MachineState: RESET manuel doit etre une commande cycle explicite."
Assert-True ($machineState -match "ResetCycleCode") "MachineState: code RESET=26 absent."
Assert-True ($machineState -match '"START", "BLOCKED"') "MachineState: START doit etre bloque clairement si le statut 7 reste actif."
Assert-True ($machineState -match 'Statut automate 7: r.?armement requis avant D.?MARRER\.') "MachineState: statut 7 doit imposer un rearmement clair avant DEMARRER."
Assert-True ($machineState -match "Statut automate 7 avec securite bloquante") "MachineState: statut 7 doit continuer a exposer la cause d'alarme si une securite bloque."
Assert-True ($machineState -match [regex]::Escape("return requiresReset;")) "MachineState: START doit etre bloque des que le statut 7 est lu."
Assert-True ($machineState -notmatch "START_AUTO_RESET") "MachineState: START ne doit plus tenter de reset automatique."
Assert-True ($machineState -notmatch "WaitForResetRequiredToClearNoLock") "MachineState: ancienne attente apres reset automatique a retirer."
Assert-True ($machineState -notmatch "START.*ResetCycleCode") "MachineState: START ne doit pas envoyer RESET=26."
Assert-True ($machineState -notmatch "ConfirmStartMotionNoLock") "MachineState: START ne doit pas bloquer le chemin critique par une confirmation synchrone."
Assert-True ($machineState -notmatch '"START_CONFIRM"') "MachineState: START ne doit pas retourner en echec apres avoir envoye 5978=31."
Assert-True ($machineState -match "CycleCommandPulseMs") "MachineState: une impulsion courte des commandes cycle doit rester configuree."
Assert-True ($machineState -match "SendCycleCommandNoLock") "MachineState: les commandes cycle doivent garder un chemin dedie."
Assert-True ($machineState -notmatch "SendCyclePulseNoLock") "MachineState: aucun ancien helper de pulse cycle ne doit revenir."
Assert-True ($machineState -match "commande cycle envoy") "MachineState: le relachement cycle 5978 doit etre trace."
Assert-True ($machineState -notmatch '"COMMAND",\s*commandName,\s*"WRITE_ONLY"') "MachineState: les commandes cycle ne doivent plus rester en write-only."
Assert-True ($machineState -match '"THRESHOLDS",\s*"SAVE_CHANNEL",\s*releaseOk \? "PULSE_RELEASE"') "MachineState: Save channel 59 doit redevenir une impulsion avec relachement."
Assert-True ($machineState -match "Save channel 59 appliqu") "MachineState: Save channel 59 doit annoncer l'impulsion appliquee."
Assert-True ($machineState -match "START_RAW") "MachineState: un diagnostic START constructeur brut doit rester disponible."
Assert-True ($machineState -match "brut constructeur") "MachineState: START_RAW doit etre visible et explicite pour l'operateur."
Assert-True ($machineState -match [regex]::Escape('_suspendThresholdSyncUntil = DateTime.Now.AddSeconds(result.Command == "RESET" ? 4 : 2);')) "MachineState: les commandes cycle doivent suspendre brievement la resynchro apres envoi."
Assert-True ($machineState -match [regex]::Escape('ApplyCycleCommandNoLock(result.Command);')) "MachineState: START doit armer le logiciel sans attendre une confirmation bloquante."
Assert-True ($machineState -match "TryPreloadThresholdsBeforeStartNoLock") "MachineState: START doit precharger les seuils machine avant la commande cycle."
Assert-True ($machineState -match "START_PRELOAD") "MachineState: la trace START_PRELOAD doit rester visible pour diagnostiquer le routage automate."
Assert-True ($machineState -match "BuildProgrammableThresholdsNoLock\(cfg, localThresholds, activeLot\)") "MachineState: START_PRELOAD doit programmer le meme modele de seuils que le routage actif."
Assert-True ($machineState -match "HasAlarm\(alarms, 2\)") "MachineState: alarme arret urgence non exposee dans le live."
Assert-True ($machineState -notmatch "PusherAutoModeValue") "MachineState: aucune valeur de repos piston ne doit etre forcee."
Assert-True ($machineState -notmatch "RestorePusherAutoModeNoLock") "MachineState: restauration auto des pistons interdite."
Assert-True ($machineState -notmatch "ReleaseManualPusherOverridesNoLock") "MachineState: aucune liberation globale des banques piston ne doit revenir."
Assert-True ($machineState -notmatch "PUSHER_AUTO_RESTORE") "MachineState: trace de liberation globale piston interdite."
Assert-True ($machineState -notmatch "START_PRELOAD.*28414..28424") "MachineState: START ne doit pas ecrire en bloc sur les enables piston."
Assert-True ($machineState -notmatch "AFTER_TEST.*28926..28936") "MachineState: test piston ne doit pas ecrire en bloc sur les sorties piston."
Assert-True ($machineState -match "PISTON_WRITE_BLOCKED") "MachineState: barriere bas niveau anti-ecriture piston absente."
Assert-True ($machineState -match "WritePistonIoSingleNoLock") "MachineState: chemin d'ecriture piston dedie absent."
Assert-True ($machineState -match "private bool WriteHoldingSingleNoLock[\s\S]*?return _modbus\.TryWriteHoldingRegisters\([\s\S]*?new\[\] \{ value \}") "MachineState: les impulsions holding unitaires doivent imiter le constructeur via WriteMultipleRegisters 0x10 avec une valeur."
Assert-True ($machineState -match "private bool WritePistonIoSingleNoLock[\s\S]*?PISTON_WRITE_BLOCKED[\s\S]*?return false;") "MachineState: le chemin dedie piston doit bloquer toute ecriture directe en reel."
Assert-True ($machineState -match "ReleaseNgPusherResetNoLock") "MachineState: liberation ciblee du reset NG absente."
Assert-True ($machineState -match "NgPusherResetRegister") "MachineState: registre cible reset NG absent."
Assert-True ($machineState -match "ReleaseNgPusherResetNoLock[\s\S]*?_modbus\.TryWriteHoldingRegisters\([\s\S]*?NgPusherResetRegister[\s\S]*?PusherResetReleasedValue") "MachineState: la liberation NG doit ecrire uniquement 28305=1 via WriteMultipleRegisters 0x10."
Assert-True ($machineState -notmatch "private bool WriteHoldingSingleNoLock[\s\S]*?return _modbus\.TryWriteSingleHoldingRegister\(") "MachineState: les commandes 5978 ne doivent pas utiliser le chemin 0x06 non constructeur."
Assert-True ($machineState -notmatch "directPusherAttempted = true") "MachineState: le routage production ne doit plus forcer les verins via banques I/O non validees."
Assert-True ($machineState -notmatch "directPusherOk = ScheduleRoutingPusherNoLock") "MachineState: la decision cellule doit rester pilotee par seuils machine, pas par impulsion piston directe."
Assert-True ($machineState -notmatch "routing_control=DIRECT_PUSHER") "MachineState: le trace decision doit annoncer le controle par seuils machine."
Assert-True ($machineState -match "routing_control=THRESHOLDS_MACHINE") "MachineState: le routage production doit rester confie a l'automate via seuils."
Assert-True ($machineState -notmatch "ScheduleNgSafetySweepForHandshakeNoLock\(cfg, handshakeValue\.Value, source, `"RAW_CYCLE_ONLY`"\)") "MachineState: un top brut ne doit plus pulser NG hors START operateur."
Assert-True ($machineState -match "TryAutoResumeLotControlForLiveCycleNoLock") "MachineState: le tri doit se rearmer sur top cellule reel si un lot actif etait reste en pause logicielle."
Assert-True ($machineState -match '"AUTO_RESUME"') "MachineState: le rearmement automatique du tri doit etre trace."
Assert-True ($machineState -match "BLOCKED_UNVALIDATED_IO") "MachineState: le test piston direct doit etre bloque tant que les banques I/O ne sont pas validees terrain."
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
Assert-True ($machineState -match "register >= PusherResetCommandBaseRegister && register <= resetEnd") "MachineState: reset constructeur NG non autorise par la barriere dediee."
Assert-True ($machineState -match "Test v.rin direct d.sactiv") "MachineState: le test piston direct doit expliquer son blocage."
Assert-True ($machineState -match "CONVEYOR_ONLY_FORWARD") "MachineState: commande convoyeur seul absente."
Assert-True ($machineState -match "CONVEYOR_FINE_FORWARD") "MachineState: micro-avance tapis absente."
Assert-True ($machineState -match "ConveyorFineForwardPulseMs = 25") "MachineState: duree micro-avance tapis absente ou modifiee."
Assert-True ($machineState -match "ConveyorForwardRegister = 5981") "MachineState: avance convoyeur seul doit utiliser 5981."
Assert-True ($machineState -match "SendCoilPulseNoLock") "MachineState: tapis doit utiliser un pulse coil 1X, pas un holding register."
Assert-True ($machineState -match "TryWriteSingleCoil") "MachineState: ecriture coil convoyeur absente."
Assert-True ($machineState -notmatch "SendHoldingPulseNoLock\(_config, ConveyorForwardRegister") "MachineState: le convoyeur 5981 ne doit plus etre pulse en holding register."
Assert-True ($machineState -match "Aucun signal piston") "MachineState: convoyeur seul doit annoncer l'absence de signal piston."
Assert-True ($modbusClient -match "BuildWriteMultipleFrame") "ModbusRtuClient: fonction 0x10 write multiple registers absente."
Assert-True ($modbusClient -match "response\[1\] != 0x10") "ModbusRtuClient: validation reponse 0x10 absente."
Assert-True ($modbusClient -match "TryWriteSingleHoldingRegister") "ModbusRtuClient: ecriture simple holding 0x06 absente."
Assert-True ($modbusClient -match "BuildWriteSingleHoldingFrame") "ModbusRtuClient: trame 0x06 holding absente."
Assert-True ($modbusClient -match "response\[1\] != 0x06") "ModbusRtuClient: validation reponse 0x06 absente."
Assert-True ($machineState -match "KeepPhysicalNgLaneOutsideThresholdsNoLock") "MachineState: la voie NG physique doit etre gardee hors seuils pour ne pas voler les GOOD."
Assert-True ($machineState -match "ResolvePhysicalNgLaneIdNoLock") "MachineState: resolution voie physique NG absente."
Assert-True ($machineState -notmatch "CreateNgSafetyCatchAllChannelThreshold") "MachineState: le catch-all NG chevauchant les GOOD ne doit pas revenir."
Assert-True ($machineState -match "NG physique voie") "MachineState: trace programmation NG hors seuils absente."
Assert-True ($machineState -match "hors seuils: les cellules non matchees partent au rejet machine par defaut") "MachineState: justification NG hors seuils absente."
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
Assert-True ($appJs -match "Avancer convoyeur seul") "UI: aide convoyeur seul absente."
Assert-True ($appJs -match "Micro-avance tapis") "UI: aide micro-avance tapis absente."
Assert-True ($appJs -match "1X 5981") "UI: aide micro-avance doit mentionner le coil constructeur 1X 5981."
Assert-True ($appJs -notmatch "reviennent . 1") "UI: ancienne valeur de repos piston a 1 ne doit pas revenir."
Assert-True ($appJs -notmatch "repos START") "UI: START ne doit plus annoncer de remise piston a 0."
Assert-True ($appJs -notmatch "Machine Ã  rÃ©armer") "UI: ne doit plus annoncer une touche de rearmement inexistante."
Assert-True ($appJs -notmatch "RÃ©armement requis") "UI: le blocage automate doit etre libelle depart bloque."

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
Assert-True ($appJs.Contains('const startAllowed = !!app?.Connected;')) "UI: DEMARRER doit rester cliquable quand le PLC est connecte, meme si une alarme explique le refus machine."
Assert-True ($appJs.Contains('Cliquer pour tenter le d')) "UI: DEMARRER doit expliquer qu'il tente le demarrage au lieu d'etre bloque par l'UI."
Assert-True ($appJs -match "getMachineAlarmNotice") "UI: les alarmes machine doivent etre derivees en message operateur explicite."
Assert-True ($appJs -match "PRESSION AIR") "UI: l'alarme pression air doit etre exposee explicitement."
Assert-True ($appJs -match "machineAlarmBanner") "UI: banniere d'alarme machine absente du rendu JS."

$styles = Read-Text (Join-Path $web "styles.css")
Assert-True ($styles -match "machine-alarm-banner") "CSS: style de banniere d'alarme machine absent."
Assert-True ($styles -match "machine-alarm-active") "CSS: etat visuel d'alarme machine absent."

Write-Host "StaticQualityChecks OK"
