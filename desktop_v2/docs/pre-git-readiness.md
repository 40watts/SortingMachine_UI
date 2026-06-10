# Etat avant Git - TriCell Pilot

Date: 2026-05-23

## Intention produit

TriCell Pilot est l'application Windows active pour piloter et tracer le tri cellule par cellule. L'objectif est un seul poste operateur clair: lot Odoo associe si disponible, prechargement des seuils machine, apprentissage ligne 10, audit CSV/UI, puis routage physique entierement par seuils PLC: voies GOOD 1..9 et voie 11 NG en fenetre catch-all constructeur.

## Regles verrouillees

- Ligne 10: apprentissage du lot uniquement.
- Echantillon: 19 cellules, constant dans le backend et l'UI.
- Apres gel: lignes 1 a 9 = 9 intervalles fixes de resistance interne.
- Tension: garde sous-charge / surcharge seulement, jamais selection de ligne.
- NG: hors garde tension ou IR, sans chevauchement avec les lignes GOOD.
- Production: l'application programme les seuils `1188..1370` (GOOD + NG catch-all); le PLC pilote tous les pistons, TriCell Pilot n'en pulse aucun.
- Plein physique: gere par la machine, pas par la decision logicielle.

## Nettoyage realise

- Secret Odoo deplace hors Desktop vers `%APPDATA%\TriCellPilot\ODOO.txt`.
- `OdooConfigLocator` extrait de `MachineState` pour isoler la lecture config/secrets.
- `.gitignore`, `.gitattributes`, `.env.example` et `tools/RepositoryPreflight.ps1` ajoutes.
- Dossiers historiques `backend/`, `frontend/`, `desktop_app/` et artefacts runtime exclus du scope Git.
- UI recette verrouillee sur `LEARNING_SAMPLE_TARGET = 19`, ligne 10, lignes 1-9 et NG fixe.
- Recette C# par defaut alignee sur `QualityBandRouting`.
- Documentation locale et NAS alignee sur 19 cellules.

## Validations executees

- `powershell -NoProfile -ExecutionPolicy Bypass -File desktop_v2\tools\RepositoryPreflight.ps1 -Root .`: OK.
- `powershell -NoProfile -ExecutionPolicy Bypass -File desktop_v2\tools\StaticQualityChecks.ps1`: OK.
- `build_desktop_v2.bat`: OK.

## Limites restantes

- `git` n'est pas disponible dans le PATH de cette machine; impossible d'initialiser ou d'inspecter le depot Git ici.
- La validation sans machine passe par `test_desktop_v2_no_machine.bat` ou par `test_desktop_v2.bat` sans variable d'opt-in: build, regressions simulateur/API locale, controles statiques et preflight, sans demarrer l'application connectee ni toucher aux ports serie. Les smoke tests qui peuvent lancer `TriCellPilot.exe` exigent `TRICELL_ALLOW_APP_SMOKE=1`.
- `MachineState.cs` et `app/web/app.js` restent de gros fichiers a decouper par tranches verifiees; cette dette n'est pas acceptee comme etat final.

## Avant premier commit

- Installer Git ou ouvrir un terminal ou `git` est disponible.
- Relancer les trois validations ci-dessus.
- Ne versionner que le scope actif: racine, `desktop_v2/app`, `desktop_v2/docs`, `desktop_v2/tools`, scripts v2 et fichiers d'hygiene Git.
- Verifier que `desktop_v2/bin/`, `webview2_pkg/`, `backend/`, `frontend/`, `desktop_app/`, `ODOO.txt`, `odoo_config.json` et `.env` restent hors Git.
