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

## Quality gate complet

```bat
test_desktop_v2.bat
```

Ce script verifie:

- compilation de l'application;
- tests du registre de routage;
- tests du routage qualite 9 intervalles;
- controles statiques anti-regression;
- smoke test API local.

## Quality gate CI / sans machine

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File desktop_v2\tools\CiQualityChecks.ps1 -Root .
```

Ce script build l'application, execute les regressions console et les checks statiques sans demarrer l'application locale.

## Regle metier actuelle

- Ligne 10: apprentissage du lot, gel obligatoire a 19 cellules.
- Lignes 1 a 9: 9 intervalles de resistance interne figes pour le lot.
- Tension: garde sous-charge / surcharge uniquement.
- NG: mesure invalide, tension hors garde, resistance hors garde ou incoherence de routage.
- Commande physique: le routage production reste confie a l'automate via les seuils machine `1188..1370`; TriCell Pilot ne pulse plus directement les pistons GOOD/NG pendant la production.
- Maintenance tapis: `Micro-avance tapis` et `Avancer convoyeur seul` utilisent uniquement le coil convoyeur constructeur `1X 5981`, sans signal piston.
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
