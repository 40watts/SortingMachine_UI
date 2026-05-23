# Contexte IA / reprise audit

Ce fichier est volontairement court. Il sert a reprendre le projet sans relire toute la conversation.

## Application active

- Racine active: `desktop_v2/`
- Executable: `desktop_v2/bin/TriCellPilot.exe`
- UI locale: `http://127.0.0.1:8050/`
- API locale: `http://127.0.0.1:8050/api/*`
- Tests: lancer `test_desktop_v2.bat` depuis la racine du depot.

Ne pas travailler dans `backend/`, `frontend/` ou `desktop_app/` sauf pour comparaison historique explicite.

## Regle de tri actuelle

1. Ligne 10 = apprentissage.
2. Le lot fige son modele uniquement apres 19 cellules ligne 10.
3. Le modele cree 9 intervalles de resistance interne.
4. Les lignes 1 a 9 correspondent directement aux intervalles 1 a 9.
5. La tension ne choisit pas la ligne; elle sert uniquement de garde sous-charge / surcharge.
6. Les bornes hautes sont exclusives: `min <= mesure < max`.
7. Les tensions et resistances negatives sont normalisees en valeur absolue.
8. Les 9 intervalles IR utilisent des quantiles regularises: assez adaptatifs pour les zones denses, mais sans ligne 1/9 enorme.
9. Le logiciel de tri ignore les etats FULL/capacite en mode intervalles: la machine gere le plein physique.
10. La voie physique 11/NG reste hors seuils programmes en mode intelligent, afin de ne jamais recouvrir les lignes GOOD.
11. Apres chaque decision cellule, TriCell Pilot garde le routage physique cote automate: seuils `1188..1370`, `START_PRELOAD`, puis cycle machine. Les banques I/O directes des pistons restent bloquees tant qu'elles ne sont pas validees terrain voie par voie.

## Fichiers a lire en premier

- `docs/code-quality-audit.md`
- `docs/quality-interval-routing.md`
- `docs/git-hygiene.md`
- `docs/pre-git-readiness.md`
- `app/QualityBandRouting.cs`
- `app/QualityIntervalAudit.cs`
- `app/SortingEngines.cs`
- `app/MachineState.cs`
- `app/web/app.js`

## Garanties a ne pas casser

- Le logiciel, l'UI, le CSV et les seuils machine doivent utiliser le meme modele.
- Le CSV `cells-audit-csv` doit contenir `routing_model`, `quality_interval`, `voltage_min`, `voltage_max`, `ir_min`, `ir_max`.
- `test_desktop_v2.bat` doit rester vert apres chaque modification.
- La ligne 10 doit collecter 19 cellules d'apprentissage; ce n'est pas une capacite physique geree par le logiciel.
- L'apprentissage ne doit jamais se figer avant 19 cellules.
- `DEMARRER` ne doit jamais envoyer `RESET=26` automatiquement; seul le bouton operateur `REARMER` peut pulser `5978=26` puis `0`.
- `NO_GOOD_LANE_AVAILABLE` ne doit pas etre produit par le routage 9 intervalles.
- Les etats ligne pleine ne doivent jamais produire de pause, bascule, NG ou changement de seuil en mode 9 intervalles.
- L'UI doit afficher "Selon IR" quand le modele 9 intervalles est actif.
- Ne jamais remettre un catch-all NG chevauchant les lignes GOOD: sur le terrain, il peut voler toutes les cellules. Les cellules non captees doivent aller au rejet machine par defaut.
- Ne pas retablir le routage direct post-decision par banques I/O: il a provoque une regression terrain ou NG pouvait bouger alors que les lignes GOOD ne poussaient pas.
- Ne pas retablir d'ecriture globale sur les banques piston avant `START` ou apres test piston: meme a `0`, `28414..28424` et `28926..28936` ne doivent pas etre ecrits en bloc par le flux normal.
- Exception limitee: pour un NG bloque sorti, `RELEASE_NG_PISTON` peut uniquement ecrire `28305=1` afin de relacher le reset NG. Ne jamais utiliser cette exception pour pulser `28424` ou `28936`.

## Dette a corriger

- Ne pas accepter `MachineState.cs` ou `app/web/app.js` comme dette durable.
- Toute extraction doit garder le comportement machine identique et le quality gate vert.
- Les tests console restent autorises pour compatibilite, mais les invariants metier et l'hygiene Git doivent etre controles.

## Style de modification attendu

- Preferer des refactors petits et verifies.
- Ne jamais changer une regle machine sans ajouter ou modifier un test de regression.
- Documenter toute nouvelle regle metier dans `docs/quality-interval-routing.md`.
- Ne documenter aucune nouvelle dette comme acceptee; soit elle est corrigee, soit elle devient un blocage explicite avant Git.
