# Fantasy Warrior — Project Status

> **Read at the start of every session, and keep updated along the way.**
> Last updated: 2026-08-25.
>
> This file holds the **current state and the decisions behind it**. It is not a
> changelog — `git log` is, and the commit messages in this repo are detailed.
> Add a decision here when the *why* would be hard to recover from a diff.

## Current state

**Live in production**, entirely on free tiers.

| | |
|---|---|
| App | https://nboisvert.github.io/fantasywarrior/ (GitHub Pages, auto-deploy on push) |
| API | https://fantasy-warrior-api.calmhill-00a494fd.canadacentral.azurecontainerapps.io |
| Database | Azure SQL serverless, free tier, resource group `fw` |
| Nightly cron | `daily-jobs.yml` — db-migrate → stats-sync → nightly → player-sync → draft-sync → news-sync |

- **Reference data**: 1 586 players, the full 2025-26 regular season (1 312 games —
  32 teams × 82, confirmed against the NHL's published schedule — ~51 k
  player-game lines, 2025-10-07 → 2026-04-16), contracts scraped from CapWages.
- **Les Mordus** is the live league — join code `TKW6UR`, season `20252026`,
  14 GMs, **404 players plus one NHL franchise each (418 roster spots)**,
  9F/4D/1G active plus the Équipe slot, 23-35 roster, **$134M cap**, scoring
  1/1/2/1/0 (goal/assist/goalie win/OT loss/shutout) and 2/0/1 for the
  franchise (win/loss/OT loss). See [mordus-pool.md](mordus-pool.md).
- **A season replay is running**, restarted from scratch on 2026-08-05 (join
  code `TKW6UR`) so the Équipe slot scores from week 1. It sits at
  **2025-12-22, week 12, weeks 1-11 banked** — advanced five weeks on
  2026-08-24, which also settled the doubt this paragraph used to carry: the
  week-7 state on record did belong to *this* replay, not the previous one; it
  was testmode.md's journal that had skipped the passages. `sim-clock` stays
  the only authority. Everything in the app believes it is whatever day it
  reports — check it before treating any date-related behaviour as a bug. See
  [testmode.md](testmode.md).

**Built and working**: player and stats services, leagues/teams/multi-tenancy,
weekly-lineup scoring with banked points, trades (propose → accept, which
applies the swap dated to the next Monday → lands → community rating), the five
screens, the news ticker, per-player news and injury status.

## Roadmap

| Scope | Status |
|---|---|
| Player service — NHL identity, rosters, prospects, draft info | **Done** |
| Stats service — game-by-game lines, daily sync, full-season backfill | **Done** |
| Core domain — users, leagues, teams, multi-tenancy | **Done** |
| Rules & scoring engine — weekly lineups, banked points | **Done** |
| Frontend — Dashboard, Standings, Team, Trades, Settings | **Done** |
| Trades — propose, respond, nightly processing, community rating | **Done** |
| Contracts — CapWages import | **Done** |
| GM-to-GM direct messages and live presence (SignalR) | **Done** |
| Cap and roster-size **enforcement** | **Done for trades** — no other path changes a roster yet |
| Draft picks — tradable, one year ahead | **Done** (the draft itself is not) |
| Real authentication | Todo |
| Free agency | Todo |
| Off-season protection & steal draft | **Foundation only** (2026-08-25) — the spot carries a status, auto-protection reads live, the card shows it, `LeagueSeasons`/phases/trade-freeze exist and are deployed. The protection and draft screens themselves are not built — both are player-row lists and need the CLAUDE.md ask first, plus two numbers Nick hasn't set. |
| 🏁 **Season-tracking MVP in prod for early October 2026** (NHL 2026-27) | — |
| Interactive live draft (target: 2027-28 season) | Todo |

## Decisions log

Newest first. Each line is a decision that is still in force, with the reason it
was taken — not a record of what changed. **Only the last ~5 days live here** —
older entries (back to 2026-07-22) are in
[decisions-archive.md](decisions-archive.md), same format, nothing dropped.

