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
- Diagnostic clair si une commande est bloquee, refusee ou envoyee.
- Les commandes machine non validees terrain doivent rester dans la zone Maintenance et demander une confirmation inline avant envoi.
- Le degagement mecanique doit se faire par une commande dediee `Avancer convoyeur seul`: pulser uniquement le coil constructeur d'avance convoyeur `1X 5981`, sans envoyer aucun signal piston, ni `0` ni `1`.
- Le realignement fin du tapis devant les pistons doit utiliser `Micro-avance tapis`: meme coil constructeur `1X 5981`, impulsion tres courte, sans aucun signal piston. Ne jamais simuler une micro-avance par une ecriture holding sur `5981`.
- Les commandes brutes `Avance`, `Recul`, `Pas a pas`, `Nettoyage`, `Test manuel` ne doivent pas etre exposees directement: elles ne garantissent pas l'absence totale d'ecriture piston.
- Les tests piston directs ligne `1..10` et `NG` doivent rester bloques dans la zone Maintenance tant que les banques I/O constructeur `28295..28314`, `28414..28433` et `28926..28945` ne sont pas validees terrain voie par voie.
- Exception securite NG: la commande `Liberer verin NG` peut uniquement remettre le reset NG `28305` a `1` (repos) si le verin NG est reste bloque sorti. Elle ne doit jamais ecrire l'enable `28424` ni la sortie `28936`.
- Si une securite machine bloque les verins, par exemple arret d'urgence ou pression d'air, le test piston doit etre refuse clairement et ne doit pas afficher un faux succes.
- Juste avant `DEMARRER`, l'application doit precharger les seuils machine `1188..1370` avec la trace `START_PRELOAD`, puis envoyer `5978=31`. Elle ne doit pas ecrire en bloc sur les banques piston `28414..28424` et `28926..28936`, meme a `0`.
- `DEMARRER`, `PAUSE`, `STOP`, `Micro-avance tapis`, `Avancer convoyeur seul` et le demarrage de l'application ne doivent jamais ecrire sur les sorties piston en production. Le routage physique des lignes doit rester confie a l'automate via les seuils machine.
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
- La voie NG hors seuils reste le principe de securite: elle ne doit pas recouvrir les lignes GOOD; si aucun intervalle GOOD ne prend la cellule, elle doit finir en NG par le rejet machine par défaut. TriCell Pilot ne pulse plus NG ni les lignes GOOD en direct pendant la production.

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

## Priorites a maintenir

1. Fiabilite machine avant cosmetique.
2. Traçabilite cellule avant confort UI.
3. Une seule app active.
4. Toute regression doit etre couverte par `test_desktop_v2.bat`.
