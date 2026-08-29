# Le concept de saison, et le cycle de vie d'une saison

> **Statut : §§1-8 sont faits, et le repêchage aussi (2026-08-25).**
> `Season` (Core), `LeagueSeasons`, les phases, le gel des échanges, le
> correctif des deux vues et le palmarès sont en place et déployés. **La salle
> de repêchage tourne** (§9 point 7) : deux segments enchaînés dans `Drafting`,
> sans horloge, avec `DraftSelections` comme journal des tours — détaillée dans
> [scoring-model.md](scoring-model.md) §11. `StealRounds = 2`,
> `MaxLossesPerTeam = 2` et `ProtectionSlots = 9` sont fixés (§10).
>
> **Les protections s'écrivent depuis le 2026-08-28**, mais par un
> auto-remplissage et non par un choix : `POST .../protections/autofill` protège
> les 9 meilleurs de chaque roster d'après la saison écoulée. **Ce qui reste
> proposé** : l'écran de sélection lui-même (§9 point 6), un
> écran-liste-de-joueurs qui doit d'abord répondre à la convention player-row de
> `CLAUDE.md`. Tant qu'il manque, aucun DG ne peut contredire le défaut.
>
> Idée de Nick, 2026-08-25 ; modélisation reprise le même jour après sa
> question — « c'est une seule colonne sur ligue ? quelle est sa valeur ? ça
> prendrait pas une table ? on entame quelle saison, 2026 ou 2027 ? ». Elle
> était juste : §1 remplace ce que la première version de ce document disait.
>
> À faire vivre : quand une phase est construite, elle passe de « proposé » à
> « fait » ici même.

---

## 1. Trois choses s'appellent « saison »

C'est là qu'est le problème de modélisation, et c'est pour ça que « 2026 ou
2027 ? » n'a pas de bonne réponse aujourd'hui : la question porte sur deux
identifiants différents qu'on appelle du même nom.

| | Quoi | Portée | Aujourd'hui |
|---|---|---|---|
| **A. La saison LNH** | `"20262027"` | **Globale** — un fait sur la LNH | Une chaîne de 8 caractères sur 7 entités |
| **B. La saison de la ligue** | « Les Mordus, saison 4 » | **Par ligue** | **N'existe pas** |
| **C. L'année de repêchage** | `2026` | Par ligue | `DraftPick.Year`, sans que rien ne dise ce que le nombre veut dire |

Le PDF source des Mordus s'intitule **« Classement Mordus pool a vie saison
3 »**. Le pool compte ses propres saisons depuis toujours — c'est son
vocabulaire — et l'app n'a aucun endroit où l'écrire.

### La réponse en une phrase

**A reste une valeur, B devient une table, C se dérive de A.**

---

## 2. A — la saison LNH : une valeur, pas une table

`"20262027"` : quatre chiffres pour l'année de départ, quatre pour l'année de
fin. C'est **l'identifiant de la LNH elle-même**, celui que ses propres API
renvoient. Même argument que `Player.PlayerId`, qui est l'id LNH et non une
colonne identity : stable, globalement unique, et déjà présent dans toutes les
charges utiles qu'on ingère.

Elle est portée par : `Games`, `PlayerGameStats`, `PlayerContracts`,
`PlayerCareerSeasonStats`, `Periods`, `SimulationState`, `Leagues`.

**Pas de table `Seasons`.** Elle ne porterait aucun attribut que la chaîne ne
donne déjà, elle imposerait une clé étrangère sur ~50 000 lignes de
`PlayerGameStats` pour rien, et la seule chose qu'on lui demanderait — la
succession — est une fonction pure.

### Ce qui manque : un helper, pas une table

La chirurgie de chaîne est aujourd'hui éparpillée, et chaque endroit la refait :

| Où | Ce qu'il fait |
|---|---|
| `Jobs/Program.cs:99` `CurrentSeason()` | `$"{start}{start + 1}"`, bascule en septembre |
| `Jobs/Program.cs:219` | `int.Parse(CurrentSeason()[..4]) + 1` pour l'année de repêchage |
| `Jobs/Program.cs` ×3 | `"20252026"` en dur comme valeur par défaut |
| `PlayerCard.tsx:207` `formatSeason` | `slice(0,4)` + `slice(6)` → `"2025-26"` |

