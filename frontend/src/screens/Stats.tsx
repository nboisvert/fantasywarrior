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
import { api, formatCap, formatShortName, posGroup, posGroupClass } from "../api";
import type { LeagueDetail, LineupDto, LineupEntry, PlayerSeasonStatsRow } from "../api";
import { LoadingLogo } from "../components/LoadingLogo";
import { PlayerCard } from "../components/PlayerCard";
import {
  ArrowDownIcon, ArrowLeftIcon, ArrowUpIcon, ChevronDownIcon, CircleCheckIcon, CircleIcon, InfoIcon,
} from "../components/Icons";

/** Slots used per position group, for the "9/9 F · 4/4 D · 1/1 G" counter. */
function countUsed(entries: LineupEntry[], activeSpotIds: string[]): Record<string, number> {
  const active = new Set(activeSpotIds);
  const used: Record<string, number> = { F: 0, D: 0, G: 0 };
  for (const e of entries) if (active.has(e.spotId)) used[e.positionGroup] = (used[e.positionGroup] ?? 0) + 1;
  return used;
}


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

/** Whether this spot's lineup status changes when the week rolls over. */
type PendingChange = "in" | "out" | null;

/** Active/bench control on a player row.
 *
 * The icon is **this week's** state — who is scoring for you right now. The
 * corner arrow is next week's: green up for a player joining the lineup, red
 * down for one dropping out. Two weeks in one control, but only one of them
 * can still be changed, which is why tapping opens next week's picker while
 * the icon keeps showing today.
 */
function LineupToggle({
  entry, editable, busy, pending, onToggle,
}: {
  entry: LineupEntry;
  editable: boolean;
  busy: boolean;
  pending: PendingChange;
  onToggle: (spotId: string) => void;
}) {
  const now = entry.active ? "active" : "benched";
  const change =
    pending === "in" ? ", coming into next week's lineup"
    : pending === "out" ? ", dropping out of next week's lineup"
    : "";
  const label = `${entry.name} — ${now}${change}${editable ? ", tap to change next week" : ""}`;
  const className = `lineup-toggle${entry.active ? " on" : ""}${editable ? "" : " static"}`;
  const icon = entry.active ? <CircleCheckIcon size={16} /> : <CircleIcon size={16} />;

  // The arrow carries direction *and* position *and* colour, so it never
  // depends on colour alone; the aria-label above states it in words.
  const flag = pending && (
    <span className={`lineup-pending lineup-pending-${pending}`} aria-hidden="true">
      {pending === "in" ? <ArrowUpIcon size={13} /> : <ArrowDownIcon size={13} />}
    </span>
  );

  if (!editable)
    return (
      <span className={className} title={label} aria-label={label}>
        {icon}
        {flag}
      </span>
    );
  return (
    <button
      type="button"
      className={className}
      onClick={() => onToggle(entry.spotId)}
      disabled={busy}
      aria-pressed={entry.active}
      aria-label={label}
      title={label}
    >
      {icon}
      {flag}
    </button>
  );
}

/** Who can take this spot's place in the lineup.
 *
 * Same position group only — the slots are per position, so a forward can
 * never stand in for a defenceman and offering him would produce a lineup the
 * server rejects. Best first, since that is the choice being made. */
function replacementsFor(entry: LineupEntry, entries: LineupEntry[]): LineupEntry[] {
  return entries
    .filter((e) => e.spotId !== entry.spotId && e.positionGroup === entry.positionGroup && e.active !== entry.active)
    .sort((a, b) => b.seasonPoints - a.seasonPoints);
}

/** The replacement sheet: pick who comes in, or bench outright.
 *
 * Opens on the lineup control rather than acting on it directly, because the
 * interesting question is not "is this player in" but "who plays instead" —
 * and answering it in one step avoids ever writing a lineup with a hole in it. */
