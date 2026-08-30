# External data sources

Three sources feed the app, each with its own constraints. **All of it is
personal, non-commercial use.** Nothing here is redistributed.

## The NHL API — `api-web.nhle.com`

Identity, rosters, prospects, game logs and career history; `NhlApiClient` is the
only caller.

- `/v1/roster/{team}/{season}` and `/v1/prospects/{team}` are the two endpoints
  `player-sync` reads. **What they cannot see**: an unsigned free agent is on no
  roster, and a fresh draftee may be on no prospect list — both are invisible to
  the roster sync, which is why a separate path exists.
- `https://search.d3.nhle.com/api/v1/search/player` is that path (different host,
  a bare JSON array whose ids are strings). `player-resolve` uses it.
- `/v1/player/{id}/landing` carries career history — including pee-wee tournaments
  and 14U showcases, which is why `PlayerCareerSeasonStats` is filtered through
  the `NotableLeagues` whitelist. A whitelist, because minor and youth league
  names are effectively unbounded.

**Two search traps, both found by tests, both live:**

- **Query the surname alone, never the full name.** The endpoint falls back to a
  fuzzy given-name match and does so silently: `q=Zack Bolduc` answers Zack
  **Smith**. Taking the first hit would write strangers onto pool rosters.
- **It truncates to the requested limit without saying so**, and a truncated
  answer looks exactly like an honest one. At limit 50 the real Jackson Smith fell
  off the end of his own surname; the limit in use is 500.

## CapWages — contracts and salaries

- **Not HTML scraping.** CapWages is a Next.js site and every page embeds, in a
  `<script id="__NEXT_DATA__">` block, the structured JSON its React tree was
  rendered from — the same numbers as the visible tables, already typed. We parse
  that, so a CSS or layout change cannot break the import, which is the usual way
  a scraper dies.
- **The player page carries `nhlId`**, so a contract joins straight onto `Players`
  with no name matching at all. Team pages do not, and fall back to name + team.
- **32 requests suffice, not ~1,000**: each team page carries its whole roster
  season by season. Player pages serve only `--resolve-unmatched`, since they
  alone carry `nhlId`.

Terms respected: 2s between requests, an honest `User-Agent` naming the project
and where to reach it, exponential backoff on 429/503, personal non-commercial
use. `robots.txt` disallows `/players/` and `/trade-tree/` **to Amazonbot only**
and allows everything for every other agent — which is exactly why the User-Agent
must never impersonate a browser.

## News and injuries — Rotowire and FantasySP

Three URLs, and **Nick's instruction is not to hunt for variants or undocumented
API endpoints**:

| Source | URL | Kind |
|---|---|---|
| `rotowire_rss` | `rotowire.com/rss/news.php?sport=NHL` | RSS feed, real dates |
| `rotowire_html` | `rotowire.com/hockey/news.php?view=injuries` | injury list, real dates |
| `fantasysp` | `fantasysp.com/injuries/nhl/` | injury list, no per-item date |

Terms respected, and they bind **both** sites: honour `robots.txt`, send an
identifiable `User-Agent`, and never exceed **one request every 2-3 seconds**.
Use is personal and non-commercial — **any redistribution or commercial use of
Rotowire or FantasySP content requires a licence** from them (see their
`/partners` page). And never store Rotowire's `news-update__analysis` block: it
is subscription-locked, and it is the one thing their terms explicitly forbid
keeping. The RSS feed carries no ANALYSIS at all, which is why that path needs no
filtering while the HTML one does.

The implementation is C# (`backend/FantasyWarrior.Jobs/News/`) and the **test
fixtures are authoritative**:
`backend/FantasyWarrior.Core.Tests/Fixtures/{fantasysp,rotowire}-injuries.html`.
Every claim below about page structure is asserted against them, so the day a
site restructures a test fails instead of a screen quietly emptying.

- **FantasySP**: one table per team, six columns `#, Player, Team, Pos, Injury,
  News`. The parser requires six cells *and* a player link before reading a row —
  a shorter row silently shifts every field left by one. The team header is an
  `<h6>` and is **not read at all**: players are identified by name, and the team
  we already hold beats the one a news page happens to print.
- **Rotowire injuries**: `div.news-update.is-injured`. The `is-injured` class is
  the site's own answer to "is this man hurt", which is why it is used instead of
  the headline — a player with a shoulder injury whose block is headlined *"Signs
  five-year contract"* would otherwise be read as healthy. Inside:
  `news-update__inj` (injury type), `news-update__news` (the factual paragraph),
  `news-update__timestamp` (a real per-item date, "August 1, 2026").
- **`news-update__analysis` is never touched.** It is Rotowire's
  subscription-locked block and **must never be stored** — the one thing the terms
  actually forbid.

FantasySP currently answers this client with 403 — the client fingerprint, not
the request, since curl gets 200 seconds later from the same IP and User-Agent.
Deliberately not chased: dressing the job up as a browser would circumvent an
access control the site chose to put up, and the cost of being turned away is
that FantasySP's injuries stop updating, not that they vanish.

### How injury lists behave

- **Both scraped pages are state, not feeds.** They publish who is hurt *today*,
  so a player disappearing from one is that source saying "cleared" — nobody ever
  announces a recovery. That single fact drives both writes: the `PlayerInjuries`
  row is resolved and the `NewsItem` is deleted. The medical record survives with
  its `ReportedUtc`/`ResolvedUtc`; the headline does not deserve to.
- **A source returning nothing is treated as broken**, never as "nobody is hurt",
  or a site rewrite would clear the whole league in one silent run.
- **Reconciled per source, never across sources.** Rotowire dropping a player says
  nothing about whether FantasySP still lists him. Where two sources report the
  same man, the API shows the one reported first.
- **Age retires a headline, never a condition.** The 30-day retention prune skips
  injury-list sources entirely. Otherwise a long-term injury falls past the cutoff,
  gets deleted nightly and re-inserted on the next run — churn, and the player's
  card loses the one item explaining the mark on his row.
- **Injured and suspended share the colour, never the symbol.**
  `InjuryClassifier` decides which, server-side, once, when the source's label is
  read. Both keep a man out of the lineup, but telling a GM his defenceman is
  injured when he was suspended is a false statement about a real person.

### Matching a name to a player

`PlayerNameIndex` resolves a source's name against our table. It falls back to
first-initial-plus-surname because a veteran between contracts is published as
"R. Gudas" while every news site writes "Radko Gudas"; the fallback only uses keys
that are **unique** — the league has two Sebastian Ahos, so "s aho" is not a key
at all.

It is the **wrong** matcher when the question is "does this name refer to anyone
at all". Asked to resolve a possibly-unknown name it answered *Mathieu* Bolduc for
"Marcel Bolduc". `PlayerSearchMatcher` is used there instead: it requires three
shared characters in the given name, keeping Zack for Zachary and Sam for Samuel
while refusing Marcel for Mathieu. Nicknames sharing no prefix (Bill for William)
are reported unresolved rather than guessed.

⚠️ **Never hand-correct the names in `data/unresolved-players.txt`.** They are
kept spelled the way the source wrote them, on purpose: the matcher is what
absorbs Zack for Zachary and Sandin Pellikka for Sandin-Pellikka. Tidying the
input by hand would silently disable the only regression signal these two
matchers give.