**✅ Construit** (2026-08-25) — `FantasyWarrior.Core/Seasons/Season.cs`, pur et
testé (35 tests), les remplace tous. Les trois premiers points de la table
délèguent désormais à `Season` ; le formatteur front reste dupliqué (deux
lignes, pas un risque de dériver un seuil).

```csharp
public static class Season
{
    public static bool   IsValid(string s);        // 8 chiffres, seconde moitié = première + 1
    public static int    StartYear(string s);      // "20262027" -> 2026
    public static int    EndYear(string s);        // -> 2027
    public static string Next(string s);           // -> "20272028"
    public static string Previous(string s);
    public static string FromStartYear(int y);     // 2026 -> "20262027"
    public static string CurrentOn(DateOnly today);// bascule en septembre
    public static string Display(string s);        // -> "2026-27"
}
```

`IsValid` mérite d'exister : la saison est aujourd'hui du texte libre, donc
`"2025-2026"` ou `"20252026 "` créerait une saison fantôme en silence, sans
qu'aucune contrainte ne s'y oppose.

---

## 3. C — l'année de repêchage : « 2026 ou 2027 ? »

Les deux, et c'est bien le problème. Ce sont deux nombres différents pour deux
choses différentes :

```
été 2026 ──── le repêchage de 2026 ──── saison LNH 2026-27
             DraftPick.Year = 2026      Season = "20262027"
             LeagueSeason  n° 4
```

**Le repêchage est nommé par l'année où il se tient ; la saison par les deux
années qu'elle chevauche.** Le repêchage de 2026 garnit la saison `20262027`.
`DraftPicksInitJob` fait déjà exactement ça — `CurrentSeason()[..4] + 1` vaut
2026 pendant `20252026` — mais aucun commentaire ne le dit, donc chaque lecteur
doit le redériver et peut se tromper.

C'est donc `Season.StartYear(s)`, rien de plus. Ce qu'il faut n'est pas une
colonne de plus mais une phrase dans `DraftPick` : *« l'année civile où le
repêchage se tient ; il garnit la saison `Season.FromStartYear(Year)` »*.

---

## 4. B — la saison de la ligue : voilà la table

C'est la réponse à « ça prendrait pas une table ? ». Oui — mais pour la saison
**de la ligue**, pas pour celle de la LNH.

```
LeagueSeasons
  LeagueSeasonId
  LeagueId          FK
  Season            "20262027" — la saison LNH qu'elle joue
  Number            4 — le compte de la ligue, celui du PDF
  Phase             tinyint, voir §5
  ChampionTeamId    FK null — écrit à la complétion
  StartedUtc, CompletedUtc
  UNIQUE (LeagueId, Season)
  UNIQUE (LeagueId, Number)
```

Et `Leagues.Season` **reste**, pointant vers la ligne courante par valeur — via
`(LeagueId, Season)`, mais **sans FK composite**. C'était le premier réflexe et
il ne tient pas : créer une ligue insère d'abord la ligne `Leagues` (c'est elle
qui distribue le `LeagueId` dont toute ligne `LeagueSeasons` a besoin), donc une
FK sur `(LeagueId, Season)` refuserait cette toute première insertion — l'œuf
et la poule, sans échappatoire de contrainte différée sous SQL Server. Le
rapprochement reste une valeur que l'application tient honnête, exactement
comme `Team.FranchiseAbbrev` et `NhlTeam.Abbrev` le font déjà. Ce n'est pas une
dénormalisation pour autant : « laquelle est la courante » est une propriété de
la ligue, pas quelque chose qu'on dérive sans ambiguïté — et ça garde
`vStandings` sur une simple jointure.

**✅ Construit** (2026-08-25) : la table, son backfill (Mordus `Number = 3`),
et cette même limite documentée dans `LeagueSeason.cs`.

### Ce que la table achète

