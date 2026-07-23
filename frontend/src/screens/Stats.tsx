// Stats — detailed season performance grid for the signed-in user's own team.
// Split out of Roster (2026-07-23, per Nick): Roster stays cap/team-composition
// only, this screen owns everything points/stats related, including the score
// headline that used to live at the top of Roster (kept here, just visually
// compacted since the main content below is a dense data grid, not a hero).
//
// PTS on both grids is the league's actual fantasy score (via ruleConfig
// point values), not raw hockey points — this screen is about pool
// performance, and a custom rule config (different goal/assist weights, a
// non-zero shutout value, etc.) must be reflected here the same way it is
// everywhere else in the app (Standings/Dashboard/Roster).

import { useEffect, useState } from "react";
import { api, posGroup } from "../api";
import type { LeagueDetail, PlayerSeasonStatsRow } from "../api";
import { ChevronDownIcon } from "../components/Icons";

const formatGaa = (goalsAgainst: number, gamesPlayed: number): number | null =>
  gamesPlayed > 0 ? goalsAgainst / gamesPlayed : null;

const formatSvPct = (saves: number, shotsAgainst: number): number | null =>
  shotsAgainst > 0 ? saves / shotsAgainst : null;

const displayRate = (v: number | null, decimals: number, stripLeadingZero = false): string => {
  if (v == null) return "—";
  const s = v.toFixed(decimals);
  return stripLeadingZero ? s.replace(/^0\./, ".") : s;
};

const signed = (n: number) => (n > 0 ? `+${n}` : String(n));

/** Same compact format as Roster.tsx's cap gauge ($9.2M / $850K) — kept as a
 * local copy per the project's small-screen-local-helper convention. */
function formatMoneyCompact(amount: number): string {
  const abs = Math.abs(amount);
  if (abs >= 1_000_000) return `$${(abs / 1_000_000).toFixed(1)}M`;
  if (abs >= 1_000) return `$${Math.round(abs / 1_000)}K`;
  return `$${abs}`;
}

/* ---------- row shapes: raw + every derived value the grid can show or sort by ---------- */

interface SkaterRow {
  id: number;
  name: string;
  position: string;
  gamesPlayed: number;
  goals: number;
  assists: number;
  nhlPoints: number;
  nhlPtsPerGame: number | null;
  poolGamesPlayed: number;
  poolPoints: number;
  poolPtsPerGame: number | null;
  plusMinus: number;
  pim: number;
  shots: number;
  capHit: number | null;
  costPerPoint: number | null;
}

interface GoalieRow {
  id: number;
  name: string;
  position: string;
  gamesPlayed: number;
  wins: number;
  otLosses: number;
  shutouts: number;
  poolGamesPlayed: number;
  poolPoints: number;
  poolPtsPerGame: number | null;
  goalsAgainst: number;
  saves: number;
  shotsAgainst: number;
  gaa: number | null;
  svPct: number | null;
  capHit: number | null;
  costPerPoint: number | null;
}

/* ---------- generic sortable-grid plumbing ---------- */

type SortDir = "asc" | "desc";

/** Nulls always sort last regardless of direction (there's nothing to rank a
 * missing GAA/€-per-point against). */
function compareNullable(a: number | null, b: number | null, dir: SortDir): number {
  if (a == null && b == null) return 0;
  if (a == null) return 1;
  if (b == null) return -1;
  return dir === "asc" ? a - b : b - a;
}

/** `colKey` is typed as `string` at the component boundary (not generic) —
 * JSX can't cleanly infer a per-usage generic when the same component is
 * instantiated for two different row shapes (Skater/Goalie) in one file, so
 * the key is cast back to `keyof T` internally where the row type is known. */
