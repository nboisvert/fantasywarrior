# Fantasy Warrior — Project Status

> **Read at the start of every session, and keep updated along the way.**
> Last updated: 2026-08-03.
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

- **Reference data**: 1 567 players, the full 2025-26 regular season (1 342 games,
  ~51 k player-game lines, 2025-10-07 → 2026-04-16), contracts scraped from CapWages.
- **Les Mordus** is the live league — join code `Q7ZJ4G`, season `20252026`,
  14 GMs, 9F/4D/1G active, 23-35 roster, $115M cap, scoring 1/1/2/1/0
  (goal/assist/goalie win/OT loss/shutout). See [mordus-pool.md](mordus-pool.md).
- **A season replay is running.** The simulated date is 2026-01-19 (week 16).
  Everything in the app believes it is that day — check `sim-clock` before
  treating any date-related behaviour as a bug. See [testmode.md](testmode.md).

**Built and working**: player and stats services, leagues/teams/multi-tenancy,
weekly-lineup scoring with banked points, trades (propose → accept/decline →
nightly-processed → community rating), the five screens, the news ticker,
per-player news and injury status.

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
| 🏁 **Season-tracking MVP in prod for early October 2026** (NHL 2026-27) | — |
| Interactive live draft (target: 2027-28 season) | Todo |

## Decisions log

Newest first. Each line is a decision that is still in force, with the reason it
was taken — not a record of what changed.

### Architecture

- **2026-08-03 — SignalR does exactly two things**: tell a league who is online,
  and deliver private messages. Polling was cheaper and was rejected — presence
  and event pop-ups both need a push. The cost is real, since a WebSocket is a
  request that never ends and the API cannot scale to zero while one is open,
  so the *client* owns the budget: connect while a league is loaded and the tab
  is visible, drop 60 s after it is hidden, stop on `pagehide`. Billing is per
  replica-second, not per connection, so fourteen GMs at once cost what one
  does; what costs money is a forgotten tab, and `visibilitychange` is the whole
  lever.
- **2026-08-03 — No idle timeout, no activity tracking.** A first version
  dropped the socket after 3 min without a pointerdown/keydown/scroll. SignalR
  keeps its own connection alive with pings, so that was never about staying
  connected — it was a budget mechanism grafted onto the connection lifecycle,
  and it was the root of most of this feature's complexity: listeners on scroll,
  "the normal state is disconnected" leaking into unrelated decisions, and a
  retry path with no backoff that could hammer `/hubs/live/negotiate` once per
  scroll event against a cold API. `visibilitychange` alone covers the case that
  actually costs money.
- **2026-08-03 — `PresenceService` owns who is online, and never touches SQL.**
  It is keyed by league and refcounted by connection, so a GM with the app on
  his phone and his laptop does not go dark when he closes one. A disconnect is
  now pure memory plus one send — measured at 0 queries, against 2 on connect
  which are unavoidable (username → id, join code → id). It lives in
  `FantasyWarrior.Core` rather than beside the hub because it contains no
  SignalR at all, and Core is the project whose tests actually run in CI.
- **2026-08-03 — The Container App is capped at one replica.** Not a budget
  decision: the hub's connection groups and `PresenceRegistry` are both
  per-process, so a second replica would silently drop messages between GMs
  split across the two and report half the league offline. Going wider needs a
  backplane and a shared presence store, not a bigger number.
- **2026-08-03 — Presence is broadcast as a roster, never as a delta.** The
  first cut pushed "nick just arrived", which the league could apply but which
  told *nick* nothing about the five people already there — his own counter read
  zero until some REST call happened to seed it. The whole league now travels in
  one payload on every connect and disconnect: a few hundred bytes for fourteen
  GMs, it reaches the arriving client through the same group as everyone else,
  and it is idempotent, so a missed event is repaired by the next one instead of
  leaving a dot stuck. `PresenceRoster.ForLeagueAsync` is the single builder,
  used by both the push and the REST fetch, so the two cannot answer differently.
- **2026-08-03 — Online means one thing: a live connection.** There is no
  "recently active so probably still around" window. The earlier version had a
  90-second one, and it cost far more than it bought: the offline announcement
  had to be delayed past it, which forced a detached timer with its own DI
  scope, which then disagreed with the very window it was built around. One
  predicate deletes that whole class of bug, and since the client only holds a
  connection while the app is actually being used, "connected" and "here" mean
  the same thing anyway. `LastSeenUtc` survives to word the label for people who
  are *not* online; it never decides the dot.
