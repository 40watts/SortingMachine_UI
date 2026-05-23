# Architecture - TriCell Pilot desktop v2

Date: 2026-05-23

## Frontieres actives

`desktop_v2` est le seul produit actif. Les anciens dossiers racine `backend`, `frontend` et `desktop_app` sont ignores par Git et ne doivent pas recevoir de nouvelle logique.

## Flux principal

1. `Program.cs` demarre la fenetre WPF.
2. `MainWindow.cs` heberge WebView2 et l'API locale.
3. `ApiServer.cs` expose les endpoints HTTP utilises par l'interface web.
4. `MachineState.cs` orchestre l'etat temps reel, les lots, le PLC et les snapshots.
5. `QualityBandRouting.cs` porte la regle 19 cellules, ligne 10 puis 9 intervalles IR.
6. `SortingEngines.cs` applique la decision cellule par cellule.
7. `RoutingLedgerService.cs` relie decision logicielle, voie appliquee et confirmation physique.
8. `TraceStore.cs`, `HistoryStore.cs`, `ObservationStore.cs` et `BusinessStore.cs` persistent les traces.

## Modules isoles

- `OdooConfigLocator.cs`: localisation et lecture des secrets Odoo hors depot.
- `ApiContractCatalog.cs`: contrat API humain/machine servi par `/api/contracts`.
- `QualityIntervalAudit.cs`: reconstruction des bornes et intervalles pour l'audit.
- `RobustWindowCalculator.cs`: calcul robuste de fenetres metrologiques.

## Garde-fous

- `RepositoryPreflight.ps1`: bloque secrets, runtime data, anciens dossiers et paquets lourds avant Git.
- `StaticQualityChecks.ps1`: bloque regressions metier, doc manquante, UI dangereuse et hygiene Git.
- `CiQualityChecks.ps1`: build + regressions console + checks statiques, sans demarrer l'application.
- `.github/workflows/windows-ci.yml`: execute `CiQualityChecks.ps1` sur `main` et pull requests.

## Dette restante non acceptee

`MachineState.cs` et `app/web/app.js` restent trop gros. Le plan propre est de les reduire par extractions verifiees, jamais par reecriture massive:

- `MachineThresholdService`: seuils `1188..1370`, preload et comparaison PLC.
- `MaintenanceCommandCatalog`: definitions et restrictions des commandes maintenance.
- `LotLifecycleService`: creation, reprise, cloture et reset apprentissage.
- `ProductionSnapshotBuilder`: construction des snapshots UI/API.
- `web/api-client.js`: appels HTTP et erreurs.
- `web/production-view.js`: panneaux production, intervalles et historique.
- `web/recipe-view.js`: edition verrouillee des recettes.

Chaque extraction doit garder `CiQualityChecks.ps1` vert et, en atelier, `test_desktop_v2.bat` vert.