### Architecture

- **2026-08-25 — Presence stamps the viewer, never the viewed.** The middleware
  read `username` from the route values *first*, and on every league-scoped team
  route that segment names the team's **owner**, not the caller. Opening a
  rival's roster, pricing a trade against him (`CreateTradeSheet` fetches his
  `season-stats` and `picks` with no `viewer` at all) or scrolling his week in
  Stats all stamped **him** as "seen just now". The whole league read as active:
  eight GMs who have never logged in showed "4h ago". The rule now lives in
  `PresenceStamping.ResolveViewer` (Core, 12 tests) instead of inline in the
  middleware, because it is a rule with a bug history. **Only the query string
  names the viewer** — `viewer` first, then `username`. A route segment counts
  only in the `/api/users/{username}/…` family, where the subject *is* the
  caller by construction and which is the first call the app makes after login.
  Everywhere else an ambiguous request stamps nobody: a missed stamp costs a
  stale label for one request, a wrong one invents activity that never happened.
  **Rows already written stay wrong** — they were not cleaned, they simply decay.
- **2026-08-25 — `LastLoginUtc` is readable, commissioner-only.** It had been
  written at every login since the SQL rebuild and read by *nothing*, so when
  the first real outside GM logged in (steeve) his arrival could only be
  inferred from a trade he declined. `GET /api/leagues/{leagueId}/activity`
  returns, per member, both timestamps side by side: `lastLoginUtc` is a
  deliberate act, `lastSeenUtc` is any traffic at all — and only the first one
  was ever immune to the bug above. No screen, on purpose: it is a diagnostic,
  and a per-GM last-login list on a public route is a surveillance feature
  nobody asked for.
- **2026-08-25 — The season-lifecycle foundation is built and deployed**, same
  day as the design doc (`season-lifecycle.md`). `Season` (Core, 35 tests)
  replaces four places that each re-derived the NHL season string on their own.
  `LeagueSeasons` exists, backfilled one row per league (`Number = 3` for Les
  Mordus, matching its own source PDF; `InSeason`), with a filtered unique
  index enforcing "at most one non-Complete row per league" as a real
  constraint rather than a sentence in a doc. `Leagues.Season` deliberately
  keeps **no** foreign key to it: a composite FK was the first thing tried and
  cannot work — creating a league inserts the `Leagues` row first, since it is
  the row that hands out the `LeagueId` any `LeagueSeason` row would need to
  reference, so a constraint requiring the reverse would refuse the very
  insert that has to happen first. `LeagueSeasonPhase` and
  `SeasonPhaseRules` (Core, tested) model the six-phase lifecycle and gate
  trades; the freeze is wired into `TradeEndpoints.ValidateAgainstEngagedAsync`,
  the one helper both propose and accept already shared, so neither path can
  drift from the other. `SeasonPhaseJob` (`season-phase --league --to <Phase>`)
  advances a league one step, flips `League.Season` and clears protections on
  entering `InSeason`, writes the champion off `vStandings` on entering
  `Complete` — **never run against a real league**, since advancing a season
  is Nick's call, not a default. `vStandings` and `vRosterSpotTotals` are now
  scoped to the league's current season (a latent bug that predates all of
  this: neither ever filtered by season, so a keeper spot's assignments from
  two different seasons would have summed together the moment any league
  reached a second one) — verified live against Les Mordus, same 435 spots and
  same 454-point leader before and after. The palmarès
  (`GET /api/leagues/{id}/seasons` + `Palmares.tsx`) is the first screen paid
  for by keeping `RosterAssignments` forever instead of clearing them; it lives
  behind a trophy icon on Standings rather than a new bottom-nav tab (already
  full) or a duplicate shortcut. **Deliberately not built**: the protection and
  draft screens themselves, both player-row lists that need the CLAUDE.md ask
  first (how many lines, name truncation, what sits on the right) and two
  numbers — protection slots, max losses per team — Nick has not set yet.

