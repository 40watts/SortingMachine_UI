# Exigences operateur - TriCell Pilot

Ce document capture la logique attendue en atelier. Il doit rester aligne avec `desktop_v2/docs/quality-interval-routing.md`.

## Objectif

Construire un seul logiciel desktop Windows, en francais, qui pilote vraiment la machine, trace les decisions et permet de comprendre cellule par cellule pourquoi une cellule est allee en ligne 1-9 ou en NG.

## Exigences verrouillees

1. Un seul point d'entree
- L'operateur utilise `TriCell Pilot`.
- Le dossier actif est `desktop_v2/`.
- Les anciens dossiers `backend/`, `frontend/` et `desktop_app/` sont des references historiques.

2. IHM operateur lisible
- Interface en francais.
- Informations critiques visibles en haut: etat machine, lot, ligne/voie live, tension, IR, NG, alarmes.
- Pas de texte coupe, pas de blanc sur blanc, pas de bouton qui cache un panneau.
- Rearmement machine et remise a zero logiciel de l'apprentissage sont separes.

3. Commandes machine
- Boutons `DEMARRER`, `PAUSE`, `STOP`.
- `REARMER` / `RESET=26` est une commande operateur explicite dans TriCell Pilot. Elle ne doit jamais etre envoyee automatiquement par `DEMARRER`; l'operateur l'utilise seulement apres controle mecanique quand le statut automate `7` empeche de relancer.
- L'automate ignore `RESET=26` tant qu'il est en marche (statut `8231=1`, constate par lecture directe le 10 juin 2026). `REARMER` doit donc reproduire le flux operateur constructeur: si le statut est `1`, envoyer d'abord `STOP=29`, attendre la sortie du run (max ~4 s), puis envoyer `26`. Trace `RESET_AUTO_STOP`.
- Chaque station piston a un enable constructeur (`28414+i`, lignes 1..10 + NG=`28424`) qui doit valoir `1` pour que le PLC puisse tirer le piston, quels que soient les seuils. Constate par lecture directe le 10 juin 2026: les enables 1..10 etaient a 0 (mis a zero par les ecritures en bloc de mai) et seul le NG arme tirait. TriCell Pilot rearme AUTOMATIQUEMENT toute station a 0 (trace `PUSHER_STATIONS_AUTO_ARM`): a la connexion et juste avant chaque `DEMARRER`, un registre a la fois, jamais les sorties `28926..28936`. L'operateur n'a aucun bouton a utiliser. Le pre-vol DEMARRER avertit si une station reste desarmee malgre tout. L'interdiction historique vise les ecritures en bloc A ZERO, qui desarment la machine.
- Diagnostic clair si une commande est bloquee, refusee ou envoyee.
- Les commandes machine non validees terrain doivent rester dans la zone Maintenance et demander une confirmation inline avant envoi.
- Le degagement mecanique doit se faire par une commande dediee `Avancer convoyeur`: pulser le coil constructeur d'avance convoyeur `1X 5981` pendant une impulsion visible terrain.
- Le realignement fin du tapis devant les pistons doit utiliser `Micro-avance tapis`: coil constructeur `1X 5981`, impulsion courte mais visible. Ne jamais simuler une micro-avance par une ecriture holding sur `5981`.
- `Avancer convoyeur` et `Micro-avance tapis` sont des commandes maintenance: elles doivent etre bloquees pendant un cycle arme et ne redevenir disponibles qu'apres `STOP` ou `PAUSE` et arret des tops `8230`.
- Les commandes brutes `Avance`, `Recul`, `Pas a pas`, `Nettoyage`, `Test manuel` ne doivent pas etre exposees directement: seule la commande dediee `Avancer convoyeur` est validee terrain.
- Les tests piston directs ligne `1..10` et `NG` doivent etre disponibles dans la zone Maintenance pour diagnostic unitaire, uniquement machine arretee. Ils doivent refuser un cycle arme, maintenir une impulsion assez longue pour observation terrain, et ne jamais pulser les resets `28295..28305`. Le test `NG` utilise les registres manuels constructeur du verin NG (slot 11): enable `28424` puis sortie `28936`, comme la page I/O du logiciel chinois.
- Exception securite NG: la commande `Liberer verin NG` peut uniquement remettre le reset NG `28305` a `1` (repos) si le verin NG est reste bloque sorti. Elle n'ecrit ni l'enable `28424` ni la sortie `28936`.
- Reglages constructeur `640` (page reglages de base du logiciel chinois): bit `0` = rearmement automatique a la mise sous tension (开机后复位), bit `2` = scan. Le PLC protege ce registre (les ecritures sont annulees). TriCell Pilot le lit en diagnostic seulement et n'y ecrit pas. L'alarme dechargement (卸料报警) correspond a un defaut de l'axe moteur de dechargement (下料电机, parametres servo `10070..10138`/`28548`): si elle persiste apres reset constructeur, le diagnostic est mecanique/electrique (moteur, variateur, capteurs de l'axe de dechargement), pas logiciel.
- La liberation automatique du reset NG (`28305=1`) est interdite pendant un cycle arme; elle ne peut agir qu'hors cycle, avant `DEMARRER`, ou via la commande operateur explicite `Liberer verin NG`.
- Diagnostic carte cible: la commande `Diagnostic sortie Y11` peut etre lancee machine arretee uniquement. Elle pulse seulement la sortie carte `4X 3144` bit `10` pour observer la LED Y; ce n'est pas le verin NG. Le verin NG se teste par le test piston NG (`28424/28936`).
- Le diagnostic brut `Y11 ON maintenu` est bloque en reel: utiliser `Diagnostic sortie Y11` pour une impulsion ON/OFF validee, ou `Y11 OFF` pour liberer une sortie restee active.
- Si une securite machine bloque les verins, par exemple arret d'urgence ou pression d'air, le test piston doit etre refuse clairement et ne doit pas afficher un faux succes.
- Juste avant `DEMARRER`, l'application doit lire et memoriser le top courant `8230` comme base de depart, precharger les seuils machine `1188..1370` avec la trace `START_PRELOAD`, puis ecrire `5978=31` UNE SEULE FOIS comme le logiciel chinois (fonction Modbus 16, jamais de remise a 0 cote PC: le PLC consomme la commande et efface le registre; reecrire 0 peut effacer la commande avant que l'automate la lise). Si `8230` est illisible, `DEMARRER` doit etre refuse: sans cette base, la premiere cellule apres START peut etre ignoree. Elle ne doit pas ecrire en bloc sur les banques piston `28414..28424` et `28926..28936`, meme a `0`.
- `DEMARRER`, `PAUSE`, `STOP` et le demarrage de l'application ne doivent jamais ecrire en bloc sur les sorties piston GOOD. En production, les pistons GOOD restent pilotes par le PLC via les seuils `1188..1370`, comme dans le logiciel chinois. TriCell Pilot ne pulse pas les pistons GOOD directement.
- Verin NG en production: le poussoir 11/NG est le dernier ejecteur mecanique de ligne. Comme dans le logiciel chinois, c'est le PLC qui le pousse et le ramene a chaque avance, pour toute cellule non captee par une voie GOOD. TriCell Pilot ne pulse aucun registre NG en production: il programme la voie 11 avec la fenetre catch-all constructeur (V 0..99.9, IR 0..999.99) et laisse l'automate executer. Le bit `10` de `3144` est une sortie carte Y, pas le verin NG; il reste reserve aux diagnostics maintenance.
- Si le scanner est active, le top constructeur attendu reste `8230=1`. Apres traitement scan, TriCell Pilot doit repondre comme le logiciel chinois: `8230=0` si barcode valide, `8230=2` si barcode absent/repli. La valeur `NoBarcodeValue` decide alors `NG` ou `CON`; cette cellule ne doit pas alimenter l'apprentissage qualite ligne 10.
- Le paquet mesure constructeur IR/tension doit etre lu par defaut a partir de `8408` sur 4 registres, comme dans le binaire du logiciel chinois (`ReadFloatsMust(8408, 2)`). `8402` reste seulement une lecture live/diagnostic; si une ancienne config locale a ete sauvee avec `8402` comme paquet de tri, elle doit etre corrigee vers `8408`.
- Si l'automate indique le statut `7` (`rearmement requis avant demarrage`), `DEMARRER` doit etre refuse sans envoyer `5978=26`. Le bouton `REARMER` peut ensuite envoyer volontairement `5978=26` puis relacher a `0`, sans aucune ecriture piston. Si une alarme claire existe, par exemple pression air, l'UI doit afficher cette cause au lieu d'un message generique.

