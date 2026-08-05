# Is He For Real?

> Wherever a player is being valued, two numbers sit under his name: the best
> season he has ever had, and what he is doing now — so the winger everyone wants
> is visibly 29 years old and 46% above anything he has ever managed.

## Why this hooks them

Suspicion. Not regret about a decision already made, not satisfaction about one
that worked — the specific unease of being about to buy something at the top.
In a keeper league that is the most expensive mistake available: a bad week costs
you a week, a career-year acquisition costs you three seasons of cap and a
roster spot you cannot get back.

The moment fires inside the trade sheet, in the two seconds before a GM taps
Accept. He knows the player is having a great year — that is why he wants him.
What he does not have in his head is that the guy has never cleared 0.78 a game
in six NHL seasons and is at 1.14 today. The number does not tell him what to do.
It makes him hesitate, and a hesitation is a phone call, and a phone call is a
negotiation.

It cuts both ways, which is why it will not feel like a nanny. The GM being
asked for the player sees the identical number and now knows exactly what he is
selling. Both sides arrive at the conversation armed, which is the only way an
argument between friends stays fun. And it produces the single most re-litigated
sentence in any keeper pool — *he's not a fluke, he changed lines* — which is a
sentence you can only say if someone has put a number in front of you first.

## The data behind it

Everything exists today. **Nothing here is missing from the schema, and no
migration is needed.**

| Source | What it gives |
|---|---|
| `PlayerCareerSeasonStats` | Season-by-season lines: `Season`, `LeagueAbbrev`, `GamesPlayed`, `Goals`, `Assists`, `Points`, and the goalie side `SavePctg`, `Wins`, `GoalsAgainstAvg` |
| `vPlayerSeasonStats` (or the sim-bounded equivalent) | What he is doing this season |
| `Players.BirthDate` | Age — the other half of the question |
| `PlayerContracts.YearsRemaining`, `.ClauseType` | How long you would be stuck with the answer |

**The Career Index**, for a skater:

```sql
-- baseline: his best prior NHL season, on a per-game basis
SELECT MAX(CAST(c.Points AS float) / NULLIF(c.GamesPlayed, 0))
FROM PlayerCareerSeasonStats c
WHERE c.PlayerId    = @playerId
  AND c.LeagueAbbrev = 'NHL'
  AND c.GameType     = 2
  AND c.Season      <  @currentSeason
  AND c.GamesPlayed >= 40
```

The index is `thisSeasonPpg / baselinePpg`, displayed as a signed percentage.
Per game, never per season, so a 56-game 2020-21 and an 82-game year are
comparable without a normalisation rule anybody has to remember.

**Three filters that are each load-bearing**, and each of which is a bug if
omitted:

- `LeagueAbbrev = 'NHL'` — `career-sync` deliberately stores junior, NCAA, KHL
  and AHL rows through the `NotableLeagues` whitelist. A 1.6-a-game QMJHL season
  as somebody's "career high" would make the feature a joke within one day.
- `Season < @currentSeason` — the current season has its own row in that table,
  and without this the player is compared to himself and every index is exactly
  100%.
- `GamesPlayed >= 40` — a nine-game call-up at 1.2 a game is not a career high.
  Forty is the floor at which a per-game rate stops being an anecdote.

**Goalies are a different question and get a different number.** Points per game
is meaningless for a goalie, and the stat this league actually pays him for —
wins — is mostly his team's doing. So the goalie index is **save percentage,
compared in save-percentage points, not as a ratio**: `.921 vs .906 (+.015)`.
That is the only figure on a goalie's line that is about the goalie. Say it in
the UI, once, and never mix the two scales.

**Immune to the one staleness risk in this area.** `career-sync` is not on the
nightly cron — it is run by hand and refreshes the most stale players first. That
would normally be a caveat, but it is not one here: the baseline comes only from
*completed prior* seasons, and those rows never change again. The only thing a
missed run costs is a brand-new player having no career rows at all, which the
rookie rule below already handles.

## What it looks like

Three surfaces, all existing, and **deliberately not the Team grid** — see the
rejections.

**1. `CreateTradeSheet` — the home of the feature.** Every player row in the
offer builder gains a second line.

**Player-row convention, decided here so Macklin has an answer to confirm rather
than a question to ask**: **two lines**, **truncated name** (`N. Kucherov`) with
the **compact position indicator** on line one, the career context on line two,
and **the index on the far right**, vertically centred across both lines. Two
lines because this is the one screen in the app where a GM is making an
irreversible decision and information beats density. Compact position because the
sheet is a multi-select list, which the position rule assigns to compact.

Line two, muted `#8b96ab`: `29 y · best 0.78 (2023-24) · 3 yrs left`.

The far-right index is a bare signed percentage with **no chip and no verdict
word**: `+46%` in rose `#f43f5e`, `-31%` in ice cyan. That colouring is a
deliberate choice and it deserves its sentence: on the screen where you are
*buying*, above-career is the thing to be careful of and below-career is the
opportunity, which is exactly what rose and ice cyan already mean everywhere
else in this app. A word like `CAREER YEAR` was rejected because whether that is
good or bad depends entirely on which end of the trade you are on, and the app
should not pick a side.

