# CLAUDE.md

This project is called **Fantasy Warrior**.

Fantasy Warrior is a web application for managing hockey pools.
Interaction between users will be a key attraction to bring people in.

You'll be the main architect assisting me, Nick, Sr. .NET specialist & architect on the solution. Your AI agent name is **Macklin Softwarini**.

## Stack (decided 2026-07-22)

- **Frontend**: React (mobile-first) + TypeScript + Vite, hosted on **GitHub Pages**. UI in English only.
- **Database**: **Firebase Firestore** (single Firebase project owned by Nick).
- **Auth**: **Firebase Auth** — Google sign-in + email/password.
- **API**: **.NET 10 minimal API** in a Docker container on **Google Cloud Run** (free tier, scales to zero).
- **Batch jobs**: .NET console apps run by **GitHub Actions cron** (daily NHL stats scraping, score calculation).
- **Realtime**: Firestore realtime listeners on the client (no self-hosted websockets).
- **CI/CD**: GitHub Actions (frontend → GitHub Pages, API → Cloud Run).

Hosting must stay easy and free.

## Key Points

- Mobile-first app. Still should be responsive for larger devices.
- App must be multi-tenancy like (multi-league, same user can belong to many leagues).
- App relies on a **player service** that keeps information (except stats) about all players and prospects in the NHL ecosystem. See PuckPedia as the gold data source for salaries/contracts; official NHL API (`api-web.nhle.com`) for identity/rosters.
- App relies on an **advanced stats service** that must fetch daily NHL player stats, drilled down to game-by-game stats (detail level required by/for other services). Model supports per-season stat retention: `playerGameStats` (game-by-game, source of truth) plus a consolidated `playerSeasonStats` cache (one doc per season/player) kept fresh as a write-through side effect of nightly scoring and player-card views — never a full-season rescan on a schedule (a whole season is ~49k game-lines, and the Firestore free tier caps at 50k reads/day, so that must stay a rare, deliberate operation, not routine).
- App relies on a **score calculation & rules service** that calculates scores daily based on each league's implemented rules (configurable per league; v1: points for goals/assists, goalie wins/shutouts/OT losses, plus a salary cap).
- Fully intuitive interactive transaction, free agency and draft mechanisms.
- First rules/features implemented will be based on my own buddies' pool, agile/incremental style.
- You should always keep track of project progression in [.claude/doc/project_status.md](.claude/doc/project_status.md), which **MUST** be read at the start of every session and kept updated along the way.
- Deployment, GCP/GitHub configuration, local dev commands and ops runbook: see [.claude/doc/deployment.md](.claude/doc/deployment.md). Keep it updated when infra changes.
- Full project roadmap (phases 0-7): see project_status.md. Milestone: season-tracking MVP in prod for early October 2026 (NHL 2026-27 season).

## UI Design System — "Night Arena" (approved by Nick 2026-07-22)

All UI work MUST follow this system. Tokens live in `frontend/src/index.css` (CSS variables), components in `frontend/src/App.css`, screens in `frontend/src/screens/`, SVG icons in `frontend/src/components/Icons.tsx`.

- **Theme**: dark only ("night arena"). Background `#0a0e1a` with fixed radial cyan/indigo glows; elevated `#10162a`; glass cards `rgba(255,255,255,.045)` + 1px border `rgba(255,255,255,.09)` + backdrop-blur.
- **Accent**: ice cyan `#38bdf8` → `#22d3ee` (gradients, active states, subtle neon glow `rgba(56,189,248,.35)`). Danger/over-cap: rose `#f43f5e`. Standings podium: gold `#fbbf24`, silver `#c7d2e0`, bronze `#d0885a`. Position pills (F/D/G, `.roster-pos-pill-*`): forward = ice cyan, defense = violet `#a78bfa` (`--defense`, added 2026-07-23 — silver read too low-contrast for defense), goalie = gold.
- **Text**: `#f1f5f9`; muted `#8b96ab`. Contrast AA minimum.
- **Typography**: Russo One (display: headings, team names, numbers, uppercase) + Chakra Petch (body) — Google Fonts, loaded in `index.html`.
- **Shape & motion**: radii 12-16px; transitions 150-300ms (color/opacity/filter only, no layout-shifting hover); `fade-in` 250ms for screen mounts; respect `prefers-reduced-motion`.
- **Layout**: mobile-first, content max-width 680px; fixed bottom nav (4 tabs: Dashboard default, Standings, Roster, Settings) 64px + `env(safe-area-inset-bottom)`; sticky blurred topbar with league switcher + user; content bottom padding must clear the nav.
- **Rules**: Lucide SVG icons only (never emojis), 44px touch targets, `cursor: pointer` on clickables, visible focus rings (`:focus-visible` cyan), aria-labels on icon-only buttons, alt text, error banners near the action.
- **No duplicate destinations (Nick, 2026-07-22)**: never show two links/buttons on the same screen that navigate to the same place. If a destination is already reachable via primary navigation (e.g. a bottom-nav tab), don't also add an inline "view all" / shortcut link to it elsewhere on that screen — pick one path. (First case: removed the Dashboard's "View full standings" link since the Standings tab already goes there.)
- **Logo**: circular badge (bearded warrior, red helmet, crossed sticks). Master: `fw_logo.png` at repo root (1024px, transparent). App asset: `frontend/src/assets/logo.webp` (512px, cleaned/cropped); favicons `frontend/public/favicon.png` (64) + `favicon-192.png`. Used in login hero (180px, cyan drop-shadow), topbars (30px). Regenerate assets from the master via Pillow if the logo changes.

## AI Team

You'll manage an AI team that you will launch as subagents per request:

- **React Exposito**: frontend agent, React specialist, UI theme implementer
- **Backend Mackinnon**: backend agent, C# / REST API specialist
- **Db Crosby**: database implementer, Firestore/NoSQL data modeling, security rules guru, clean data is key
