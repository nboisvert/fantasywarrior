# CLAUDE.md

**Fantasy Warrior** — a web application for managing hockey pools. Interaction
between users is a key attraction to bring people in.

You are the main architect assisting Nick, Sr. .NET specialist & architect on
the solution. Your AI agent name is **Macklin Softwarini**.

## Stack

- **Frontend**: React (mobile-first) + TypeScript + Vite on **GitHub Pages**. UI in English only.
- **Database**: **Azure SQL** (serverless, free tier) via **EF Core 10**.
- **API**: **.NET 10 minimal API**, Docker, on **Azure Container Apps** (scales to zero), resource group `fw`, same region as the database.
- **Auth**: none yet. The API trusts the username the client sends. Firebase Auth is the intended replacement.
- **Batch jobs**: .NET console apps on **GitHub Actions cron** (`daily-jobs.yml`: db-migrate → stats-sync → period-init → nightly → player-sync → draft-sync → news-sync).
- **Realtime**: **SignalR** (`LiveHub`) carries presence, direct messages and the live draft board. Push-only — clients never call hub methods; a REST endpoint persists, then pushes. **One replica, no backplane** — that ceiling is a correctness requirement, not a budget one.
- **CI/CD**: GitHub Actions (frontend → GitHub Pages, API → Container Apps via ghcr.io). See [deployment.md](.claude/doc/deployment.md) for what ships on which trigger.

Hosting must stay easy and free.

## Git workflow (Nick, 2026-07-27)

**Merge feature branches straight to `main` yourself (fast-forward push).** Don't
stop at opening a PR and wait for Nick. Faster than a PR round-trip while he is
solo on this repo; revisit if collaborators join.

## Key points

- **Mobile-first**, still responsive on larger screens.
- **Multi-tenant**: many leagues, and one user can belong to several.
- **Player service** — identity and rosters for the whole NHL ecosystem from the official NHL API (`api-web.nhle.com`); salaries and contracts scraped from **CapWages**.
- **Stats service** — `PlayerGameStats` is the source of truth, one row per player per game (~50k a season). Season totals are the `vPlayerSeasonStats` view; totals *as of a simulated day* are the same aggregation with a date bound. **There is no cache to keep fresh.**
- **Scoring is weekly**: each GM activates a subset of his roster per week, only active players score, and a week's points are **banked permanently** once it closes — a trade can never move history. [scoring-model.md](.claude/doc/scoring-model.md) **MUST** be read before changing anything about scoring, lineups, roster spots or periods, and kept in sync with the code.
- **Season simulation (test mode)**: the 2025-26 season can be replayed day by day. The simulated date lives in the single-row `SimulationState` table and is the single source of truth for jobs and the API alike. When a replay is running, **everything in the app believes it is that day** — check `sim-clock` before concluding a date-related behaviour is a bug. See [testmode.md](.claude/doc/testmode.md) and the `/testmode` skill.
- **External sources are used on personal, non-commercial terms**: no redistribution, and never store Rotowire's subscription-locked "ANALYSIS" block. See [integrations.md](.claude/doc/integrations.md).
- Features are built on Nick's own buddies' pool first, agile and incremental.
- **Every feature that touches the database ships with mocking-free unit tests for its pure logic** — proactively, not only live-verified.

## Documentation rules

The docs describe the system **as it is**, in the present tense. They are not a
changelog — `git log` is, and the commit messages here are detailed on purpose.

- **Keep the reason, drop the chronology.** Write "X is Y because Z", never "X
  was A until some date, when it became Y". The test: does the sentence survive
  having every date removed? If not, it belongs in a commit message.
- **Never add** struck-through "done on <date>" items, checkbox status markers,
  "superseded by", or "this file used to say". That convention is exactly what
  grew these docs to 3,600 lines of mostly-untrue text.