function LineupPicker({
  entry, entries, slotIsFree, busy, onSwap, onBench, onClose,
}: {
  entry: LineupEntry;
  entries: LineupEntry[];
  slotIsFree: boolean;
  busy: boolean;
  onSwap: (inSpotId: string) => void;
  onBench: () => void;
  onClose: () => void;
}) {
  const options = replacementsFor(entry, entries);
  const title = entry.active ? `Replace ${formatShortName(entry.name)}` : `Bring in ${formatShortName(entry.name)}`;

  return (
    <div className="lineup-picker-overlay" role="dialog" aria-modal="true" aria-label={title} onClick={onClose}>
      <div className="lineup-picker" onClick={(e) => e.stopPropagation()}>
        <div className="lineup-picker-head">
          <span className={`roster-pos-pill roster-pos-pill-${entry.positionGroup.toLowerCase()}`}>
            {entry.positionGroup}
          </span>
          <span className="lineup-picker-title">{title}</span>
        </div>

        {entry.active ? (
          <>
            <button type="button" className="lineup-picker-option" disabled={busy} onClick={onBench}>
              <span className="lineup-picker-name">Bench, leave the slot open</span>
            </button>
            {options.length > 0 && <span className="lineup-picker-sub">or swap in</span>}
          </>
        ) : slotIsFree ? (
          <button type="button" className="lineup-picker-option" disabled={busy} onClick={() => onSwap(entry.spotId)}>
            <span className="lineup-picker-name">Activate — a {entry.positionGroup} slot is open</span>
          </button>
        ) : (
          <span className="lineup-picker-sub">
            Every {entry.positionGroup} slot is taken. Choose who sits out.
          </span>
        )}

        {options.map((o) => (
          <button
            key={o.spotId}
            type="button"
            className="lineup-picker-option"
            disabled={busy}
            onClick={() => onSwap(o.spotId)}
          >
            <span className="lineup-picker-name">{formatShortName(o.name)}</span>
            <span className="muted">{o.team ?? "—"}</span>
            <span className="lineup-picker-pts">{o.seasonPoints} pts</span>
          </button>
        ))}

        {entry.active && options.length === 0 && (
          <span className="lineup-picker-sub">Nobody on the bench plays {entry.positionGroup}.</span>
        )}

        <button type="button" className="btn-ghost lineup-picker-cancel" onClick={onClose}>
          Cancel
        </button>
      </div>
    </div>
  );
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
  const [lineup, setLineup] = useState<LineupDto | null>(null);
  const [nextLineup, setNextLineup] = useState<LineupDto | null>(null);
  const [lineupError, setLineupError] = useState("");
  const [saving, setSaving] = useState(false);
  // Which lineup control is open. One at a time: this is a choice about a
  // single slot, not a multi-select.
  const [pickerSpotId, setPickerSpotId] = useState<string | null>(null);

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

  // Two weeks are in play at once, and keeping them apart is the whole point:
  //
  //   `lineup`     — the week being played. What the grid's icons show. Frozen
  //                  once the week starts, so it is never what a tap edits.
  //   `nextLineup` — the week being prepared. What the picker writes, and what
  //                  the arrow on a row is comparing against.
  //
  // Showing next week's set in the icons instead would answer the wrong
  // question: on the Team screen you are looking at who is scoring for you
  // right now, and the arrow is what says "this changes on Monday".
  useEffect(() => {
    let ignore = false;
    setLineup(null);
    setNextLineup(null);
    api
      .lineup(league.id, targetUsername, username)
      .then(async (current) => {
        if (ignore) return;
        setLineup(current);
        if (!current.isOwner) return;
        const next = current.periods.find((p) => p.index === current.periodIndex + 1);
        if (!next) return;
        const upcoming = await api.lineup(league.id, targetUsername, username, next.index);
        if (!ignore) setNextLineup(upcoming);
      })
      // A league with no period calendar yet simply has no lineup to show;
      // that must not take the stats grid down with it.
      .catch(() => !ignore && setLineup(null));
    return () => {
      ignore = true;
    };
  }, [league.id, targetUsername, username]);

  /** Writes **next week's** active set: optimistic locally, PUT, roll back on
   * refusal. Never the week on screen — that one is already locked. */
  async function applyActiveSet(next: string[]) {
    if (!nextLineup) return;
    const wanted = new Set(next);
    const previous = nextLineup;
    setNextLineup({
      ...nextLineup,
      entries: nextLineup.entries.map((e) => ({ ...e, active: wanted.has(e.spotId) })),
      used: countUsed(nextLineup.entries, next),
      setBy: username,
    });
    setLineupError("");
    setSaving(true);
    try {
      await api.setLineup(league.id, targetUsername, nextLineup.periodIndex, next);
    } catch (e: unknown) {
      setNextLineup(previous);
      setLineupError(e instanceof Error ? e.message : "Could not save the lineup.");
    } finally {
      setSaving(false);
    }
  }

  /** Swaps one player out and another in, in a single write.
   *
   * One call rather than a bench-then-activate pair: the intermediate state
   * has a slot free and would be a legal lineup the GM never chose, and if the
   * second half failed he would silently be a player short. */
  const swap = (outSpotId: string, inSpotId: string) =>
    applyActiveSet([
      ...(nextLineup?.entries ?? []).filter((e) => e.active && e.spotId !== outSpotId).map((e) => e.spotId),
      inSpotId,
    ]);

  /** Benching or activating outright — only legal when a slot is free. */
  const setActive = (spotId: string, active: boolean) =>
    applyActiveSet(
      active
        ? [...(nextLineup?.entries ?? []).filter((e) => e.active).map((e) => e.spotId), spotId]
        : (nextLineup?.entries ?? []).filter((e) => e.active && e.spotId !== spotId).map((e) => e.spotId),
    );

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
      // `?? 0` guards the window where the deployed API predates these fields:
      // Pages deploys on push, Cloud Run is manual, so the two are briefly out
      // of step and undefined here would render NaN across the grid.
      poolGamesPlayed: p.spotActiveGamesPlayed ?? 0,
      poolGoals: p.spotActiveGoals ?? 0,
      poolAssists: p.spotActiveAssists ?? 0,
      poolPoints: p.spotActivePoints ?? 0,
      poolPtsPerGame: p.spotActiveGamesPlayed > 0 ? p.spotActivePoints / p.spotActiveGamesPlayed : null,
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
      costPerPoint: p.capHit != null && p.spotActivePoints > 0 ? p.capHit / p.spotActivePoints : null,
    };
  });

  const sort = useSort<PlayerRow>(rows, "poolPoints");

  // The grid is keyed by playerId; the lineup by roster-spot id. One open spot
  // per player per team, so this mapping is unambiguous.
  const lineupBySpotPlayer = new Map<number, LineupEntry>(
    (lineup?.hidden ? [] : (lineup?.entries ?? [])).map((e) => [e.playerId, e]),
  );

  /** Next week's status per spot, for the arrow. Keyed by spot rather than by
   * player: a player traded away and back would be two spots, and only the one
   * still held should be compared. */
  const nextActiveBySpot = new Map<string, boolean>(
    (nextLineup?.hidden ? [] : (nextLineup?.entries ?? [])).map((e) => [e.spotId, e.active]),
  );

  /** In, out, or unchanged when the week rolls over. Null while next week has
   * not loaded — an arrow that guessed would be worse than no arrow. */
  const pendingFor = (entry: LineupEntry): PendingChange => {
    const next = nextActiveBySpot.get(entry.spotId);
    if (next === undefined || next === entry.active) return null;
    return next ? "in" : "out";
  };

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
      </div>

      {/* The week bar (its number, dates, lock pill, slot counter and points)
          was removed 2026-08-02 per Nick — a good idea to bring back later, but
          the width it took is what the lineup controls needed. The slot counter
          is the one piece genuinely missed; the picker enforces the same limits
          at the point of choosing, which is where it matters most. */}
      {lineupError && <p className="error-banner">{lineupError}</p>}

      {loading && <LoadingLogo label="Loading stats…" />}
      {!loading && error && <p className="error-banner">{error}</p>}

      {!loading && !error && (
        <div>
          <span className="stats-table-title">
            Roster
            <span className="stats-table-title-sub">
              {" "}
              {rows.length}
              {maxRosterSize != null ? ` / ${maxRosterSize}` : ""} player
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
                        {lineupBySpotPlayer.get(r.id) && (
                          <LineupToggle
                            entry={lineupBySpotPlayer.get(r.id)!}
                            // Editable whenever *next* week is: this week's own
                            // lock is irrelevant, since a tap never touches it.
                            editable={!!nextLineup && !nextLineup.locked && nextLineup.isOwner}
                            busy={saving}
                            pending={pendingFor(lineupBySpotPlayer.get(r.id)!)}
                            onToggle={setPickerSpotId}
                          />
                        )}
                        <button type="button" className="stats-player-btn" onClick={() => setOpenPlayerId(r.id)}>
                          <span className="stats-player-name">{formatShortName(r.name)}</span>
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
      {pickerSpotId && lineup && (() => {
        const entry = lineup.entries.find((e) => e.spotId === pickerSpotId);
        if (!entry) return null;
        const max =
          entry.positionGroup === "F" ? lineup.slots.forwards
          : entry.positionGroup === "D" ? lineup.slots.defense
          : lineup.slots.goalies;
        const close = () => setPickerSpotId(null);
        return (
          <LineupPicker
            entry={entry}
            entries={lineup.entries}
            slotIsFree={(lineup.used[entry.positionGroup] ?? 0) < max}
            busy={saving}
            onSwap={(inSpotId) => {
              close();
              // Activating a benched player into a free slot is the one case
              // with nobody to take out.
              if (inSpotId === entry.spotId) setActive(entry.spotId, true);
              else if (entry.active) swap(entry.spotId, inSpotId);
              else swap(inSpotId, entry.spotId);
            }}
            onBench={() => {
              close();
              setActive(entry.spotId, false);
            }}
            onClose={close}
          />
        );
      })()}

      {openPlayerId != null && (
        <PlayerCard playerId={openPlayerId} onClose={() => setOpenPlayerId(null)} />
      )}
    </section>
  );
}