- **2026-08-03 — Presence is still passive underneath.** No heartbeat: a
  middleware stamps `User.LastSeenUtc` from ordinary traffic, throttled to one
  write a minute per user.
- **2026-08-02 — Firestore → Azure SQL + EF Core.** Most of the backend's
  remaining complexity existed only to dodge Firestore's 50 000 reads/day free
  tier: a season-stats cache with its own invalidation rules, twelve
  denormalised columns on `Teams`, a hand-maintained `score = finalizedScore +
  periodPoints` invariant. In SQL those are a `GROUP BY`, some joins and a
  `SUM()`. The UI did not change by a line — every API response stayed identical
  field for field. See [data-model.md](data-model.md).
- **2026-08-02 — Cloud Run → Azure Container Apps.** Cloud Run has no stable
  outbound IP, so it could never be allowed through the Azure SQL firewall
  without paying ~$32/month for Cloud NAT. Container Apps environments have
  stable outbound IPs, so the firewall rule is derived by the deploy workflow
  rather than maintained by hand.
- **2026-08-02 — No registry credentials on the Container App.** `GITHUB_TOKEN`
  expires when the workflow run ends, and an app with stored credentials never
  falls back to an anonymous pull — so a dead token 401s even on a public image,
  hours after a green deploy. The ghcr package is public; the deploy asserts
  `/health` instead of only printing it.
- **2026-08-02 — Salaries come from CapWages, scraped.** PuckPedia is the gold
  source but its data API is private and paid. CapWages embeds the figures in
  Next.js `__NEXT_DATA__` JSON with an `nhlId` for exact joins. Contracts live in
  their own `PlayerContracts` table rather than a column on `Player`, because
  they change every year and the old column needed hand-written merge-field
  protection to survive a player sync.
- **2026-07-22 — Auth deliberately bypassed.** Login is username-only and the API
  trusts the client, so the UI and league schema could be built first. This is
  now the single biggest open risk (see below).

### Scoring

- **2026-07-31 — Weekly lineups with permanently banked points**, replacing the
  season-cumulative model. The old design recomputed every player's whole season
  nightly, auto-selected a top-X per position, and wrote a compensating ledger
  entry on every transaction so totals wouldn't jump — three mechanisms fighting
  to produce a number nobody could explain, at ~90 000 Firestore reads a night
  against a 50 000/day tier. Because a week's points are now banked when it
  closes, **a trade can never move history**, and the entire compensation
  apparatus became meaningless and was deleted. [scoring-model.md](scoring-model.md)
  is authoritative.
- **2026-08-02 — Cap hit is shown for the season being displayed**, not the
  newest contract on file. Contracts run years ahead (Jack Eichel is $10M in
  2025-26 and $13.5M from 2026-27), so taking the latest made the player card
  disagree with the Team grid about the same player — and the grid was right.
- **The "Équipe" roster slot scores nothing yet** — the rule has never been
  specified.

### UI

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

- **2026-08-02 — GM Office's dashboard replaced "League News" with "Top
  Reserve" and "Top Free Agents".** Two leaderboard card grids: the viewer's
  currently-benched players ranked by what they scored last week, and
  league-wide unrostered players ranked by fantasy points under the league's
  own scale — which is what lets a goalie's wins/saves compete with a
  skater's goals/assists for a spot. New `GET /free-agents` endpoint; Top
  Reserve needed no new endpoint, just two `lineup` calls joined on `spotId`.
- **2026-08-02 — The Team screen shows a second grid for departed players.** A
  team keeps whatever a player banked for it, so the standings figure is not
  explained by the current roster alone. Both grids share one `RosterGrid`
  component: 21 columns and a derived footer duplicated would drift silently,
  and this table changes often.
- **2026-07-26 — The Roster screen was retired.** Its player list duplicated what
  the Team/Stats grids already showed; only its cap-gauge detail was unique, so
  that moved into Team as a collapsible section. Same reasoning as the
  no-duplicate-destinations rule.
- **2026-07-23 — Position display is standardised to F/D/G everywhere**, with
  exactly two visual patterns (pill or bare coloured letter) chosen by data
  density. Display-only: the raw NHL position code stays in the data. An audit
  found the rule was being broken in 4 of 8 places before it was written down.
