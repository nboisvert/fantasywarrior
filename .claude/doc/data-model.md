# Modèle de données — référence

> Le schéma Azure SQL et **pourquoi il est ce qu'il est**. Issu du chantier de
> migration Firestore → SQL (2026-08-02), conservé comme référence vivante.
> Les règles de pointage elles-mêmes vivent dans [scoring-model.md](scoring-model.md).

## Le principe directeur

**Un seul grain honnête : le `RosterAssignment`** — ce qu'un joueur a produit
pour une équipe pendant une semaine. Tout le reste est un `SUM`.

```
RosterAssignment.FantasyPoints     ← calculé une fois, gelé au banquage
  └─ SUM par spot    = ce que ce joueur a rapporté à cette équipe   (vue)
  └─ SUM par équipe  = le classement                                 (vue)
  └─ SUM par période = l'historique hebdomadaire                     (vue)
```

Aucune dérive possible, aucun job de synchronisation. Le modèle Firestore
stockait le même chiffre à trois grains et tenait l'invariant
`score = finalizedScore + periodPoints` à la main — c'est précisément ce qui
avait causé des bugs. Si le classement devenait lent (il ne l'est pas à ~10 k
lignes), l'échappatoire est une vue indexée, pas une colonne à maintenir.

Deux corollaires :

- **Les totaux de saison sont une vue, pas une table.** Un `GROUP BY` sur 51 k
  lignes avec index prend quelques millisecondes. La table de cache n'existait
  que pour économiser des lectures Firestore — et elle avait dû se doter d'un
  `throughDate` et de toute une logique d'invalidation pour servir le mode test.
  Un `WHERE GameDate <= @asOf` fait disparaître le problème *et* son code.
- **`Period` est une entité de premier plan**, pas un détail : la semaine
  lundi→dimanche sur la date de match, globale à toutes les ligues, immuable une
  fois écrite.

## Conventions

Tables au pluriel, PK `<Entity>Id`. Dates de match en `date` — le tri ordinal sur
chaîne était une contrainte Firestore. Horodatages en `datetime2`. Argent en
`bigint` (dollars entiers).

`EnableRetryOnFailure` est **obligatoire** : le plan gratuit s'auto-suspend et la
reprise lève une transitoire. Conséquence à connaître — une transaction manuelle
doit alors passer par `db.Database.CreateExecutionStrategy()`, sinon EF refuse.

Les migrations s'appliquent par commande explicite (`db-migrate`), jamais au
démarrage de l'API : plusieurs instances qui migrent en parallèle est un scénario
à éviter.

---

## Référence NHL (globale, hors ligue)

**`NhlTeams`** — `Abbrev` (PK, char(3)), Name, ConferenceName, DivisionName, LogoUrl

**`Players`** — `PlayerId` (PK, = id NHL, pas identity), FirstName, LastName,
Position (char(1)), PositionGroup (char(1), calculée persistée), TeamAbbrev (FK),
Status, SweaterNumber, ShootsCatches, BirthDate, BirthCountry, HeightCm, WeightKg,
HeadshotUrl, DraftYear, DraftRound, DraftOverall, DraftTeamAbbrev, DraftChecked,
CapWagesSlug, CareerStatsSyncedUtc, CareerNhlGames (null), LastSyncedUtc
→ index : (LastName, FirstName) pour la recherche, (TeamAbbrev), (Status)

> **`CareerNhlGames`** — matchs de saison régulière LNH en carrière, la somme
> des lignes `PlayerCareerSeasonStats` où `LeagueAbbrev = 'NHL'`. **Null
> exactement quand `CareerStatsSyncedUtc` l'est** : zéro match et « jamais
> regardé » sont deux états différents, et un vétéran dont la synchro a échoué
> ne doit surtout pas se lire comme une recrue.
>
> Stockée plutôt que sommée à la lecture, pour la même raison que
> `PositionGroup` : elle a **un seul écrivain** — `career-sync`, qui l'écrit dans
> le même `SaveChanges` que les lignes dont elle dérive, donc elle ne peut pas
> diverger — et une colonne se compare en SQL, là où une somme sur une autre
> table obligerait le seuil qui la lit à être écrit deux fois.
>
> C'est ce que lit `ProtectionRules.IsAutoProtected` : trop peu de matchs LNH et
> personne ne peut repêcher ce joueur à son DG (gardien ≤ 50, patineur ≤ 100).
> **On stocke la mesure, on dérive le verdict** — déplacer un seuil ne réécrit
> alors aucune ligne. Périmée d'au plus la fenêtre de fraîcheur de `career-sync`
> (30 jours) ; sans effet à ces seuils, mais c'est une raison de plus pour que le
> repêchage lui-même **gèle** le chiffre plutôt que de le lire vivant.

