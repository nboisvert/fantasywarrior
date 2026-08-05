# Call Your Shot

> Before the Monday lock, every GM names the one *other* team he thinks banks the
> most points this week — and the moment it locks, everybody sees who nobody
> picked.

## Why this hooks them

Conviction, and the sting of being nobody's pick. Not regret, not vindication,
not the hunt for an edge: the specific discomfort of having to state an opinion in
public, in writing, with your name on it, before the outcome is known.

The moment fires at 00:01 Monday. Thirteen people were each asked to name the
best team in the league this week, they were not allowed to say themselves, and
the distribution goes public. A GM in fourth place discovers nobody named him.
A GM in ninth discovers four people did. That is a compliment and an insult
nobody had to write, produced entirely as a by-product of a mechanic whose stated
purpose is a joke currency.

**The by-product is the feature.** Fourteen forced choices a week, with self-votes
banned, is a crowd-sourced weekly power ranking of the league — the thing every
pool argues about endlessly and nobody ever writes down. It costs nothing to
produce because the GMs produce it, and it is the only number in this app that is
made of opinions rather than measurements, which is precisely why it will be
argued about differently from everything else here.

It also gives cockcoin a reason to exist. The ledger currently pays for one thing
— voting on a trade — and there is nothing to spend it on. This makes the balance
a season-long record of having called it right, which is a better prize than
anything a shop could sell.

## The data behind it

**This is the only idea in this folder that needs a migration**, and I would
rather say so in the first line than bury it.

| Source | Status |
|---|---|
| **`WeeklyCalls`** — PK `(LeagueId, PeriodId, UserId)`, `PickedTeamId`, `SubmittedUtc` | **New table. Does not exist.** |
| **`CockcoinAwards.PeriodId`** — nullable, plus a filtered unique index | **New column. Does not exist.** See below. |
| `vTeamPeriodScores` | Exists — the settlement, unchanged |
| `CockcoinAwards`, `vCockcoinBalance` | Exist — the payout and the ranking |
| `Periods.LockUtc`, `.FinalizedUtc`, `.GameCount` | Exist — both deadlines and the dead-week rule |

The mechanic, completely specified:

- **Opens** when the previous week banks. **Closes at `Period.LockUtc`** — the
  same instant the lineup locks. One deadline in this app, not two; a second
  deadline on a different clock is how a feature starts being missed.
- **One pick per GM per week**: which team banks the most *active* points that
  period.
- **You may not pick your own team.** This is the load-bearing rule and every
  other property of the feature depends on it — see the rules section.
- **Settlement** runs inside `PeriodRollupJob`, at the rollover that banks the
  week, reading the same `vTeamPeriodScores` rows that produced the standings. It
  therefore cannot disagree with the standings, ever, because it is the same
  numbers a millisecond later.
- **Payout**: 10 cockcoin for a correct call, **plus 5 if fewer than three people
  made the same call**. The contrarian bonus is not decoration — without it,
  fourteen people pick the obvious juggernaut every week, everyone collects 10,
  and the balance never separates anybody from anybody.

**Why `CockcoinAwards` needs a `PeriodId`.** The nightly job is rerunnable by
design (`nightly --backfill-from N`), and a currency that doubles on a rerun is a
bug discovered weeks later and impossible to undo cleanly. `Reason` is free text
and documented as "a short, stable code" — encoding a period into it would break
that contract. One nullable `int` column and one filtered unique index on
`(UserId, Reason, PeriodId) WHERE PeriodId IS NOT NULL` makes the award
idempotent and leaves every existing `trade-vote` row untouched.

There is no query here worth writing out: the winner is `MAX(ActivePoints)` over
fourteen rows.

## What it looks like

**GM Office (Dashboard)**, one card. It is a weekly action with a deadline, which
is what that screen is for, and it is the only card there that ever asks the GM
for something rather than telling him something.

Title `Call your shot`, `TargetIcon` at 16px — **a new icon**, Lucide `target`,
added to `Icons.tsx`. Deliberately not `TrophyIcon`, which already means the
standings leader, and not `CircleCheckIcon`, which already means "active in the
lineup".

**Three states, one card.**

**Open** (previous week banked → `LockUtc`): thirteen rows, your own team absent.

**Row convention, decided here so Macklin has an answer to confirm rather than a
question to ask**: **one line**, **truncated team name**, **the far right is the
number of times that GM has been picked this season**. One line so thirteen rows
fit on a phone without scrolling, which is what makes this a five-second action
rather than a chore. The season count on the right is what quietly turns the
picker itself into the standing power ranking — you cannot choose without seeing
who the league has been believing in.

Tapping a row selects it (44px target, `CircleCheckIcon` on the selection, ice
cyan). A countdown to lock sits in the card header, muted.

**Locked** (`LockUtc` → banked): the same thirteen rows, re-sorted by picks
received *this week*, each with a thin ice-cyan bar behind it proportional to the
count, the count on the far right replacing the season figure. Your pick keeps its
check. A team with zero picks gets no bar and its count renders `0` in `--muted`,
never blank — blank looks like missing data and this one is the point.

**Settled**: one strip replaces the card body. `You called Rochette. Rochette
won. +15 cc` in `--success` `#4ade80`, or `You called Rochette. Lachance won.` in
`--muted`. **Never rose** — being wrong about a prediction is not danger, and
rose in this app means "do not do this".

