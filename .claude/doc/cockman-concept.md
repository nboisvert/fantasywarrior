# Garry Cockman & Cockcoin — concept doc

> Living document — keep appending as Nick brainstorms more of this. Not a
> spec to build against blindly; check with Nick before turning a new idea
> below into actual code, same as any other feature.
>
> Started 2026-07-27. Status: **UI mock skeleton built** (see
> `project_status.md`'s 2026-07-27 "Garry Cockman" entry for what's actually
> implemented) — no backend, no real AI, no real token system yet. Everything
> below the "Implemented so far" line is idea/concept only, not built.

## The pitch

**Garry Cockman** is a parody AI-mascot chatbot: "President" of {poolname}
(the user's league), produced and sponsored by Fantasy Warrior. A joke on the
real NHL commissioner (the avatar is an AI-generated cartoon caricature —
navy suit, red tie, corporate-office background — deliberately reads as a
"commissioner" type). He's reachable from the commissioner-only area of
Settings, and pops up a chat styled like a real corporate helpdesk/support
widget (Intercom/Zendesk/Drift-style) — clean, but deliberately clashing with
the rest of the app's dark "Night Arena" look, so it reads as an embedded
3rd-party widget bolted onto the app, not a native screen.

## Cockcoin — the token concept

- **Cockcoin** is Fantasy Warrior's in-universe, entirely fictional token
  economy. Presented completely straight-faced by Cockman (in on the joke:
  "very real-sounding, very fake").
- Has its own icon: a glossy, saturated, "Candy Crush"-style gold coin —
  deliberately breaks the app's flat-stroke Lucide-icon convention on
  purpose, for comic/visual contrast. Shown inline next to every mention of
  the word "cockcoin."
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

## UI/UX direction (established, 2026-07-27)

- Trigger: a single button in Settings' commissioner-only block ("Chat with
  Cockman"), no duplicate entry points.
- Chat surface: floating widget, docks bottom-right on desktop (not a
  full page modal/bottom-sheet) — the single biggest visual signal that this
  is "a widget," not "a screen."
- Palette/type/shape entirely its own — light corporate-SaaS look (white/
  light-grey surfaces, one confident brand blue, system-UI font, flat/no
  blur), nothing shared with the app's cyan-glass Night Arena tokens.
- Still respects the app's underlying accessibility rules even though it
  breaks every visual-theme rule: real SVG icons (no emojis), 44px touch
  targets, visible focus rings, `aria-label`s, `prefers-reduced-motion`.
- Everything scripted/local-state only — no backend calls anywhere yet.

## Implemented so far (2026-07-27)

- `frontend/src/components/CockmanChat.tsx` + `.css` — the modal itself.
- `frontend/src/components/Icons.tsx` — `CockcoinIcon`.
- `frontend/src/screens/Settings.tsx` — the trigger button.
- `frontend/src/assets/cockman.png` — the avatar.
- Scripted flow: self-intro → what cockcoin is → "you have a respectable
  amount" joke → how cockcoin is earned/what it unlocks → first bonus-entry
  question (random pooler, three-word description) → one canned "logged"
  reward reply on the user's first response, generic deflection replies after
  that.

## Open / not yet decided

- What "exclusive content" concretely is.
- Whether cockcoin ever becomes a real, persisted, backend-tracked balance,
  or stays a permanent in-joke with no real mechanic behind it.
- Further bonus-entry prompt types beyond the first one.
- Whether Cockman ever says anything league-specific/dynamic beyond the
  league name and a random pooler's name (e.g. reacting to standings, recent
  trades, etc.) — nothing like that exists yet, would need real data wiring
  if it happens.