**`PlayerContracts`** — `PlayerContractId`, PlayerId (FK), Season, CapHit, Aav,
TotalValue, YearsRemaining, ClauseType, Source, ImportedUtc → unique (PlayerId, Season)

> Une table plutôt qu'une colonne `Player.CapHit` : les contrats changent chaque
> année, et l'ancienne colonne avait besoin d'une protection merge-field écrite à
> la main pour survivre à un `player-sync`. Une table sépare structurellement.
>
> **Toujours filtrer par saison.** Les contrats courent des années à l'avance ;
> prendre le plus récent fait mentir l'affichage (Jack Eichel : 10 M$ en 2025-26,
> 13,5 M$ dès 2026-27).

**`Games`** — `GameId` (PK, = id NHL), Season (char(8)), GameType (tinyint),
GameDate (date), HomeTeamAbbrev, AwayTeamAbbrev, HomeScore, AwayScore,
LastPeriodType, SyncedUtc → index (GameDate), (Season, GameType, GameDate)

**`PlayerGameStats`** — PK composite (GameId, PlayerId). GameDate/Season/GameType
dénormalisés (pour un index couvrant sans jointure), TeamAbbrev, OpponentAbbrev,
Position, IsGoalie, IsHome, Toi, Pim, puis colonnes typées patineur (Goals,
Assists, Points, PlusMinus, Shots, Hits, BlockedShots, PowerPlayGoals) et gardien
(ShotsAgainst, Saves, GoalsAgainst, Decision, Starter, Shutout, OtLoss), SyncedUtc
→ index (GameDate) INCLUDE stats, (PlayerId, GameDate)

