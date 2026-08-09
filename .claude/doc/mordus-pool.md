# Ligue « Les Mordus » — import des rosters et correspondance de modèle

> Source : `Classement Mordus pool a vie saison 3 — PoolExpert.com`, PDF fourni par Nick le 2026-07-31.
> Données extraites : [`data/mordus-rosters.json`](../../data/mordus-rosters.json).

Ce document sert deux fins : la **correspondance entre le vocabulaire du pool actuel (PoolExpert) et le modèle de l'app**, et l'**historique des joueurs non appariés**, maintenant tous résolus (§2).

## État

**Ligue recréée sur Azure SQL le 2026-08-05 — code d'accès `TKW6UR`**, saison `20252026`, 14 équipes, **404 joueurs plus une franchise NHL par équipe (418 spots)**, commissaire `nick`. Créée par `seed-mordus`, qui vérifie chaque identifiant de joueur avant d'écrire quoi que ce soit et refuse d'écraser une ligue existante du même nom.

> Historique : créée le 2026-07-31, recréée sur Azure SQL le 2026-08-02 avec 360 joueurs et le code `Q7ZJ4G`. Le 2026-08-05, les 44 entrées non appariées du §2 ont été résolues et la ligue a été refaite (`wipe-pools` puis `seed-mordus`, code `P7R9CT`) pour que les rosters reflètent la réalité. Le même jour, une deuxième reconstruction a ajouté les 14 slots Équipe (§3) et émis le code actuel, `TKW6UR` — `P7R9CT` ne répond plus. Le code d'accès change à chaque re-seed, il est tiré au hasard.

Les usernames sont le prénom du GM, désambiguïsé par l'initiale du nom en cas de collision (`jonathan` / `jonathanr`). Nicolas Boisvert réutilise son compte existant `nick` plutôt qu'un nouveau `nicolas`, pour que ses deux ligues vivent sous un seul login.

Les `RosterSpot` et l'alignement de la semaine 1 sont matérialisés depuis le JSON : `seed-mordus` ouvre les 404 spots au début de la semaine 1 et sème l'alignement actif/réserve tel que le PDF le donnait.

---

## 1. Correspondance PoolExpert → Fantasy Warrior

| PoolExpert | Fantasy Warrior | Note |
|---|---|---|
| Participant (« Steeve Lachance Montreal ») | `User` + `Team` dans la ligue | Un `Team` par participant par ligue. |
| Concession NHL du participant (« Montreal ») | `Team.name` + `Team.franchiseAbbrev` | Voir §3 — le slot `E` du PDF. |
| Colonne `T` vide / `D` / `G` | `Player.position` → groupe F/D/G | **Ignorée à l'import** : la position vient de la table `Players`, qui fait autorité. Le PDF ne sert qu'à savoir *qui* est sur l'équipe. |
| Bloc du haut (avant « JOUEURS DE RÉSERVE ») | Joueurs **actifs** de la période courante | Devient le `Lineup` de la semaine (`activeSpotIds`). |
| « JOUEURS DE RÉSERVE » | Joueurs **au banc** de la période courante | Même `RosterSpot`, simplement absent de `activeSpotIds`. |
| Appartenance d'un joueur à une équipe | `RosterSpot` | Persiste d'une semaine à l'autre; le banc n'est pas une entité distincte. |
| `PSal` | `Player.capHit` | Le PDF est en millions (`9.50`); `capHit` est en dollars (`9500000`). |
| `PPts`, `PPP`, `PJ`, `B`, `P`, colonnes `1/7/30` | — | **Non importés.** Nick : « les pts ne te servent pas, ne les considère pas. » Les stats viennent de `playerGameStats`. |

### Règles de la ligue (Nick, 2026-07-31)

Appliquées en prod via `set-league-rules`.

| Règle | Valeur |
|---|---|
| But | **1** |
| Passe | **1** |
| Victoire de gardien | **1** |
| Défaite en prolongation (gardien) | **1** |
| Blanchissage | **0** (non listé par Nick) |
| Taille de roster | **23 min, 35 max** |
| Masse salariale | **134 M** (Nick, 2026-08-05) |

