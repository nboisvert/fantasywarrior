# Design system — "Night Arena"

> The **rules** you follow without being reminded live in `CLAUDE.md` (dark
> theme only, Lucide icons never emojis, 44px targets, no duplicate
> destinations, the player-row ask, the two position-indicator patterns). This
> is the detail you look up: exact values, class names, procedures.

Tokens: `frontend/src/index.css` · components: `frontend/src/App.css` ·
screens: `frontend/src/screens/` · icons: `frontend/src/components/Icons.tsx`.

## Colours

| Role | Token | Value |
|---|---|---|
| Background | `--bg` | `#0a0e1a`, with fixed cyan/indigo radial halos on `body` |
| Elevated surface | `--bg-elevated` | `#10162a` |
| Glass card | `--surface` / `--border` | `rgba(255,255,255,.045)` + 1px `rgba(255,255,255,.09)` + backdrop-blur |
| Accent gradient | `--ice` → `--ice-bright` | `#38bdf8` → `#22d3ee` |
| Neon glow | `--ice-glow` | `rgba(56,189,248,.35)` |
| Danger / over cap | `--danger` | `#f43f5e` |
| Success, presence | `--success`, `--online` | `#4ade80` — one green under two names, so the two uses can diverge later without a rename |
| Standings podium | `--gold` / `--silver` / `--bronze` | `#fbbf24` / `#c7d2e0` / `#d0885a` |
| Text | `--text` / `--text-muted` | `#f1f5f9` / `#8b96ab` |

AA contrast minimum, everywhere. **Positions**: forward = ice cyan ·
defenseman = violet `#a78bfa` (`--defense`, chosen over silver, too
low-contrast) · goalie = gold.

**Team slot** (`T`, the NHL franchise a GM owns) = the palette's neutral
`#c7d2e0` (`--franchise`). Deliberately **not** a fourth saturated hue: F/D/G
are positions sharing a colour code, and a franchise is not one of them. Every
saturated colour left already means something — green is "online", rose is
"danger", orange collides with gold — so a fourth would read as a status rather
than a kind. Both mandatory patterns exist: `.roster-pos-pill-t`, `.pos-compact-t`.

## Typography, shape, motion

**Russo One** for display (headings, team names, figures, uppercase),
**Chakra Petch** for body. Google Fonts, loaded in `index.html`. Radii 12–16px.
Transitions 150–300ms and **only** on `color`/`opacity`/`filter` — never
anything that moves layout on hover. `.fade-in` 0.25s on screen mount.
`prefers-reduced-motion` respected.

## Layout

Mobile-first, content capped at 680px. Fixed bottom nav at `--nav-height`
(64px) + `env(safe-area-inset-bottom)`, news ticker (`--ticker-height`, 44px)
directly above it; content bottom padding clears both. **Four tabs**: GM Office
(default, internal key `dashboard`), Standings, Team (key `stats`), Trades. A
fifth **Draft** tab appears only while a draft is running, plus the
commissioner's preview in Protecting — a permanent tab leading to "no draft is
running" would be a dead destination in an already full bar. Settings lives in
a top-bar icon button, not in the nav.

Sticky blurred top bar, in order: logo (`.topbar-logo`, 40px), league switcher,
week badge, then **pinned right** the messaging icon and the profile menu. The
switcher carries the league name alone, no season — it does not change week to
week and did not pay for its width; Settings shows it where it is read. The
week badge does earn its width and is a `<span>`, not a button: a status, not a
destination. The messaging icon is **bare**, no pill background — the profile
pill is right beside it, and two pills side by side read as a segmented control
rather than as an identity and an action; its unread badge is `--ice`, since
gold stays reserved for "an offer is waiting". The profile's green counter
counts **others only** (`otherMembers` excludes the viewer): including yourself
made a badge that never dropped below 1, so "1 online" and "nobody" looked
alike. In the menu the GM list comes before Settings/Log out, a compact footer
(36px, under 44px, accepted).

## Dashboard leaderboards

