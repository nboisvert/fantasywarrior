// Stats — detailed season performance grid for the signed-in user's own team.
// Split out of Roster (2026-07-23, per Nick): Roster stays cap/team-composition
// only, this screen owns everything points/stats related, including the score
// headline that used to live at the top of Roster (kept here, just visually
// compacted since the main content below is a dense data grid, not a hero).
//
// One merged roster grid (2026-07-27, per Nick) — skaters and goalies used to
// be two separate tables; they're now one, sorted together, so a goalie's
// "Fantasy point"/"NHL" numbers just show 0 in the goal/assist columns and a
// skater's GAA/SV% show "—" (not applicable), and only the goalie-specific W
// column dashes out for skaters instead of the other way around.
//
// "Fantasy point" PTS is the league's actual fantasy score for this player's
// *current roster stint* (via ruleConfig point values, precomputed nightly —
// never recomputed client-side); "NHL" PTS is his raw full-season hockey
// points (goals+assists). A custom rule config (different goal/assist
// weights, a non-zero shutout value, etc.) is reflected in the Fantasy point
// group the same way it is everywhere else in the app (Standings/Dashboard).

import { useEffect, useState } from "react";
import { api, formatCap, posGroup, posGroupClass } from "../api";
import type { LeagueDetail, PlayerSeasonStatsRow } from "../api";
import { LoadingLogo } from "../components/LoadingLogo";
import { PlayerCard } from "../components/PlayerCard";
import { ArrowLeftIcon, ChevronDownIcon, InfoIcon } from "../components/Icons";

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

/** Compact money format ($9.2M / $850K) — used for both the player-row
 * salary/cost columns and the cap-gauge disclosure below. */
function formatMoneyCompact(amount: number): string {
  const abs = Math.abs(amount);
  if (abs >= 1_000_000) return `$${(abs / 1_000_000).toFixed(1)}M`;
  if (abs >= 1_000) return `$${Math.round(abs / 1_000)}K`;
  return `$${abs}`;
}

/* ---------- row shape: raw + every derived value the grid can show or sort by ---------- */

