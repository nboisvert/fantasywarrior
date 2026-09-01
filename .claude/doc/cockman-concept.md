# Garry Cockman & Cockcoin — concept doc

> Living document — keep appending as Nick brainstorms more of this. Not a
> spec to build against blindly; check with Nick before turning a new idea
> below into actual code, same as any other feature.
>
> Started 2026-07-27. Status: cockcoin is a real, persisted balance with
> several earning paths and a scheduled-notification system (Cockman
> campaigns) — see "Earning paths and campaigns" below. The chat/mascot
> widget (`CockmanChat.tsx`) is still UI mock only, no real AI. See
> `project_status.md`'s entries for what's actually implemented. Only the
> "Open / not yet decided" section at the bottom is idea/concept, not built.

## The pitch

**Garry Cockman** is a parody AI-mascot chatbot: "President" of {poolname}
(the user's league), produced and sponsored by Fantasy Warrior. A joke on the
real NHL commissioner (the avatar is an AI-generated cartoon caricature —
navy suit, red tie, corporate-office background — deliberately reads as a
"commissioner" type). He's reachable from the commissioner-only area of
Settings, and pops up in the app's own dark "Night Arena" look — the
originally-planned "corporate helpdesk widget that clashes on purpose" was
reversed (2026-08-30, per Nick): he now reads as a native screen, not a
3rd-party widget, with a gold rule marking his lines and an ice one marking
the GM's own replies instead of the twin-bubble chat shape `ChatSheet` owns.

Every GM also sees a quick, standalone explainer — a small info trigger next
to the cockcoin balance in `ProfileMenu`, popping the same small-card style
`TradeRatingInfo` already uses, quoting Cockman rather than the full chat
(which stays commissioner-only).

## Cockcoin — the token concept

- **Cockcoin** is Fantasy Warrior's in-universe, entirely fictional token
  economy. Presented completely straight-faced by Cockman (in on the joke:
  "very real-sounding, very fake").
- Has its own icon: a glossy gold coin embossed with Garry Cockman's own
  face (helmet, visor, beard, crossed sticks) — deliberately breaks the
  app's flat-stroke Lucide-icon convention on purpose, for comic/visual
  contrast. Shown inline next to every mention of the word "cockcoin."
  Real artwork now (2026-08-03) — `frontend/src/assets/cockcoin.png`,
  rendered by `CockcoinIcon` (`Icons.tsx`) clipped to a circle in CSS
  regardless of the source PNG's own edge (a faint rendering-artifact halo
  sits right at its rim in the raw asset).
- **Currency symbol: `CK`** (2026-08-03) — shown right after the amount
  ("42 CK"), with the word "cockcoin" trailing/below as the quieter unit
  label. Both the bank display (`ProfileMenu`) and the reward pop
  (`CockcoinReward`) follow this amount → CK → "cockcoin" hierarchy.
- **How it's earned**: cockcoin tracks toward the user's *interaction within
  the app* — the more you use/engage with the app, the more you accrue.
- **What it unlocks**: access to *exclusive content within the app* once a
  user has built up enough of a balance. (Exactly what "exclusive content"
  means — a cosmetic, a feature unlock, a Cockman-only chat topic, something
  else — is still open; nothing beyond the phrase itself has been decided.)
- **Bonus entries**: a gamified way to earn cockcoin faster than passive
  interaction — quick prompts/questions Cockman asks that reward a fast
  answer. First one built: **"Describe {a random fellow pooler} in three
  words."** (pooler picked at random from the league's actual members). The
  idea is this becomes a small *library* of bonus-entry prompt types over
  time, not just this one — more should be added as they're thought up.

## UI/UX direction (established 2026-07-27, palette reversed 2026-08-30)

- Trigger: a single button in Settings' commissioner-only block ("Chat with
  Cockman"), no duplicate entry points — plus the standalone cockcoin-info
  popup in `ProfileMenu`, reachable by every GM, not just the commissioner.
- Chat surface: floating widget, docks bottom-right on desktop (not a
  full page modal/bottom-sheet) — that part of the "it's a widget, not a
  screen" framing stayed.
- Palette/type/shape read from `index.css`'s own Night Arena tokens now —
  gold marks Cockman's lines, ice marks the GM's own replies, `--font-display`
  for his name. The original light-corporate-SaaS skin (white surfaces, one
  brand blue, system-UI font) was the deliberate "clashing 3rd-party widget"
  bit; Nick reversed that call, so nothing here fights the rest of the app's
  look anymore.
- Respects the app's underlying accessibility rules: real SVG icons (no
  emojis), 44px touch targets, visible focus rings, `aria-label`s,
  `prefers-reduced-motion`.