- **2026-07-23 — Ask before building any player-row list** (how many lines, name
  truncation, what sits on the right). Several rounds were spent guessing this
  per screen; screens intentionally differ.
- **2026-07-22 — Never two destinations to the same place on one screen.**
- **2026-07-22 — Dashboard is the default tab**, Settings lives in a topbar icon
  rather than the bottom nav, which freed the slot for Trades.

### Trades

- **2026-08-03 — The cap is enforced against *engaged* figures, not the
  standings.** An accepted trade is irreversible and lands at the next week
  boundary, but `vStandings` still describes today's roster. Validating against
  it would let a GM accept a $9M contract in the morning and bust the cap in the
  afternoon, each trade looking fine on its own. `vTeamCommitments` carries the
  difference, and it is a **view rather than a `TradeEngaged` flag**: an
  aggregate over an honest event log cannot drift, whereas a flag nobody cleared
  freezes a player forever, silently. Same reasoning as the cockcoin ledger.
- **2026-08-03 — Accepted trades lock their assets; pending ones do not.**
  Shopping the same player to three GMs is normal and only one offer can ever be
  accepted — the others are refused at that moment. Once accepted, re-offering
  the same player would otherwise blow up inside the nightly job at 09:30 UTC
  rather than at the proposal.
- **2026-08-03 — Validation runs at propose *and* accept**, through one shared
  helper. Rosters move in between, and accepting is the last moment anyone can
  be told. Execution is deliberately not a third checkpoint: refusing there
  would need a new terminal status for a trade both GMs already agreed to.
- **2026-08-03 — A player with no contract counts as $0 and is reported.** 16 of
  701 active NHL players have no salary on file. We cannot validate what we do
  not know, so the count of unknowns is shown next to the figure instead of
  being quietly folded into it.
- **2026-07-23 — Acceptance does not execute the trade.** It sits `accepted`
  until the nightly job swaps the rosters, so a day's score is always computed on
  that day's rosters before any trade takes effect.
- **2026-07-23 — `cancelled` is distinct from `declined`.** The proposer
  withdrawing and the counterparty rejecting are different events; the status
  alone communicates who acted, with no extra field.
- **2026-07-23 — Pending, declined and cancelled trades are private** to the two
  teams involved. Accepted and processed trades are public to the league.
- **2026-07-23 — Trade votes record which team was favoured**
  (`FavoredTeamId` + magnitude), not a proposer-relative 1-5 scale. A "which GM
  wins their trades most" rollup can then aggregate across trades without
  knowing each trade's roles.

### Product

- **2026-08-03 — Direct messages are 1-to-1 and scoped to a league**, not a
  league-wide room. Threads are per pool because the context of a conversation
  *is* the pool, and it keeps the contact list trivially correct — it is the
  league's membership, never a union across pools. There is no `Conversations`
  table: a thread is just the messages between two users, read both ways.
- **2026-08-03 — The chat sheet is inside the Night Arena theme**, which is the
  exact opposite of the Cockman rule below and for the same reason. Cockman
  clashes because it plays a bolted-on third-party widget; this is a native
  screen and has to read as one.
- **2026-07-27 — Merge feature branches straight to `main`** while Nick is solo.
- **2026-07-27 — Garry Cockman clashes with the theme on purpose.** The mascot
  chat is a UI mock with literal hex values, a light corporate palette and a
  system font stack, so it reads as a real embedded third-party helpdesk widget
  bolted onto the app. No backend. See [cockman-concept.md](cockman-concept.md).
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
- **2026-07-28 — News is not league-scoped.** The ticker's news half is a generic
  NHL feed; only trades are league-scoped. Roster-move items were removed from
  the ticker in the same change.
- **2026-07-22 — UI in English only.**
- **2026-07-22 — The draft happens outside the app for 2026-27.** A live
  interactive draft targets 2027-28.

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
- **Cap and roster size are displayed but not enforced** — no add/drop or trade
  is rejected for breaking them.
- **The "Équipe" roster slot scores nothing** — rule never specified.
- **Unmatched Les Mordus players** still to reconcile — see [mordus-pool.md](mordus-pool.md).
- **Rotate the deploy service principal secret.** It was passed through a chat
  session and phone photos on 2026-08-02. Only the `AZURE_CREDENTIALS` GitHub
  secret would need replacing.
- **Free agency and the draft** are modelled in the schema but not built —
  neither needs a migration.