Deux défauts de l'app diffèrent de cette ligue et ont été écrasés : la victoire de gardien valait 2, et le plafond initial était à 100 M.

**Le plafond a été à 115 M jusqu'au 2026-08-05** — le chiffre de la LNH, pas celui de la ligue. Il mettait Montréal (128,1 M) et Detroit (116,1 M) hors limites sur papier, et après l'ajout des 44 joueurs du §2 il en aurait mis **9 sur 14**. À 134 M, les 14 équipes sont conformes. `seed-mordus` prend maintenant 134 M par défaut.

**Le slot Équipe** vaut 2 par victoire et 1 par défaite en prolongation depuis
le 2026-08-05 (§3). C'était la dernière règle manquante ; le barème de la ligue
est complet.

### Validation croisée par la taille de roster

La règle 23-35 confirme indépendamment l'extraction. En ajoutant à chaque équipe ses joueurs non appariés (§2) :

- **les 14 équipes atterrissent dans 23-35** ;
- **Jonathan Rochette tombe exactement sur 35**, le maximum pile — un découpage qui sur- ou sous-compterait ne produirait pas ça ;
- les **seules 2 équipes actuellement sous le minimum** (Akexandre Giguere Briere 21, Nicolas Boisvert 22) sont exactement expliquées par leur nombre de manquants.

Le registre du §2 est donc vraisemblablement complet, et le découpage des noms juste.

### Règles de format confirmées

- **Alignement actif : 9 F + 4 D + 1 G.** Déduit de l'extraction et vérifié indépendamment sur 11 des 14 équipes (les 3 autres n'ont que des joueurs non appariés en écart — voir §2).
- **Réserve : aucune taille fixe**, bornée par le plafond salarial et la taille de roster 23-35 (les réserves observées vont de 7 à 20 joueurs).
- **Échange actif ↔ réserve chaque semaine** (Nick, 2026-07-31).
- **Pool keeper** : les rosters se reportent d'une saison à l'autre, **les points repartent à zéro** à chaque saison. Il n'y a donc *pas* de cumul à vie à modéliser, malgré le titre « pool à vie » du rapport.

---

## 2. Joueurs non appariés — résolus le 2026-08-05