- Everything scripted/local-state only — no backend calls anywhere yet.

## Implemented so far (2026-07-27)

- `frontend/src/components/CockmanChat.tsx` + `.css` — the modal itself.
- `frontend/src/components/CockcoinInfo.tsx` + `.css` — the standalone
  cockcoin explainer popup, next to the balance in `ProfileMenu`.
- `frontend/src/components/Icons.tsx` — `CockcoinIcon`.
- `frontend/src/screens/Settings.tsx` — the trigger button.
- `frontend/src/assets/cockman.png` — the avatar.
- Scripted flow: self-intro → what cockcoin is → "you have a respectable
  amount" joke → how cockcoin is earned/what it unlocks → first bonus-entry
  question (random pooler, three-word description) → one canned "logged"
  reward reply on the user's first response, generic deflection replies after
  that.

## Cockcoin — now a real balance (2026-08-03)

- **Model**: `CockcoinAward` (`backend\FantasyWarrior.Data\Entities\CockcoinAward.cs`)
  is a ledger — one row per earning event (UserId, Amount, Reason, AwardedUtc),
  never a mutable running total. `vCockcoinBalance`
  (`backend\FantasyWarrior.Data\Migrations\20260803014914_CockcoinBalanceView.cs`)
  is a `SUM(Amount) GROUP BY UserId` view, same "recompute on read" pattern as
  `vStandings`/`vPoolerTradeRecord` — no row at all for a user who's never
  earned any; `GET /api/users/{username}/cockcoin` is what turns that into a
  displayed 0 ("everyone starts at 0").
- **Reasons** live in `FantasyWarrior.Core.Cockcoin.CockcoinReasons` (a plain
  set of constants, not a validated whitelist like `StatKeys` — nothing here
  is ever user-supplied). `TradeVote` = 2 cockcoin, awarded server-side in the
  same transaction as the vote itself (`TradeEndpoints.cs`'s vote handler),
  never a separate client-triggered call. See "Earning paths" below for the
  rest.
- **Display**: the balance shows in `ProfileMenu`'s panel header (top-right,
  fetched lazily on open) as "N cockcoin" next to the `CockcoinIcon`.
- **The "wow" moment**: `CockcoinReward` (`frontend\src\components\CockcoinReward.tsx`)
  is a generic floating "+N cockcoin" pop — scale-bounce in, drift up, fade
  out, 3s, mobile-game style — with an optional second, quieter line naming
  *why* it fired (2026-09-01, per Nick: "involve the user"), passed
  pre-translated by the caller (the component looks nothing up itself, since
  every call site already knows its own reason). Every synchronous earning
  action shows it inline over wherever the action happened: `TradeVoteWidget`
  (over the widget), `ChatSheet` (over the composer), `Trades` (over the
  header, since `CreateTradeSheet` closes before a 3s animation could ever
  finish, so the reward is lifted to the parent screen instead of shown
  inside the closing sheet). The done-deal bonus — the one earning path with
  no live action to anchor over, landing overnight while the GM is offline —
  gets its own full-screen celebration instead; see below.

## Earning paths and campaigns — now real (2026-08-31)

- **Fibonacci milestones**: chat messages and trade offers both earn cockcoin
  on the same curve — `FantasyWarrior.Core.Cockcoin.FibonacciMilestones`
  (pure, unit-tested) rewards a running count landing on a Fibonacci number
  (1, 2, 3, 5, 8, ...): 5 CK the first time, 2 CK every milestone after. No
  cap — the check keeps working at any count, milestones just get rarer.
  - **Chat** (`CockcoinReasons.ChatMessageMilestone`): the count is per "room"
    — the (LeagueId, SenderUserId, RecipientUserId) tuple `Messages` is
    already scoped to — so chatting with a new fellow GM starts its own curve.
    Only the sender earns. Checked in `MessageEndpoints.cs`'s send handler,
    right after the message itself is saved.
  - **Trade offers** (`CockcoinReasons.TradeOfferMilestone`): same curve,
    scoped per (LeagueId, ProposerTeamId, CounterpartyTeamId) — a new curve
    per opponent, mirroring chat rooms. The proposer earns it on send,
    checked in `TradeEndpoints.cs`'s propose handler.
  - **Accepting** (`CockcoinReasons.TradeOfferAccepted`, 2026-09-01, per
    Nick — "click should pop the same as vote"): the symmetric milestone for
    the *counterparty*, same (proposer, counterparty) pairing and curve,
    counting trades this counterparty has accepted from this specific
    proposer (`Status == Accepted || Status == Processed`). Checked in
    `TradeEndpoints.cs`'s respond/accept handler, after the roster swap's own
    `SaveChangesAsync` so the count includes this trade's own new row.
