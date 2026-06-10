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
- La voie NG (slot 11) est programmee en fenetre catch-all constructeur: toute cellule non prise par une ligne GOOD est poussee par le verin NG, par l'automate, comme avec le logiciel chinois.
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
- Si le tapis n'est plus exactement en face des pistons, utiliser `Micro-avance tapis` dans Maintenance par petites impulsions: coil convoyeur constructeur `1X 5981` pendant `200 ms`.
- Si un vérin reste mécaniquement sorti parce que le convoyeur est arrêté au mauvais point, utiliser `Avancer convoyeur` dans Maintenance: le meme coil convoyeur pendant `1000 ms`.
- Si le vérin NG reste sorti, utiliser `Libérer vérin NG`: l'application remet seulement `28305=1` pour relacher le reset NG, puis affiche le retour `28689`. Si `28689` reste a `0`, verifier rearmement, air ou blocage mecanique.
- Les tests pistons directs sont disponibles en Maintenance pour diagnostic unitaire machine arretee. Ils refusent un cycle arme, maintiennent l'impulsion de sortie `1000 ms`, et ne pulsent pas les resets. Avant chaque `DÉMARRER`, TriCell Pilot lit le top courant `8230` comme base, precharge les seuils machine, puis envoie la commande cycle `5978=31` avec une impulsion `500 ms`. Si `8230` n'est pas lisible, `DÉMARRER` est refuse pour eviter de manquer la premiere cellule.
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

## Validation terrain hors production

1. Preparer une cellule factice ou un lot test, zone machine degagee.
2. Lancer la surveillance lecture seule:

```bat
C:\Users\Administrator\Desktop\SortingMachine_UI\validate_tricell_field.bat 180
```

3. Cliquer sur `DEMARRER` dans l'IHM seulement quand l'operateur est devant la machine.
4. Remplir le CSV d'observation annonce par le script: il est pre-rempli avec une ligne par voie GOOD requise; completer `timestamp`, `handshake`, `observed_lane`, `ng_pulse_seen`, `operator`, `notes` sans supprimer `expected_lane`.
5. Observer les lignes GOOD requises `1..9` au moins une fois chacune: IR, ligne attendue, ligne physique, et le poussoir 11/NG qui pousse puis revient sur chaque cellule non triee GOOD.
6. Si le CSV est complete apres la fin de la surveillance, actualiser le rapport depuis le CSV sans relancer la machine:

```bat
C:\Users\Administrator\Desktop\SortingMachine_UI\refresh_tricell_field_result.bat
```

7. Conserver le rapport source `field_validation_*.md`, le rapport actualise si present, le CSV `field_validation_*_observations.csv`, le `Runtime trace` et idealement une video courte.
8. Verifier le rapport terrain:

```bat
C:\Users\Administrator\Desktop\SortingMachine_UI\check_tricell_field_result.bat
```

Le lanceur appelle `Start-FieldValidationWatch.ps1`, qui demarre l'API si necessaire sans envoyer de commande machine, puis lance `Watch-TriCellFieldValidation.ps1` avec `MinAcceptedTops=9`, `MinCounterDelta=9`, `MinPhysicalObservations=9` et `RequiredGoodLanes=1,2,3,4,5,6,7,8,9`. Le watcher force aussi chaque minimum effectif a etre au moins egal au nombre de lignes GOOD requises. Si l'operateur complete le CSV apres coup, `refresh_tricell_field_result.bat` appelle `Update-FieldValidationReportFromCsv.ps1`: ce script ne fait que relire le rapport source et le CSV, puis produit un nouveau rapport sans envoyer de commande machine. Il ne peut pas convertir une preuve courte en OK: les tops `8230` acceptes et le delta compteurs du rapport source doivent couvrir toutes les voies GOOD demandees. Le rapport separe volontairement la preuve trace logiciel, le delta compteurs machine, l'observation physique et la couverture voies GOOD. Le tri reel n'est prouve que si ces verdicts sont `OK` et si `check_tricell_field_result.bat` confirme que le `Lot: #...` du rapport correspond au lot courant de l'API.
Le verificateur relit aussi les details `Couverture voies GOOD requise/observee/manquante` et les `Minimums effectifs`; un verdict copie sans ces details coherents est refuse.

## En cas de comportement suspect

1. Ne pas modifier les recettes au hasard.
2. Exporter `Audit cellules CSV`.
3. Exporter `Runtime trace`.
4. Noter le nombre de cellules visibles par ligne physique.
5. Comparer voie appliquee, confirmation et mismatch dans l'historique.