4. Logique lot
- Association lot Odoo conservee tant que le lot local continue.
- Recommencer l'apprentissage ne doit pas deselectionner le lot Odoo.
- Le logiciel doit distinguer nouveau lot, lot repris, lot cloture et lot sans association Odoo.

5. Tri qualite actuel
- La ligne 10 recoit les cellules d'apprentissage.
- Le modele du lot se fige uniquement apres 19 cellules ligne 10.
- Apres gel, les lignes 1 a 9 sont les 9 intervalles de resistance interne du lot.
- La resistance choisit la ligne.
- La tension ne choisit jamais la ligne; elle sert seulement de garde sous-charge / surcharge.
- Une cellule hors garde tension ou hors garde resistance part en NG.
- La garde resistance doit prevoir une reserve adaptative pour eviter le NG massif quand les 19 cellules d'apprentissage ne couvrent pas toute la queue naturelle du lot.
- La machine gere le plein physique; le logiciel de tri ignore les etats FULL/capacite pour decider une cellule.
- Une fois le modele fige pour un lot, il ne doit plus bouger pendant la production.
- La voie NG en fenetre catch-all constructeur est le principe de securite: le PLC evalue les voies dans l'ordre 1..N avec NG en dernier, donc la voie NG ne vole pas les lignes GOOD. Toute cellule qu'aucun intervalle GOOD ne prend est poussee physiquement par le verin NG au slot 11, par l'automate. TriCell Pilot ne pulse aucun piston en production. Une voie 11 laissee hors seuils est interdite: elle laisse les cellules non matchees filer jusqu'au dechargement et declenche l'alarme dechargement (constatee les 4-8 juin 2026).