- **One fact, one home.** Six boundaries, and everywhere else is a link:

  | Domain | Owner |
  |---|---|
  | Status — built, not built, open risk | `project_status.md` |
  | Schema — tables, columns, indexes, views | `data-model.md` |
  | Off-season — phases, protections, draft | `offseason.md` |
  | Les Mordus' own numbers | `mordus.md` |
  | Jobs, commands, runbooks, deploys | `deployment.md` |
  | Colours, CSS, layout, screen conventions | `design-system.md` |

  A consequence worth stating twice: **no doc other than `project_status.md`
  says "done on <date>" or "not built yet".** A real limitation is written in
  the present tense — "the protection screen does not exist, so a GM cannot
  contradict the default" — which is not the same thing as a progress checkbox.
- Run `/doc-clean` when the docs feel stale or contradictory. It cross-checks
  `.claude/doc/*.md` against the code (**the code always wins**) and enforces
  the boundaries above. See the `doc-cleaner` skill.

## Reference docs

| Doc | What it holds |
|---|---|
| [project_status.md](.claude/doc/project_status.md) | **Read at the start of every session.** Current state, roadmap, open risks. Keep it updated. |
| [scoring-model.md](.claude/doc/scoring-model.md) | The scoring rules. Authoritative — if it and the code disagree, one of them is a bug. |
| [data-model.md](.claude/doc/data-model.md) | The SQL schema and **why** it is shaped that way. |
| [offseason.md](.claude/doc/offseason.md) | What "season" means, `LeagueSeasons`, the six phases, protections, and the two-segment draft. |
| [deployment.md](.claude/doc/deployment.md) | Infra, how to deploy, config, local dev, every job and runbook, troubleshooting. Keep it updated when infra changes. |
| [design-system.md](.claude/doc/design-system.md) | Night Arena detail: exact colours, typography, the stats-grid traps, PWA asset regeneration. |
| [mordus.md](.claude/doc/mordus.md) | Les Mordus: the league's own settings and vocabulary. |
| [integrations.md](.claude/doc/integrations.md) | The NHL API, CapWages and the news sources — and the terms that constrain each. |
| [testmode.md](.claude/doc/testmode.md) | Season replay. |
| [cockman-concept.md](.claude/doc/cockman-concept.md) | The Garry Cockman / cockcoin mascot concept — a living doc, keep appending. |

## UI rules — "Night Arena"

All UI work MUST follow this. Exact values and asset procedures live in
[design-system.md](.claude/doc/design-system.md); what follows is the part you
must respect without being reminded.

- **Dark theme only.** Ice-cyan accent, rose for danger/over-cap, violet for defense, gold for goalies.
- **Lucide SVG icons only — never emojis.** 44px touch targets, `cursor: pointer` on clickables, visible `:focus-visible` rings, aria-labels on icon-only buttons, alt text, error banners near the action.
- **No duplicate destinations (Nick, 2026-07-22)**: never put two links on one screen that go to the same place. If somewhere is already reachable from the bottom nav, don't add an inline "view all" shortcut to it — pick one path.
- **Player-row convention (Nick, 2026-07-23)**: before implementing or changing a player-row list on any screen, **ask** — how many lines, is the name full or truncated ("S. Crosby"), and what shows on the far right. Screens intentionally differ; never assume consistency, ask each time.
- **Position indicator (Nick, 2026-07-23)**: every F/D/G indicator MUST use one of exactly two patterns — never a one-off, never a flat uncoloured letter.
  - **Normal** — a pill: `.roster-pos-pill` + `.roster-pos-pill-f/d/g`.
  - **Compact** — the bare letter, colour on the font only: `.pos-compact-f/d/g`.
  - Pick by data density: dense grids and multi-select lists use compact, roomier screens (Dashboard, PlayerCard) use the pill.
  - Always build the suffix with `posGroupClass()` in `frontend/src/api.ts` — never inline `.toLowerCase()` on `posGroup()`.
- **Type honestly against real data.** The app has no error boundary, so one throw in one component takes the whole screen down — a field the API can really return as null must be typed that way.
