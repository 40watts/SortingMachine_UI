# Routage qualite par intervalles IR

## Objectif

Le mode intelligent actuel classe un lot en 9 intervalles de resistance, associes directement aux lignes 1 a 9. La ligne 10 est reservee a l'apprentissage du lot.

## Cycle d'un lot

1. Les cellules d'apprentissage vont en ligne 10.
2. Le modele se fige uniquement apres 19 cellules ligne 10.
3. Les signaux de ligne pleine restent des alarmes machine; ils ne changent pas le modele de tri logiciel.
4. Une fois le modele fige, les intervalles restent attaches a la reference du lot et ne sont plus recalcules pendant la production.

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

La voie physique NG reste volontairement hors seuils programmes. Elle ne doit pas recouvrir les lignes GOOD: sur le PLC terrain, une voie NG large peut voler toutes les cellules si la derniere voie qui matche gagne la priorite. Les cellules non captees par les lignes GOOD doivent donc tomber en NG par le rejet machine par defaut, sans seuil NG chevauchant.

Le routage physique reste confie a l'automate: TriCell Pilot programme les seuils `1188..1370`, precharge ces seuils avant `DEMARRER`, puis laisse la machine pousser la cellule dans la voie appliquee. Les banques I/O directes des pistons restent bloquees tant qu'elles ne sont pas validees terrain voie par voie.

## Construction des intervalles

Les 19 mesures de la ligne 10 donnent une plage de garde IR autour du lot. Le logiciel ajoute une reserve adaptative autour du minimum et du maximum observes pour eviter qu'un echantillon d'apprentissage qui manque une queue naturelle du lot transforme trop de cellules en NG. Cette reserve reste bornee par la recette.

Les 8 coupures internes sont des quantiles regularises: elles suivent legerement la distribution reelle de resistance du lot, puis sont surtout ramenees vers un decoupage regulier. Cette regle evite les deux extremes:

- des intervalles tous identiques mais mal adaptes aux zones denses du lot;
- une ligne 1 ou une ligne 9 enorme parce qu'elle absorbe toute la marge d'extremite.

Les bornes de tension sont construites autour de la moyenne de tension du lot avec une fenetre robuste, plafonnee par la recette.

## Garantie importante

Les seuils programmes dans la machine et la decision affichee par le logiciel utilisent la meme source: le modele fige dans `LotReference.QualityIntervals`. Cela evite une divergence entre ce qui est affiche, ce qui est decide et ce qui est programme.

Les bornes hautes sont exclusives, comme dans le moteur constructeur: `min <= mesure < max`. L'audit cellule exporte donc le modele, l'intervalle, les bornes tension et les bornes IR qui ont ete appliquees a chaque cellule.

## Tests associes

Le script `test_desktop_v2.bat` relance les regressions de routage. Le test `QualityIntervalRoutingRegression` verifie notamment les 9 intervalles, leurs largeurs regularisees, les bornes exclusives, la normalisation des tensions negatives, la repartition sur une production IR irreguliere, un cas reel ou la queue IR haute ne doit pas devenir du NG massif, le backfill de l'audit cellule, le gel obligatoire a 19 cellules et l'ignorance totale des etats ligne pleine dans la decision de tri.
