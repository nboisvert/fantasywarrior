# League rules — the catalogue

> Every rule a league plays by: what it is, what it defaults to, and **where it
> is enforced**. Les Mordus' own values are in [mordus.md](mordus.md); the schema
> is in [data-model.md](data-model.md); how scoring works is in
> [scoring-model.md](scoring-model.md); the off-season mechanics are in
> [offseason.md](offseason.md).

## Where the rules live

**One JSON document per season**, on `LeagueSeasons.Rules`. The types are
`Core/Rules/RuleSet.cs`; the serializer settings are the storage format and live
in `RuleSetJson`. `League` carries **no rules at all** — it is identity and
membership.

Rules are stored per season because rules have a season. They used to be ten
columns on `Leagues` plus a `LeagueScoringRules` table, all mutated in place, so
a keeper pool had no way to answer "what were season 2's rules?" and a
mid-season scale change left no record of what it had been. One document per
season makes that history a consequence of where the rules are kept rather than
a feature somebody maintains.

## Which season's rules

**Two documents are live through the whole off-season**, and reading the wrong
one is the mistake this design exists to prevent. `RuleSetResolver` is the only
read path, and its entry points *are* the three questions:

| Method | Which row | Who reads it |
|---|---|---|
| `ForScoringAsync` | `Season == League.Season` — the season being **played** | scoring, lineups, the free-agent leaderboard, a free-agent claim's `RosterMax` check |
| `ForActiveSeasonAsync` | the row that is not `Complete` — the season being **prepared** | protections, the draft, trades, `draft-picks-init` |
| `ForSeasonAsync` | any named season | the history |
| `ForEditingAsync` | the prepared season, tracked | `PATCH /rules` |

In July, season 3 is `Complete` and still what the standings pay under, while
season 4 sits `Protecting` under its own document. Scoring a banked week under
next season's scale, or drafting under the rules of the season that just ended,
are both silent errors a single "get the rules" method would invite.

**Trades read the open season**, not the scored one. They are the same row all
through `InSeason`; in `PreSeason` they differ, and a trade then is repairing a
roster for the season about to start, so the bounds that matter are the ones it
will be judged by.

**A rules change never restates a finished season**: `ForEditingAsync` returns
the prepared season, so a `Complete` row cannot be edited at all.

## Recorded is not enforced

`RuleSetCapabilities.Unsupported` returns every value in a document that nothing
acts on. A commissioner may record the pool's real rules before the code catches
up; the rules panel badges each one where it sits, and `PATCH /rules` returns the
same list.

It answers **by value, not by field** — `scoring.includePlayoffs` is supported at
`false` and not at `true`, and a field-level answer could not say that.

> **The invariant that makes the badge safe.** A consumer that meets a value it
> cannot honour **refuses the action and names the rule**. It never falls back to
> a default. That is the exact failure this replaces: `StealRounds` sat `NULL`,
> `draft/open` never read it, and `?? 0` downstream produced an all-rookie draft
> with uncapped losses that nobody would have noticed until a GM asked where his
> steal turn went.

Removing an entry from `RuleSetCapabilities` is how a feature ships: the wiring
and the claim that it exists sit one function apart, so they cannot drift.

## An unwritten document is refused

`Rules` defaults to `'{}'`, which deserializes with `Version = 0` —
`RuleSet.IsUnwritten`. It deliberately does **not** read as a new league's
defaults: a league whose rules were never converted would otherwise report "no
cap, no slots, no protections" and look identical to a correctly configured
permissive league. Every reader refuses instead.

## The catalogue

`Enforced?` is what the code acts on today. A value marked **badge** is stored,
displayed, and refused by any consumer that would have had to act on it.

### Pool

| Path | Type | Default | Enforced? |
|---|---|---|---|
| `poolType` | `keeper` / `singleSeason` | `keeper` | `keeper` yes; `singleSeason` **badge** |

