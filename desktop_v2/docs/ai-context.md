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
10. La voie physique 11/NG est programmee avec la fenetre catch-all constructeur (V 0..99.9, IR 0..999.99). Le PLC evalue les voies dans l'ordre 1..N avec NG en dernier: la voie NG ne vole pas les GOOD.
11. TriCell Pilot programme les seuils `1188..1370`; le PLC pilote tous les pistons (GOOD et NG) comme dans le logiciel chinois. TriCell Pilot ne pulse aucun piston en production.
12. Le verin NG constructeur = slot piston 11 (manuel `28424/28936`, reset `28305`, retour `28689`, etat `28168`). Le bit `10` de `3144` est une sortie carte Y, pas le verin NG: les pulses Y11 ne bougent rien physiquement (constate terrain 10 juin 2026). Une voie 11 hors seuils laisse les cellules non matchees filer au dechargement (alarme dechargement permanente des 4-8 juin 2026).

## Fichiers a lire en premier

- `docs/code-quality-audit.md`
- `docs/architecture.md`
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
- `DEMARRER` ne doit jamais envoyer `RESET=26` automatiquement; seul le bouton operateur `REARMER` peut ecrire `5978=26`.
- Les commandes cycle `5978` s'ecrivent UNE SEULE FOIS (fonction 16), comme le logiciel chinois: le PLC consomme la commande et remet le registre a 0. Ne jamais reecrire 0 cote PC (l'ancienne impulsion 500 ms + relachement effacait la commande par intermittence: RESET/START sans effet les 8-10 juin 2026). A la connexion, ecrire `5978=57` une fois (MC.Start du binaire chinois).
- Avant `DEMARRER`, TriCell Pilot doit lire le top courant `8230` et l'utiliser comme base de depart. Si `8230` est illisible, START doit etre refuse pour ne pas perdre la premiere cellule.
- Le paquet mesure constructeur IR/tension se lit par defaut a `8408` longueur 4, conformement au binaire chinois (`ReadFloatsMust(8408, 2)`). `8402` reste une lecture live/diagnostic; une config locale erronement sauvegardee avec `8402` comme paquet de tri doit migrer vers `8408`.
- `NO_GOOD_LANE_AVAILABLE` ne doit pas etre produit par le routage 9 intervalles.
- Les etats ligne pleine ne doivent jamais produire de pause, bascule, NG ou changement de seuil en mode 9 intervalles.
- L'UI doit afficher "Selon IR" quand le modele 9 intervalles est actif.
- La voie 11/NG doit garder sa fenetre catch-all constructeur: le PLC evalue dans l'ordre 1..N (NG en dernier), donc elle ne vole pas les GOOD. Si toutes les cellules partent NG, chercher la cause dans les fenetres GOOD (ex: garde tension trop etroite, cas du 19 mai 2026), pas dans la voie NG.
- Ne jamais remettre la voie 11 hors seuils: les cellules non matchees ne seraient plus poussees par le verin NG et fileraient au dechargement (alarme dechargement permanente des 4-8 juin 2026).
- Ne pas revenir au mode direct GOOD ni au pulse NG cote PC: tous les pistons sont pilotes par le PLC via seuils machine. TriCell Pilot n'ecrit aucun piston en production.
- Si le scanner est active, respecter le handshake constructeur: accepter `8230=1`, puis repondre `8230=0` si barcode valide ou `8230=2` si barcode absent/repli. `NoBarcodeValue=NG/CON` doit decider le resultat et ne doit pas alimenter l'apprentissage qualite.
- Ne pas retablir d'ecriture globale sur les banques piston avant `START` ou apres test piston: meme a `0`, `28414..28424` et `28926..28936` ne doivent pas etre ecrits en bloc par le flux normal.
- Exceptions limitees: pour un NG bloque sorti, `RELEASE_NG_PISTON` ecrit `28305=1` afin de relacher le reset NG; il n'ecrit pas l'enable/sortie `28424/28936`. Le test piston NG maintenance (machine arretee) utilise les registres manuels constructeur `28424` puis `28936`, comme la page I/O chinoise.
- `AUTO_NG_RELEASE` ne doit pas reecrire `28305=1` pendant un cycle arme; il est reserve au hors-cycle.
- Reglages constructeur `640` (page reglages de base chinoise): bit `0` = rearmement auto a la mise sous tension (开机后复位), bit `2` = scan. ATTENTION: ne pas confondre avec le moteur de dechargement — erreur deja faite le 10 juin 2026. Le PLC annule les ecritures sur `640` (write OK puis relecture inchangee): lecture diagnostique seulement (trace `MACHINE_SWITCHES`). Le "moteur de dechargement" (下料电机) est un axe servo avec parametres `10070..10138`/`28548`; l'alarme dechargement (卸料报警, bit 5) = defaut de cet axe, diagnostic mecanique/electrique si elle persiste.
- Diagnostic limite: `DIAG_PULSE_NG` pulse la sortie carte Y11 `3144.10` machine arretee pour identifier la LED Y; ce n'est pas le verin NG. Il doit refuser un cycle arme. Le `Y11 ON maintenu` brut est bloque en reel; utiliser l'impulsion ON/OFF ou `Y11_OUTPUT_OFF`.
- Les commandes convoyeur de maintenance (`CONVEYOR_FINE_FORWARD`, `CONVEYOR_ONLY_FORWARD`) doivent aussi refuser un cycle arme; elles restent des actions manuelles hors production.

## Dette a corriger

- Ne pas accepter `MachineState.cs` ou `app/web/app.js` comme dette durable.
- Toute extraction doit garder le comportement machine identique et le quality gate vert.
- Les tests console restent autorises pour compatibilite, mais les invariants metier et l'hygiene Git doivent etre controles.

## Style de modification attendu

- Preferer des refactors petits et verifies.
- Ne jamais changer une regle machine sans ajouter ou modifier un test de regression.
- Documenter toute nouvelle regle metier dans `docs/quality-interval-routing.md`.
- Ne documenter aucune nouvelle dette comme acceptee; soit elle est corrigee, soit elle devient un blocage explicite avant Git.
