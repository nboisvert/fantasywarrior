# Garry Cockman & Cockcoin — concept doc

> Living document — keep appending as Nick brainstorms more of this. Not a
> spec to build against blindly; check with Nick before turning a new idea
> below into actual code, same as any other feature.
>
> Started 2026-07-27. Status: **cockcoin is now a real, persisted balance**
> (2026-08-03) — the chat/mascot half is still UI mock only, no real AI. See
> `project_status.md`'s entries for what's actually implemented. Everything
> below the "Implemented so far" line is idea/concept only, not built.

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
  is ever user-supplied). First and only one so far: `TradeVote` = 2 cockcoin,
  awarded server-side in the same transaction as the vote itself
  (`TradeEndpoints.cs`'s vote handler), never a separate client-triggered call.
- **Display**: the balance shows in `ProfileMenu`'s panel header (top-right,
  fetched lazily on open) as "N cockcoin" next to the `CockcoinIcon`.
- **The "wow" moment**: `CockcoinReward` (`frontend\src\components\CockcoinReward.tsx`)
  is a generic floating "+N cockcoin" pop — scale-bounce in, drift up, fade
  out, ~1.5s, mobile-game style. `TradeVoteWidget` is the first (only) caller;
  meant to be reused by every future earning action below.

## Open / not yet decided

- What "exclusive content" concretely is.
- Further bonus-entry prompt types beyond the first one, and whether/how each
  one becomes a real `CockcoinReasons` entry with its own award amount.
- No retroactive backfill: votes cast before this shipped earned nothing —
  forward-looking only, per Nick.
- Whether Cockman ever says anything league-specific/dynamic beyond the
  league name and a random pooler's name (e.g. reacting to standings, recent
  trades, etc.) — nothing like that exists yet, would need real data wiring
  if it happens.