- **Done deal bonus** (`CockcoinReasons.DoneDeal`, flat 10 CK): both GMs earn
  it the moment their trade reaches `TradeStatus.Processed` — awarded in
  `WeekAheadJob.cs`'s nightly landing loop, not tied to the Fibonacci curve.
  No live connection exists to push this to (the Jobs project doesn't
  reference the API project or `IHubContext<LiveHub>`, and the GM is offline
  at 09:30 UTC anyway), so it needs its own surfacing mechanism to get a "wow"
  moment at all: `CockcoinAward` carries a nullable `AcknowledgedUtc`, and
  `GET /api/users/{username}/cockcoin/pending-reward` sums every
  unacknowledged `done-deal` award for that user (several trades landing the
  same night become one pop, not a queue — they carry no per-item content
  worth separating) while `POST .../pending-reward/ack` marks all of them
  acknowledged in one write. `DoneDealRewardGate` (mirrors
  `CockmanCampaignGate`'s "fetch once per mount" shape, no league scope)
  shows `DoneDealRewardPopup` when something's pending — a full-screen
  celebration card (cloned modal mechanics from `CockmanCampaignPopup`)
  wrapping an unmodified `CockcoinReward`, scaled up via a `transform` on its
  *container* rather than the reward itself — the reward's own `transform` is
  fully owned by its keyframe animation for the whole 3s run, so overriding
  it directly gets clobbered the instant the animation starts.
- **Cockman campaigns** — the generalized shape the "library of bonus-entry
  prompt types" idea grew into: a scheduled message with an optional
  multiple-choice question and cockcoin reward, shown once per user while its
  window is open. `CockmanCampaign`/`CockmanCampaignView`
  (`backend\FantasyWarrior.Data\Entities\`) hold structure and scheduling
  only — a stable `Key`, whether there's a question, valid choice keys, the
  reward, `StartUtc`/`EndUtc` (null = forever) — the actual bilingual copy
  lives in the frontend's `cockmanCampaigns` i18n dictionary, keyed by `Key`,
  same convention as every other Cockman line. `CockmanCampaignView`'s mere
  existence for a (campaign, user) pair means "seen": that's the entire
  mechanism stopping a campaign from reappearing once dismissed or answered.
  `FantasyWarrior.Core.Cockman.CampaignSelection` (pure, unit-tested) decides
  which one campaign, if any, is due for a user right now — earliest active,
  unseen window wins, and a campaign whose window has already closed is
  simply never shown, which is what stops a brand-new user from being handed
  a backlog of every past campaign at once. No admin screen yet: a new
  campaign is a migration (`CockmanCampaignSeed.cs`, same pattern as
  `NhlTeamSeed.cs`) plus a dictionary entry.
  - **Welcome campaign** (`Key = "welcome"`): evergreen (no `EndUtc`),
    message-only (no question/reward), shown once to every user on first
    login. Three beats (2026-09-01, per Nick — reworked from the original
    one-liner, based on `CockmanChat`'s own scripted intro): an in-character
    opener naming the league (the same "President of {league}" line
    `CockmanChat` opens with), a stats line naming this GM's *actual* league
    (GM count, commissioner — `CockmanCampaignGate` takes a `LeagueDetail`
    now, not just a username, so the popup has real numbers to quote), and a
    call to action on Trades and the weekly lineup, with the jersey icon
    inlined into the CTA the same way `CockmanChat` inlines the cockcoin icon
    into its own copy (`CockmanCampaignPopup`'s `CtaText` splits on a
    language-neutral `%jersey%` token). Every campaign's dictionary entry is
    expected to supply `${key}Intro` / `${key}Stats` / `${key}Cta`.
  - `CockmanCampaignGate` (mounted in `App.tsx` next to `UnreadBridge`) fetches
    the due campaign once per session and renders `CockmanCampaignPopup` — a
    centered dialog (`CockcoinInfo`'s shell shape, not the docked chat
    widget), 56px gold-bordered avatar.

## Open / not yet decided

- What "exclusive content" concretely is.
- Whether/when campaigns need an admin screen to create — seeded in code for
  now, revisit if the backlog of ideas in this doc grows past what a
  migration-per-campaign can keep up with.
- No retroactive backfill: votes cast before this shipped earned nothing —
  forward-looking only, per Nick. Same posture for every earning path added
  since.
- Whether Cockman ever says anything league-specific/dynamic beyond the
  league name and a random pooler's name (e.g. reacting to standings, recent
  trades, etc.) — nothing like that exists yet, would need real data wiring
  if it happens.