**`PlayerCareerSeasonStats`** — `PlayerCareerSeasonStatId` (PK, surrogate),
PlayerId (FK), Season (char(8)), GameType (regular season only pour l'instant),
LeagueAbbrev, TeamName, GamesPlayed, puis colonnes patineur (Goals, Assists,
Points, Pim, PlusMinus) et gardien (Wins, Losses, OtLosses, GoalsAgainst,
GoalsAgainstAvg, SavePctg, Shutouts) → unique (PlayerId, Season, GameType,
LeagueAbbrev, TeamName)

> Carrière complète (junior, NCAA, Europe, AHL, NHL) tirée du player-landing
> de l'API NHL, filtrée par [`NotableLeagues`](../../backend/FantasyWarrior.Core/Players/NotableLeagues.cs)
> (liste blanche des ligues majeures — l'API renvoie aussi des tournois pee-wee).
> Alimentée par `career-sync`, qui rafraîchit les joueurs les plus périmés
> (`Player.CareerStatsSyncedUtc`) plutôt qu'une synchro unique comme
> `DraftChecked` : la ligne de la saison en cours change toute l'année.
> Manuel pour l'instant, pas encore dans le cron nightly.

> **La notion de prospect se lit ici, et nulle part ailleurs** (2026-08-25). Un
> joueur est un prospect tant qu'il n'a **aucun match de carrière LNH** : pas de
> ligne `LeagueAbbrev = 'NHL'` avec `GamesPlayed > 0`. Le jour où il joue son
> premier match, il cesse d'en être un sans que personne ait à le dire.
>
> **Ce n'est pas `Players.Status`**, qui vaut pourtant déjà `"prospect"` — celui-là
> veut dire « pas sur l'alignement d'une équipe LNH », ce qui est un autre
> ensemble. Sur les 41 joueurs des Mordus sans match LNH le 2026-08-25, **19
> portaient `Status = "nhl"`** : habillés par un club, pas encore une minute de
> jeu. Lire `Status` ici répondrait silencieusement à une autre question.
>
> **Dérivé, jamais stocké** (choix de Nick). Une colonne devrait être entretenue
> par `career-sync`, qui n'est pas dans le cron nightly — elle serait périmée
> exactement le jour qui compte, celui des débuts. Le calcul vit dans
> [`Prospects.ForAsync`](../../backend/FantasyWarrior.Data/Players/Prospects.cs),
> une lecture indexée, et un joueur que `career-sync` n'a jamais atteint
> (`CareerStatsSyncedUtc` nul) n'est **pas** rapporté comme prospect : « on a
> regardé, il n'a rien joué » et « on n'a jamais regardé » sont deux états
> différents, la même distinction que `DraftChecked`.
>
> **Sous la replay, la règle ignore la date simulée.** Les lignes de carrière
> sont par saison, pas par date, donc un joueur qui a débuté en mars 2026 compte
> comme ayant joué même quand le curseur est en décembre 2025. Au 2025-12-22,
> **14 joueurs des Mordus** sont dans ce cas — toute leur carrière LNH tient
> dans 2025-26 et aucun match avant le curseur. Corrigeable si ça gêne : borner
> la saison en cours par `PlayerGameStats` × `Games.GameDate`, qui eux sont
> datés. Pas fait, personne ne l'a demandé.

> **Colonnes typées, pas clé/valeur.** `StatLine`/`StatKeys` restent la
> représentation de *pointage* — une map, ce qui permet à un commissaire de scorer
> n'importe quelle statistique sans changement de schéma — et
> `StatLine.FromGameLine` reste l'adaptateur. Le stockage, lui, est typé : c'est
> tout l'intérêt de SQL.

**`NewsItems`** — Id, Source, Headline, **Body**, Url, PlayerId (FK null),
PlayerName, PublishedUtc, FetchedUtc, ExternalKey (unique, upsert idempotent)

> `Body` est le paragraphe factuel sous le titre — ce qui rend l'onglet News de
> la carte joueur utile plutôt que décoratif. **Jamais** le bloc « ANALYSIS » de
> Rotowire, verrouillé par abonnement.

**`PlayerInjuries`** — PlayerId (FK), Status, InjuryType, ReportedUtc,
ExpectedReturn (jamais alimentée), Source, ResolvedUtc → index filtré sur
PlayerId `WHERE ResolvedUtc IS NULL`

> Une ligne ouverte par joueur **et par source** tant que cette source le liste.
> Les deux pages scrapées sont des pages d'*état* : elles publient qui est
> blessé aujourd'hui, donc un joueur qui en disparaît est guéri — c'est
> `news-sync` qui ferme la ligne, personne n'annonce une guérison. Une source
> qui ne renvoie rien est traitée comme cassée, jamais comme « plus personne
> n'est blessé ».
>
> `Status` porte le **genre** d'indisponibilité (`Injured` / `Suspended`), pas
> une sévérité : aucune des deux sources ne publie « Out / IR / Day-to-day »,
> seulement une cause courte. `InjuryClassifier` est le seul auteur.
> `ExpectedReturn` reste vide — le retour n'est dit qu'en prose (« out until at
> least early November ») et le deviner serait pire que ne rien montrer.

## Calendrier

**`Periods`** — `PeriodId`, Season, Number, StartDate, EndDate, LockUtc, GameCount,
FinalizedUtc, CreatedUtc → unique (Season, Number). Globale, append-only.

**`SimulationState`** — ligne unique (`Id = 1` avec CHECK), AsOfDate, Season,
Enabled, UpdatedUtc

## Pool

**`Users`** — `UserId`, Username (unique, normalisé), DisplayName,
ExternalAuthId (null, prévu pour l'auth), CreatedUtc, LastLoginUtc

**`Leagues`** — `LeagueId`, Name, Season, CommissionerUserId (FK), **`JoinCode`
(unique, court)**, CapAmount, RosterMin, RosterMax, **ProtectionSlots (null)**,
**StealRounds (null)**, **MaxLossesPerTeam (null)**, DraftRounds (null),
ActiveForwards, ActiveDefense, ActiveGoalies, CreatedUtc

> Le `JoinCode` est ce que l'API expose comme `id`. Le frontend traite `league.id`
> comme une chaîne opaque et le garde en `localStorage` — exposer un code court
> plutôt qu'un entier a permis de ne pas toucher `LeagueGate`/`Settings`.

> **`ProtectionSlots`** — combien de spots un DG peut protéger avant le
> repêchage de vol. Null tant qu'aucun commissaire ne l'a fixé ; Les Mordus n'a
> jamais tenu ce repêchage, il n'y a donc pas de vrai chiffre à défaut.

> **`StealRounds`** et **`MaxLossesPerTeam`** — 2 et 2 chez Les Mordus. Le
> premier dimensionne le segment de vol **sans générer une seule ligne** : un
> tour de vol ne s'échange pas, donc chaque équipe en a exactement ce nombre et
> il n'y a rien à posséder. Le second referme le bassin par en dessous pendant
> que le repêchage tourne — quand une équipe atteint sa limite, tous ses joueurs
> restants cessent d'être disponibles pour tout le monde, ce qui est la raison
> pour laquelle la liste des disponibles est recalculée à chaque tour et jamais
> mise en cache. Null pour les deux = « la ligue n'a pas cette règle », pas zéro.
>
> **`StealRounds` est délibérément indépendant de `DraftRounds`** : ils
> dimensionnent deux repêchages différents qui s'enchaînent, et les voir
> diverger n'est pas une erreur.

**`LeagueSeasons`** — `LeagueSeasonId`, LeagueId (FK), Season (char(8)), Number,
Phase (tinyint : Preparing/Protecting/Drafting/PreSeason/InSeason/Complete),
ChampionTeamId (FK Teams, null), StartedUtc, CompletedUtc
→ unique (LeagueId, Season) ; unique (LeagueId, Number)
→ index unique **filtré** `(LeagueId) WHERE Phase <> 5` — au plus une ligne
non-`Complete` par ligue, une vraie contrainte plutôt qu'une phrase de doc

> Le pendant table de « saison » : `Leagues.Season` reste une valeur nommant la
> saison LNH dont les points comptent maintenant ; cette table est le compte
> **propre à la ligue** (« saison 3 » des Mordus) et le domicile de `Phase`.
> **Pas de FK composite** de `Leagues.Season` vers cette table : créer une
> ligue insère d'abord `Leagues` (elle distribue le `LeagueId` que toute ligne
> `LeagueSeasons` réclame), donc une FK dans l'autre sens refuserait la toute
> première insertion — l'œuf et la poule. Le rapprochement reste une valeur,
> comme `Team.FranchiseAbbrev`/`NhlTeam.Abbrev`. Voir
> [season-lifecycle.md](season-lifecycle.md).

**`LeagueMembers`** — PK (LeagueId, UserId), JoinedUtc

**`LeagueScoringRules`** — PK (LeagueId, StatKey), PointValue (float). L'API
réassemble la même forme JSON qu'avant, donc `RulesPanel.tsx` n'a pas bougé.

**`Teams`** — `TeamId`, LeagueId (FK), OwnerUserId (FK), Name, FranchiseAbbrev
(FK NhlTeams, null), CreatedUtc → unique (LeagueId, OwnerUserId)

> `Teams.FranchiseAbbrev` est l'**identité** de l'équipe dans le pool et ne
> bouge jamais. La franchise qu'elle **possède** est un `RosterSpot` de groupe
> `T`, et c'est celle-là qu'un échange déplace. Les deux partent égales chez Les
> Mordus et doivent pouvoir diverger — le club qu'on est n'est pas le club qu'on
> possède (Nick, 2026-08-05).

> **`Teams` ne porte aucune colonne de score.** Douze dénormalisations Firestore
> ont disparu ici : `playerIds`, `playerPoints`, `playerNhlPoints`,
> `rosterGamesPlayed`, `capTotal`, `score`, `finalizedScore`, `periodPoints`,
> `benchScore`, `currentPeriodIndex`, `periodScores`,
> `finalizedThroughPeriodIndex`. C'est le plus gros nettoyage du chantier.

**`RosterSpots`** — `RosterSpotId`, LeagueId, TeamId (FK), **PlayerId (FK null)**,
**FranchiseAbbrev (FK NhlTeams, null)**, PositionGroup (`F`/`D`/`G`/`T`, gelé à
l'ouverture), StartDate, StartReason (tinyint), StartTradeId (FK null),
StartDraftPickId (FK null), EndDate (null), **EndReason (tinyint null :
Release/Trade/Draft)**, EndTradeId (FK null), **ProtectionStatus (tinyint,
défaut 0)**, OpenedUtc, ClosedUtc

> `EndReason.Draft` — perdu au repêchage de vol. Ajoutée le 2026-08-25,
> **jamais encore posée** : rien n'écrit de vol aujourd'hui. `RosterChange
> .ApplyAsync` sait déjà accepter `StartReason.Draft` (existait avant elle) —
> un vol emprunte le même chemin qu'un échange, rien de nouveau à écrire côté
> mutation de roster le jour où le repêchage existera.

→ CHECK `CK_RosterSpots_PlayerOrFranchise` : exactement un des deux, en accord
avec PositionGroup
→ index unique **filtré** `(LeagueId, PlayerId) WHERE EndDate IS NULL AND PlayerId IS NOT NULL`
→ index unique **filtré** `(LeagueId, FranchiseAbbrev) WHERE EndDate IS NULL AND FranchiseAbbrev IS NOT NULL`
→ index unique **filtré** `(TeamId) WHERE EndDate IS NULL AND PositionGroup = 'T'`
→ index (TeamId) WHERE EndDate IS NULL ; (LeagueId, StartDate, EndDate)

> **`ProtectionStatus`** (`Unprotected` 0 / `Protected` 1) — le DG a-t-il dépensé
> une de ses places de protection sur ce spot, mettant son joueur hors d'atteinte
> au repêchage d'entre-saison. **La décision du DG, rien d'autre.**
>
> « Auto-protégé » n'y figure volontairement pas : c'est un fait sur le
> **joueur**, dérivé de `Players.CareerNhlGames`, et il s'applique aussi à un
> agent libre qui n'a aucun spot. L'y fondre détruirait de surcroît la distinction
> dont la phase protection a le plus besoin — « intouchable de toute façon » et
> « le DG a brûlé une place sur lui » ne sont pas la même réponse.
>
> **Une colonne et non une ligne par repêchage**, parce que la valeur est
> éphémère (Nick, 2026-08-25) : elle ne vaut qu'un été et expire au début de la
> saison qu'elle protégeait. Il n'y a pas d'historique à garder, seulement une
> ardoise à effacer — c'est ce que fait le job `protection-reset`.

> Le premier index filtré fait de « un joueur, un seul propriétaire par ligue »
> une **contrainte de base de données**, là où Firestore exigeait un scan
> applicatif de toutes les équipes. Le contrôle applicatif subsiste, mais
> seulement pour produire un message lisible — ce n'est plus lui qui garantit
> quoi que ce soit.
>
> Un spot est **fermé, jamais supprimé** : l'équipe garde définitivement ce que
> ce joueur lui a banqué.
>
> **`StartDate` et `EndDate` peuvent être dans le futur** (2026-08-07). Accepter
> un échange l'exécute immédiatement, daté au lundi suivant : le spot sortant
> reçoit une `EndDate` au dimanche, le spot entrant une `StartDate` au lundi.
> C'est ce qui permet à un GM de régler l'alignement de la semaine prochaine
> avec le roster qu'il aura vraiment.
>
> Conséquence : « jamais fermé » a cessé de vouloir dire « détenu aujourd'hui ».
> Les trois questions vivent dans `RosterWindow` (voir
> [scoring-model.md](scoring-model.md) §4) et l'ancien filtre désigne maintenant
> l'**engagé**. Les index filtrés ci-dessus s'en trouvent inchangés et corrects :
> le spot sortant quitte le filtre au moment même où l'entrant y entre, donc
> jamais deux propriétaires.
>
> **Un spot tient un joueur *ou* une franchise NHL** (le slot Équipe, groupe de
> position `T`) depuis le 2026-08-05 — voir [mordus-pool.md](mordus-pool.md) §3
> pour le renversement de décision que ça représente. Trois détails qui ne sont
> pas des détails :
>
> - `PlayerId IS NOT NULL` dans le filtre du premier index est **obligatoire** :
>   SQL Server considère deux NULL comme égaux dans un index unique, donc sans
>   lui la deuxième équipe d'une ligue à ouvrir un spot Équipe entrerait en
>   collision avec la première.
> - L'index unique `(TeamId) WHERE PositionGroup = 'T'` est la raison pour
>   laquelle le slot Équipe n'a **pas** de bouton actif/banc : une franchise, un
>   siège, donc aucune décision à prendre. Il rend aussi un échange
>   franchise-contre-joueur impossible plutôt que simplement refusé.
> - Il se déclare par la surcharge nommée `HasIndex([...], "nom")`. Deux appels
>   `HasIndex(x => x.TeamId)` ne créent pas deux index, ils en redéfinissent un —
>   le second avait silencieusement transformé « le roster courant d'une équipe »
>   en « une équipe ne peut tenir qu'un seul spot ouvert ».

**`RosterAssignments`** — `RosterAssignmentId`, RosterSpotId (FK), PeriodId (FK),
IsActive, EffectiveFrom, EffectiveTo (la fenêtre réellement possédée, issue de
`StatWindow.Intersect`), les 14 statistiques agrégées de la période, **plus
TeamWins / TeamLosses / TeamOtLosses** pour le slot Équipe,
**FantasyPoints**, GamesPlayed, IsFinalized, ScoredUtc → unique (RosterSpotId, PeriodId)

> Les trois colonnes d'équipe sont **distinctes** de Wins/OtLosses du gardien.
> « Mon gardien a gagné » et « ma franchise a gagné » sont deux événements
> différents le même soir, et une ligue qui les paie différemment n'a aucun
> moyen de le dire s'ils partagent une colonne. Chez Les Mordus les deux valent
> 2 et 1 : c'est une coïncidence.

> **C'est ici que vit le banquage** : `IsFinalized` + `Period.FinalizedUtc`. Une
> ligne finalisée n'est jamais recalculée — un changement de barème ne réécrit
> pas le passé.

**`TeamPeriodLineups`** — PK (TeamId, PeriodId), SetBy (`auto` | username),
SubmittedUtc → porte l'information « alignement automatique » qu'affiche l'UI

**`DraftPicks`** — `DraftPickId`, LeagueId, Year, Round, PickInRound (null),
OriginalTeamId (FK), CurrentTeamId (FK), PlayerId (FK null), UsedUtc, CreatedUtc
→ unique (LeagueId, Year, Round, OriginalTeamId)

> Propriétaire distinct de l'origine : ça donne gratuitement l'affichage
> « 2e ronde de PIT via BOS ».

**`DraftSelections`** — `DraftSelectionId`, LeagueSeasonId (FK, cascade),
OverallIndex, Segment (tinyint), Round, TeamId (FK), PlayerId (FK null =
tour passé), StolenFromTeamId (FK null), DraftPickId (FK null), MadeUtc
→ unique (LeagueSeasonId, OverallIndex) — `UX_DraftSelections_OneSelectionPerTurn`
→ unique (DraftPickId) filtré — une habilitation ne se dépense qu'une fois
→ unique (LeagueSeasonId, PlayerId) filtré — un joueur ne bouge qu'une fois
→ CHECK : un vol ne porte pas de `DraftPickId` ; une pige recrue en porte un et
  ne vole personne ; un tour passé ne vole personne

> **Pourquoi cette table plutôt qu'une dérivation.** La tentation est de relire
> le repêchage dans les `RosterSpots` qu'il a ouverts — ils portent déjà
> `StartReason = Draft`. **C'est impossible** : `SeedMordusJob` a ouvert les 418
> spots de l'import des Mordus avec exactement cette raison, donc la dérivation
> compterait 418 piges fantômes avant la première vraie.
>
> Et même dans une ligue vierge ça resterait faux : un vol est une **paire** de
> spots sans clé pour l'événement entre les deux, et l'ordre du repêchage
> finirait par reposer sur celui d'une colonne identity.
>
> La raison porteuse est ailleurs. Les tours de vol ne s'échangent pas, donc ils
> n'ont **aucune ligne à réclamer** : sans horloge, deux DG peuvent soumettre au
> même instant en croyant tous deux être au tour 7, avec deux joueurs
> différents — aucune autre contrainte du schéma ne le remarquerait.
> `UX_DraftSelections_OneSelectionPerTurn` est la seule chose qui l'arrête.
>
> C'est la même distinction que `Trades` à côté de `RosterSpot.StartTradeId`, et
> personne ne reconstruit un échange à partir d'une paire de spots : un spot
> enregistre **une possession dans le temps**, ceci enregistre **un événement
> dans une séquence**.

**`Trades`** — `TradeId`, LeagueId, ProposerTeamId, CounterpartyTeamId, Status
(tinyint), CreatedUtc, RespondedUtc, ProcessedUtc, EffectiveDate

**`TradeAssets`** — `TradeAssetId`, TradeId (FK), FromTeamId, ToTeamId, AssetType,
PlayerId (FK null), DraftPickId (FK null)
→ CHECK : exactement un des deux non-null, cohérent avec `AssetType`

> Une ligne par actif, et From/To **par actif** plutôt que sur l'échange. Ça rend
> un échange à trois équipes possible sans changement de schéma, sans compliquer
> le cas à deux. Ça couvre aussi d'office « joueurs et choix, toutes les
> combinaisons » : la contrainte porte sur l'actif individuel, jamais sur
> l'échange.

**`TradeVotes`** — PK (TradeId, UserId), FavoredTeamId (FK null = « équitable »),
Magnitude, VotedUtc

**`Messages`** — `MessageId` (bigint), LeagueId (FK), SenderUserId (FK),
RecipientUserId (FK), Body (nvarchar 1000), SentUtc, ReadUtc (null)
→ index (LeagueId, SenderUserId, RecipientUserId, SentUtc) pour lire un fil
→ index filtré `IX_Messages_Unread` sur (RecipientUserId, LeagueId) `WHERE ReadUtc IS NULL`

> **Les fils sont par ligue.** Un usager appartient à plusieurs pools ; les mêmes
> deux personnes qui se parlent dans deux ligues ont deux conversations, parce
> que le contexte est le pool. Ça garde aussi la liste de contacts trivialement
> juste : c'est la membriété de la ligue, jamais une union entre pools.
>
> **Pas de table `Conversations`.** Un fil, c'est « les messages entre ces deux
> usagers », lu dans les deux sens ; à une douzaine de GMs, la jointure que ça
> économiserait ne vaut pas la ligne qu'elle coûterait. Le regroupement vit dans
> `ConversationSummary` (Core), donc il est testé sans base.
>
> **`ReadUtc` null = non lu**, et c'est toute la requête du badge — aucun
> compteur à tenir en accord avec les lignes. L'index filtré reste de la taille
> de ce qui est effectivement non lu plutôt que de l'historique, qui lui ne fait
> que grossir.
>
> ⚠️ **Les deux FK vers `Users` sont en NO ACTION**, et pas seulement par
> l'habitude conservatrice du reste du schéma : deux chemins de cascade vers la
> même table, c'est l'erreur « may cause cycles or multiple cascade paths » et
> SQL Server refuse carrément de créer la contrainte.

**`Users.LastSeenUtc`** — estampillé par le middleware de présence de l'API sur
le trafic ordinaire, pas seulement au login.

> Sert **uniquement à formuler le libellé** (« last seen 45min ago ») pour ceux
> qui ne sont pas en ligne. La pastille verte, elle, ne dépend que du registre
> de connexions SignalR en mémoire : en ligne == une connexion vivante, point.
> Il y a eu une fenêtre de grâce de 90 s ; elle obligeait à retarder l'annonce
> hors-ligne au-delà d'elle-même, donc à un timer détaché, qui finissait par la
> contredire. Un seul prédicat supprime toute cette classe de bug.

## Vues

| Vue | Ce qu'elle donne |
|---|---|
| `vPlayerSeasonStats` | totaux saison par joueur (`GROUP BY` sur `PlayerGameStats`) |
| `vRosterSpotTotals` | points et matchs par spot **cette saison**, actifs et banc séparés |
| `vTeamPeriodScores` | points actifs/banc par équipe par semaine → l'historique hebdomadaire |
| `vStandings` | classement : SUM par équipe, cap et effectif **d'aujourd'hui** et **engagés**, matchs du roster, points/match |

Les totaux **à une date** (mode test) sont des requêtes paramétrées, pas des
vues — c'est la même agrégation avec un `WHERE GameDate <= @asOf`.

> **`vStandings` et `vRosterSpotTotals` filtrent maintenant par saison**
> (2026-08-25) : un `RosterSpot` de pool keeper survit à une frontière de
> saison, donc sans ce filtre les deux vues auraient sommé les
> `RosterAssignments` de *toutes* les saisons ensemble — un score qui ne
> repart jamais à zéro. Le filtre joint `Periods` et compare `p.Season` à
> `Leagues.Season` ; il ne change rien tant qu'aucune ligue n'a de deuxième
> saison en base (vérifié sur Les Mordus après déploiement : mêmes 435 lignes,
> mêmes totaux). Une future carrière/palmarès à vie ne passe **pas** par ces
> deux vues — elle lit `RosterAssignments` sans ce filtre. Voir
> [season-lifecycle.md](season-lifecycle.md) §§6-8.

> **`vStandings` est la seule vue du schéma qui sait quel jour on est.** Sa CTE
> `Today` lit le même curseur que tout le monde : `SimulationState.AsOfDate + 1`
> quand un replay tourne — la relation que `SimulationClockService` dérive — et
> la vraie date de l'Est sinon.
>
> Elle en a besoin parce qu'un spot peut commencer ou finir dans le futur. Le
> plafond affiché ne compte que les spots **actifs aujourd'hui** (Nick,
> 2026-08-07) : un joueur promis en échange y compte encore, son remplaçant pas
> encore.
>
> **`vTeamCommitments` a disparu le 2026-08-07.** Elle portait le delta de cap et
> d'effectif des échanges acceptés, parce que les rosters ne bougeaient qu'à
> l'exécution. Ils bougent à l'acceptation maintenant, donc ce delta est dans les
> dates des spots : `CapTotal` et `EngagedCapTotal` sont la même agrégation à un
> filtre près, dans le même énoncé, où ils ne peuvent plus diverger. Une addition
> en C# entre une vue et une autre en moins.
>
> La CTE `Scoring` n'est pas concernée : elle somme des `RosterAssignments`, qui
> n'existent que pour les semaines déjà atteintes.

---

## CapWages — la source des contrats

CapWages est un site **Next.js**. Chaque page embarque dans un bloc
`<script id="__NEXT_DATA__">` le JSON structuré à partir duquel son React a été
rendu : les mêmes chiffres que les tableaux visibles, déjà typés. **On parse ce
JSON, pas le HTML rendu** — un changement de CSS ou de mise en page ne peut donc
pas casser l'import, ce qui est la façon habituelle dont meurent les scrapers.

- **La page joueur porte `nhlId`** : un contrat se joint directement à `Players`,
  sans aucun appariement par nom.
- **32 requêtes suffisent, pas ~1 000** : chaque page d'équipe porte le détail
  saison par saison de tout son roster. Les pages joueur ne servent qu'en
  repêchage pour les non-appariés (`--resolve-unmatched`), puisqu'elles seules
  portent `nhlId`.

Conditions respectées : 2 s entre requêtes, User-Agent honnête nommant le projet,
backoff exponentiel sur 429/503, **usage personnel non commercial**. `robots.txt`
vérifié le 2026-08-01 : `/players/` et `/trade-tree/` ne sont interdits qu'à
Amazonbot.

---

## La validation contre l'oracle

Le résultat le plus important du chantier, conservé parce qu'il dit ce en quoi on
peut avoir confiance.

En rejouant les semaines 1 et 2 contre un instantané pré-migration
(`golden-scores-preSql.json`), joueur par joueur :

- **Les alignements sont identiques.** Les deux systèmes choisissent exactement
  les mêmes 14 joueurs. Auto-remplissage, fenêtre de statistiques, banquage et
  vues reproduisent l'ancien moteur à l'identique.
- **Un seul écart par joueur, et c'est l'ancien système qui avait tort.** Sergei
  Bobrovsky, semaine 1 : 3 matchs, 3 victoires. À 2 pts la victoire, SQL calcule
  6 ; Firestore avait `gamesPlayed=3, points=3` — ses champs de décision de
  gardien n'étaient pas correctement captés.
- Cette seule classe d'erreur explique **tous** les écarts restants : ils vont
  tous dans le même sens (SQL ≥ Firestore), et les plus gros appartiennent aux
  équipes qui alignent le plus de départs de gardien.

**Conséquence** : l'écart de 1 265 lignes (49 999 contre 51 264) n'était pas une
perte côté SQL — l'ancien journal de match était partiellement faux. La règle est
désormais épinglée par
`StatColumnsTests.ThreeGoalieWinsScoreSixUnderTheMordusScale`.

⚠️ **Pour refaire cette comparaison**, semer avec `--no-opening-lineup` :
l'ancien système n'utilisait jamais la liste `Active` du PDF, il auto-remplissait.
Les deux produisent des scores légitimes mais différents, et l'oracle ne valide
le moteur que si les *entrées* concordent.

## Pièges trouvés en route

Cinq bugs silencieux, tous corrigés, tous instructifs :

1. **Une semaine banquée sans avoir été scorée.** L'étape de pointage ne traite
   que la semaine *en cours*, qui au moment où une semaine devient banquable est
   déjà la *suivante*. Les semaines gelaient sur des chiffres partiels — zéro
   dans un rejeu. Le banquage rescore une dernière fois avant de geler, ce qui
   donne aussi son sens au jour de grâce.
2. **`wipe-pools` laissait les périodes marquées « banquées ».** Les frontières
   de semaine sont du calendrier et survivent ; « banquée » est de l'état de pool
   et ne doit pas. Une ligue fraîche ne pouvait plus jamais banquer ces semaines.
3. **L'auto-remplissage ne classait rien** — `SeasonPointsToDate` valait 0 pour
   tous les candidats, donc « les meilleurs disponibles » signifiait en fait
   « les plus petits ids ».
4. **`season-stats` ignorait le curseur de simulation** alors que la carte joueur
   le respectait : le même joueur affichait 74 matchs d'un côté et 0 de l'autre,
   sur le même écran.
5. **`.gitignore` ne protégeait pas `appsettings.Local.json`** — le motif
   `appsettings.*.Local.json` exige un segment au milieu. **Le dépôt est public**,
   donc un identifiant commité y serait lisible pour toujours.

## Hors périmètre, mais déjà au schéma

Repêchage, agence libre, authentification, échanges à trois équipes. Les
construire ne demandera **aucune migration**.

Le plafond et les tailles de roster sont appliqués sur les échanges depuis le
2026-08-03 ; les points du slot Équipe existent depuis le 2026-08-05 et ont
demandé une migration, contrairement à ce que cette section annonçait — un spot
polymorphe n'était pas prévu au schéma.
