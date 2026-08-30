# Fantasy Warrior — Project Status

> **Read at the start of every session, and keep it updated along the way.**
>
> This is the only doc that says what is built, what is not, and what is
> broken. Every other doc describes the system in the present tense and never
> dates anything. If a status claim lives somewhere else, it is in the wrong
> file.

## Current state

**Live in production**, entirely on free tiers.

| | |
|---|---|
| App | https://nboisvert.github.io/fantasywarrior/ (GitHub Pages) |
| API | https://fantasy-warrior-api.calmhill-00a494fd.canadacentral.azurecontainerapps.io |
| Database | Azure SQL serverless, free tier, resource group `fw` |
| Nightly cron | `daily-jobs.yml` — db-migrate → stats-sync → period-init → nightly → player-sync → draft-sync → news-sync |

See [deployment.md](deployment.md) for how a deploy happens, the jobs and the
runbooks.

**Reference data**: ~1 586 players, the full 2025-26 regular season (1 312
games — 32 teams × 82 — and 51 264 player-game lines, 2025-10-07 → 2026-04-16),
contracts scraped from CapWages.

**Two leagues exist.**

- **Les Mordus** — the live league, join code `TKW6UR`, season `20252026`. Its
  rosters, rules, cap and scoring scale are in [mordus.md](mordus.md).
- **Mordus2** — a throwaway copy of Les Mordus, join code `6HEURH`, created by
  `clone-league`, sitting in `Drafting` for season `20262027`. Same rules, same
  rosters, **no history**. It exists so the off-season is rehearsed on a copy
  and never on the live pool. Delete it whenever; the SQL is in
  [deployment.md](deployment.md).

**A season replay is running.** It sits at **2025-12-22, week 12, weeks 1-11
banked**. `sim-clock` is the only authority: everything in the app believes it
is whatever day that job reports, so **check it before treating any
date-related behaviour as a bug**. See [testmode.md](testmode.md).

## Roadmap

| Scope | Status |
|---|---|
| Player service — NHL identity, rosters, prospects, draft info | Done |
| Stats service — game-by-game lines, daily sync, full-season backfill | Done |
| Core domain — users, leagues, teams, multi-tenancy | Done |
| Rules & scoring engine — weekly lineups, banked points | Done |
| Frontend — GM Office, Standings, Team, Trades, Settings | Done |
| Trades — propose, respond, nightly processing, community rating | Done |
| Contracts — CapWages import | Done |
| GM-to-GM direct messages and live presence (SignalR) | Done |
| Draft picks — tradable, one year ahead | Done |
| Season-lifecycle foundation — `Season`, `LeagueSeasons`, the six phases, the trade freeze, the palmarès | Done |
| Season calendar — the `Seasons` table, `season-init`, a weekly calendar that can be built from declared dates before a single game is imported | Done |
| League rules — the whole catalogue as one versioned document per season, its validation, the "not enforced yet" badge, and the rules panel that writes it | Done. Catalogue in [league-rules.md](league-rules.md); what is modelled but inert is listed there. |
| Interactive live draft — steal rounds then rookie rounds, one room, asynchronous, no pick clock | Done |
| Off-season protections — `ProtectionSlots`, the autofill default, the public protection slates | **Written, but no GM-facing screen.** See below. |
| Cap and roster-size enforcement | **Partial.** The cap is enforced on trades and on draft picks. `RosterMin`/`RosterMax` are enforced on trades only — `DraftRules.ValidateSelection` deliberately passes both as null (`DraftRules.cs:54`), because `PreSeason` exists to repair a roster that came out of a draft off-bounds. No other path changes a roster. |
| **Protection screen** — a GM contradicting the autofill's default | **Not built.** `GET /protections` reads and `POST /protections/autofill` writes the whole league at once; nothing lets one GM choose his own nine. It is a player-row list and owes the CLAUDE.md player-row ask before a line is written. |
| Real authentication | Todo |
| Free agency | Todo. `GET /free-agents` is a read-only leaderboard, not a claim path. |
| 🏁 **Season-tracking MVP in prod for early October 2026** (NHL 2026-27) | — |

## Open items

- **No authentication.** The API trusts the username the client sends. Weekly
  lineups make this materially worse than it sounds: silently benching a
  rival's best player every Sunday would be undetectable. Direct messages
  changed the *nature* of the risk, not just its size — the hub and the message
  routes trust a username in the query string like everything else, so anyone
  who knows a handle can read that person's private threads. It is the first
  place where the gap exposes content rather than actions.

- **Les Mordus' three off-season numbers are still not entered on the live
  league.** `protection.slots`, `draft.steal.rounds` and
  `draft.steal.maxLossesPerTeam` are settled ([mordus.md](mordus.md)) and now
  all three are on the rules panel, so entering them is a five-minute job for
  the commissioner. Until that happens `draft/open` **refuses** rather than
  opening a draft with no steal segment, so the danger is gone and only the
  data entry is left.

- **`period-rollup` scores a league whatever its phase.** It iterates every
  league and knows nothing about `LeagueSeasonPhase`, so a copy sitting in
  `Drafting` gets this week auto-filled and scored like any other. Harmless
  today — the draft order is frozen at creation — but its standings will not
  stay at zero, and [offseason.md](offseason.md) says a league outside
  `InSeason` should not be scored.

- **There is no error boundary anywhere in the frontend.** One throw in one
  component takes the whole screen down rather than that component — which is
  how a single null field once read as "the app is broken".

- **`TradeSchedule.NextPeriodStart` returns null past the last week of a
  season** (`TradeSchedule.cs:33`). A trade in `PreSeason` is therefore refused
  even though the phase itself allows it — the one piece of the season
  lifecycle that is still wrong.

- **Nothing announces a failed nightly.** `daily-jobs.yml` once failed 17
  nights in a row and it only surfaced because the app broke. The chain is
  ordered, so a red first step silently stops all data movement. No
  notification exists. See deployment.md's troubleshooting log.

- **FantasySP answers the job's HttpClient with 403**, from an IP and
  User-Agent that curl gets 200 on seconds later; Accept headers and HTTP/2
  changed nothing, so it is the client fingerprint. Deliberately not chased —
  dressing the client up as a browser would circumvent an access control the
  site chose to put up. Cost is bounded: news-sync treats a failed fetch as
  "unknown", so FantasySP's injuries stop updating rather than vanishing, and
  Rotowire covers the same ground. Worth re-checking from a GitHub runner,
  which is a different IP. **One consequence: Charlie McAvoy's suspension is
  the only Mordus case of the gavel icon and it comes from FantasySP alone, so
  nothing in the league currently exercises that path.**

- **Injuries are real-world, the replay is not.** The scrapers read today's
  pages, so during the 2025-26 replay the Team grid marks players who are hurt
  *now* against a roster that believes it is December. Harmless in prod next
  season, confusing while testing — and unfixable, since no source publishes a
  historical injury list. Check `sim-clock` before treating an odd-looking
  marker as a bug.

- **Rotate the deploy service principal secret.** It was passed through a chat
  session and phone photos. Only the `AZURE_CREDENTIALS` GitHub secret would
  need replacing.

- **Backend validation-error strings are English-only.** The UI itself is
  bilingual, but a message like `"Username must be 2-30 characters."` comes
  straight from the API and is shown verbatim regardless of the viewer's
  language. Translating those needs an error-code system on the backend (a
  code the frontend maps to its own dictionary) rather than string matching
  — a separate, larger change, deliberately not bundled into the bilingual UI
  work.