Points resetting each season is a property of `keeper` **as implemented** —
`vStandings` filters by season — not a separate setting. A pool that accumulated
points for life would be a **third value of this enum**, never a second field.

### Salary cap

| Path | Type | Default | Enforced? |
|---|---|---|---|
| `cap.max` | dollars, null = no cap | null | yes — trades and draft selections |
| `cap.min` | dollars, null = no floor | null | **badge** |
| `cap.defaultCapHit` | dollars | 1 000 000 | yes — `vStandings` and trade validation |

`defaultCapHit` is what a player with no contract costs. "No contract" is the
permanent, ordinary state of an unsigned free agent and of a drafted prospect,
not a data gap; 0 carries them free. Not nullable — null would only be "$0"
spelled at greater length.

### Roster

| Path | Type | Default | Enforced? |
|---|---|---|---|
| `roster.min` / `.max` | count, null = no limit | null | yes — trades, on proposal and on acceptance |
| `roster.byPosition.{forwards,defense,goalies}.{min,max}` | count, null = no bound | null | **badge** |
| `roster.franchiseSlot` | bool | false | yes — but **not editable** |

`franchiseSlot` says whether every team owns one NHL franchise that scores (the
Équipe slot). It is a fact about how the league was built: turning it on creates
no spots and turning it off deletes none, so `PATCH /rules` refuses a change to
it rather than accepting a no-op that looks like a rule.

**Neither bound is enforced on a draft selection**, deliberately —
[offseason.md](offseason.md) §4.

### Weekly lineup

| Path | Type | Default | Enforced? |
|---|---|---|---|
| `lineup.mode` | `activeSelection` / `topN` | `activeSelection` | `activeSelection` yes; `topN` **badge** |
| `lineup.slots.{forwards,defense,goalies}` | count | 0 | yes — at lineup submission |
| `lineup.onMissing` | `carryForward` / `scoreZero` | `carryForward` | `carryForward` yes; `scoreZero` **badge** |

**`mode` splits two decisions that used to be one setting.** `topCount`'s own
documentation said "how many players count toward the team score" while the UI
labelled it "Active forwards", and what it actually fed was
`LineupRules.SlotsFrom`. Those are two scoring models: in `activeSelection` the
GM picks the week's actives and the lineup locks Monday; in `topN` the best N per
group score automatically with nothing to submit. Both use the same counts.

Fielding **fewer** than the slots is allowed; fielding more is refused.

### Point values

| Path | Type | Default | Enforced? |
|---|---|---|---|
| `scoring.values{}` | stat name → points | the five historic values | yes — at scoring |
| `scoring.byPosition{}` | group → stat → points | empty | **badge** |
| `scoring.includePlayoffs` | bool | false | `false` yes; `true` **badge** |

The scale is a map over `StatKeys`, not a fixed list: scoring blocked shots is a
setting, not a release. An unknown key is **rejected** rather than absorbed — it
would score zero forever and read as a calculation bug rather than the typo it
is. The three `team*` keys are the Équipe slot's own and are deliberately not the
goalie's; pricing them with no franchise slot is refused.

`includePlayoffs` names a rule that had no name: `GameType == 2` is filtered in
the rollup job, the views and every API read.

### Trades

| Path | Type | Default | Enforced? |
|---|---|---|---|
| `trades.enabled` | bool | true | yes |
| `trades.picksTradable` | bool | true | yes |
| `trades.pickYearsAhead` | count | 1 | 1 yes; >1 **badge** |
| `trades.approval` | `none` / `commissioner` / `leagueVote` | `none` | `none` yes; rest **badge** |

Trades are also frozen by phase during `Protecting` and `Drafting`, which is a
separate rule and not configurable — see [offseason.md](offseason.md) §2.
`TradeVotes` rate a trade and have never blocked one.

### Protections

