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
  fantasyPoints: number;
  ptsPerGame: number | null;
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
  fantasyPoints: number;
  goalsAgainst: number;
  saves: number;
  shotsAgainst: number;
  gaa: number | null;
  svPct: number | null;
  capHit: number | null;
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

function SortableHead({
  label,
  colKey,
  active,
  dir,
  onSort,
  accent,
  alignLeft,
}: {
  label: string;
  colKey: string;
  active: boolean;
  dir: SortDir;
  onSort: (k: string) => void;
  accent?: boolean;
  alignLeft?: boolean;
}) {
  return (
    <th
      scope="col"
      className={`stats-sortable${accent ? " accent" : ""}${alignLeft ? " stats-col-player" : ""}`}
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

  const { goal, assist, goalieWin, goalieOtLoss, shutout } = league.ruleConfig.pointValues;

  const skaterRows: SkaterRow[] = (players ?? [])
    .filter((p) => !p.isGoalie)
    .map((p) => {
      const fantasyPoints = p.goals * goal + p.assists * assist;
      return {
        id: p.id,
        name: p.name,
        position: p.position,
        gamesPlayed: p.gamesPlayed,
        goals: p.goals,
        assists: p.assists,
        fantasyPoints,
        ptsPerGame: p.gamesPlayed > 0 ? fantasyPoints / p.gamesPlayed : null,
        plusMinus: p.plusMinus,
        pim: p.pim,
        shots: p.shots,
        capHit: p.capHit,
        costPerPoint: p.capHit != null && fantasyPoints > 0 ? p.capHit / fantasyPoints : null,
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
      fantasyPoints: p.wins * goalieWin + p.otLosses * goalieOtLoss + p.shutouts * shutout,
      goalsAgainst: p.goalsAgainst,
      saves: p.saves,
      shotsAgainst: p.shotsAgainst,
      gaa: formatGaa(p.goalsAgainst, p.gamesPlayed),
      svPct: formatSvPct(p.saves, p.shotsAgainst),
      capHit: p.capHit,
    }));

  const skaterSort = useSort<SkaterRow>(skaterRows, "fantasyPoints");
  const goalieSort = useSort<GoalieRow>(goalieRows, "fantasyPoints");

  const sum = <T,>(rows: T[], pick: (r: T) => number) => rows.reduce((acc, r) => acc + pick(r), 0);
  const skatersGp = sum(skaterRows, (r) => r.gamesPlayed);
  const skatersFp = sum(skaterRows, (r) => r.fantasyPoints);
  const skatersCap = sum(skaterRows, (r) => r.capHit ?? 0);
  const goaliesGp = sum(goalieRows, (r) => r.gamesPlayed);
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
                    <tr>
                      <SortableHead label="Player" colKey="name" active={skaterSort.key === "name"} dir={skaterSort.dir} onSort={skaterSort.toggle} alignLeft />
                      <SortableHead label="GP" colKey="gamesPlayed" active={skaterSort.key === "gamesPlayed"} dir={skaterSort.dir} onSort={skaterSort.toggle} />
                      <SortableHead label="G" colKey="goals" active={skaterSort.key === "goals"} dir={skaterSort.dir} onSort={skaterSort.toggle} />
                      <SortableHead label="A" colKey="assists" active={skaterSort.key === "assists"} dir={skaterSort.dir} onSort={skaterSort.toggle} />
                      <SortableHead label="PTS" colKey="fantasyPoints" active={skaterSort.key === "fantasyPoints"} dir={skaterSort.dir} onSort={skaterSort.toggle} accent />
                      <SortableHead label="PTS/M" colKey="ptsPerGame" active={skaterSort.key === "ptsPerGame"} dir={skaterSort.dir} onSort={skaterSort.toggle} />
                      <SortableHead label="+/-" colKey="plusMinus" active={skaterSort.key === "plusMinus"} dir={skaterSort.dir} onSort={skaterSort.toggle} />
                      <SortableHead label="PIM" colKey="pim" active={skaterSort.key === "pim"} dir={skaterSort.dir} onSort={skaterSort.toggle} />
                      <SortableHead label="SOG" colKey="shots" active={skaterSort.key === "shots"} dir={skaterSort.dir} onSort={skaterSort.toggle} />
                      <SortableHead label="Salary" colKey="capHit" active={skaterSort.key === "capHit"} dir={skaterSort.dir} onSort={skaterSort.toggle} />
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
                        <td>{r.gamesPlayed}</td>
                        <td>{r.goals}</td>
                        <td>{r.assists}</td>
                        <td className="accent">{r.fantasyPoints}</td>
                        <td>{displayRate(r.ptsPerGame, 2)}</td>
                        <td>{signed(r.plusMinus)}</td>
                        <td>{r.pim}</td>
                        <td>{r.shots}</td>
                        <td>{r.capHit != null ? formatMoneyCompact(r.capHit) : "—"}</td>
                        <td>{r.costPerPoint != null ? formatMoneyCompact(r.costPerPoint) : "—"}</td>
                      </tr>
                    ))}
                  </tbody>
                  <tfoot>
                    <tr>
                      <th className="stats-col-player" scope="row">
                        Total
                      </th>
                      <td>{skatersGp}</td>
                      <td>{sum(skaterRows, (r) => r.goals)}</td>
                      <td>{sum(skaterRows, (r) => r.assists)}</td>
                      <td className="accent">{skatersFp}</td>
                      <td>{displayRate(skatersGp > 0 ? skatersFp / skatersGp : null, 2)}</td>
                      <td>{signed(sum(skaterRows, (r) => r.plusMinus))}</td>
                      <td>{sum(skaterRows, (r) => r.pim)}</td>
                      <td>{sum(skaterRows, (r) => r.shots)}</td>
                      <td>{formatMoneyCompact(skatersCap)}</td>
                      <td>{skatersFp > 0 ? formatMoneyCompact(skatersCap / skatersFp) : "—"}</td>
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
                    <tr>
                      <SortableHead label="Player" colKey="name" active={goalieSort.key === "name"} dir={goalieSort.dir} onSort={goalieSort.toggle} alignLeft />
                      <SortableHead label="GP" colKey="gamesPlayed" active={goalieSort.key === "gamesPlayed"} dir={goalieSort.dir} onSort={goalieSort.toggle} />
                      <SortableHead label="W" colKey="wins" active={goalieSort.key === "wins"} dir={goalieSort.dir} onSort={goalieSort.toggle} />
                      <SortableHead label="OTL" colKey="otLosses" active={goalieSort.key === "otLosses"} dir={goalieSort.dir} onSort={goalieSort.toggle} />
                      <SortableHead label="SO" colKey="shutouts" active={goalieSort.key === "shutouts"} dir={goalieSort.dir} onSort={goalieSort.toggle} />
                      <SortableHead label="PTS" colKey="fantasyPoints" active={goalieSort.key === "fantasyPoints"} dir={goalieSort.dir} onSort={goalieSort.toggle} accent />
                      <SortableHead label="GAA" colKey="gaa" active={goalieSort.key === "gaa"} dir={goalieSort.dir} onSort={goalieSort.toggle} />
                      <SortableHead label="SV%" colKey="svPct" active={goalieSort.key === "svPct"} dir={goalieSort.dir} onSort={goalieSort.toggle} />
                      <SortableHead label="Salary" colKey="capHit" active={goalieSort.key === "capHit"} dir={goalieSort.dir} onSort={goalieSort.toggle} />
                    </tr>
                  </thead>
                  <tbody>
                    {goalieSort.sorted.map((r) => (
                      <tr key={r.id}>
                        <td className="stats-col-player">
                          <span className="stats-player-name">{r.name}</span>
                          <span className="stats-player-pos">{posGroup(r.position)}</span>
                        </td>
                        <td>{r.gamesPlayed}</td>
                        <td className="accent">{r.wins}</td>
                        <td>{r.otLosses}</td>
                        <td>{r.shutouts}</td>
                        <td className="accent">{r.fantasyPoints}</td>
                        <td>{displayRate(r.gaa, 2)}</td>
                        <td>{displayRate(r.svPct, 3, true)}</td>
                        <td>{r.capHit != null ? formatMoneyCompact(r.capHit) : "—"}</td>
                      </tr>
                    ))}
                  </tbody>
                  <tfoot>
                    <tr>
                      <th className="stats-col-player" scope="row">
                        Total
                      </th>
                      <td>{goaliesGp}</td>
                      <td className="accent">{sum(goalieRows, (r) => r.wins)}</td>
                      <td>{sum(goalieRows, (r) => r.otLosses)}</td>
                      <td>{sum(goalieRows, (r) => r.shutouts)}</td>
                      <td className="accent">{sum(goalieRows, (r) => r.fantasyPoints)}</td>
                      <td>{displayRate(goaliesGp > 0 ? goaliesGa / goaliesGp : null, 2)}</td>
                      <td>{displayRate(goaliesShotsAgainst > 0 ? goaliesSaves / goaliesShotsAgainst : null, 3, true)}</td>
                      <td>{formatMoneyCompact(goaliesCap)}</td>
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