**Le rollover devient une insertion, pas une écrasure.** Aujourd'hui avancer la
saison veut dire écraser `Leagues.Season` — ce qui détruit la trace que la ligue
ait jamais joué 2025-26. Avec la table, on ferme une ligne et on en ouvre une :
l'historique **est** la table.

**La phase trouve un domicile honnête** — voir §5. C'est une correction : la
première version de ce document mettait `Phase` sur `Leagues`, et ça ne tenait
pas (« la ligue repêche » — pour quelle saison ?).

**Le champion existe.** `ChampionTeamId`, écrit quand la saison se complète.
Aujourd'hui il n'y a nulle part où mettre « champion de la saison 3 ».

**Le numéro de saison existe.** Les Mordus disent « saison 3 » depuis trois ans.
Un pool à vie compte en saisons, pas en années LNH.

### Ce que la table n'est pas

`Periods` **ne pointe pas** vers `LeagueSeasons`. Une semaine est une propriété
du calendrier LNH, pas d'un pool — c'est ce qui permet au job nocturne d'aller
chercher les lignes de match une seule fois et de servir toutes les ligues
([scoring-model.md](scoring-model.md) §7). `Periods` garde la chaîne LNH ;
`LeagueSeasons` la référence aussi. Deux grains différents, correctement.

---

## 5. Les phases vivent sur la ligne de saison

Chaque `LeagueSeason` a son propre cycle : **on la prépare, on la joue, on la
clôt.**

**✅ Construit** (2026-08-25) : `LeagueSeasonPhase` (Core, avec
`SeasonPhaseRules.CanTransition`/`CanTrade`, testés) ; `SeasonPhaseJob`
(`season-phase --league <code> --to <Phase> [--dry-run]`) fait avancer une
ligue d'un pas, ouvre la saison suivante depuis `Preparing`, bascule
`Leagues.Season` et vide les protections en entrant en `InSeason`, écrit le
champion en entrant en `Complete`. **Jamais exécuté sur une vraie ligue** —
avancer réellement une saison est une décision de Nick, pas la mienne.

```
Preparing ──> Protecting ──> Drafting ──> PreSeason ──> InSeason ──> Complete
```

**Exactement une ligne par ligue n'est pas `Complete` : c'est la saison
courante.** C'est ce qui règle le « à qui appartient l'entre-saison » qui
embrouillait la première version : les phases d'entre-saison appartiennent à la
saison qu'on **prépare**, pas à celle qui vient de finir.

Concrètement, en juillet 2026 chez Les Mordus :

| Ligne | Phase |
|---|---|
| saison 3 — `20252026` | `Complete`, champion écrit |
| saison 4 — `20262027` | `Protecting` |

Le classement à l'écran est encore celui de la saison 3, parce que
`Leagues.Season` pointe encore sur elle. Il bascule d'un coup en entrant dans
`InSeason`.

| Phase | Ouvert | Fermé |
|---|---|---|
| `Preparing` | rien | tout — l'état de départ |
| `Protecting` | Le DG choisit ses protégés | **Échanges gelés** |
| `Drafting` | Les vols, à tour de rôle | Échanges, protections |
| `PreSeason` | Échanges, réparation du roster sous le minimum | Alignements |
| `InSeason` | Alignements hebdo, échanges, pointage | Protections, repêchage |
| `Complete` | rien | tout — lecture seule pour toujours |

`PreSeason` existe parce qu'une équipe peut sortir du repêchage sous
`RosterMin` — deux joueurs perdus, un seul repêché — et qu'il lui faut une
fenêtre pour se remettre en règle avant que le pointage reprenne.

### Où chaque contrôle se branche

| Mécanisme | Point d'accroche | Règle |
|---|---|---|
| Échanges | `TradeValidation.Validate` — **prend déjà `League` en paramètre** | Refusés en `Protecting` et `Drafting` |
| Alignements | verrou de période, existe | Inchangé : hors saison il n'y a pas de semaine |
| Protections | à écrire | `Protecting` seulement |
| Vols | à écrire | `Drafting` seulement |
| `protection-reset` | existe | Joué en entrant dans `InSeason` |

### Le gel des échanges n'est pas cosmétique