The two leaderboard cards show **NHL numbers, not fantasy scores**: points for
a skater, wins for a goalie (`nhlHeadline`, `screens/Dashboard.tsx`). The
fantasy score stays the *ranking* key — the only thing that can compare a
goalie with a winger — but the figure on the card is the one a GM already knows
from a box score. The unit stays welded to the figure ("9 W") with the window
stacked underneath: a goalie's bare 9 above "last 2 weeks" reads as nine
points. The raw line below (`rawLine`) is `GP · G · A` for skaters,
`GP · W-OTL · SV` for goalies, because 0G 0A is true and useless. **Top
Reserve** sums the last two weeks; **Top Free Agents** covers the whole season
to date, because a claim is a season-long bet, not a reaction to one good
Saturday.

## Team grid (`screens/Stats.tsx`)

One stat block per swipe: the twenty numeric columns scroll horizontally under
the sticky name column and rest **block by block**, never straddling two
categories (Fantasy point, Record, NHL, Extra, Cap hit). Pure CSS —
`scroll-snap-type: x mandatory` on `.stats-grid-scroll` guarantees the resting
point, `scroll-snap-stop: always` on `.stats-group-start` makes one gesture
advance exactly one block. No new markup: `.stats-group-start` already marked
each group's first column in the header, every row and the footer.

Roster, Departed and Incoming are three separate `<table>`s sharing one column
width: `useSharedPlayerColumnWidth`, mounted on the screen's root `<section>`
(not on a grid), measures the widest name cell across all three and writes
`--stats-player-w`. Departed and Incoming have no active/bench toggle, so
`.stats-toggle-spacer` (an empty box the size of `.lineup-toggle`) keeps the
row structure identical everywhere.

**An unavailable player is marked twice, and neither mark costs the row a
column**: a rose edge on the sticky identity cell plus a badge right after the
name. The badge (`.stats-injury`) sits **in the flow**, not absolutely
positioned, so `.stats-player-name` ellipsizes to make room — the mark matters
more than the last letters of a surname. The edge is an `inset` box-shadow on
`.stats-row-out .stats-col-player`, not a border on the row, so it rides with
the sticky cell instead of scrolling out of view. Injured and suspended share
the rose but never the symbol — a gavel, not a cross — and the glyph, `title`
and aria-label carry the meaning, so colour is never the only signal.

**Prospects** — players with no NHL career games (rule and provenance in
[data-model.md](data-model.md)) are pinned **below** the roster in a fixed
**F → D → G, then name** order and **excluded from sorting**: a zone that
always keeps the same shape is the only one a border can honestly delimit. It
opens with a dedicated row — "PROSPECTS" at the ticker "Trade Alert" strip's
proportions (16px, tiny bold uppercase, wide letter-spacing) so both read as
the same *kind* of mark, but muted, not its colours: gold already means
"trade", ice already means "headline figure" on the PT column nearby.