- **2026-08-25 — The season rollover moves a filter; it never deletes a row.**
  Nick's first shape for the keeper rollover was to delete the finished
  season's `RosterAssignments`, which does reset the standings. It also empties
  `vRosterSpotTotals`, which reads the same rows — so the Team screen's PTS
  column would go to zero **for a player still on the roster**, since a keeper
  spot survives the season. "What has he produced for me since I got him" is
  the question a lifetime pool exists to answer, and deleting is the one way to
  make it permanently unanswerable. It also contradicts banking itself: a
  week's points belong permanently to whoever fielded the player. And it is not
  recoverable in the way that matters — `PlayerGameStats` survives, so a replay
  is possible, but only under *today's* scale, restating history that a scale
  change is explicitly never allowed to restate. For 5,434 rows (≈14k a full
  season, both leagues) against 50k game lines, there was no pressure to relieve.
  The fix is one `WHERE p.Season = l.Season` in each of the two views. Points
  reset because the filter moves. Design in
  [season-lifecycle.md](season-lifecycle.md).

- **2026-08-25 — Three different things are called "season", and only one of
  them wants a table.** Nick asked what `League.Season` actually is and whether
  it should be a table; the question was right and it split into three.
  **(A)** the NHL season, `"20262027"` — the NHL's own identifier, same argument
  as `Player.PlayerId`, so it stays a value: a `Seasons` table would carry no
  attribute the string lacks, would put a foreign key on ~50k
  `PlayerGameStats` rows for nothing, and the one thing it would be asked —
  succession — is a pure function. What is missing there is a `Season` helper in
  Core, not a table: the string surgery is currently repeated in four places
  (`CurrentSeason()`, the draft-year `[..4] + 1`, three hardcoded defaults, and
  the frontend's `formatSeason`), and the column is free text, so `"2025-2026"`
  would create a phantom season in silence.
  **(B)** the league's own season — "Les Mordus, saison 4". **This is the table**,
  `LeagueSeasons(LeagueId, Season, Number, Phase, ChampionTeamId)`, and it does
  not exist at all today even though the source PDF is titled *"Classement
  Mordus pool a vie **saison 3**"* — the pool has counted its own seasons for
  three years. It turns the rollover into an insert rather than an overwrite of
  `League.Season` (which would destroy the record the league ever played
  2025-26), and it is the only place a champion can be written.
  **(C)** the draft year, `2026` — derived from (A), never stored twice: the
  draft is named for the summer it is held in, the season for the two years it
  spans, and the 2026 draft stocks `"20262027"`. `DraftPicksInitJob` already
  computes exactly this; nothing says so, so every reader re-derives it.
  **Correction to the same day's earlier entry**: `Phase` belongs on the
  `LeagueSeason` row, not on `League` — "the league is drafting" cannot say
  *for which season*. Each row walks `Preparing → Protecting → Drafting →
  PreSeason → InSeason → Complete`, exactly one row per league is not
  `Complete`, and the off-season phases belong to the season being **prepared**.

- **2026-08-25 — The measurement is stored, the verdict stays derived.**
  Off-season protection needs to know who has too few NHL games to be draftable.
  Two different things were hiding in that: the **games count**, reference data
  with a single writer, and **auto-protection**, a comparison against a
  threshold. `Players.CareerNhlGames` is written by `career-sync` in the same
  `SaveChanges` as the career rows it sums, so it cannot drift;
  `ProtectionRules.IsAutoProtected` stays a comparison, written once. Storing
  nothing would have made every read sum `PlayerCareerSeasonStats` and — the day
  a view needed it — **copied the threshold into SQL**; that, not the query
  cost, is what decided it. Storing the verdict instead would have meant
  rewriting rows every time a prospect plays a game, and losing the number
  itself, which is the thing worth displaying. The precedent was in the same
  table: `Player.PositionGroup` is a computed **persisted** column for exactly
  those two reasons.
  Accepted consequence: `CareerNhlGames` is stale by at most 30 days
  (career-sync's window, because the current season's row keeps changing all
  year). Irrelevant at 50 and 100 games — but one more reason the draft itself
  must **freeze** the figure rather than read it live.

- **2026-08-07 — `sim-advance` is also an API endpoint, Nick-only.** Advancing
  the replay used to mean a PowerShell prompt on `C:\Nick\fw`; now
  `POST /api/testmode/advance?username=nick&to=...` on the deployed API runs
  the same job, so it can be triggered from a phone. Gated on
  `username == "nick"` (403 otherwise) because the job it wraps banks weeks
  permanently and executes trades across both leagues at once — not real auth,
  just a guard against a pool-mate's stray tap until the app has something
  stronger. Meant to be removed with the rest of test mode once the real
  season starts. `FantasyWarrior.Api` now references `FantasyWarrior.Jobs` to
  reach `SimAdvanceJob` directly rather than shelling out.

### Scoring

- **2026-08-07 — Next week's lineup is written, not previewed.** The endpoint
  used to compute what the carry-forward *would* pick and never store it,
  because the rows only appeared when the scoring pass reached the week — by
  which time it was locked, and the "preview" had never been anyone's choice.
  `WeekAheadJob` writes them every night, fills in what is missing and rewrites
  nothing, so the forgotten-lineup rule is data rather than a guess and a trade
  can edit next week's lineup like anything else. `setBy: "auto"` is now a
  stored value.
- **2026-08-05 — The "Équipe" slot is a roster spot, not a column.** Every GM in
  Les Mordus owns one NHL franchise for life; it now banks its own record — 2
  per win, 1 per overtime loss here, priced per league through
  `extraPointValues` like any other stat. **This reverses the modelling decision
  taken that morning** (mordus-pool.md §3), which kept the franchise off
  `RosterSpots` to stop a polymorphic spot "contaminating the whole roster,
  lineup and transaction model". The reversal is what that shape is worth: a
  franchise opens a spot, produces one assignment a week, banks points and can
  be traded — it *is* a roster spot, and the alternative was a second scoring
  engine, a second trade path and a second grid. The cost came to one nullable
  column and one CHECK constraint.
  Three unique indexes carry the rules: one owner per franchise per league, one
  Équipe slot per team — which is exactly why the slot has no active/bench
  control — and the existing one-owner-per-player index re-filtered on
  `PlayerId IS NOT NULL`, without which the second team in a league to open a
  franchise spot would collide with the first.
  `Teams.FranchiseAbbrev` stays: it is the team's identity and never moves,
  while the spot is the asset a trade can carry. The two start equal and are
  meant to be able to diverge.

### UI

- **2026-08-25 — Auto-protection is marked on the player card, and nowhere
  else.** Protection is an off-season mechanism, but one half of it matters all
  year: whether a kid on your roster is out of anyone's reach. That is the only
  part worth showing during a season (Nick), so it is a single `AUTO` pill at
  the right of the card's header row and nothing on any list or grid.
  A new `.pc-protect-pill` rather than `.roster-pos-pill`: that pattern is
  reserved for the F/D/G indicator, and a second pill of the same shape on the
  same row saying something else is exactly what that rule prevents. Ice-cyan,
  because rose on this card is spoken for by the injury mark and the two must
  never be confused at a glance. It pins right and refuses to shrink, so the
  team abbreviation ellipsizes to make room — the same call as the injury badge.
  **Nothing is drawn when the answer is unknown.** `autoProtected` is `bool?`,
  null when career-sync has never reached the player, and the card renders on
  `=== true` only. An `AUTO` badge on a veteran whose sync failed would be a
  false statement about a real person; no badge is merely a gap.

- **2026-08-04 — An unavailable player is marked twice on the Team grid, and
  neither mark costs the row a column**: a rose edge on the sticky identity
  cell, and a badge immediately after the name. The badge is in the flow rather
  than absolutely positioned, so the name ellipsizes to make room — Nick's call,
  the mark matters more than the last letters of a surname. The edge is an
  `inset` box-shadow on the sticky cell rather than a border on the row, so it
  stays on screen while the twenty numeric columns scroll under it.
- **2026-08-04 — Injured and suspended share the colour, never the symbol.**
  Both keep a player out of the lineup, which is the whole point of the marker,
  so both rows are rose. But a gavel instead of a cross, because telling a GM
  his defenceman is *injured* when he was suspended six games for slashing is a
  false statement about a real person. `InjuryClassifier` decides which,
  server-side, once, at the moment the source's label is read.
- **2026-08-04 — The player card's News tab carries every source, not just
  injuries.** A contract signing and a knee are both things a GM wants, and
  splitting them would hide whichever tab he did not think to open. Lazy-loaded
  on first open, same as Career: most players have no news at all.

- **2026-08-04 — The two dashboard leaderboards show *NHL* numbers, over
  longer windows.** The headline figure on a card is now the stat a GM already
  knows from a box score — points for a skater, wins for a goalie — not the
  league's fantasy score. The fantasy score stays as the *ranking* key, because
  it is the only thing that can compare a goalie with a winger; it just no
  longer has to be the number on the card. The unit stays welded to the figure
  ("9 W"), with the window stacked underneath: a goalie's 9 sitting above a bare
  "last 2 weeks" reads as nine points. Windows widened with it: Top Reserve sums
  the last **two** weeks — named under every figure, since the section title
  does not say it — and Top Free Agents covers the **whole season to date** — a claim is a season-long bet, and a one-week window put a fourth-liner
  who scored twice on Saturday ahead of a 60-point winger nobody had taken.
  `GET /free-agents` accordingly lost its `period` parameter and now aggregates
  the season in SQL (tens of thousands of game lines, none wanted individually),
  bounded to the simulated day like every other season total.
- **2026-08-25 — Fixed: the player card crashed for every true prospect.**
  `PlayerDetail.season` was typed `string`, never `string | null` — but the API
  genuinely sends `null` for a player with zero NHL games on record, and
  `formatSeason(null)` threw reading `.length`. No error boundary exists
  anywhere in the app, so the crash took the whole screen down, not just the
  card. Nick reported it as "the player card doesn't work" for Zharovsky; the
  same crash hits every prospect, not just him. Fixed at the type (now
  honest) and the one call site; the "Season" heading now omits the year it
  doesn't have, and the stats panel shows "No stats this season" instead of a
  wall of zeros, which is what a truthiness check on the always-non-null
  `seasonTotals` used to render instead.

### Trades

- **2026-08-07 — Accepting a trade executes it, dated to the next Monday.** The
  effect did not move: the swap still lands on a week boundary and no player
  ever owns two teams on one day. What moved is when the database is told.
  Between agreeing and executing, the app used to still believe the old
  rosters — and that window is exactly when a GM sets the lineup for the week
  the trade lands in, so he was offered a player who would be gone and denied
  the one who would arrive. The old effective date was a *consequence of job
  scheduling* rather than a rule: the nightly job ran trades on banking nights,
  and the grace day pushed them a week later than `scoring-model.md` promised.
  It is now `TradeSchedule.NextPeriodStart`, pure and tested.
- **2026-08-07 — A `RosterSpot` can start or end in the future, and that is the
  property everything else follows from.** "Never closed" and "held today" split
  apart for exactly the two spots a trade creates. `RosterWindow` names the
  three questions that used to share one filter. The surprise: the old
  `EndDate IS NULL` did not become wrong, it changed meaning — it is now the
  *engaged* figure.
- **2026-08-07 — `vTeamCommitments` is gone, folded into `vStandings`.** The
  delta it carried is in the spots' own dates now, so today's cap and the
  engaged cap are the same aggregate one filter apart, in one statement, where
  they cannot drift from each other. That costs `vStandings` a `Today` CTE
  reading `SimulationState` — the first date logic in any view here — because
  the displayed cap must keep counting only the spots active today (Nick).

### Product

- **2026-08-05 — A player with no contract stops being free.** Both cap views
  counted him at $0, which treats "no contract" as a data gap to wait out. It is
  not: an unsigned free agent and an undrafted prospect are permanent states, and
  a keeper pool holds plenty of both — 30 on Mordus rosters today. He now costs
  `Leagues.DefaultCapHit`, $1M by default and editable per league from the Rules
  panel (0 restores the old behaviour). Both views had to move together at the
  time, since callers summed `vStandings` and `vTeamCommitments` — the latter is
  gone as of 2026-08-07 and both figures now come from one. `vStandings` also gained
  `UnknownContracts`: folding an assumed salary into the total makes it
  invisible, and a figure that is part measurement and part house rule should
  say so.
- **2026-08-05 — Half the "missing" Mordus players were never missing, and no
  third-party source was needed for the other half.** The import's 44 unmatched
  names were diagnosed in July as players the NHL API does not expose, which
  pointed at scraping EliteProspects. Both halves of that were wrong. 25 were
  already in `Players`, stored "J. Klingberg" — the shape the NHL publishes a
  man between contracts in — so it was a matching failure at import, not a
  missing row. The other 19 are genuinely unreachable through the two endpoints
  `player-sync` reads, but the official search endpoint returns every one of
  them. **EliteProspects is not integrated and is not needed**: a player in this
  situation still has an NHL id. `PlayerCareerSeasonStats` already covers the
  junior/KHL/NCAA history that motivated it.
- **2026-08-05 — `PlayerNameIndex` is the wrong matcher when the question is
  "does this name refer to anyone?"** Its first-initial fallback is right where
  a news source abbreviates a player we already hold, but asked to resolve a
  name that may match nobody it answered *Mathieu* Bolduc for "Marcel Bolduc" —
  the only M. Bolduc among seven namesakes. `PlayerSearchMatcher` requires three
  shared characters in the given name instead, keeping Zack for Zachary and Sam
  for Samuel while refusing Marcel for Mathieu. Nicknames sharing no prefix
  (Bill for William) are reported unresolved rather than guessed. Found by the
  tests, not by review.
- **2026-08-05 — The Mordus cap was the NHL's number, not the league's.** It had
  been seeded at $115M since July; the real rule is **$134M** (Nick). At $115M,
  nine of fourteen teams would have been over budget once the 44 missing players
  were added. At $134M all fourteen are compliant.
- **2026-08-04 — An injury list is not a news feed, and news-sync now treats
  them differently.** The two scraped sources publish who is hurt *today*, so a
  player disappearing from one is that source saying "cleared" — nobody ever
  announces a recovery. That single fact drives both writes: his
  `PlayerInjuries` row is resolved, and his `NewsItem` is deleted, because
  "out with a knee" is no longer true. The medical record survives in
  `PlayerInjuries` with its `ReportedUtc`/`ResolvedUtc`; the headline does not
  deserve to. A source returning nothing is treated as broken, never as "nobody
  is hurt" — a site rewrite would otherwise clear the league in one silent run.
- **2026-08-04 — Age retires a headline, never a condition.** The 30-day
  retention prune now skips injury-list sources entirely. Once the Rotowire
  timestamp was read properly, Troy Terry's hip — reported 18 June, still out —
  fell past the cutoff and was deleted every night and re-inserted on the next
  run, taking the one item that explained the mark on his row with it. An
  injury list already has a truer deletion rule: the source stopped listing him.
- **2026-08-04 — A news source's name is matched through `PlayerNameIndex`,
  which falls back to first-initial-plus-surname.** player-sync stores a player
  as the NHL currently publishes him, and a veteran between contracts is
  published as "R. Gudas" — so seven real NHL players (Gudas, Arvidsson,
  Jensen, Schwartz, Bogosian, MacEwen, Pitlick) matched nothing and could never
  be marked injured, silently, because an unmatched name is stored with a null
  PlayerId rather than failing. The fallback only uses keys that are unique:
  the league has two Sebastian Ahos, so "s aho" is not a key. A wrong match
  puts another man's injury on a GM's roster, which is worse than no match.
  news-sync now names every unmatched player in its output.
- **2026-08-04 — Injuries are reconciled per source, never across sources.**
  Rotowire dropping a player says nothing about whether FantasySP still lists
  him, and letting one site's silence resolve the other's report is how a flag
  starts lying. Where two sources report the same man, the API shows the one
  reported first — its "hurt since" date is the true one.

## Open items

- **No authentication.** The API trusts the username the client sends. Weekly
  lineups make this materially worse than it sounds: silently benching a rival's
  best player every Sunday would be undetectable.
  **Direct messages changed the nature of this risk, not just its size**
  (2026-08-03): the hub and the message routes trust a username in the query
  string like everything else, so anyone who knows a handle can read that
  person's private threads. For a pool of friends that is a tolerable trade,
  but it is the first place where the gap exposes content rather than actions.
- **FantasySP started answering the job's HttpClient with 403** (2026-08-04),
  from an IP and User-Agent that curl got 200 on seconds later; Accept headers
  and HTTP/2 changed nothing, so it is the client fingerprint. Deliberately not
  chased — dressing the client up as a browser would circumvent an access
  control the site chose to put up. Cost is bounded: news-sync treats a failed
  fetch as "unknown", so FantasySP's injuries stop updating rather than
  vanishing, and Rotowire covers the same ground. Worth re-checking whether it
  also 403s from a GitHub runner, which is a different IP; it worked from there
  on 2026-08-02. **One consequence today: Charlie McAvoy's suspension is the
  only Mordus case of the gavel icon, and it comes from FantasySP alone — so
  nothing in the league currently exercises that path.**
- **Injuries are real-world, the replay is not.** The scrapers read today's
  pages, so during the 2025-26 replay the Team grid marks players who are hurt
  in *August 2026* against a roster that believes it is January. Harmless in
  prod next season, confusing while testing — and unfixable, since no source
  publishes a historical injury list. Check `sim-clock` before treating an
  odd-looking marker as a bug.
- ~~Cap and roster size are displayed but not enforced~~ — **enforced on trades
  since 2026-08-03**, against the *engaged* figures. No other path can change a
  roster, so there is nothing left unguarded; free agency will need its own
  check when it arrives.
- ~~The "Équipe" roster slot scores nothing~~ — **done 2026-08-05**. See the
  decisions log and [scoring-model.md](scoring-model.md) §1.
- ~~Unmatched Les Mordus players~~ — **done 2026-08-05**, all 44 resolved. See
  [mordus-pool.md](mordus-pool.md) §2.
- **Rotate the deploy service principal secret.** It was passed through a chat
  session and phone photos on 2026-08-02. Only the `AZURE_CREDENTIALS` GitHub
  secret would need replacing.
- ~~Nothing in the app can cross a season boundary~~ — **the foundation is
  built** (2026-08-25): `vStandings` and `vRosterSpotTotals` are scoped to the
  league's season (verified live: Les Mordus' 435 spots and top score of 454
  unchanged after the migration), `LeagueSeasons` exists and is backfilled
  (Number 3 for Les Mordus), and `LeagueSeasonPhase` plus the trade freeze are
  wired into `TradeEndpoints`. **`TradeSchedule.NextPeriodStart` still returns
  null past the last week of a season**, so a trade in `PreSeason` would still
  be refused even though the phase itself allows it — the one piece of this
  that is still wrong today. See [season-lifecycle.md](season-lifecycle.md).
- **Free agency and the draft** are modelled in the schema but not built —
  neither needs a migration.
- **Nothing announces a failed nightly.** `daily-jobs.yml` failed 17 nights in a
  row (2026-08-08 → 2026-08-24) and it only surfaced because the app broke. The
  chain is `db-migrate → stats-sync → nightly → player-sync → draft-sync →
  news-sync`, so a red first step silently stops all data movement. No
  notification exists today. See deployment.md's troubleshooting log for that
  outage; the CI-side cause of the abort is still unproven.