**2. `PlayerCard` header — one line.** Under the name, before the tabs:
`29 y · career high 78 pts (2023-24) · +46% · 3 yrs left`. The Career tab
already holds the full table; this is the summary, so the verdict does not
require opening a tab a GM did not think to open. Not a link, so the
no-duplicate-destinations rule is untouched.

**3. Top Free Agents on the GM Office dashboard — one suffix.** The existing
card grid ranks unrostered players by season fantasy points. A free agent at
+80% over his career is either the pickup of the season or a mirage, and that is
precisely the decision the card exists to support. The index goes under the
existing window label, muted, same colours.

No new Lucide icons. No new colours.

## The rules

- **A player with no qualifying prior NHL season** — a rookie, or one of the
  eight men on Mordus rosters who have never played an NHL game — shows a
  `ROOKIE` chip in `--muted` and **no index, never `0%` or `+∞`**. This is the
  honest and important case: a rookie cannot be having a career year, because he
  has no career. Same principle as a null `TraderRating`.
- **A player with prior NHL seasons but none over 40 games** (a career fourth
  liner, a goalie who has only ever backed up) gets `NO BASELINE`, also with no
  index. Distinct from `ROOKIE` in wording only, but the distinction is true and
  costs nothing.
- **Fewer than 10 games this season**: index suppressed, `—`. In the first
  fortnight of October a 3-game sample produces indexes of +300% and the feature
  would be noise on the day it launched.
- **Goalies** use save percentage, compared in SV% points. A skater's `+46%` and
  a goalie's `+.015` must never appear in the same column with the same
  formatting.
- **Non-NHL leagues are excluded from the baseline**, always. A player returning
  from the KHL is compared to his last NHL season, however old, or gets
  `NO BASELINE`.
- **The current season is excluded from the baseline**, always.
- **Sim clock**: this season's line is bounded by `SimulationState.AsOfDate` like
  every other season total in the app. The career rows are historical and take no
  bound. During the replay the index therefore moves week by week, which is
  correct and is also how it gets tested.
- **A player traded mid-season, in the NHL or in the pool**: no special case at
  all. The index is a property of the player, not of who owns him.
- **Playoffs excluded** — `GameType = 2` on the career rows as everywhere else.
- **Banking**: this reads nothing scored and writes nothing. It cannot restate a
  week or touch a `RosterAssignment`.
- **Week 1 of a brand-new league**: the 10-game floor means every index reads
  `—` for about two weeks, then they all light up together. That is the correct
  behaviour and it should not be softened.

## What it costs

No migration. One query — a `MAX` over `PlayerCareerSeasonStats` with three
`WHERE` clauses — joined onto the player payloads the trade sheet and the player
card **already fetch**, so there is no new endpoint and no new spinner on either
screen. The free-agent list gets the same two fields on rows it already returns.
Frontend: a second line on trade-sheet rows, one line on the `PlayerCard`
header, one suffix on the free-agent cards. The pure logic worth mocking-free
tests is small and genuinely tricky: the baseline picker (league filter, season
filter, 40-game floor, no qualifying season) and the two index formulas, skater
and goalie, including the divide-by-zero and the rookie path.

## What I rejected

- **Projecting an 82-game total** ("on pace for 91 points"). An extrapolation is
  a model; a ratio of two measured per-game rates is arithmetic over two things
  that actually happened. `four-game-weeks` drew the same line for the same
  reason, and the two files should not disagree about it.
- **A buy-low / regression board** ranking every rostered player by his last-10
  rate against his season rate. I took this seriously and killed it on rule 1:
  **there is no historical injury data**, so a "cold" player is very often just
  hurt, and the board would spend half its rows pointing at men the Team grid
  already marks in rose. It also duplicates the shape of the two dashboard
  leaderboards that shipped on 2026-08-02.
- **A "he's due" indicator from shooting percentage.** `Shots` and `Goals` are
  both stored per game, so it is computable — and it is the purest possible form
  of the model this app refuses to ship. It would be the only number in the
  product that is not a measurement of something that happened.
- **A column on the Team grid.** Twenty-one columns already, one horizontal
  scroll, and `four-game-weeks` has a much stronger claim on the single
  pre-scroll slot because it is time-critical and this is not. This number
  belongs where a decision is made, not where a roster is reviewed.
- **Comparing to the career average rather than the career best.** The best
  season is the one the seller quotes and the one everybody remembers. Nobody
  has ever said "well, his career average is 0.61" in a trade negotiation.
- **Age curves, peak-age adjustment, aging models.** Show the age and let them
  argue about what it means. Fourteen die-hards have fourteen theories about when
  a winger falls off and the app has no business having a fifteenth.
- **Guessing UFA/RFA status to weight the risk.** Not stored, not parsed from
  CapWages, and a false statement about a real player's contractual rights is
  worse than silence.