**Position filter** — All/F/D/G as a segmented pill, right-aligned against the
ROSTER title (`.stats-table-head`, a flex `space-between` row).
`PositionFilterControl` is exported from `Stats.tsx` and reused by the draft
room. One control for the whole screen: `positionFilter` flows into all three
`RosterGrid`s, `onPositionFilterChange` only into the first — its presence is
what decides whether an instance draws the buttons. The "26 / 35 player"
subtitle ignores it: that is the real roster size. The active button borrows
the filtered position's colour (the same ice/violet/gold as
`.roster-pos-pill`/`.pos-compact`); "All" takes the ordinary ice "selected"
treatment. A grid emptied **by the filter** says `positionFilterEmptyLabel`
("No defensemen here.") rather than the generic message ("Nobody has left this
roster."), which would stay correct but lie by omission.

### Traps in the stats grid

- **`scroll-padding-left` must equal the sticky name column's width**, or a
  snapped block lands *under* it and its first column goes off screen. A
  hardcoded value is wrong — the width is content-driven (long name, injury
  marker, franchise logo) — so it reuses `var(--stats-player-w, 9.5rem)`, where
  the 9.5rem is only a floor.
- **Under `mandatory` you can only rest at a block start**, so a group wider
  than the visible area (screen minus the name column) has an unreachable last
  column. `Extra` (+/-, PIM, SOG, GAA, SV%) is the widest and the one to watch
  if a column is added. The fallback is one word: `mandatory` → `proximity`.
- **`table-layout: auto` gives each grid its own column width** from its own
  longest name, so `.stats-col-player` needs a real `width: var(--stats-player-w)`,
  not just `min-width`, or nothing forces the three tables to agree. The
  variable being *absent* (invalid `var()` → initial `auto`, `min-width`
  governs) is the unconstrained state the hook needs in order to measure.
- **`.stats-col-player` must never carry `display: flex`.** A flexed `<td>`
  drops out of the table's column-tracking algorithm and each cell then rounds
  its own height independently under `border-collapse` — a sub-pixel border
  misalignment, invisible at 100% and obvious at zoom. Flex layout goes on an
  inner `<div className="stats-col-player-inner">`.
- **`position: sticky` on a `<span>` nested inside a `colSpan` cell fails
  silently** — it renders at its static flow position. Reuse
  `.stats-col-player` (the prospect label row does); never hand-roll a second
  sticky mechanism in the same table.
- **Specificity, twice.** `.pos-compact-d/-g` carry only `color` at (0,1,0)
  while `.stats-position-filter-btn.active` claims it at (0,2,0), so the
  combined `.active.pos-compact-d/-g` rule must restate `color` at (0,3,0) or
  ice wins the colour it is meant to yield. Same trap on the prospect label
  row: `border-top: none` must out-specify the generic separator
  `.stats-grid tbody tr + tr td`, and `padding-left` must be written at three
  classes to beat the row rule's own `padding: 0`.

## Player card — the `AUTO` protection pill

Its own class `.pc-protect-pill`, deliberately **not** `.roster-pos-pill`: that
pattern is reserved for the F/D/G indicator, and a second pill of the same
shape on the same row saying something else is exactly what the rule exists to
prevent. Ice-cyan, because rose on this card is spoken for by the injury mark
and the two must never be confused at a glance. `margin-left: auto` pins it
right and `flex-shrink: 0` makes `.pc-team` ellipsize to make room — the same
call as the injury badge. **Nothing is drawn when the answer is unknown**:
`autoProtected` is `boolean | null`, null when career-sync has never reached
the player, and the card renders on `=== true` only. An `AUTO` badge on a
veteran whose sync failed would be a false statement about a real person; no
badge is merely a gap.

## Messaging

DMs live in a **full-screen sheet**, prefix `chat-`
(`components/ChatSheet.css`), and **inside** the theme — unlike
`CockmanChat.css`, self-contained and clashing on purpose because it plays a
third-party widget bolted onto the app. One sheet, two swapping views,
list ↔ thread, the same shape as `ProfileMenu`'s two panels. Two doors, two
distinct destinations, so no-duplicate-destinations holds: the top-bar bubble
opens the **list**, a GM row's message button in `ProfileMenu` opens **one
thread**. Conversation row: avatar with presence dot, name, time right; second
line the last-message preview (prefixed "You: " when ours) and the unread
badge. Live event pop-ups are prefixed `toast-` and sit **above** the news
ticker, never over it.

## Logo

Circular crest: bearded warrior, red helmet, crossed sticks. Master
`fw_logo.png` at the repo root (1024px, transparent); app asset
`frontend/src/assets/logo.webp` (512px, cleaned and cropped). Used in the login
hero (`.hero-logo`, 180px, cyan drop shadow) and the top bar (40px).

## Home-screen and PWA icons

`frontend/public/manifest.json` (linked from `index.html`; `name`/`short_name`
"Fantasy Warrior", `display: standalone`, `background_color` and `theme_color`
`#0a0e1a`) is what makes "Add to Home Screen" install a real app icon instead
of a browser shortcut in a white box. All assets are regenerated from
`logo.webp` with Pillow — crop to the content's bounding box, centre, resize.
**If the logo changes, regenerate them the same way.**

| File | Size | Background | Role |
|---|---|---|---|
| `favicon.png` | 64 | transparent | browser tab only |
| `favicon-192.png` | 192 | transparent | manifest, `purpose: "any"` |
| `favicon-512.png` | 512 | transparent | manifest, `purpose: "any"` |
| `maskable-icon.png` | 512 | **`#0a0e1a` opaque** | manifest, `purpose: "maskable"` |
| `apple-touch-icon.png` | 180 | **`#0a0e1a` opaque** | iOS |

The last two **must never be transparent or white**. On `maskable-icon.png` the
logo is scaled to ~72% to survive Android's adaptive-icon safe-zone crop; a
transparent background is exactly what produced the old "white circle with a
Chrome badge" rendering. iOS does not composite alpha reliably on touch icons,
hence the opaque background there too. `index.html` also carries
`apple-mobile-web-app-capable`, `-status-bar-style` and `-title` for a clean
standalone launch on iOS.
