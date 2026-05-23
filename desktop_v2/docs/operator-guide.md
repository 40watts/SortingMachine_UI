# Guide operateur

## Demarrer

1. Lancer `TriCell Pilot`.
2. Verifier que l'etat machine indique `Connectee`.
3. Associer un lot Odoo si disponible.
4. Cliquer sur `DEMARRER`.

Le demarrage reste possible sans lot Odoo, mais la tracabilite est alors locale uniquement.

## Comprendre le tri

- La ligne 10 collecte les cellules d'apprentissage.
- Le modele se fige uniquement a 19 cellules ligne 10.
- Apres gel, les cellules vont en lignes 1 a 9 selon leur resistance interne.
- La tension sert uniquement a detecter une cellule sous-chargee ou surchargee.
- Les cellules hors garde partent en NG.
- La voie NG reste hors seuils pour ne pas recouvrir les lignes GOOD: si une cellule n'est pas prise par une ligne GOOD ou continue jusqu'au bout, elle doit tomber en NG par le rejet machine.
- Apres chaque decision cellule, le logiciel met a jour les seuils de routage et laisse l'automate piloter physiquement la voie appliquee. Le logiciel ne pulse plus les pistons en direct pendant la production.
- La machine gere le plein physique des lignes; le logiciel affiche le tri et les compteurs, pas une disponibilite de bac.

## Lire le panneau production

- `Tension live`: tension normalisee en valeur absolue.
- `IR live`: resistance interne normalisee en valeur absolue.
- `Ligne GOOD courante`: voie live ou derniere voie appliquee.
- `Prochaine ligne`: affiche `Selon IR` quand les 9 intervalles sont actifs.
- `Caracteristiques de tri`: bornes tension et resistance actuellement affichees.
- `Cellules NG`: dernieres cellules rejetees et motif.

## Réarmement machine vs apprentissage

- TriCell Pilot n'envoie pas le réarmement automate `RESET=26` automatiquement.
- Si la machine demande un réarmement, vérifier d'abord la mécanique, puis utiliser `RÉARMER` dans TriCell Pilot ou le pupitre constructeur. `RÉARMER` envoie uniquement `5978=26` puis relâche à `0`.
- Si le tapis n'est plus exactement en face des pistons, utiliser `Micro-avance tapis` dans Maintenance par petites impulsions. L'application pulse uniquement le coil convoyeur constructeur `1X 5981` et n'envoie aucun signal piston, ni `0` ni `1`.
- Si un vérin reste mécaniquement sorti parce que le convoyeur est arrêté au mauvais point, utiliser `Avancer convoyeur seul` dans Maintenance: l'application pulse uniquement le meme coil convoyeur plus longtemps. Elle n'envoie aucun signal piston, ni `0` ni `1`.
- Si le vérin NG reste sorti, utiliser `Libérer vérin NG`: l'application remet seulement `28305=1` pour relacher le reset NG, puis affiche le retour `28689`. Si `28689` reste a `0`, verifier rearmement, air ou blocage mecanique.
- Les tests pistons directs sont bloques tant que les banques I/O constructeur ne sont pas validees terrain. Avant chaque `DÉMARRER`, TriCell Pilot precharge les seuils machine, puis envoie la commande cycle.
- `Recommencer apprentissage` remet a zero la logique de lignes du lot dans le logiciel.
- La remise a zero de l'apprentissage conserve l'association Odoo.

## Historique et CSV

L'onglet `Historique / Exports` permet de verifier:

- decision logicielle;
- voie appliquee;
- confirmation physique;
- modele de routage;
- intervalle qualite;
- bornes tension et IR;
- mismatch eventuel.

Le CSV principal est `Audit cellules CSV`.

## En cas de comportement suspect

1. Ne pas modifier les recettes au hasard.
2. Exporter `Audit cellules CSV`.
3. Exporter `Runtime trace`.
4. Noter le nombre de cellules visibles par ligne physique.
5. Comparer voie appliquee, confirmation et mismatch dans l'historique.
