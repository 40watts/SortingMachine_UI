# Routage qualite par intervalles IR

## Objectif

Le mode intelligent actuel classe un lot en 9 intervalles de resistance, associes directement aux lignes 1 a 9. La ligne 10 est reservee a l'apprentissage du lot.

## Cycle d'un lot

1. Les cellules d'apprentissage vont en ligne 10.
2. Le modele se fige uniquement apres 19 cellules ligne 10.
3. Les signaux de ligne pleine restent des alarmes machine; ils ne changent pas le modele de tri logiciel.
4. Une fois le modele fige, les gardes IR/tension et la voie NG ne bougent plus. Les 8 coupures internes beneficient d'une calibration continue pendant les 100 premieres cellules de production (voir section dediee), puis se figent definitivement.

## Regle de tri

La resistance interne est le seul axe de classement:

- ligne 1: intervalle IR le plus bas;
- lignes 2 a 8: intervalles IR intermediaires;
- ligne 9: intervalle IR le plus haut;
- NG: tension hors garde, resistance hors garde ou mesure invalide.

Une ligne 1 a 9 n'est jamais consideree "indisponible" par la decision metier. La machine gere le plein physique; le logiciel ne s'en sert ni pour arreter le tri, ni pour changer la ligne, ni pour envoyer une cellule en NG.

La tension ne choisit jamais la ligne. Elle sert uniquement de garde de charge:

- sous-charge: tension sous la borne basse du lot;
- surcharge: tension au-dessus de la borne haute du lot.

## Filet de securite NG

La voie physique NG (slot 11) est programmee avec la fenetre catch-all constructeur: V `0..99.9`, IR `0..999.99`. Le PLC evalue les voies dans l'ordre 1..N avec NG en dernier: une cellule qui matche une voie GOOD part en GOOD, et toute cellule non captee matche la voie 11 et est poussee physiquement par le verin NG, comme sous le logiciel chinois. Si toutes les cellules partent NG, la cause est dans les fenetres GOOD (ex: garde tension trop etroite, incident du 19 mai 2026), pas dans la voie NG. Une voie 11 hors seuils est interdite: les cellules non matchees ne sont plus poussees, filent au dechargement et declenchent l'alarme dechargement (constate du 4 au 8 juin 2026). Le verin NG constructeur correspond aux registres manuels `28424/28936`, reset `28305`, retour `28689`; le bit `10` de `3144` est une sortie carte Y sans lien avec le verin NG.

Le routage physique reprend la logique machine du logiciel chinois: TriCell Pilot lit d'abord le `8230` courant comme base START, programme les seuils `1188..1370` (voies GOOD + voie 11 catch-all) et les precharge avant `DEMARRER`, puis le PLC pilote tous les pistons selon ces seuils. TriCell Pilot ne pulse aucun piston en production.

## Construction des intervalles

Les 19 mesures de la ligne 10 donnent une plage de garde IR autour du lot. Le logiciel ajoute une reserve adaptative autour du minimum et du maximum observes pour eviter qu'un echantillon d'apprentissage qui manque une queue naturelle du lot transforme trop de cellules en NG. Cette reserve reste bornee par la recette.

Les 8 coupures internes visent l'equilibrage des bacs: chaque voie GOOD doit recevoir ~1/9 des cellules du lot pour que les bacs operateur se remplissent au meme rythme. Elles sont un melange pondere de trois methodes:

- 60% quantiles gaussiens: centre (mediane) et ecart-type robustes estimes sur les 19 cellules, coupures a `mu + sigma * z(k/9)` — etroites au centre dense, larges aux queues, queues mieux extrapolees que le min/max de l'echantillon;
- 30% quantiles reels de l'echantillon: si le lot n'est pas gaussien (asymetrique, bimodal), la forme reelle corrige le modele;
- 10% decoupage regulier: stabilisateur contre le bruit d'un petit echantillon.

Les largeurs d'intervalles sont donc volontairement inegales (c'est le comptage par voie qui est egalise, pas la largeur). La regression `AssertGaussianLotFillsLanesEvenly` verifie qu'un lot gaussien remplit chaque voie entre 5% et 20% (uniforme = 11%).

## Calibration continue des coupures

19 cellules d'apprentissage ne suffisent jamais a placer parfaitement les coupures: l'echantillon peut tomber un peu plus haut ou plus bas que la vraie population (constate le 10 juin 2026: voies 1-4 a 77% du lot, voie 9 a 2 cellules). Pendant les `100` premieres cellules de production dans la garde IR:

- toutes les `5` cellules, chaque coupure interne glisse d'un pas amorti (`30%`) vers le quantile reel `k/9` des cellules de production mesurees;
- les gardes IR/tension, la voie NG et la decision GOOD/NG ne bougent jamais — seule la repartition entre voies GOOD s'affine;
- les coupures restent strictement croissantes, avec largeur minimale, a l'interieur des gardes;
- chaque mise a jour est tracee (`INTERVALS_CALIBRATED`) et reprogramme les seuils machine; chaque cellule est auditee avec les bornes exactes qui lui ont ete appliquees;
- a la 100e cellule, les coupures se figent pour le reste du lot (trace `FROZEN`).

Les premieres dizaines de cellules d'un bac peuvent donc chevaucher legerement la voie voisine, le temps de la convergence; c'est le prix accepte pour des bacs equilibres sur toute la production. La regression `AssertCalibrationRebalancesBiasedLot` verifie qu'un apprentissage biaise converge vers des bacs equilibres.

Les bornes de tension sont construites autour de la moyenne de tension du lot avec une fenetre robuste, plafonnee par la recette.

## Garantie importante

Les seuils programmes dans la machine et la decision affichee par le logiciel utilisent la meme source: le modele fige dans `LotReference.QualityIntervals`. Cela evite une divergence entre ce qui est affiche, ce qui est decide et ce qui est programme.

Les bornes hautes sont exclusives, comme dans le moteur constructeur: `min <= mesure < max`. L'audit cellule exporte donc le modele, l'intervalle, les bornes tension et les bornes IR qui ont ete appliquees a chaque cellule.

## Tests associes

Le script `test_desktop_v2.bat` relance les regressions de routage. Le test `QualityIntervalRoutingRegression` verifie notamment les 9 intervalles, leurs largeurs regularisees, les bornes exclusives, la normalisation des tensions negatives, la repartition sur une production IR irreguliere, un cas reel ou la queue IR haute ne doit pas devenir du NG massif, le backfill de l'audit cellule, le gel obligatoire a 19 cellules et l'ignorance totale des etats ligne pleine dans la decision de tri.
