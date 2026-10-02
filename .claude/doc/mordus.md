# Les Mordus — the league's rules

The pool Fantasy Warrior is built for first. **Its rules live in one file,
[`data/mordus-rules.json`](../../data/mordus-rules.json)** — a serialized
`RuleSet`, the same document a league stores on its `LeagueSeason`. What each
field means and where it is enforced is in [league-rules.md](league-rules.md);
this file holds only what is particular to Les Mordus and why.

The file has two readers:

- **`seed-mordus` writes it to the new season verbatim** — the job holds no
  rule of its own, and refuses a file `RuleSetValidation` rejects. Command in
  [deployment.md](deployment.md).
- **The unit tests load it as-is** (`MordusRuleSet`), so an edit the code cannot
  honour fails a test rather than a seed.

The live league's own copy is its `LeagueSeason.Rules`, edited from the
commissioner's rules panel (`PATCH /api/leagues/{joinCode}/rules`). **A rule
changed on the panel must be changed in the file too**, or the next reseed
silently reverts it.

Its rosters come from PoolExpert's standings export and its pick ownership from
the league's own spreadsheet, both checked in as data files — how they are read
is in [deployment.md](deployment.md).

## Identity

Join code `TKW6UR`, commissioner `nick`. Usernames are the GM's first name,
disambiguated by a surname initial on a collision (`jonathan` / `jonathanr`).
The pool has counted its own seasons for years, which is why `seed-mordus`
takes `--season-number` explicitly: `LeagueSeasons.Number` predates the app and
is derivable from nothing else.

## What is particular to the league

**Keeper, points reset each season.** Rosters carry over, totals start at zero
every season — there is no lifetime total to model.

**The Équipe slot (`T`).** Every GM owns one NHL franchise, held in a roster
spot like any player (`roster.franchiseSlot`). It scores its club's record and
can change hands. How the slot is modelled, and why the club you *are* can
diverge from the club you *own*, is in [data-model.md](data-model.md).

**Franchise results have their own keys.** `teamWins` / `teamOtLosses` /
`teamLosses` are priced apart from the goalie's `wins` / `otLosses`. They can
carry the same values, but "my goalie won" and "my franchise won" are different
events a league must be able to pay apart. The franchise total is read off the
`Games` table, never the players' game log (`FranchiseResults.For`).

**A contractless player counts at `cap.defaultCapHit`** rather than zero, so a
roster cannot dodge the cap with unsigned players.

**The bench has no fixed size**: only the active lineup is slotted, within the
roster's min/max. Active ↔ reserve swaps are weekly.

**Two auto-protection bars, not one.** A goalie plays about half his club's
games: measured at the skaters' bar he would stay untouchable twice as long.
Auto-protection is free — it does not cost a protection slot.

**Unclaimed exposed players stay on their team** after the steal rounds
(`protection.afterDraft`). Off-season mechanics live in
[offseason.md](offseason.md).

**A GM may dress non-NHL players.** They get a normal roster assignment that
scores nothing, rather than being refused a spot or silently dropped.

## Known gap

The cap floor (`cap.min`) is recorded but not enforced — only the ceiling is.
Tracked in [project_status.md](project_status.md).
