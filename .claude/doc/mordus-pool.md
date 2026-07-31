# Ligue « Les Mordus » — import des rosters et correspondance de modèle

> Source : `Classement Mordus pool a vie saison 3 — PoolExpert.com`, PDF fourni par Nick le 2026-07-31.
> Données extraites : [`data/mordus-rosters.json`](../../data/mordus-rosters.json).

Ce document sert deux fins : la **correspondance entre le vocabulaire du pool actuel (PoolExpert) et le modèle de l'app**, et le **registre des joueurs non appariés** restant à ajouter.

## État

**Ligue créée en prod le 2026-07-31 — id `haPRaAJ3Vo3nqPufYGOM`**, saison `20252026`, 14 équipes, 360 joueurs, plafond 100 M, commissaire `nick`. Créée par `seed-mordus`, qui vérifie chaque identifiant de joueur avant d'écrire quoi que ce soit et refuse d'écraser une ligue existante du même nom.

Les usernames sont le prénom du GM, désambiguïsé par l'initiale du nom en cas de collision (`jonathan` / `jonathanr`). Nicolas Boisvert réutilise son compte existant `nick` plutôt qu'un nouveau `nicolas`, pour que ses deux ligues vivent sous un seul login.

**Pas encore fait** : les `RosterSpot` et les `Lineup` hebdomadaires (modèles livrés en C4/C5 de la refonte). La répartition actif/réserve est conservée dans le JSON, donc la première semaine pourra être matérialisée depuis ce même fichier. Le barème de points est resté aux valeurs par défaut — voir §4.

---

## 1. Correspondance PoolExpert → Fantasy Warrior

| PoolExpert | Fantasy Warrior | Note |
|---|---|---|
| Participant (« Steeve Lachance Montreal ») | `User` + `Team` dans la ligue | Un `Team` par participant par ligue. |
| Concession NHL du participant (« Montreal ») | `Team.name` + `Team.franchiseAbbrev` | Voir §3 — le slot `E` du PDF. |
| Colonne `T` vide / `D` / `G` | `Player.position` → groupe F/D/G | **Ignorée à l'import** : la position vient de la collection `players`, qui fait autorité. Le PDF ne sert qu'à savoir *qui* est sur l'équipe. |
| Bloc du haut (avant « JOUEURS DE RÉSERVE ») | Joueurs **actifs** de la période courante | Devient le `Lineup` de la semaine (`activeSpotIds`). |
| « JOUEURS DE RÉSERVE » | Joueurs **au banc** de la période courante | Même `RosterSpot`, simplement absent de `activeSpotIds`. |
| Appartenance d'un joueur à une équipe | `RosterSpot` | Persiste d'une semaine à l'autre; le banc n'est pas une entité distincte. |
| `PSal` | `Player.capHit` | Le PDF est en millions (`9.50`); `capHit` est en dollars (`9500000`). |
| `PPts`, `PPP`, `PJ`, `B`, `P`, colonnes `1/7/30` | — | **Non importés.** Nick : « les pts ne te servent pas, ne les considère pas. » Les stats viennent de `playerGameStats`. |

### Règles de format confirmées

- **Alignement actif : 9 F + 4 D + 1 G.** Déduit de l'extraction et vérifié indépendamment sur 11 des 14 équipes (les 3 autres n'ont que des joueurs non appariés en écart — voir §2).
- **Réserve : aucune taille fixe**, bornée uniquement par le plafond salarial (les réserves observées vont de 7 à 20 joueurs).
- **Plafond salarial : ~100 M** — masses observées de 80,4 à 98,8 M.
- **Échange actif ↔ réserve chaque semaine** (Nick, 2026-07-31).
- **Pool keeper** : les rosters se reportent d'une saison à l'autre, **les points repartent à zéro** à chaque saison. Il n'y a donc *pas* de cumul à vie à modéliser, malgré le titre « pool à vie » du rapport.

---

## 2. Joueurs non appariés — à ajouter

**39 entrées** n'ont pas trouvé de correspondance dans la collection `players` (1 386 documents). Ils sont conservés ici pour ajout futur.

### Pourquoi ils manquent

`PlayerSyncJob` alimente `players` depuis deux sources seulement : les **alignements d'équipe** et les **listes d'espoirs** de l'API NHL. Deux catégories passent donc entre les mailles :

1. **Les joueurs autonomes sans contrat** — ils ne figurent sur aucun alignement. C'est une lacune réelle pour un pool keeper, où l'on conserve un joueur à travers son autonomie. Concernés : Jonathan Drouin, Marcus Johansson, Sam Montembeault, Reilly Smith, John Klingberg, Philipp Kurashev, Eeli Tolvanen, Connor Ingram, Fabian Lysell, Zack Bolduc, Isaac Howard, Nick Blankenburg.
2. **Les repêchés 2024-2025 pas encore dans une liste d'espoirs** — Easton Cowan, Caleb Desnoyers, Kashawn Aitcheson, Henry Mews, Cole Reschny, Ethan Wyttenbach, Tij Iginla, Igor Chernyshov, Michael Brandsegg-Nygard, etc.

Un `player-sync` relancé le 2026-07-31 n'en a récupéré aucun (1 385 documents, inchangé) — confirmant que l'API ne les expose pas par ces deux endpoints.

### Piste de résolution

L'endpoint de recherche public `https://search.d3.nhle.com/api/v1/search/player?q=<nom>` retourne l'identifiant NHL par nom; l'endpoint « landing » par joueur donne ensuite position et équipe. Un job `player-resolve` bâti là-dessus corrigerait les deux catégories de façon permanente. Suivi comme tâche distincte.

### Registre

Les entrées en **Actif** sont prioritaires : elles laissent l'alignement de l'équipe incomplet. Certaines lignes contiennent deux noms accolés (l'extraction PDF les a fusionnés) — à séparer au moment de la résolution.

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

---

## 3. Le slot « Équipe » (`E`)

Le PDF donne à chaque participant une ligne `E` contenant **sa propre franchise NHL** (Lachance → Canadiens Montreal, Boisvert → Avalanche Colorado). Elle apparaît exactement 14 fois, à 0 $ de salaire, et n'est jamais échangée.

**Décision de modélisation : un champ `franchiseAbbrev` sur le document `Team`, et non un `RosterSpot`.** Un spot polymorphe — contenant tantôt un joueur, tantôt une équipe — contaminerait tout le modèle de roster, de lineup et de transaction pour un cas unique, permanent et non échangeable. Ses points se calculeront depuis la collection `games`, déjà présente.

Nick a confirmé que ce slot **rapporte des points** en plus de porter l'identité. **La règle exacte reste à obtenir** avant de pouvoir la coder.

---

## 4. Reste à obtenir de Nick

1. **Le barème de points** de la ligue (but, passe, et les stats de gardien retenues). Le PDF ne contient pas les règles, et ses totaux sont explicitement à ignorer.
2. **La règle de pointage du slot Équipe** (§3).
3. **Le plafond salarial exact** (~100 M déduit des masses observées, à confirmer).