**39 entrées, 44 noms** (l'extraction PDF avait fusionné certaines lignes) n'avaient pas trouvé de correspondance dans `Players`. **Les 44 sont résolus, sans ambiguïté ni intervention manuelle.** La liste vit maintenant dans [`data/unresolved-players.txt`](../../data/unresolved-players.txt) et le job `player-resolve` la traite.

### Pourquoi ils manquaient, et comment résolus (résumé)

Le diagnostic initial (`PlayerSyncJob` ne lit que deux endpoints — alignements et listes d'espoirs) n'expliquait que 19 des 44. Les 25 autres étaient déjà dans `Players`, stockés sous forme abrégée (`J. Klingberg`) — la façon dont la LNH publie un joueur entre deux contrats — donc c'était un échec d'appariement à l'import, pas d'ingestion ; 5 de ceux-là étaient de simples variantes d'orthographe (Zack/Zachary, Sam/Samuel, etc.).

Résolus par `player-resolve` (voir [deployment.md](deployment.md)), qui interroge `https://search.d3.nhle.com/api/v1/search/player` par **nom de famille seul** — aucun scraping, aucune source tierce. Deux pièges trouvés par les tests : chercher un nom complet peut retourner un homonyme en silence (`q=Zack Bolduc` → Zack **Smith**), et `PlayerNameIndex` (repli par initiale) est le mauvais outil pour résoudre un nom inconnu — `PlayerSearchMatcher` (trois caractères communs au prénom) le remplace ici. L'endpoint tronque aussi à la limite demandée sans le dire ; la limite utilisée est 500.

### Enrichissement

Les 19 nouveaux ont ensuite été traités par les jobs existants : `draft-sync`, `career-sync`, `capwages-sync --resolve-unmatched`. Sur les 44 : **44 ont un historique de carrière**, 43 des informations de repêchage (Ilya Nabokov n'a jamais été repêché), **27 un contrat**. Les 17 sans contrat sont les autonomes non signés et les repêchés 2025 sans contrat d'entrée — ils n'ont réellement aucune masse salariale, ce n'est pas un échec d'import.

### Registre — où chacun a été placé

Le placement vient de ce tableau, tel que relevé du PDF. Contrôle indépendant après ajout : **les 14 équipes tombent sur 9F/4D/1G exactement**, toutes dans 23-35, et **Jonathan Rochette pile sur 35**, le maximum. Un découpage fautif n'aurait pas produit ça.

> **Huit de ces joueurs n'ont joué aucun match de la LNH de la saison** (Nabokov, Reschny, Wyttenbach, Aitcheson, Mews, Jackson Smith, Desnoyers, Iginla) et occupent pourtant des postes actifs chez Sylvain et Rochette. Ce n'est pas une erreur : **un DG a le droit d'habiller des joueurs non-LNH** (Nick, 2026-08-05). Ils reçoivent une assignation normale avec toutes les statistiques à zéro. C'est ce qui explique que ces deux équipes marquent très peu dans une replay, où l'alignement de la semaine 1 est reporté tel quel.

| Équipe | Section | Nom(s) |
|---|---|---|
| Eric Labrecque | **Actif** | Igor Chernyshov |
| Nicolas Boisvert | **Actif** | John Klingberg |
| Antoine Sylvain | **Actif** | Michael Brandsegg-Nygard · Cole Reschny |
| Antoine Sylvain | **Actif** | Ethan Wyttenbach · Tij Iginla |
| Antoine Sylvain | **Actif** | Jackson Smith |
| Antoine Sylvain | **Actif** | Ilya Nabokov |
| Jonathan Rochette | **Actif** | Easton Cowan |
| Jonathan Rochette | **Actif** | Caleb Desnoyers |
| Jonathan Rochette | **Actif** | Kashawn Aitcheson |
| Jonathan Rochette | **Actif** | Henry Mews |
| Steeve Lachance | Réserve | Carter Bear · Max Plante |
| Jonathan Marcil | Réserve | Cole Eiserman |
| Christian Drouin | Réserve | Jack Nesbitt |
| Christian Drouin | Réserve | Nick Blankenburg |
| Christian Drouin | Réserve | Trey Augustine |
| Akexandre Giguere Briere | Réserve | Calum Ritchie |
| Akexandre Giguere Briere | Réserve | Brennan Othmann |
| Akexandre Giguere Briere | Réserve | Alfons Freij |
| Alain Rodrigue | Réserve | Eeli Tolvanen |
| Alain Rodrigue | Réserve | Philipp Kurashev |
| Eric Labrecque | Réserve | Jonathan Lekkerimaki · Brady Martin |
| Eric Labrecque | Réserve | Braeden Cootes |
| Eric Labrecque | Réserve | Brayden Yager |
| Patrick Rheaume | Réserve | Benjamin Kindel |
| Patrick Rheaume | Réserve | William Horcoff |
| Patrick Rheaume | Réserve | Connor Ingram |
| Patrick Rheaume | Réserve | Sebastian Cossa |
| Yvan Meunier | Réserve | Nate Danielson |
| Yvan Meunier | Réserve | Fabian Lysell |
| Nicolas Boisvert | Réserve | Jonathan Drouin |
| Michel Allen | Réserve | Marcus Johansson |
| Michel Allen | Réserve | Zack Bolduc |
| Michel Allen | Réserve | Victor Eklund · Cayden Lindstrom |
| Michel Allen | Réserve | Nikita Artamonov |
| Mathieu Letourneau | Réserve | Isaac Howard |
| Mathieu Letourneau | Réserve | Dmitriy Simashev |
| Dany Blouin | Réserve | Sam Montembeault |
| Antoine Sylvain | Réserve | Axel Sandin Pellikka |
| Jonathan Rochette | Réserve | Reilly Smith |

### Une correction hors registre : Alex DeBrincat

Après ajout des 44, une seule équipe restait hors format — **Yvan Meunier à 8F/4D/1G**, un attaquant de moins. Ses deux entrées ci-dessus sont toutes deux en Réserve, donc le registre ne pouvait pas le corriger.

Nick a tranché (2026-08-05) : **Alex DeBrincat** (8479337, DET), que l'extraction avait classé en réserve, était en réalité dans l'alignement actif. Remonté dans `active`, Meunier passe à 9F/4D/1G et **les 14 équipes sont conformes**. Son total reste 27 — un déplacement entre sections ne change pas la taille du roster.

---

## 3. Le slot « Équipe » (`T`)

Le PDF donne à chaque participant une ligne `E` contenant **sa propre franchise NHL** (Lachance → Canadiens Montreal, Boisvert → Avalanche Colorado). Elle apparaît exactement 14 fois et à 0 $ de salaire.

**La franchise rapporte des points** (Nick, 2026-08-05) : **2 par victoire, 1 par défaite en prolongation**, 0 par défaite en temps réglementaire. Trois clés de barème à elle (`teamWins`, `teamLosses`, `teamOtLosses`), volontairement distinctes de celles du gardien — les deux valent 2 et 1 ici, mais « mon gardien a gagné » et « ma franchise a gagné » restent deux événements différents, et une ligue doit pouvoir les payer séparément. Le calcul lit la table `Games`, jamais le journal de match des joueurs : `FranchiseResults.For`, pur et testé.

### Le renversement de décision du 2026-08-05

Ce document a d'abord tranché l'inverse, le matin même :

> **Décision de modélisation : une colonne `FranchiseAbbrev` sur `Teams`, et non un `RosterSpot`.** Un spot polymorphe — contenant tantôt un joueur, tantôt une équipe — contaminerait tout le modèle de roster, de lineup et de transaction pour un cas unique, permanent et non échangeable.

Nick l'a renversée le jour même, et le raisonnement d'origine s'est révélé faux sur ses trois termes. Le cas n'est ni **unique** — une franchise ouvre un spot, produit une ligne `RosterAssignment` par semaine, banque ses points, exactement comme un joueur — ni **non échangeable**, puisqu'il l'est, contre une autre franchise. Et la « contamination » redoutée s'est chiffrée : **une colonne nullable et une contrainte CHECK**. Le prix de l'alternative était plus élevé — un deuxième moteur de pointage, un deuxième chemin d'échange et une deuxième grille, pour un actif qui se comporte en tout point comme un joueur.

Ce qui reste vrai de la crainte initiale : la nullabilité de `PlayerId` s'est propagée à une quinzaine d'appels, et l'un d'eux était un vrai piège — `rosteredIds` alimentait un `NOT IN` que le premier NULL aurait rendu NULL pour toutes les lignes, vidant le tableau des joueurs autonomes sans un mot dans les logs.

**Deux vérités distinctes, assumées** : `Teams.FranchiseAbbrev` est l'identité de l'équipe dans le pool et ne bouge jamais ; le spot `T` est l'actif échangeable. Ils partent égaux et peuvent diverger — le club qu'on est n'est pas le club qu'on possède.

---

## 4. Reste à obtenir de Nick

*(Plus rien de bloquant : la dernière règle manquante, le pointage du slot Équipe, a été tranchée le 2026-08-05 — voir §3.)*

Les salaires actuels de `players` sont estimés (`capHitSource: "estimated"`), pas réels. Le PDF contient la vraie colonne `PSal` par joueur; l'importer donnerait des masses salariales exactes et rendrait le plafond de 115 M significatif. Non fait — les valeurs sont dans les colonnes numériques du PDF, que l'extraction n'apparie pas de façon fiable aux noms (c'est justement le problème d'entremêlement des colonnes qui a rendu nécessaire le découpage par dictionnaire). Faisable si Nick veut un export CSV de PoolExpert.
