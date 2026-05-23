# Contrat API local

Base URL:

```text
http://127.0.0.1:8050
```

## Endpoints de lecture principaux

- `GET /api/state`: etat complet UI.
- `GET /api/config`: configuration machine active.
- `GET /api/contracts`: description des champs principaux.
- `GET /api/recipes/intelligent`: recettes intelligentes 21700 / 18650.
- `GET /api/recipes/legacy`: seuils legacy.
- `GET /api/cells/audit?limit=500`: audit cellule enrichi.
- `GET /api/lots/history?limit=20`: historique lots.
- `GET /api/runtime-trace?limit=200`: trace runtime.
- `GET /api/maintenance`: commandes validees et commandes expert.
- `POST /api/maintenance/piston-test`: demande de test piston ligne `1..10` ou `NG`; en mode reel, le backend refuse tant que les banques I/O constructeur ne sont pas validees terrain.

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
- En mode intelligent, la plage de routage conserve la voie physique `11`, mais cette voie NG reste hors seuils pour ne pas recouvrir les lignes GOOD. Les cellules non matchees reviennent au rejet machine par defaut.
- Avant chaque `START`, l'application programme les seuils avec la trace `START_PRELOAD`, puis envoie la commande cycle. Elle ne libere plus les banques piston en bloc: aucune ecriture globale `0` ou `1` sur `28414..28424` / `28926..28936`. Seule exception autorisee: remettre le reset NG `28305` a `1` (repos) si ce registre est reste bloque a `0`.
- Pendant le cycle machine, le routage physique reste confie a l'automate via les seuils `1188..1370`. TriCell Pilot ne pulse plus NG ni les lignes GOOD en direct avec les banques I/O non validees.
- Si le statut automate `8231` vaut `7`, `DEMARRER` est refuse clairement avec la cause d'alarme visible, par exemple pression air. TriCell Pilot ne tente aucun rearmement automatique; le bouton `REARMER` envoie volontairement `5978=26` puis `0`, sans ecriture piston.
- Le CSV audit doit contenir les colonnes `routing_model`, `quality_interval`, `voltage_min`, `voltage_max`, `ir_min`, `ir_max`.
- Quand `Production.QualityIntervals` est non vide, il doit contenir 9 elements.
- `/api/maintenance/command` avec `CONVEYOR_FINE_FORWARD` pulse uniquement le coil constructeur d'avance convoyeur `1X 5981=true` puis relache `false` avec une impulsion tres courte de realignement tapis. `CONVEYOR_ONLY_FORWARD` utilise le meme coil avec une impulsion plus longue de degagement. Aucun registre piston n'est ecrit, ni a `0` ni a `1`. Ne pas utiliser `WriteHoldingRegisters` sur `5981` pour le tapis. Les commandes brutes de mouvement ne sont pas exposees dans l'UI.
- `/api/maintenance/command` avec `RELEASE_NG_PISTON` ecrit uniquement `28305=1` pour relacher le reset du verin NG. Il lit `28689`, `28424` et `28936` pour diagnostic, mais n'ecrit ni l'enable NG ni la sortie NG.
- `/api/maintenance/piston-test` accepte uniquement `lane` (`1..10` ou `NG`) mais renvoie une erreur claire en mode reel: les banques `28295..28314`, `28414..28433` et `28926..28945` restent non validees pour la production.
- Si une securite machine bloque les verins, par exemple arret d'urgence ou pression d'air, `/api/maintenance/piston-test` renvoie aussi une erreur claire et n'ecrit aucun registre piston.