Un échange ferme un spot et en ouvre un neuf. Le spot neuf **n'hérite d'aucune
protection** — le joueur deviendrait exposé sans que personne ne l'ait décidé.
C'est une faille, pas une question d'ergonomie.

**✅ Construit** : le contrôle est dans `TradeEndpoints.ValidateAgainstEngagedAsync`
— le point d'entrée partagé par proposer et accepter un échange — donc les deux
chemins le refusent identiquement. Les protections et les vols eux-mêmes
restent **à écrire** (voir le statut en tête de document).

---

## 6. Ce qu'on ne fait **pas** : effacer les `RosterAssignments`

La première forme de l'idée effaçait les assignations de la saison écoulée au
rollover. Ça remet bien le classement à zéro — et ça coûte quatre choses.

**Ça ne touche pas que le classement.** `vRosterSpotTotals` lit les mêmes
lignes. En keeper, un `RosterSpot` survit à la saison : effacer 2025-26 met la
colonne PTS de l'écran Team à 0 **pour un joueur encore sur le roster**, et rend
« combien ce joueur m'a rapporté depuis que je l'ai » sans réponse pour toujours.

**Ça contredit l'invariant central.** `RosterAssignment` est *le* grain honnête
du modèle, et toute la mécanique de banque existe pour qu'une semaine appartienne
définitivement à qui a aligné le joueur ([scoring-model.md](scoring-model.md)
§4). Effacer au rollover dit l'inverse.

**C'est irréversible là où ça compte.** `PlayerGameStats` survit (≈50 000
lignes) donc on pourrait rejouer — mais **sous le barème d'aujourd'hui**. Or
changer le barème ne recalcule jamais le passé ; un efface-et-rejoue
recalculerait tout. Le chiffre banqué est le procès-verbal de ce qui était vrai,
pas un cache reconstructible.

**Ça n'achète rien.** 5 434 lignes au 2026-08-25 (11 semaines, 2 ligues), ≈14 000
pour une saison complète, contre 50 000 `PlayerGameStats`. Ni problème d'espace,
ni problème de vitesse.

---

## 7. Ce que l'historique rend possible — et c'est le sujet

`CLAUDE.md` pose l'interaction entre DG comme l'attrait principal du produit.
Une fois `LeagueSeasons` en place **et les assignations conservées**, tout ce qui
suit est à une requête près — et rien de tout ça n'est possible si on efface.

| Écran | La requête derrière |
|---|---|
| « Les Mordus — saison 3, champion : Lachance » | `LeagueSeasons.ChampionTeamId` |
| Le palmarès : qui a gagné chaque saison | Une ligne par `LeagueSeason` |
| « Boisvert n'a jamais fini devant Lachance en 4 ans » | Classements par saison, comparés |
| « Ta meilleure semaine à vie : 47 pts, saison 2, semaine 14 » | `MAX` sur `RosterAssignments` par équipe |
| « Crosby t'a rapporté 312 pts en 3 saisons » | `RosterAssignments`/`RosterSpots`, **hors** `vRosterSpotTotals` |
| « Meilleure saison de l'histoire du pool » | `MAX` sur les totaux par `LeagueSeason` |
| Le trophée du DG le plus actif en échanges, à vie | `vPoolerTradeRecord` par saison |

C'est la différence entre un pool qui recommence chaque année et un **pool à
vie** — ce que Les Mordus est déjà dans le titre de son propre rapport. Le
filtre de saison donne les deux lectures depuis les mêmes lignes, mais **pas la
même vue** : `vRosterSpotTotals` sert désormais le "cette saison" du §8 (elle a
dû être scopée pour la même raison que `vStandings`), et une carrière se lit en
requêtant `RosterAssignments` directement, sans passer par elle.

**✅ Construit** (2026-08-25) : `GET /api/leagues/{leagueId}/seasons` +
l'écran `Palmares.tsx`, une ligne par `LeagueSeason`, atteint depuis un lien
sur Standings plutôt qu'un nouvel onglet (la barre du bas était déjà pleine).
Ce n'est pas un écran de joueurs — chaque ligne est une saison/équipe — donc il
ne déclenche pas la convention player-row de `CLAUDE.md`. Les autres lignes du
tableau ci-dessus restent illustratives, non construites.

