# TriCell Pilot - application active

Application desktop Windows de pilotage et d'audit pour la machine de tri cellules 18650 / 21700.

## Perimetre officiel

Le seul logiciel actif est:

```text
desktop_v2/
```

Les dossiers historiques suivants restent hors perimetre Git actif. S'ils existent localement, ils servent uniquement a comparaison ponctuelle et ne doivent pas etre utilises en production:

- `backend/`
- `frontend/`
- `desktop_app/`

## Lancement

```bat
run_desktop.bat
```

ou directement:

```bat
desktop_v2\bin\TriCellPilot.exe
```

## Build

```bat
build_desktop_v2.bat
```

## Quality gate par defaut

```bat
test_desktop_v2.bat
```

Ce script lance la gate sans machine ci-dessous et s'arrete par defaut avant tout test qui demarre l'application. Les smoke tests applicatifs sont optionnels:

```bat
set TRICELL_ALLOW_APP_SMOKE=1
test_desktop_v2.bat
```

Avec cet opt-in explicite, le script peut lancer `desktop_v2\bin\TriCellPilot.exe` pour `FieldValidationWatcherRegression` et `ApiSmokeCheck`. Ne pas utiliser cette option pendant une interdiction de test machine.

## Quality gate CI / sans machine

```bat
test_desktop_v2_no_machine.bat
```

Ce script build l'application, execute les regressions console, l'API locale en simulateur, le simulateur NG sweep, les checks statiques et le preflight sans lancer TriCell Pilot connecte et sans envoyer de commande machine.

Le sous-ensemble CI historique reste disponible:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File desktop_v2\tools\CiQualityChecks.ps1 -Root .
```

Ce script build l'application, execute les regressions console principales et les checks statiques sans demarrer l'application locale.

## Regle metier actuelle

- Ligne 10: apprentissage du lot, gel obligatoire a 19 cellules.
- Lignes 1 a 9: 9 intervalles de resistance interne figes pour le lot.
- Tension: garde sous-charge / surcharge uniquement.
- NG: mesure invalide, tension hors garde, resistance hors garde ou incoherence de routage.
- Commande physique: tout le routage production (GOOD et NG) est confie a l'automate via les seuils machine `1188..1370`; TriCell Pilot ne pulse aucun piston en production. La voie physique 11/NG est programmee avec la fenetre catch-all constructeur (V 0..99.9, IR 0..999.99): le PLC pousse et ramene le verin NG sur chaque cellule non captee par une voie GOOD, comme le logiciel chinois. Le verin NG constructeur correspond au slot piston 11 (manuel `28424/28936`, reset `28305`, retour `28689`); le bit `10` de `3144` est une sortie carte Y, pas le verin NG.
- Maintenance tapis: `Micro-avance tapis` et `Avancer convoyeur` utilisent uniquement le coil convoyeur constructeur `1X 5981`.
- Ligne pleine/capacite: geree par la machine, ignoree par la decision de tri logiciel.
- Les bornes hautes sont exclusives: `min <= mesure < max`.

## Documentation

- `desktop_v2/docs/code-quality-audit.md`: audit qualite, architecture et dette a eliminer avant Git.
- `desktop_v2/docs/architecture.md`: frontieres du produit actif, modules et plan de decoupage.
- `desktop_v2/docs/pre-git-readiness.md`: etat date avant Git, validations et limites restantes.
- `desktop_v2/docs/quality-interval-routing.md`: logique de tri 9 intervalles.
- `desktop_v2/docs/ai-context.md`: contexte court pour reprise par IA ou nouvel auditeur.
- `desktop_v2/docs/operator-guide.md`: guide operateur.
- `desktop_v2/docs/api-contract.md`: endpoints locaux utiles.
- `desktop_v2/docs/git-hygiene.md`: fichiers exclus de Git et gestion des secrets.

## Donnees runtime

Les donnees runtime sont stockees dans:

```text
desktop_v2\bin\data\
```

Les exports importants sont accessibles depuis l'UI et via l'API locale `http://127.0.0.1:8050/api/*`.
