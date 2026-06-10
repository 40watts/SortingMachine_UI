# Contrat API local

Base URL:

```text
http://127.0.0.1:8050
```

## Endpoints de lecture principaux

- `GET /api/state`: etat complet UI.
- `GET /api/config`: configuration machine active. Le registre mesure par defaut est `8408` (paquet IR/tension constructeur, longueur 4, comme l'appel `ReadFloatsMust(8408, 2)` du logiciel chinois). `8402` reste reserve a la lecture live/diagnostic; une config locale qui l'aurait pris comme paquet de tri est migree vers `8408`.
- `GET /api/contracts`: description des champs principaux.
- `GET /api/diagnostic`: diagnostic general, avec bloc `PhysicalRouting`.
- `GET /api/diagnostic/physical-routing`: diagnostic cible routage physique atelier.
- `GET /api/diagnostic/start-readiness`: pre-vol operateur avant `DEMARRER`, sans ecriture machine. Il expose aussi `HandshakeReady`, `HandshakeRegister`, `HandshakeValue` et `HandshakeChangedAt` pour confirmer que `8230` a ete lu avant START.
- `GET /api/diagnostic/field-validation`: etat du dernier rapport terrain operateur hors smoke Codex.
- `GET /api/recipes/intelligent`: recettes intelligentes 21700 / 18650.
- `GET /api/recipes/legacy`: seuils legacy.
- `GET /api/cells/audit?limit=500`: audit cellule enrichi.
- `GET /api/lots/history?limit=20`: historique lots.
- `GET /api/runtime-trace?limit=200`: trace runtime.
- `GET /api/maintenance`: commandes validees et commandes expert.
- `POST /api/maintenance/piston-test`: demande de test piston ligne `1..10` ou `NG`; en mode reel, le backend refuse un cycle arme ou une securite bloquante. Chaque voie pulse uniquement l'enable/sortie constructeur de la voie demandee (`28414+i` / `28926+i`; pour `NG`, slot 11: `28424` / `28936`). Les resets `28295..28305` ne sont jamais pulses.

## Exports

- `GET /api/export/cells-audit-csv`
- `GET /api/export/recipes/json`
- `GET /api/export/diagnostic/json`
- `GET /api/export/runtime-trace`
- `GET /api/export/observation/csv`

## Champs obligatoires audit cellule

Chaque ligne de `GET /api/cells/audit` doit exposer:

- `Sequence`
- `LotId`
- `Timestamp`
- `Voltage`
- `Ir`
- `RoutingModel`
- `QualityInterval`
- `VoltageMin`
- `VoltageMax`
- `IrMin`
- `IrMax`
- `Decision`
- `IntendedLane`
- `EffectiveLane`
- `ConfirmationLane`
- `Status`
- `DataQuality`
- `Mismatch`
- `RejectReason`
- `ThresholdSource`

## Invariants API

- Les recettes intelligentes doivent contenir les lignes GOOD `1..10`.
- `SampleSize` doit valoir `19` pour le modele actuel.
- La ligne `10` collecte `19` cellules d'apprentissage; le logiciel ne gere pas la capacite physique des lignes en mode 9 intervalles.
- En mode intelligent, la voie physique `11` (NG) est programmee avec la fenetre catch-all constructeur (V `0..99.9`, IR `0..999.99`): le PLC pousse le verin NG sur chaque cellule non captee par une voie GOOD. Le PLC evalue dans l'ordre 1..N, NG en dernier.
- `PhysicalRouting` expose la ligne attendue, la ligne appliquee/confirmee si connue, le dernier top `8230`, le statut `8231`, les seuils programmes/relus, les alarmes `22808..22811`, le dernier pulse Y11 maintenance et `PhysicalRoutingMode=PLC_THRESHOLDS_NG_CATCHALL`. `LastNgPulse` decrit la sortie carte Y11 (`OutputPath=Y11_4X_3144_BIT_10`, `OutputImageRegister=3144`, `OutputBit=10`), utilisee seulement en diagnostic maintenance; les anciens champs `EnableRegister/OutputRegister` restent seulement compatibles.
- `StartReadiness` expose `ReadyToStart`, `HandshakeReady`, `HandshakeRegister`, `HandshakeValue`, `HandshakeChangedAt`, les raisons bloquantes, les avertissements non bloquants et les controles operateur requis avant START. `ReadyToStart=true` n'autorise pas un START distant sans operateur devant la machine.
- `FieldValidation` expose `HasReport`, `Verified`, `Status`, `ReportLotId`, `CurrentLotId`, `MatchesCurrentLot`, les verdicts `TraceVerdict`, `CounterVerdict`, `PhysicalObservationVerdict`, `LaneCoverageVerdict` et les commandes `validate_tricell_field.bat 180` / `check_tricell_field_result.bat`. Si le CSV d'observation est complete apres la surveillance, `refresh_tricell_field_result.bat` regenere un rapport depuis le CSV sans commande machine et sans inventer les traces source. `Verified=true` exige un vrai rapport operateur du lot courant avec trace logiciel, compteurs machine, observation physique et couverture voies GOOD `1..9` `OK`; les rapports `field_validation_codex*` sont ignores.
- Avant chaque `START`, l'application lit et memorise le top courant `8230` comme base de depart, programme les seuils avec la trace `START_PRELOAD`, puis ecrit la commande cycle `5978=31` une seule fois (fonction 16, sans remise a 0: le PLC consomme la commande, comme le logiciel chinois). Si `8230` est illisible, `START` est refuse pour eviter de manquer la premiere cellule. Elle ne libere plus les banques piston en bloc: aucune ecriture globale `0` ou `1` sur `28414..28424` / `28926..28936`. Seule exception autorisee: remettre le reset NG `28305` a `1` (repos) si ce registre est reste bloque a `0`.
- Pendant le cycle machine, TriCell Pilot programme les seuils `1188..1370` (voies GOOD + voie 11 NG catch-all); le PLC pilote tous les pistons selon ces seuils. TriCell Pilot ne pulse aucun piston en production.
- Si `ScanEnabled=true`, le top cellule constructeur accepte reste `8230=1`. Apres la decision scan, TriCell Pilot repond sur le meme registre: `8230=0` pour barcode valide, `8230=2` pour barcode absent/repli. `NoBarcodeValue=NG` force une decision NG; `NoBarcodeValue=CON` garde le resultat special CON. Dans les deux cas, la cellule sans barcode ne nourrit pas l'apprentissage qualite.
- Si le statut automate `8231` vaut `7`, `DEMARRER` est refuse clairement avec la cause d'alarme visible, par exemple pression air. TriCell Pilot ne tente aucun rearmement automatique; le bouton `REARMER` ecrit volontairement `5978=26` une seule fois (le PLC consomme), sans ecriture piston.
- Le CSV audit doit contenir les colonnes `routing_model`, `quality_interval`, `voltage_min`, `voltage_max`, `ir_min`, `ir_max`.
- Quand `Production.QualityIntervals` est non vide, il doit contenir 9 elements.
- `/api/maintenance/command` avec `CONVEYOR_FINE_FORWARD` pulse le coil constructeur d'avance convoyeur `1X 5981=true` pendant `200 ms` et relache `false`. `CONVEYOR_ONLY_FORWARD` utilise le meme coil pendant `1000 ms` pour un degagement visible. Ne pas utiliser `WriteHoldingRegisters` sur `5981` pour le tapis. Les commandes brutes de mouvement ne sont pas exposees dans l'UI.
- Ces commandes convoyeur de maintenance sont bloquees pendant un cycle arme; envoyer `STOP` ou `PAUSE` et attendre l'arret des tops `8230` avant une avance manuelle.
- `/api/maintenance/command` avec `RELEASE_NG_PISTON` relache le reset du verin NG via `28305=1` si necessaire. Il n'ecrit pas l'enable/sortie `28424/28936` et lit `28689` pour le retour terrain.
- La liberation automatique du reset NG (`AUTO_NG_RELEASE`, `28305=1`) est ignoree pendant un cycle arme; le backend ne doit pas reecrire ce reset periodiquement pendant le tri.
- `/api/maintenance/command` avec `DIAG_PULSE_NG` est un diagnostic carte machine arretee: il refuse le cycle arme, puis pulse uniquement la sortie carte Y11 `4X 3144` bit `10` pour observer la LED Y. Ce n'est pas le verin NG; aucun convoyeur ni piston n'est ecrit.
- `/api/maintenance/command`: le diagnostic expert `Y11 ON maintenu` est bloque en reel; utiliser `DIAG_PULSE_NG` pour une impulsion ON/OFF ou `Y11_OUTPUT_OFF` pour liberer une sortie active.
- `/api/maintenance/piston-test` accepte uniquement `lane` (`1..10` ou `NG`) et sert au diagnostic unitaire machine arretee. L'impulsion maintenance est maintenue `1000 ms` pour etre observable. Le routage production n'utilise pas ce chemin: tous les pistons sont pilotes par le PLC via les seuils `1188..1370`.
- Si une securite machine bloque les verins, par exemple arret d'urgence ou pression d'air, `/api/maintenance/piston-test` renvoie aussi une erreur claire et n'ecrit aucun registre piston.