---

## 8. Le correctif qui tient tout : scoper les vues

Les points repartent à zéro parce que **le filtre bouge**, pas parce que la
donnée meurt.

Dans `vStandings`, le CTE `Scoring` :

```sql
FROM [RosterAssignments] a
JOIN [RosterSpots] sp ON sp.[RosterSpotId] = a.[RosterSpotId]
JOIN [Periods]     p  ON p.[PeriodId]      = a.[PeriodId]
JOIN [Teams]       t  ON t.[TeamId]        = sp.[TeamId]
JOIN [Leagues]     l  ON l.[LeagueId]      = t.[LeagueId]
WHERE p.[Season] = l.[Season]
```

Le même filtre dans `vRosterSpotTotals`. `vTeamPeriodScores` est déjà par
période, donc déjà scopable.

**C'est un bug latent aujourd'hui, indépendamment de tout le reste** : les deux
vues sont fausses dès la première `Period` d'une deuxième saison, repêchage ou
pas.

### La conséquence à assumer

`vStandings` joint `PlayerContracts` sur `l.Season`. Avancer `Leagues.Season`
reprice donc tout le plafond au même instant — c'est correct, les contrats de la
nouvelle saison sont les bons, mais ça arrive d'un coup et il faut le voir une
fois avant que 14 personnes le découvrent.

C'est aussi pourquoi **`Leagues.Season` s'avance en dernier**, en entrant dans
`InSeason` : jusque-là le classement affiche encore la saison qui vient de finir,
ce qu'on veut précisément regarder en juillet.

**✅ Construit et déployé** (2026-08-25) : migration
`ScopeStandingsAndRosterTotalsBySeason`, vérifiée sur la base réelle — les
14 équipes des Mordus affichent toujours leurs bons totaux (454 pts en tête,
435 lignes dans `vRosterSpotTotals`), le filtre étant aujourd'hui un no-op
puisqu'aucune ligue n'a encore atteint une deuxième saison.

---

## 9. Ordre de construction

1. ✅ **`Season` (Core), pur et testé** — remplace les quatre endroits de §2.
2. ✅ **Scoper `vStandings` et `vRosterSpotTotals`** — corrige un bug qui existait déjà. Déployé.
3. ✅ **`LeagueSeasons`** + backfill — une ligne par ligue existante (`Number = 3` pour Les Mordus, `Phase = InSeason`). Déployé.
4. ⬜ `period-init --season 20262027` — **pas fait** : aucun match `20262027` n'est encore en base (`Games`), le job refuserait honnêtement de tourner. Rien à faire tant que le calendrier 2026-27 n'existe pas.
5. ✅ Les phases : `SeasonPhaseRules` + `SeasonPhaseJob` + gel dans `TradeEndpoints`.
6. 🟨 `Protecting` : **l'auto-remplissage est fait** (2026-08-28) —
   `ProtectionSlots = 9`, `ProtectionAutofill` (Core, pur, 9 tests) et
   `POST .../protections/autofill` protègent les 9 meilleurs de chaque roster
   d'après la saison écoulée, au barème de la ligue. **L'écran de sélection
   reste à faire** : c'est une liste de joueurs et il doit d'abord répondre à la
   convention player-row. Tant qu'il n'existe pas, un DG ne peut pas contredire
   le défaut.
7. ✅ `Drafting` : ordre, vols, quotas — **fait le 2026-08-25**. Deux segments (vol puis recrue/autonome), sans horloge, `DraftSelections` comme journal, écran `DraftRoom` avec un onglet qui n'existe que pendant le repêchage.
8. ✅ `SeasonPhaseJob --to InSeason/Complete` : `Leagues.Season` bascule, `protection-reset` tourne, `ChampionTeamId` s'écrit. **Jamais exécuté sur une vraie ligue.**
9. ✅ **Le palmarès** (§7) — `GET .../seasons` + `Palmares.tsx`, déployé.

