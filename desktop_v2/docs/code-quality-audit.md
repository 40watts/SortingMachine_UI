# Audit qualite code - desktop v2

Date: 2026-05-23

## Objectif

Ce document sert de point d'entree pour un audit technique externe. Il explique les zones critiques, les garanties attendues et les tests a relancer avant une demonstration ou une livraison.

## Regles metier verrouillees

- La ligne 10 est la seule ligne d'apprentissage du lot.
- L'apprentissage fige le modele uniquement a 19 cellules.
- Apres gel, les lignes 1 a 9 representent 9 intervalles de resistance interne.
- La tension ne choisit jamais la ligne; elle sert uniquement de garde sous-charge / surcharge.
- Les bornes hautes sont exclusives partout: `min <= mesure < max`.
- Le modele du lot est fige dans `LotReference.QualityIntervals` et ne bouge plus pendant la production.
- Le logiciel et les seuils machine partagent la meme source de verite.
- En mode 9 intervalles, les etats FULL/capacite ne participent pas au tri: la machine gere le plein physique.

## Structure critique

- `QualityBandRouting.cs`: construit le modele 9 intervalles, applique les gardes tension/IR et resout la ligne cible.
- `QualityIntervalAudit.cs`: reconstruit le contexte d'audit cellule par cellule, y compris pour les anciens tickets sans bornes stockees.
- `SortingEngines.cs`: applique le cycle apprentissage ligne 10 puis le routage ligne 1-9.
- `MachineState.cs`: orchestre PLC, lots, compteurs, exports, historique et API.
- `RoutingLedgerService.cs`: registre FIFO entre decision logicielle, voie appliquee et confirmation physique.
- `app/web/app.js`: UI operateur, historique audit, affichage graphique des intervalles.
- `tools/StaticQualityChecks.ps1`: controles statiques anti-regression.
- `tools/ApiSmokeCheck.ps1`: smoke test API local.
- `tools/RepositoryPreflight.ps1`: controle avant Git des secrets et artefacts interdits.

## Points de refactor deja faits

- Extraction de `QualityIntervalAudit` hors de `MachineState` pour isoler la logique de backfill audit.
- Suppression des champs herites 3-bandes dans `QualityBandWindows`.
- Alignement de la recette par defaut sur `LearningSampleCount = 19`.
- Export CSV et historique UI enrichis avec modele, intervalle et bornes appliquees.
- Tests de regression dedies au routage 9 intervalles et au registre de routage.
- README racine remplace pour pointer uniquement vers `desktop_v2`.
- `run_app.bat` redirige maintenant vers `run_desktop.bat` au lieu de lancer l'ancien backend.
- `run_desktop.bat` build directement `build_desktop_v2.bat`.
- Extraction de `OdooConfigLocator` hors de `MachineState`: les secrets ne sont plus lies au fichier orchestrateur.
- Verrouillage UI de la recette 19 cellules: `LEARNING_SAMPLE_TARGET = 19`, ligne 10 apprentissage, lignes 1-9 intervalles IR, voie NG fixe.
- Recette C# par defaut alignee sur `QualityBandRouting.BuildDefaultGoodLanes()` et `LearningLaneId`.
- Preflight Git ajoute et execute: secrets, artefacts runtime et anciens dossiers exclus du scope Git.

## Tests reproductibles

Depuis `C:\Users\Administrator\Desktop\SortingMachine_UI`:

```bat
test_desktop_v2.bat
```

Ce script fait:

- build de `TriCellPilot.exe`;
- compilation et execution de `RoutingLedgerRegression.exe`;
- compilation et execution de `QualityIntervalRoutingRegression.exe`.
- controles statiques documentation/UI/termes obsoletes/scripts de lancement;
- smoke test API sur `/api/state`, `/api/recipes/intelligent`, `/api/cells/audit`, `/api/export/cells-audit-csv` et `/api/contracts`.

## Couverture actuelle

- FIFO du registre de routage.
- Confirmation physique des voies.
- Reset du registre de routage.
- Creation du modele `IR_9_INTERVALS`.
- Presence des 9 intervalles.
- Regularisation des largeurs d'intervalles et repartition sur un echantillon IR irregulier.
- Normalisation des tensions negatives en valeur absolue.
- Bornes hautes exclusives tension et IR.
- Gel obligatoire a 19 cellules meme si un signal ligne pleine remonte.
- Etats FULL/capacite ignores par la decision 9 intervalles: pas de pause, pas de bascule, pas de NG.
- Backfill audit d'une cellule GOOD vers son intervalle.
- Backfill audit d'une cellule NG vers les gardes du lot.
- Routage physique de production confie a l'automate apres programmation des seuils; les banques I/O directes restent hors production tant qu'elles ne sont pas validees terrain.
- Interdiction des ecritures de production sur banques piston: le flux normal programme les seuils `1188..1370`, trace `START_PRELOAD`, puis laisse l'automate piloter les sorties physiques.
- Presence des documents humains et IA.
- Garde-fous Git: `.gitignore`, `.gitattributes`, exemple `.env`, verification absence secrets locaux dans le depot.
- Scripts de lancement alignes sur l'application active.
- Contrat API minimal et colonnes CSV obligatoires.
- Validation 2026-05-23: `RepositoryPreflight OK`, `StaticQualityChecks OK`, `build_desktop_v2.bat` OK.

## Dette technique a eliminer avant Git

La dette suivante n'est pas acceptee comme etat final du depot:

- `MachineState.cs` ne doit plus etre traite comme un bloc unique; les zones Odoo, seuils, maintenance, lots et snapshots doivent etre isolees progressivement.
- `app/web/app.js` ne doit plus etre traite comme un bloc unique; les zones API, production, recettes, historique, maintenance et Odoo doivent etre separees.
- Les regressions console restent utilisables pour compatibilite .NET Framework, mais le quality gate doit couvrir explicitement les invariants metier et l'hygiene Git.

## Prochaine etape recommandee

La prochaine tranche de qualite doit extraire de `MachineState.cs`:

- `LotLifecycleService` pour creation, reprise, cloture et reset lignes;
- `MachineThresholdService` pour programmation et comparaison des seuils PLC;
- `CellAuditService` pour historique, CSV et donnees audit;
- `MachineCounterService` pour synchronisation compteurs machine / lot.

Chaque extraction doit garder `test_desktop_v2.bat` vert avant de passer a la suivante. Aucune nouvelle dette ne doit etre documentee comme acceptee.