## The rules

- **You cannot pick your own team.** If a GM could, thirteen of fourteen picks
  would be self-picks, the distribution would be noise, and "nobody picked you"
  could never happen. Thirteen options, enforced server-side, and the API rejects
  a self-pick rather than silently dropping it.
- **The deadline is `Period.LockUtc`**, to the second, shared with the lineup.
  A pick submitted after it is rejected with the reason, in an error banner near
  the action.
- **Picks are private until lock, public after** — the same shape as the trade
  visibility rule (2026-07-23). **With no authentication this privacy is a
  courtesy, not a guarantee**: anyone who knows a username can read another GM's
  pick through the API before the lock, exactly as they can read his DMs
  (2026-08-03 open item). That is a reason to keep the stake at cockcoins and
  pride forever, and never at anything that touches score.
- **A dead week** (`Periods.GameCount = 0`) opens no call at all. A week where
  fourteen teams score zero has fourteen winners and no meaning.
- **A tie for the week's top team**: everybody who called any tied team is paid.
  Ties on a season-long float sum are near-impossible; the rule exists so the job
  never has to choose.
- **A GM who misses the deadline gets nothing — no pick, no award, no penalty,
  and no auto-pick.** Auto-filling a lineup is correct because a missing lineup
  costs *score*, and scoring-model.md says a GM on vacation is not punished in
  score. Auto-filling an *opinion* puts words in his mouth, which is the one thing
  this feature must never do.
- **A GM who joins mid-season** calls from his first unlocked week. No catch-up,
  no starting balance, and his "picked this season" count honestly reads 0.
- **Minimum league size: 8 teams.** Below that a GM is choosing among six or
  fewer and the contrarian bonus is unreachable; the card does not render at all.
  This is the one idea in the folder that genuinely needs a crowd, and fourteen is
  comfortably enough while four is not.
- **Idempotence**: settlement is guarded by the filtered unique index above, so
  `nightly --backfill-from 3` pays nobody twice. This must be tested, not assumed.
- **Banking**: reads finalized `vTeamPeriodScores` rows and writes only to
  `CockcoinAwards`. It cannot touch a `RosterAssignment`, a `FantasyPoints` or a
  `FinalizedUtc`, and no cockcoin balance may ever affect the standings.
- **Week 1**: works immediately. Every "picked this season" count is 0, which is
  honest rather than empty.
- **Interaction with `four-game-weeks`**: if that ships, a GM who reads the
  schedule column makes better calls. That is a feature, not a leak — the app
  still never computes the answer, which is exactly why that file rejected a
  "best week ahead" leaderboard. A human prediction informed by public facts is
  the whole game; a machine prediction would end it.

## What it costs

**The most expensive of the five, and the only one with a migration.** One new
table (`WeeklyCalls`, four columns), one nullable column and one filtered unique
index on `CockcoinAwards`. Two endpoints: `POST` a call, rejected after
`LockUtc` and on a self-pick; and the week's state, which folds into the dashboard
payload the GM Office already fetches. Settlement is roughly thirty lines inside
`PeriodRollupJob`, at the point where it already knows which period just
finalized and already holds the scores. One new Lucide icon. Frontend: one card
with three states, which is more UI than any other idea in this folder needs. The
pure logic worth mocking-free tests: the award calculator — winner selection, tie
handling, the contrarian threshold at exactly three, the self-pick rejection, and
the dead-week skip.

## What I rejected

- **A shop.** There is nothing this league wants to buy that is not either
  cosmetic — which is exactly the gamification gloss this project is not supposed
  to be about — or rule-bending, which corrupts a keeper pool people take
  seriously. A permanent record of having called it right is a better prize than a
  hat, and it costs nothing to build.
- **Cockcoin as a trade asset or side payment.** Native, tempting, and fatal: a
  currency with no cap hit inside a hard-cap keeper league is a hole in the one
  constraint that makes the league a game. It would also put a farmable price on
  the trade vote, which is the last thing that vote needs.
- **Letting a GM pick himself.** Destroys the only interesting output. Everything
  good about this feature is downstream of the ban.
- **Multiple questions a week, or prop bets on individual players.** More
  questions, thinner answers, and every one needs its own settlement rule and its
  own edge cases. One forced question a week is a habit; five is a chore, and the
  fifth would be the one that breaks in February.
- **Penalising a GM who does not call.** The vacation principle from
  scoring-model.md, extended: nothing that is not score may punish absence.
- **Auto-picking for the silent**, even "carry forward last week's pick". It
  fabricates an opinion, and the whole social object is that these are things
  people actually said.
- **Paying cockcoin for accurate trade votes instead of building this.** I
  rejected a vote-accuracy *rating* in `who-won-the-trade.md` on sample size —
  three or four decided trades a season per GM is noise wearing a suit — and
  converting it to a payout does not fix the sample, it hides it inside a total.
  The weekly call produces twenty-eight events a GM a season instead of three,
  which is the entire reason this is the version that works.
- **Letting the result touch the standings in any way**, including a tiebreak.
  Immediately fatal, and it would make the no-authentication problem a scoring
  problem.
- **Making it a bottom-nav destination.** It is a five-second weekly action. It
  belongs on the screen a GM already opens first.