function useSort<T extends object>(rows: T[], initialKey: keyof T) {
  const [key, setKey] = useState<keyof T>(initialKey);
  const [dir, setDir] = useState<SortDir>("desc");

  const toggle = (k: string) => {
    const typedKey = k as keyof T;
    if (typedKey === key) setDir((d) => (d === "asc" ? "desc" : "asc"));
    else {
      setKey(typedKey);
      setDir("desc");
    }
  };

  const sorted = [...rows].sort((a, b) => {
    const av = a[key];
    const bv = b[key];
    if (typeof av === "number" || av == null || typeof bv === "number" || bv == null) {
      return compareNullable(av as number | null, bv as number | null, dir);
    }
    const cmp = String(av).localeCompare(String(bv));
    return dir === "asc" ? cmp : -cmp;
  });

  return { sorted, key: key as string, dir, toggle };
}

/** Top row of the two-row grouped header: a label spanning the columns
 * belonging to one data source (NHL season totals / this-stint Pool points /
 * Extra counting stats / Salary), so the grid reads as "where did this
 * number come from" at a glance. */
function GroupHead({ label, span, accent }: { label: string; span: number; accent?: boolean }) {
  return (
    <th colSpan={span} className={`stats-group-th${accent ? " accent" : ""}`} scope="colgroup">
      {label}
    </th>
  );
}

function SortableHead({
  label,
  colKey,
  active,
  dir,
  onSort,
  accent,
  spotlight,
  groupStart,
}: {
  label: string;
  colKey: string;
  active: boolean;
  dir: SortDir;
  onSort: (k: string) => void;
  accent?: boolean;
  /** The one column that's the actual headline number (Pool PTS) — a
   * background tint, not just tinted text, so it reads as THE stat at a
   * glance rather than just another accented column (2026-07-23, per Nick). */
  spotlight?: boolean;
  /** First column of a group (NHL/Pool/Extra/Salary) — draws the vertical
   * divider down through the header/body/footer, matching the group-label
   * row's own border-left above it. */
  groupStart?: boolean;
}) {
  return (
    <th
      scope="col"
      className={`stats-sortable${accent ? " accent" : ""}${spotlight ? " stats-col-spotlight" : ""}${groupStart ? " stats-group-start" : ""}`}
      aria-sort={active ? (dir === "asc" ? "ascending" : "descending") : "none"}
    >
      <button type="button" className="stats-sort-btn" onClick={() => onSort(colKey)}>
        {label}
        {active && (
          <ChevronDownIcon size={12} className={`stats-sort-icon${dir === "asc" ? " asc" : ""}`} />
        )}
      </button>
    </th>
  );
}