interface PlayerRow {
  id: number;
  name: string;
  position: string;
  isGoalie: boolean;
  // Fantasy point group — scoped to this player's *current roster stint*.
  poolGamesPlayed: number;
  poolGoals: number;
  poolAssists: number;
  poolPoints: number;
  poolPtsPerGame: number | null;
  // NHL group — full season totals.
  gamesPlayed: number;
  goals: number;
  assists: number;
  nhlPoints: number;
  nhlPtsPerGame: number | null;
  // Goalie — season totals, dash-rendered for skaters.
  wins: number;
  otLosses: number;
  shutouts: number;
  // Extra
  plusMinus: number;
  pim: number;
  shots: number;
  goalsAgainst: number;
  saves: number;
  shotsAgainst: number;
  gaa: number | null;
  svPct: number | null;
  // Salary
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
 * instantiated for two different row shapes in one file, so the key is cast
 * back to `keyof T` internally where the row type is known. */
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
 * belonging to one data source (Fantasy point / NHL season totals / Extra
 * counting stats / Salary), so the grid reads as "where did this number come
 * from" at a glance. */
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
  /** The one column that's the actual headline number (Fantasy PTS) — a
   * background tint, not just tinted text, so it reads as THE stat at a
   * glance rather than just another accented column (2026-07-23, per Nick). */
  spotlight?: boolean;
  /** First column of a group (Fantasy point/NHL/Extra/Salary) — draws the
   * vertical divider down through the header/body/footer, matching the
   * group-label row's own border-left above it. */
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

export function Stats({
  league,
  username,
  targetUsername,
  onBackToStandings,
}: {
  league: LeagueDetail;
  username: string;
  /** Whose stats to show — the signed-in user by default, or a team picked
   * from Standings. */
  targetUsername: string;
  onBackToStandings: () => void;
}) {
  const [players, setPlayers] = useState<PlayerSeasonStatsRow[] | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const [capExpanded, setCapExpanded] = useState(false);
  const [openPlayerId, setOpenPlayerId] = useState<number | null>(null);

  useEffect(() => {
    let ignore = false;
    setLoading(true);
    setError("");
    api
      .teamSeasonStats(league.id, targetUsername)
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
  }, [league.id, targetUsername]);

  const viewedTeam = league.teams.find((t) => t.ownerUsername === targetUsername);
  const isOwnTeam = targetUsername === username;
  if (!viewedTeam) return <p className="empty-state">Team not found in this league.</p>;

  // Cap gauge figures — moved here verbatim from the retired Roster screen
  // (2026-07-26, per Nick): the Stats grid already lists every rostered
  // player, so a separate roster-list screen was redundant; only the cap
  // detail itself was worth keeping, tucked behind a disclosure.
  const capUsed = viewedTeam.capTotal;
  const capMax = league.capAmount;
  const over = capMax != null && capUsed > capMax;
  const pctRaw = capMax ? (capUsed / capMax) * 100 : 0;
  const pctBarWidth = Math.min(100, Math.max(0, pctRaw));
  const pctDisplay = Math.round(pctRaw);
  const capAvailable = capMax != null ? capMax - capUsed : null;

  // NHL group is season-complete (goals/assists as raw hockey points, GP/G
  // over the whole year). Fantasy point group is scoped to this player's
  // *current roster assignment* — precomputed nightly by score-calc off the
  // league's actual rule config, never recomputed client-side — so its own
  // GP/G/A/W/PTS/PTS-per-G can (and, after a trade, will) differ from the NHL
  // columns next to it.
  const rows: PlayerRow[] = (players ?? []).map((p) => {
    const isGoalie = p.isGoalie;
    const nhlPoints = p.goals + p.assists;
    return {
      id: p.id,
      name: p.name,
      position: p.position,
      isGoalie,
      poolGamesPlayed: p.assignmentGamesPlayed,
      poolGoals: p.assignmentGoals,
      poolAssists: p.assignmentAssists,
      poolPoints: p.assignmentFantasyPoints,
      poolPtsPerGame: p.assignmentGamesPlayed > 0 ? p.assignmentFantasyPoints / p.assignmentGamesPlayed : null,
      gamesPlayed: p.gamesPlayed,
      goals: p.goals,
      assists: p.assists,
      nhlPoints,
      nhlPtsPerGame: p.gamesPlayed > 0 ? nhlPoints / p.gamesPlayed : null,
      wins: p.wins,
      otLosses: p.otLosses,
      shutouts: p.shutouts,
      plusMinus: p.plusMinus,
      pim: p.pim,
      shots: p.shots,
      goalsAgainst: p.goalsAgainst,
      saves: p.saves,
      shotsAgainst: p.shotsAgainst,
      gaa: isGoalie ? formatGaa(p.goalsAgainst, p.gamesPlayed) : null,
      svPct: isGoalie ? formatSvPct(p.saves, p.shotsAgainst) : null,
      capHit: p.capHit,
      costPerPoint: p.capHit != null && p.assignmentFantasyPoints > 0 ? p.capHit / p.assignmentFantasyPoints : null,
    };
  });

  const sort = useSort<PlayerRow>(rows, "poolPoints");

  const sum = <T,>(list: T[], pick: (r: T) => number) => list.reduce((acc, r) => acc + pick(r), 0);
  const goalieRows = rows.filter((r) => r.isGoalie);
  const poolGp = sum(rows, (r) => r.poolGamesPlayed);
  const poolGoalsTotal = sum(rows, (r) => r.poolGoals);
  const poolAssistsTotal = sum(rows, (r) => r.poolAssists);
  const poolPtsTotal = sum(rows, (r) => r.poolPoints);
  const nhlGp = sum(rows, (r) => r.gamesPlayed);
  const nhlGoalsTotal = sum(rows, (r) => r.goals);
  const nhlAssistsTotal = sum(rows, (r) => r.assists);
  const nhlPtsTotal = sum(rows, (r) => r.nhlPoints);
  const winsTotal = sum(rows, (r) => r.wins);
  const otLossesTotal = sum(rows, (r) => r.otLosses);
  const shutoutsTotal = sum(rows, (r) => r.shutouts);
  const plusMinusTotal = sum(rows, (r) => r.plusMinus);
  const pimTotal = sum(rows, (r) => r.pim);
  const shotsTotal = sum(rows, (r) => r.shots);
  const goaliesGp = sum(goalieRows, (r) => r.gamesPlayed);
  const goaliesGa = sum(goalieRows, (r) => r.goalsAgainst);
  const goaliesSaves = sum(goalieRows, (r) => r.saves);
  const goaliesShotsAgainst = sum(goalieRows, (r) => r.shotsAgainst);
  const capTotal = sum(rows, (r) => r.capHit ?? 0);

  const maxRosterSize = league.ruleConfig.rosterSize.max;

  return (
    <section className="fade-in" style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
      {!isOwnTeam && (
        <button type="button" className="btn-ghost stats-back" onClick={onBackToStandings}>
          <ArrowLeftIcon size={16} />
          Back to standings
        </button>
      )}

      <div className="card stats-header">
        <div className="stats-header-top">
          <span className="roster-team-name">{viewedTeam.name}</span>
          <span className="stats-score-col">
            <span className="stats-score-value">{viewedTeam.score}</span>
            <span className="stats-score-label">Points</span>
          </span>
        </div>
        {!isOwnTeam && <small className="muted stats-team-owner">@{viewedTeam.ownerUsername}</small>}
        {capMax != null && (
          <>
            <button
              type="button"
              className="stats-cap-summary"
              onClick={() => setCapExpanded((v) => !v)}
              aria-expanded={capExpanded}
            >
              <InfoIcon size={14} />
              <span>
                Cap {formatMoneyCompact(capUsed)} / {formatMoneyCompact(capMax)}
              </span>
              <ChevronDownIcon size={14} className={`stats-cap-chevron${capExpanded ? " asc" : ""}`} />
            </button>
            {capExpanded && capAvailable != null && (
              <div className="roster-cap">
                <div className="pc-tiles roster-cap-tiles">
                  <div className={`pc-tile${over ? " danger" : " accent"}`}>
                    <span className="pc-tile-value">{formatMoneyCompact(Math.abs(capAvailable))}</span>
                    <span className="pc-tile-label">{over ? "Over budget" : "Available"}</span>
                  </div>
                  <div className={`pc-tile${over ? " danger" : ""}`}>
                    <span className="pc-tile-value">{pctDisplay}%</span>
                    <span className="pc-tile-label">Used</span>
                  </div>
                </div>
                <div
                  className="cap-track"
                  role="progressbar"
                  aria-valuenow={Math.round(pctBarWidth)}
                  aria-valuemin={0}
                  aria-valuemax={100}
                  aria-valuetext={`${pctDisplay}% of cap used, ${formatMoneyCompact(Math.abs(capAvailable))} ${
                    over ? "over budget" : "available"
                  }`}
                  aria-label="Salary cap used"
                >
                  <div className={`cap-fill${over ? " over" : ""}`} style={{ width: `${pctBarWidth}%` }} />
                </div>
                <small className="muted roster-cap-sub">
                  {formatCap(capUsed)} committed of {formatCap(capMax)} cap
                </small>
              </div>
            )}
          </>
        )}
        {viewedTeam.adjustmentsTotal !== 0 && (
          <div className="stats-adj-line">
            <span
              className={`stats-adj-pill ${
                viewedTeam.adjustmentsTotal > 0 ? "stats-adj-pill-pos" : "stats-adj-pill-neg"
              }`}
            >
              {viewedTeam.adjustmentsTotal > 0 ? "+" : ""}
              {viewedTeam.adjustmentsTotal} pts
            </span>
            <small className="muted">
              carried over from past trades/roster moves, so the total stayed fair at the time —
              the current roster alone has scored {viewedTeam.rawTopXScore} pts
            </small>
          </div>
        )}
      </div>

      {loading && <LoadingLogo label="Loading stats…" />}
      {!loading && error && <p className="error-banner">{error}</p>}

      {!loading && !error && (
        <div>
          <span className="stats-table-title">
            Roster
            <span className="stats-table-title-sub">
              {" "}
              ({rows.length}
              {maxRosterSize != null ? ` / ${maxRosterSize}` : ""} player)
            </span>
          </span>
          {rows.length === 0 ? (
            <p className="empty-state">No players on this roster.</p>
          ) : (
            <div className="stats-grid-scroll">
              <table className="stats-grid">
                <thead>
                  <tr className="stats-group-row">
                    <th className="stats-col-player stats-sortable" rowSpan={2} scope="col">
                      <button type="button" className="stats-sort-btn" onClick={() => sort.toggle("name")}>
                        Player
                        {sort.key === "name" && (
                          <ChevronDownIcon size={12} className={`stats-sort-icon${sort.dir === "asc" ? " asc" : ""}`} />
                        )}
                      </button>
                    </th>
                    <GroupHead label="Fantasy point" span={5} accent />
                    <GroupHead label="Goalie" span={3} />
                    <GroupHead label="NHL" span={5} />
                    <GroupHead label="Extra" span={5} />
                    <GroupHead label="Salary" span={2} />
                  </tr>
                  <tr>
                    <SortableHead label="GP" colKey="poolGamesPlayed" active={sort.key === "poolGamesPlayed"} dir={sort.dir} onSort={sort.toggle} accent groupStart />
                    <SortableHead label="G" colKey="poolGoals" active={sort.key === "poolGoals"} dir={sort.dir} onSort={sort.toggle} accent />
                    <SortableHead label="A" colKey="poolAssists" active={sort.key === "poolAssists"} dir={sort.dir} onSort={sort.toggle} accent />
                    <SortableHead label="PTS" colKey="poolPoints" active={sort.key === "poolPoints"} dir={sort.dir} onSort={sort.toggle} accent spotlight />
                    <SortableHead label="PTS/G" colKey="poolPtsPerGame" active={sort.key === "poolPtsPerGame"} dir={sort.dir} onSort={sort.toggle} accent />
                    <SortableHead label="W" colKey="wins" active={sort.key === "wins"} dir={sort.dir} onSort={sort.toggle} groupStart />
                    <SortableHead label="OTL" colKey="otLosses" active={sort.key === "otLosses"} dir={sort.dir} onSort={sort.toggle} />
                    <SortableHead label="SO" colKey="shutouts" active={sort.key === "shutouts"} dir={sort.dir} onSort={sort.toggle} />
                    <SortableHead label="GP" colKey="gamesPlayed" active={sort.key === "gamesPlayed"} dir={sort.dir} onSort={sort.toggle} groupStart />
                    <SortableHead label="G" colKey="goals" active={sort.key === "goals"} dir={sort.dir} onSort={sort.toggle} />
                    <SortableHead label="A" colKey="assists" active={sort.key === "assists"} dir={sort.dir} onSort={sort.toggle} />
                    <SortableHead label="PTS" colKey="nhlPoints" active={sort.key === "nhlPoints"} dir={sort.dir} onSort={sort.toggle} spotlight />
                    <SortableHead label="PTS/G" colKey="nhlPtsPerGame" active={sort.key === "nhlPtsPerGame"} dir={sort.dir} onSort={sort.toggle} />
                    <SortableHead label="+/-" colKey="plusMinus" active={sort.key === "plusMinus"} dir={sort.dir} onSort={sort.toggle} groupStart />
                    <SortableHead label="PIM" colKey="pim" active={sort.key === "pim"} dir={sort.dir} onSort={sort.toggle} />
                    <SortableHead label="SOG" colKey="shots" active={sort.key === "shots"} dir={sort.dir} onSort={sort.toggle} />
                    <SortableHead label="GAA" colKey="gaa" active={sort.key === "gaa"} dir={sort.dir} onSort={sort.toggle} />
                    <SortableHead label="SV%" colKey="svPct" active={sort.key === "svPct"} dir={sort.dir} onSort={sort.toggle} />
                    <SortableHead label="Salary" colKey="capHit" active={sort.key === "capHit"} dir={sort.dir} onSort={sort.toggle} groupStart />
                    <SortableHead label="$/PTS" colKey="costPerPoint" active={sort.key === "costPerPoint"} dir={sort.dir} onSort={sort.toggle} />
                  </tr>
                </thead>
                <tbody>
                  {sort.sorted.map((r) => (
                    <tr key={r.id}>
                      <td className="stats-col-player">
                        <button type="button" className="stats-player-btn" onClick={() => setOpenPlayerId(r.id)}>
                          <span className="stats-player-name">{r.name}</span>
                          <span className={`stats-player-pos pos-compact-${posGroupClass(r.position)}`}>
                            {posGroup(r.position)}
                          </span>
                        </button>
                      </td>
                      <td className="accent stats-group-start">{r.poolGamesPlayed}</td>
                      <td className="accent">{r.poolGoals}</td>
                      <td className="accent">{r.poolAssists}</td>
                      <td className="accent stats-col-spotlight">{r.poolPoints}</td>
                      <td className="accent">{displayRate(r.poolPtsPerGame, 2)}</td>
                      <td className="stats-group-start">{r.isGoalie ? r.wins : "—"}</td>
                      <td>{r.isGoalie ? r.otLosses : "—"}</td>
                      <td>{r.isGoalie ? r.shutouts : "—"}</td>
                      <td className="stats-group-start">{r.gamesPlayed}</td>
                      <td>{r.goals}</td>
                      <td>{r.assists}</td>
                      <td className="stats-col-spotlight">{r.nhlPoints}</td>
                      <td>{displayRate(r.nhlPtsPerGame, 2)}</td>
                      <td className="stats-group-start">{signed(r.plusMinus)}</td>
                      <td>{r.pim}</td>
                      <td>{r.shots}</td>
                      <td>{displayRate(r.gaa, 2)}</td>
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
                    <td className="accent stats-group-start">{poolGp}</td>
                    <td className="accent">{poolGoalsTotal}</td>
                    <td className="accent">{poolAssistsTotal}</td>
                    <td className="accent stats-col-spotlight">{poolPtsTotal}</td>
                    <td className="accent">{displayRate(poolGp > 0 ? poolPtsTotal / poolGp : null, 2)}</td>
                    <td className="stats-group-start">{winsTotal}</td>
                    <td>{otLossesTotal}</td>
                    <td>{shutoutsTotal}</td>
                    <td className="stats-group-start">{nhlGp}</td>
                    <td>{nhlGoalsTotal}</td>
                    <td>{nhlAssistsTotal}</td>
                    <td className="stats-col-spotlight">{nhlPtsTotal}</td>
                    <td>{displayRate(nhlGp > 0 ? nhlPtsTotal / nhlGp : null, 2)}</td>
                    <td className="stats-group-start">{signed(plusMinusTotal)}</td>
                    <td>{pimTotal}</td>
                    <td>{shotsTotal}</td>
                    <td>{displayRate(goaliesGp > 0 ? goaliesGa / goaliesGp : null, 2)}</td>
                    <td>{displayRate(goaliesShotsAgainst > 0 ? goaliesSaves / goaliesShotsAgainst : null, 3, true)}</td>
                    <td className="stats-group-start">{formatMoneyCompact(capTotal)}</td>
                    <td>{poolPtsTotal > 0 ? formatMoneyCompact(capTotal / poolPtsTotal) : "—"}</td>
                  </tr>
                </tfoot>
              </table>
            </div>
          )}
        </div>
      )}
      {openPlayerId != null && (
        <PlayerCard playerId={openPlayerId} onClose={() => setOpenPlayerId(null)} />
      )}
    </section>
  );
}