---

## 10. Encore ouvert

- **`TradeSchedule.NextPeriodStart` retourne `null`** passé la dernière semaine
  d'une saison, donc aucun échange n'est possible en `PreSeason` — alors que §5
  les dit ouverts. Il doit savoir atteindre la semaine 1 de la saison suivante.
  **Toujours ouvert** ; `SeasonPhaseRules.CanTrade` autorise déjà `PreSeason`,
  mais `TradeSchedule` bloquerait quand même faute de semaine à viser.
- **`period-init --season 20262027` ne peut pas encore tourner** : il dérive le
  calendrier des matchs déjà en base (`Games`), et 2026-27 n'y est pas. Se
  résout tout seul le jour où `stats-sync`/`player-sync` voient la nouvelle
  saison ; rien à construire.
- **Les règles de la saison 2, c'était quoi ?** Le barème vit sur `League` et
  change en place. Un pool à vie finira par vouloir le figer par saison. Pas
  aujourd'hui — mais `LeagueSeasons` est l'endroit où ça atterrira.
- ~~**Combien de pertes maximum par équipe**~~ — **2** (Nick, 2026-08-25),
  colonne `League.MaxLossesPerTeam`, avec `League.StealRounds = 2` à côté.
- ~~**Combien de joueurs protégeables par DG**~~ — **9** (Nick, 2026-08-28),
  `League.ProtectionSlots`. Écrit par le panneau Règles ; l'auto-remplissage le
  consomme.
- **Il n'y a aucun retour en arrière.** `Drafting → PreSeason → InSeason`
  bascule `Leagues.Season` sur la saison préparée — qui n'a ni `Games` ni
  `Periods` tant que la LNH n'a pas publié son calendrier — donc le classement
  se viderait. Revenir à la saison qu'on jouait est du SQL, pas une transition.
  Décision de Nick (2026-08-28) : pas de job de rembobinage ; le SQL est écrit
  d'avance dans [deployment.md](deployment.md) plutôt qu'improvisé.
- **Qui déclenche une transition ?** Le commissaire à la main
  (`season-phase --to <Phase>`, construit) ou le job nocturne quand la dernière
  semaine banque. La main est plus simple et plus sûre pour une première saison,
  et c'est ce qui est construit — rien n'appelle `SeasonPhaseJob` automatiquement.
- ~~**Les listes de protection sont-elles publiques** avant le repêchage ?~~ —
  **oui, pour toute la ligue** (Nick, 2026-08-29). `GET .../protections` et
  l'onglet **Protections** de la salle montrent l'ardoise de n'importe quelle
  équipe, une à la fois. Les cacher n'achetait rien : le bassin de vol les donne
  déjà par omission — un vétéran absent des disponibles est un vétéran que
  quelqu'un a protégé. L'onglet n'est pas verrouillé par phase, parce qu'il sert
  surtout pendant `Protecting`, et il reste juste pendant `Drafting` : un vol ne
  prend qu'un exposé, donc rien de ce qu'il affiche ne bouge.
- **L'écran de protection et l'écran de vol** sont des listes de joueurs.
  `CLAUDE.md` demande de demander — combien de lignes, nom tronqué ou pas, quoi
  à droite — avant de construire n'importe quel écran-liste-de-joueurs ; ça n'a
  pas été fait ici, donc ces deux écrans restent non construits même si leur
  mécanique serait simple à câbler sur ce qui existe déjà
  (`RosterSpots.ProtectionStatus`, `ProtectionRules.IsAutoProtected`,
  `RosterChange.ApplyAsync` avec `startReason: Draft`).
- ~~**La barre de navigation du bas reste pleine.**~~ — réglé pour le Draft
  (2026-08-25) : son onglet **n'existe que pendant la phase `Drafting`**, avec
  une pastille glace « LIVE » et l'onglet en or quand c'est ton tour. Une
  destination qui apparaît puis disparaît ne prend pas de place le reste de
  l'année, et les trois volets de la salle (bassin, piges, équipes) vivent dans
  l'écran plutôt que dans la barre. **Protection devra encore trouver la
  sienne.**
