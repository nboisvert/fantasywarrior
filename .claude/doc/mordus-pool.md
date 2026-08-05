# Ligue « Les Mordus » — import des rosters et correspondance de modèle

> Source : `Classement Mordus pool a vie saison 3 — PoolExpert.com`, PDF fourni par Nick le 2026-07-31.
> Données extraites : [`data/mordus-rosters.json`](../../data/mordus-rosters.json).

Ce document sert deux fins : la **correspondance entre le vocabulaire du pool actuel (PoolExpert) et le modèle de l'app**, et l'**historique des joueurs non appariés**, maintenant tous résolus (§2).

## État

**Ligue recréée sur Azure SQL le 2026-08-05 — code d'accès `P7R9CT`**, saison `20252026`, 14 équipes, **404 joueurs**, commissaire `nick`. Créée par `seed-mordus`, qui vérifie chaque identifiant de joueur avant d'écrire quoi que ce soit et refuse d'écraser une ligue existante du même nom.

> Historique : créée le 2026-07-31, recréée sur Azure SQL le 2026-08-02 avec 360 joueurs et le code `Q7ZJ4G`. Le 2026-08-05, les 44 entrées non appariées du §2 ont été résolues et la ligue a été refaite au complet (`wipe-pools` puis `seed-mordus`) pour que les rosters reflètent la réalité. Le code d'accès change à chaque re-seed, il est tiré au hasard.

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

**Encore inconnu** : la règle de pointage du slot Équipe (§3).

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

### Pourquoi ils manquaient — le diagnostic de 2026-07-31 était à moitié faux

Ce document affirmait que `PlayerSyncJob` ne les voyait pas parce qu'il ne lit que deux endpoints (alignements d'équipe et listes d'espoirs). C'est vrai, mais ça n'explique que **19 des 44**.

**Les 25 autres étaient déjà dans la table depuis le début** — stockés `J. Klingberg`, `R. Smith`, `E. Cowan`, `M. Brandsegg-Nygård`. C'est la forme sous laquelle la LNH publie un joueur entre deux contrats, et c'est l'appariement de l'import qui a échoué, pas l'ingestion. Le `player-sync` relancé le 2026-07-31 « n'en a récupéré aucun » précisément parce qu'il n'y avait rien à récupérer.

Cinq autres ont échoué sur une simple variante d'orthographe : Zack/**Zachary** Bolduc, Sam/**Samuel** Montembeault, Dmitriy/**Dmitri** Simashev, Benjamin/**Ben** Kindel, Axel Sandin Pellikka/**Sandin-Pellikka**.

Restent **19 vrais absents**, qui relèvent bien des deux catégories annoncées : autonomes sans contrat (sur aucun alignement) et repêchés récents (sur aucune liste d'espoirs).

### Comment ils ont été résolus

Le job `player-resolve` (voir [deployment.md](deployment.md)) lit la liste de noms et interroge `https://search.d3.nhle.com/api/v1/search/player`. **Aucun scraping, aucune source tierce** — tout vient de l'API NHL officielle. La piste EliteProspects envisagée un moment n'a pas lieu d'être : les 44 ont tous un identifiant NHL.

Deux pièges, tous deux trouvés par les tests :

- **Chercher le nom complet retourne le mauvais joueur, en silence.** `q=Zack Bolduc` répond Zack **Smith**, `q=Cole Reschny` répond Cole **Brady**. Il faut chercher par nom de famille seul et désambiguïser localement.
- **`PlayerNameIndex` est le mauvais outil ici.** Son repli par initiale est juste quand une source abrège un joueur qu'on possède déjà, mais quand la question est « ce nom désigne-t-il quelqu'un ? », il résolvait « Marcel Bolduc » vers **Mathieu** Bolduc, seul M. Bolduc parmi sept homonymes. `PlayerSearchMatcher` exige trois caractères communs au prénom, ce qui garde Zack pour Zachary et refuse Marcel pour Mathieu.

L'endpoint tronque aussi à la limite demandée sans le dire : à 50, Jackson Smith et Brady Martin disparaissaient de leur propre nom de famille. La limite est à 500.

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

## 3. Le slot « Équipe » (`E`)

Le PDF donne à chaque participant une ligne `E` contenant **sa propre franchise NHL** (Lachance → Canadiens Montreal, Boisvert → Avalanche Colorado). Elle apparaît exactement 14 fois, à 0 $ de salaire, et n'est jamais échangée.

**Décision de modélisation : une colonne `FranchiseAbbrev` sur `Teams`, et non un `RosterSpot`.** Un spot polymorphe — contenant tantôt un joueur, tantôt une équipe — contaminerait tout le modèle de roster, de lineup et de transaction pour un cas unique, permanent et non échangeable. Ses points se calculeront depuis la table `Games`, déjà présente.

Nick a confirmé que ce slot **rapporte des points** en plus de porter l'identité. **La règle exacte reste à obtenir** avant de pouvoir la coder.

---

## 4. Reste à obtenir de Nick

1. **La règle de pointage du slot Équipe** (§3) — la seule règle encore manquante pour que le calcul soit complet.

Les salaires actuels de `players` sont estimés (`capHitSource: "estimated"`), pas réels. Le PDF contient la vraie colonne `PSal` par joueur; l'importer donnerait des masses salariales exactes et rendrait le plafond de 115 M significatif. Non fait — les valeurs sont dans les colonnes numériques du PDF, que l'extraction n'apparie pas de façon fiable aux noms (c'est justement le problème d'entremêlement des colonnes qui a rendu nécessaire le découpage par dictionnaire). Faisable si Nick veut un export CSV de PoolExpert.