export function Stats({ league, username }: { league: LeagueDetail; username: string }) {
  const [players, setPlayers] = useState<PlayerSeasonStatsRow[] | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let ignore = false;
    setLoading(true);
    setError("");
    api
      .teamSeasonStats(league.id, username)
      .then((res) => {
        if (ignore) return;
        setPlayers(res.players);
        setLoading(false);
      })
      .catch((e: unknown) => {
        if (ignore) return;
        setError(e instanceof Error ? e.message : "Could not load stats.");
        setLoading(false);
      });
    return () => {
      ignore = true;
    };
  }, [league.id, username]);

  const myTeam = league.teams.find((t) => t.ownerUsername === username);
  if (!myTeam) return <p className="empty-state">You don't have a team in this league.</p>;

  // NHL group is season-complete (goals/assists as raw hockey points, GP/M
  // over the whole year). Pool group is scoped to this player's *current
  // roster assignment* — precomputed nightly by score-calc off the league's
  // actual rule config, never recomputed client-side — so its own GP/PTS/M
  // can (and, after a trade, will) differ from the NHL columns next to it.
  const skaterRows: SkaterRow[] = (players ?? [])
    .filter((p) => !p.isGoalie)
    .map((p) => {
      const nhlPoints = p.goals + p.assists;
      return {
        id: p.id,
        name: p.name,
        position: p.position,
        gamesPlayed: p.gamesPlayed,
        goals: p.goals,
        assists: p.assists,
        nhlPoints,
        nhlPtsPerGame: p.gamesPlayed > 0 ? nhlPoints / p.gamesPlayed : null,
        poolGamesPlayed: p.assignmentGamesPlayed,
        poolPoints: p.assignmentFantasyPoints,
        poolPtsPerGame: p.assignmentGamesPlayed > 0 ? p.assignmentFantasyPoints / p.assignmentGamesPlayed : null,
        plusMinus: p.plusMinus,
        pim: p.pim,
        shots: p.shots,
        capHit: p.capHit,
        costPerPoint: p.capHit != null && p.assignmentFantasyPoints > 0 ? p.capHit / p.assignmentFantasyPoints : null,
      };
    });

  const goalieRows: GoalieRow[] = (players ?? [])
    .filter((p) => p.isGoalie)
    .map((p) => ({
      id: p.id,
      name: p.name,
      position: p.position,
      gamesPlayed: p.gamesPlayed,
      wins: p.wins,
      otLosses: p.otLosses,
      shutouts: p.shutouts,
      poolGamesPlayed: p.assignmentGamesPlayed,
      poolPoints: p.assignmentFantasyPoints,
      poolPtsPerGame: p.assignmentGamesPlayed > 0 ? p.assignmentFantasyPoints / p.assignmentGamesPlayed : null,
      goalsAgainst: p.goalsAgainst,
      saves: p.saves,
      shotsAgainst: p.shotsAgainst,
      gaa: formatGaa(p.goalsAgainst, p.gamesPlayed),
      svPct: formatSvPct(p.saves, p.shotsAgainst),
      capHit: p.capHit,
      costPerPoint: p.capHit != null && p.assignmentFantasyPoints > 0 ? p.capHit / p.assignmentFantasyPoints : null,
    }));

  const skaterSort = useSort<SkaterRow>(skaterRows, "nhlPoints");
  const goalieSort = useSort<GoalieRow>(goalieRows, "poolPoints");

  const sum = <T,>(rows: T[], pick: (r: T) => number) => rows.reduce((acc, r) => acc + pick(r), 0);
  const skatersGp = sum(skaterRows, (r) => r.gamesPlayed);
  const skatersNhlPts = sum(skaterRows, (r) => r.nhlPoints);
  const skatersPoolGp = sum(skaterRows, (r) => r.poolGamesPlayed);
  const skatersPoolPts = sum(skaterRows, (r) => r.poolPoints);
  const skatersCap = sum(skaterRows, (r) => r.capHit ?? 0);
  const goaliesGp = sum(goalieRows, (r) => r.gamesPlayed);
  const goaliesPoolGp = sum(goalieRows, (r) => r.poolGamesPlayed);
  const goaliesPoolPts = sum(goalieRows, (r) => r.poolPoints);
  const goaliesGa = sum(goalieRows, (r) => r.goalsAgainst);
  const goaliesSaves = sum(goalieRows, (r) => r.saves);
  const goaliesShotsAgainst = sum(goalieRows, (r) => r.shotsAgainst);
  const goaliesCap = sum(goalieRows, (r) => r.capHit ?? 0);

  return (
    <section className="fade-in" style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
      <div className="card stats-header">
        <div className="stats-header-top">
          <span className="roster-team-name">{myTeam.name}</span>
          <span className="stats-score-col">
            <span className="stats-score-value">{myTeam.score}</span>
            <span className="stats-score-label">Points</span>
          </span>
        </div>
        {myTeam.adjustmentsTotal !== 0 && (
          <div className="stats-adj-line">
            <span
              className={`stats-adj-pill ${
                myTeam.adjustmentsTotal > 0 ? "stats-adj-pill-pos" : "stats-adj-pill-neg"
              }`}
            >
              {myTeam.adjustmentsTotal > 0 ? "+" : ""}
              {myTeam.adjustmentsTotal} pts
            </span>
            <small className="muted">
              carried over from past trades/roster moves, so your total stayed fair at the time —
              your current roster alone has scored {myTeam.rawTopXScore} pts
            </small>
          </div>
        )}
      </div>

      {loading && <p className="empty-state">Loading stats…</p>}
      {!loading && error && <p className="error-banner">{error}</p>}

      {!loading && !error && (
        <>
          <div>
            <span className="stats-table-title muted">Skaters</span>
            {skaterRows.length === 0 ? (
              <p className="empty-state">No skaters on this roster.</p>
            ) : (
              <div className="stats-grid-scroll">
                <table className="stats-grid">
                  <thead>
                    <tr className="stats-group-row">
                      <th className="stats-col-player stats-sortable" rowSpan={2} scope="col">
                        <button type="button" className="stats-sort-btn" onClick={() => skaterSort.toggle("name")}>
                          Player
                          {skaterSort.key === "name" && (
                            <ChevronDownIcon size={12} className={`stats-sort-icon${skaterSort.dir === "asc" ? " asc" : ""}`} />
                          )}
                        </button>
                      </th>
                      <GroupHead label="NHL" span={5} />
                      <GroupHead label="Pool" span={3} accent />
                      <GroupHead label="Extra" span={3} />
                      <GroupHead label="Salary" span={2} />
                    </tr>
                    <tr>
                      <SortableHead label="GP" colKey="gamesPlayed" active={skaterSort.key === "gamesPlayed"} dir={skaterSort.dir} onSort={skaterSort.toggle} groupStart />
                      <SortableHead label="G" colKey="goals" active={skaterSort.key === "goals"} dir={skaterSort.dir} onSort={skaterSort.toggle} />
                      <SortableHead label="A" colKey="assists" active={skaterSort.key === "assists"} dir={skaterSort.dir} onSort={skaterSort.toggle} />
                      <SortableHead label="PTS" colKey="nhlPoints" active={skaterSort.key === "nhlPoints"} dir={skaterSort.dir} onSort={skaterSort.toggle} spotlight />
                      <SortableHead label="PTS/M" colKey="nhlPtsPerGame" active={skaterSort.key === "nhlPtsPerGame"} dir={skaterSort.dir} onSort={skaterSort.toggle} />
                      <SortableHead label="GP" colKey="poolGamesPlayed" active={skaterSort.key === "poolGamesPlayed"} dir={skaterSort.dir} onSort={skaterSort.toggle} accent groupStart />
                      <SortableHead label="PTS" colKey="poolPoints" active={skaterSort.key === "poolPoints"} dir={skaterSort.dir} onSort={skaterSort.toggle} accent spotlight />
                      <SortableHead label="PTS/M" colKey="poolPtsPerGame" active={skaterSort.key === "poolPtsPerGame"} dir={skaterSort.dir} onSort={skaterSort.toggle} accent />
                      <SortableHead label="+/-" colKey="plusMinus" active={skaterSort.key === "plusMinus"} dir={skaterSort.dir} onSort={skaterSort.toggle} groupStart />
                      <SortableHead label="PIM" colKey="pim" active={skaterSort.key === "pim"} dir={skaterSort.dir} onSort={skaterSort.toggle} />
                      <SortableHead label="SOG" colKey="shots" active={skaterSort.key === "shots"} dir={skaterSort.dir} onSort={skaterSort.toggle} />
                      <SortableHead label="Salary" colKey="capHit" active={skaterSort.key === "capHit"} dir={skaterSort.dir} onSort={skaterSort.toggle} groupStart />
                      <SortableHead label="$/PTS" colKey="costPerPoint" active={skaterSort.key === "costPerPoint"} dir={skaterSort.dir} onSort={skaterSort.toggle} />
                    </tr>
                  </thead>
                  <tbody>
                    {skaterSort.sorted.map((r) => (
                      <tr key={r.id}>
                        <td className="stats-col-player">
                          <span className="stats-player-name">{r.name}</span>
                          <span className="stats-player-pos">{posGroup(r.position)}</span>
                        </td>
                        <td className="stats-group-start">{r.gamesPlayed}</td>
                        <td>{r.goals}</td>
                        <td>{r.assists}</td>
                        <td className="stats-col-spotlight">{r.nhlPoints}</td>
                        <td>{displayRate(r.nhlPtsPerGame, 2)}</td>
                        <td className="accent stats-group-start">{r.poolGamesPlayed}</td>
                        <td className="accent stats-col-spotlight">{r.poolPoints}</td>
                        <td className="accent">{displayRate(r.poolPtsPerGame, 2)}</td>
                        <td className="stats-group-start">{signed(r.plusMinus)}</td>
                        <td>{r.pim}</td>
                        <td>{r.shots}</td>
                        <td className="stats-group-start">{r.capHit != null ? formatMoneyCompact(r.capHit) : "—"}</td>
                        <td>{r.costPerPoint != null ? formatMoneyCompact(r.costPerPoint) : "—"}</td>
                      </tr>
                    ))}
                  </tbody>
                  <tfoot>
                    <tr>
                      <th className="stats-col-player" scope="row">
                        Total
                      </th>
                      <td className="stats-group-start">{skatersGp}</td>
                      <td>{sum(skaterRows, (r) => r.goals)}</td>
                      <td>{sum(skaterRows, (r) => r.assists)}</td>
                      <td className="stats-col-spotlight">{skatersNhlPts}</td>
                      <td>{displayRate(skatersGp > 0 ? skatersNhlPts / skatersGp : null, 2)}</td>
                      <td className="accent stats-group-start">{skatersPoolGp}</td>
                      <td className="accent stats-col-spotlight">{skatersPoolPts}</td>
                      <td className="accent">{displayRate(skatersPoolGp > 0 ? skatersPoolPts / skatersPoolGp : null, 2)}</td>
                      <td className="stats-group-start">{signed(sum(skaterRows, (r) => r.plusMinus))}</td>
                      <td>{sum(skaterRows, (r) => r.pim)}</td>
                      <td>{sum(skaterRows, (r) => r.shots)}</td>
                      <td className="stats-group-start">{formatMoneyCompact(skatersCap)}</td>
                      <td>{skatersPoolPts > 0 ? formatMoneyCompact(skatersCap / skatersPoolPts) : "—"}</td>
                    </tr>
                  </tfoot>
                </table>
              </div>
            )}
          </div>

          {goalieRows.length > 0 && (
            <div>
              <span className="stats-table-title muted">Goalies</span>
              <div className="stats-grid-scroll">
                <table className="stats-grid">
                  <thead>
                    <tr className="stats-group-row">
                      <th className="stats-col-player stats-sortable" rowSpan={2} scope="col">
                        <button type="button" className="stats-sort-btn" onClick={() => goalieSort.toggle("name")}>
                          Player
                          {goalieSort.key === "name" && (
                            <ChevronDownIcon size={12} className={`stats-sort-icon${goalieSort.dir === "asc" ? " asc" : ""}`} />
                          )}
                        </button>
                      </th>
                      <GroupHead label="NHL" span={4} />
                      <GroupHead label="Pool" span={3} accent />
                      <GroupHead label="Extra" span={2} />
                      <GroupHead label="Salary" span={2} />
                    </tr>
                    <tr>
                      <SortableHead label="GP" colKey="gamesPlayed" active={goalieSort.key === "gamesPlayed"} dir={goalieSort.dir} onSort={goalieSort.toggle} groupStart />
                      <SortableHead label="W" colKey="wins" active={goalieSort.key === "wins"} dir={goalieSort.dir} onSort={goalieSort.toggle} accent />
                      <SortableHead label="OTL" colKey="otLosses" active={goalieSort.key === "otLosses"} dir={goalieSort.dir} onSort={goalieSort.toggle} />
                      <SortableHead label="SO" colKey="shutouts" active={goalieSort.key === "shutouts"} dir={goalieSort.dir} onSort={goalieSort.toggle} />
                      <SortableHead label="GP" colKey="poolGamesPlayed" active={goalieSort.key === "poolGamesPlayed"} dir={goalieSort.dir} onSort={goalieSort.toggle} accent groupStart />
                      <SortableHead label="PTS" colKey="poolPoints" active={goalieSort.key === "poolPoints"} dir={goalieSort.dir} onSort={goalieSort.toggle} accent spotlight />
                      <SortableHead label="PTS/M" colKey="poolPtsPerGame" active={goalieSort.key === "poolPtsPerGame"} dir={goalieSort.dir} onSort={goalieSort.toggle} accent />
                      <SortableHead label="GAA" colKey="gaa" active={goalieSort.key === "gaa"} dir={goalieSort.dir} onSort={goalieSort.toggle} groupStart />
                      <SortableHead label="SV%" colKey="svPct" active={goalieSort.key === "svPct"} dir={goalieSort.dir} onSort={goalieSort.toggle} />
                      <SortableHead label="Salary" colKey="capHit" active={goalieSort.key === "capHit"} dir={goalieSort.dir} onSort={goalieSort.toggle} groupStart />
                      <SortableHead label="$/PTS" colKey="costPerPoint" active={goalieSort.key === "costPerPoint"} dir={goalieSort.dir} onSort={goalieSort.toggle} />
                    </tr>
                  </thead>
                  <tbody>
                    {goalieSort.sorted.map((r) => (
                      <tr key={r.id}>
                        <td className="stats-col-player">
                          <span className="stats-player-name">{r.name}</span>
                          <span className="stats-player-pos">{posGroup(r.position)}</span>
                        </td>
                        <td className="stats-group-start">{r.gamesPlayed}</td>
                        <td className="accent">{r.wins}</td>
                        <td>{r.otLosses}</td>
                        <td>{r.shutouts}</td>
                        <td className="accent stats-group-start">{r.poolGamesPlayed}</td>
                        <td className="accent stats-col-spotlight">{r.poolPoints}</td>
                        <td className="accent">{displayRate(r.poolPtsPerGame, 2)}</td>
                        <td className="stats-group-start">{displayRate(r.gaa, 2)}</td>
                        <td>{displayRate(r.svPct, 3, true)}</td>
                        <td className="stats-group-start">{r.capHit != null ? formatMoneyCompact(r.capHit) : "—"}</td>
                        <td>{r.costPerPoint != null ? formatMoneyCompact(r.costPerPoint) : "—"}</td>
                      </tr>
                    ))}
                  </tbody>
                  <tfoot>
                    <tr>
                      <th className="stats-col-player" scope="row">
                        Total
                      </th>
                      <td className="stats-group-start">{goaliesGp}</td>
                      <td className="accent">{sum(goalieRows, (r) => r.wins)}</td>
                      <td>{sum(goalieRows, (r) => r.otLosses)}</td>
                      <td>{sum(goalieRows, (r) => r.shutouts)}</td>
                      <td className="accent stats-group-start">{goaliesPoolGp}</td>
                      <td className="accent stats-col-spotlight">{goaliesPoolPts}</td>
                      <td className="accent">{displayRate(goaliesPoolGp > 0 ? goaliesPoolPts / goaliesPoolGp : null, 2)}</td>
                      <td className="stats-group-start">{displayRate(goaliesGp > 0 ? goaliesGa / goaliesGp : null, 2)}</td>
                      <td>{displayRate(goaliesShotsAgainst > 0 ? goaliesSaves / goaliesShotsAgainst : null, 3, true)}</td>
                      <td className="stats-group-start">{formatMoneyCompact(goaliesCap)}</td>
                      <td>{goaliesPoolPts > 0 ? formatMoneyCompact(goaliesCap / goaliesPoolPts) : "—"}</td>
                    </tr>
                  </tfoot>
                </table>
              </div>
            </div>
          )}
        </>
      )}
    </section>
  );
}