6. Cohérence affichage / machine
- La decision affichee, le CSV, l'historique, les compteurs et les seuils programmes doivent raconter la meme chose.
- Le CSV d'audit doit indiquer modele, intervalle, bornes tension, bornes IR, voie voulue, voie appliquee, confirmation et mismatch.
- Les bornes hautes sont exclusives comme dans le moteur machine: `min <= mesure < max`.

7. Mesures et comptage
- Tension et IR sont traitees en valeur absolue.
- Le logiciel ne doit pas compter de cellules fictives.
- Les compteurs machine restent prioritaires quand ils sont disponibles.
- Toute incoherence entre intention logicielle et voie appliquee doit etre visible dans l'audit.

8. Logs et diagnostic
- `runtime_trace.csv` doit permettre de comprendre les lectures PLC, commandes, seuils, decisions et alertes.
- L'historique UI doit montrer les cellules recentes avec les seuils appliques.
- Les exports API doivent rester lisibles sans ouvrir l'UI.
- La validation terrain hors production doit utiliser le lanceur racine `validate_tricell_field.bat`, qui appelle `desktop_v2/tools/Start-FieldValidationWatch.ps1` puis `desktop_v2/tools/Watch-TriCellFieldValidation.ps1` en lecture seule. Le rapport doit separer verdict trace logiciel, delta compteurs machine, observation physique operateur et couverture voies GOOD. L'observation physique doit etre saisie dans un CSV pre-rempli par voie GOOD requise avec `expected_lane`, `observed_lane` et `ng_pulse_seen`; si ce CSV est complete apres la fenetre de surveillance, `refresh_tricell_field_result.bat` doit appeler `desktop_v2/tools/Update-FieldValidationReportFromCsv.ps1` pour produire un rapport actualise sans commande machine. Le tri reel n'est prouve que si ces verdicts sont `OK`, si les lignes GOOD `1..9` requises ont toutes ete observees, si la preuve source contient aussi assez de tops `8230` acceptes et de delta compteurs, et que `check_tricell_field_result.bat` valide aussi que le `Lot: #...` du rapport correspond au lot courant de l'API.

## Defauts deja observes a ne plus reintroduire

- Ancienne logique d'apprentissage court / ligne 1 comme apprentissage principal.
- Ancienne logique 3 familles.
- Gel d'apprentissage avant 19 cellules a cause d'un signal ligne pleine.
- Tension negative envoyee NG au lieu d'etre normalisee en valeur absolue.
- Affichage NG different de la logique appliquee.
- CSV sans les bornes qui expliquent la decision.
- Lot Odoo deselectionne apres rearmement machine.
- Ligne 1-9 interpretee comme sequence fixe alors que la ligne depend maintenant de l'IR.
- Ligne pleine interpretee comme "ligne indisponible" puis cellule envoyee NG.
- Ligne pleine interpretee comme ordre de pause, bascule ou arret logiciel.
- Bouton apprentissage trop gros ou place hors du panneau de pilotage.
- Intervalles IR trop dependants des quantiles, avec une ligne extreme enorme et trop de NG naturels.
- Voie 11/NG programmee hors seuils (fenetre vide): le PLC ne pousse jamais le verin NG, les cellules non matchees filent au dechargement et l'alarme dechargement devient permanente.
- Pulse PC du bit 10 de 3144 presente comme balayage NG: ce bit est une sortie carte Y, pas le verin NG; l'ecriture reussit en Modbus mais rien ne bouge physiquement.

## Priorites a maintenir

1. Fiabilite machine avant cosmetique.
2. Traçabilite cellule avant confort UI.
3. Une seule app active.
4. Toute regression doit etre couverte par `test_desktop_v2.bat`.