| Path | Type | Default | Enforced? |
|---|---|---|---|
| `protection.slots` | count, null = no rule | null | yes |
| `protection.slotsByPosition` | counts, null | null | **badge** |
| `protection.auto.enabled` | bool | true | yes |
| `protection.auto.skaterMaxCareerGames` | count | 100 | yes |
| `protection.auto.goalieMaxCareerGames` | count | 50 | yes |
| `protection.afterDraft` | `stayWithTeam` / `releasedToFreeAgents` | `stayWithTeam` | `stayWithTeam` yes; the other **badge** |

Null slots means "the league has no protection rule", which is not zero — the
autofill refuses on exactly that distinction.

The auto-protection bars were two constants in `ProtectionRules`, invisible from
any league's settings. They are passed explicitly everywhere, never defaulted at
a call site: a default would quietly apply one pool's threshold to another pool's
draft. Goalies count separately and lower because a goalie plays roughly half his
club's games.

### Off-season draft

| Path | Type | Default | Enforced? |
|---|---|---|---|
| `draft.unprotectedDisposition` | `stealRounds` / `openPool` | `stealRounds` | `stealRounds` yes; `openPool` **badge** |
| `draft.steal.rounds` | count | 0 | yes |
| `draft.steal.turnsTradable` | bool | false | `false` yes; `true` **badge** |
| `draft.steal.maxLossesPerTeam` | count, null = uncapped | null | yes |
| `draft.rookieRounds` | count, null = no draft | null | yes — `draft-picks-init` generates one pick per team per round |
| `draft.snake` | bool | false | `false` yes; `true` **badge** |

`steal.rounds` and `rookieRounds` size two different drafts that run back to back
in one room; seeing them diverge is not an error.

**`draft/open` refuses** on any unsupported draft or protection value, on steal
rounds with no protection slots, and on steal rounds with an empty protection
slate — each naming the rule at fault.

### Free agency

| Path | Type | Default | Enforced? |
|---|---|---|---|
| `freeAgency.mode` | `none` / `anytime` / `windows` | `none` | `none` yes; rest **badge** |
| `freeAgency.allow` | `add` / `drop` / `both` | `both` | **badge** |
| `freeAgency.movesPerPeriod` | count, null = unlimited | null | **badge** |
| `freeAgency.windows[]` | named date ranges | empty | **badge** |

`GET /free-agents` is a read-only leaderboard ranked under the league's own
scale. `POST /teams/{username}/roster` claims a free agent onto a roster —
refused if he is already owned, or if the team is already at `roster.max` —
and `DELETE /teams/{username}/roster/{playerId}` releases one; GM Office's Top
Free Agents section is the one UI path to the add, from the player card. None
of the four rules in the table above gate either endpoint — `mode`, `allow`,
`movesPerPeriod` and `windows` are all still **badge**.

## Validation

`RuleSetValidation` refuses only **contradictions** — a rule that cannot mean
anything, or that means the opposite of another rule in the same document. Its
own catalogue: unknown stat keys, negative counts and amounts, a floor over a
ceiling, a minimum over a maximum, per-position bounds that no roster could
satisfy, a lineup bigger than the roster maximum, per-position protection slots
over the league total, `team*` values with no franchise slot, an open pool with
steal rounds, and free-agency windows that overlap or run backwards.

"The code does not honour this yet" is a different judgement and belongs to
`RuleSetCapabilities`. Every violation is returned rather than the first, so the
panel shows them all at once.

## Writing them

`PATCH /api/leagues/{code}/rules`, commissioner only, and the **League rules**
panel in the app — which reads the whole document and sends it straight back, so
anything it dropped would be a rule silently reset to its default. It writes to
the season being prepared.

`seed-mordus` and `clone-league` write a document as they build the season they
create; creating a league through `POST /api/leagues` opens its first season with
the defaults. A league with no `LeagueSeason` row has nowhere to keep its rules,
so none of these may skip it.

Not modelled, and worth naming so nobody assumes otherwise: a **trade deadline**,
and a **maximum number of GMs**.
