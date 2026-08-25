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

import { Fragment, useEffect, useLayoutEffect, useRef, useState } from "react";
import { api, formatCap, formatShortName, posGroup, posGroupClass } from "../api";
import type {
  InjuryFields, LeagueDetail, LineupDto, LineupEntry, PlayerPeriods as PlayerPeriodsDto,
  PlayerSeasonStatsRow,
} from "../api";

type InjuryStatus = NonNullable<InjuryFields["injuryStatus"]>;
import { LoadingLogo } from "../components/LoadingLogo";
import { PlayerCard } from "../components/PlayerCard";
import {
  ArrowDownIcon, ArrowLeftIcon, ArrowUpIcon, ChevronDownIcon, CircleCheckIcon, CircleIcon, InfoIcon,
  CalendarIcon, CrossIcon, GavelIcon, LogInIcon, LogOutIcon,
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
 * cap-hit/cost columns and the cap-gauge disclosure below. */
function formatMoneyCompact(amount: number): string {
  const abs = Math.abs(amount);
  if (abs >= 1_000_000) return `$${(abs / 1_000_000).toFixed(1)}M`;
  if (abs >= 1_000) return `$${Math.round(abs / 1_000)}K`;
  return `$${abs}`;
}

/** What happens to this spot when the week rolls over: it joins the lineup, it
 * drops out of it, or the player leaves the team altogether. */
type PendingChange = "in" | "out" | "gone" | null;

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
  // A player who leaves before next week has no lineup row to set, so there is
  // nothing to tap even when the week itself is open. Shown inert rather than
  // hidden: a slot going quietly empty is the confusing outcome.
  const gone = pending === "gone";
  const canTap = editable && !gone;

  const now = entry.active ? "active" : "benched";
  const change =
    pending === "in" ? ", coming into next week's lineup"
    : pending === "out" ? ", dropping out of next week's lineup"
    : gone ? ", leaving the team before next week"
    : "";
  const label = `${entry.name} — ${now}${change}${canTap ? ", tap to change next week" : ""}`;
  const className = `lineup-toggle${entry.active ? " on" : ""}${canTap ? "" : " static"}`;
  const icon = entry.active ? <CircleCheckIcon size={16} /> : <CircleIcon size={16} />;

  // The mark carries shape *and* position *and* colour, so it never depends on
  // colour alone; the aria-label above states it in words.
  const flag = pending && (
    <span className={`lineup-pending lineup-pending-${pending}`} aria-hidden="true">
      {pending === "in" ? <ArrowUpIcon size={13} />
        : pending === "out" ? <ArrowDownIcon size={13} />
        : <LogOutIcon size={13} />}
    </span>
  );

  if (!canTap)
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
 * server rejects. Best first, since that is the choice being made.
 *
 * **These come from the week being written, not the week on screen.** They used
 * to come from the current one, which was wrong every time the two rosters
 * differed: a player traded away was offered as a replacement for a week he
 * would not play, and the man arriving in the same trade could not be picked at
 * all.
 *
 * A departing player stays in the list rather than disappearing from it —
 * `leaving` marks him and the option is inert. Watching a name vanish and a
 * slot go quietly empty is worse than being told why. */
function replacementsFor(entry: LineupEntry, entries: LineupEntry[]): LineupEntry[] {
  return entries
    .filter((e) => e.spotId !== entry.spotId && e.positionGroup === entry.positionGroup)
    .filter((e) => e.leaving || e.active !== entry.active)
    .sort((a, b) => Number(a.leaving ?? false) - Number(b.leaving ?? false) || b.seasonPoints - a.seasonPoints);
}

/** The replacement sheet: pick who comes in, or bench outright.
 *
 * Opens on the lineup control rather than acting on it directly, because the
 * interesting question is not "is this player in" but "who plays instead" —
 * and answering it in one step avoids ever writing a lineup with a hole in it. */
function LineupPicker({
  entry, entries, slotIsFree, busy, periodIndex, onSwap, onBench, onClose,
}: {
  entry: LineupEntry;
  entries: LineupEntry[];
  slotIsFree: boolean;
  busy: boolean;
  /** The week this picker actually writes to — always "next week", never the
   * one on screen. Named in the title so it's unambiguous which week is being
   * set. */
  periodIndex: number;
  onSwap: (inSpotId: string) => void;
  onBench: () => void;
  onClose: () => void;
}) {
  const options = replacementsFor(entry, entries);
  const title = entry.active
    ? `Replace ${formatShortName(entry.name)} for week ${periodIndex}`
    : `Bring in ${formatShortName(entry.name)} for week ${periodIndex}`;

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
            className={`lineup-picker-option${o.leaving ? " gone" : ""}`}
            disabled={busy || o.leaving}
            aria-label={
              o.leaving
                ? `${o.name} — leaves the team before week ${periodIndex}, cannot be selected`
                : undefined
            }
            onClick={() => onSwap(o.spotId)}
          >
            <span className="lineup-picker-name-wrap">
              <span className="lineup-picker-name">{formatShortName(o.name)}</span>
              {/* The same icon-and-colour pairing the grid's trade marks use,
                  so it reads as one convention rather than two. */}
              {o.leaving && (
                <span className="lineup-picker-note leaving">
                  <LogOutIcon size={11} />
                  leaves the team
                </span>
              )}
              {o.arriving && (
                <span className="lineup-picker-note arriving">
                  <LogInIcon size={11} />
                  arriving via trade
                </span>
              )}
            </span>
            <span className="muted">{o.team ?? "—"}</span>
            <span className="lineup-picker-pts">{o.seasonPoints} pts</span>
          </button>
        ))}

        {entry.active && options.length === 0 && (
          <span className="lineup-picker-sub">Nobody on the bench plays {entry.positionGroup}.</span>
        )}

        <button type="button" className="btn-outline lineup-picker-cancel" onClick={onClose}>
          Cancel
        </button>
      </div>
    </div>
  );
}

/** The infirmary mark, sitting immediately after the name — which is what
 * squeezes the name into an ellipsis when the column runs short (2026-08-04,
 * per Nick: the mark matters more than the last letters of a surname).
 *
 * Two symbols, one colour: hurt and suspended both keep a player out of the
 * lineup, so both rows carry the same rose bar, but a gavel never claims a
 * suspended man is injured. The kind is decided server-side — see
 * InjuryClassifier — so this only picks a glyph. */
function InjuryMark({ status, type }: { status: InjuryStatus; type: string | null }) {
  const kind = status === "Suspended" ? "Suspended" : "Injured";
  const label = type ? `${kind} — ${type}` : kind;
  return (
    <span className="stats-injury" role="img" aria-label={label} title={label}>
      {status === "Suspended" ? <GavelIcon size={11} /> : <CrossIcon size={10} />}
    </span>
  );
}

/** The trade mark, sitting after the name (and after the injury mark, if both
 * apply — a player can be hurt and already promised away at once).
 *
 * Rose/log-out for leaving, green/log-in for arriving — the same pairing this
 * screen already uses for next week's lineup arrow (`.lineup-pending-in/-out`),
 * so it reads as a continuation of an existing convention rather than a new
 * one. Icon + aria-label always accompany the colour. */
function TradeMarkIcon({ direction }: { direction: "out" | "in" }) {
  const label = direction === "out" ? "Leaving via trade" : "Arriving via trade";
  return (
    <span className={`stats-trade stats-trade-${direction}`} role="img" aria-label={label} title={label}>
      {direction === "out" ? <LogOutIcon size={11} /> : <LogInIcon size={11} />}
    </span>
  );
}

/** "Nov 10" */
const shortDate = (iso: string) =>
  new Date(`${iso}T12:00:00Z`).toLocaleDateString(undefined, {
    month: "short", day: "numeric", timeZone: "UTC",
  });

/** A player's season with this team, one row per week.
 *
 * The point of the panel is the active column, not the stats: a season total
 * says what a player produced, this says which weeks his GM actually fielded
 * him for — and therefore what the bench cost. Every row is one
 * RosterAssignment, which is the only grain this app stores.
 */
function PlayerPeriods({ data, isGoalie }: { data: PlayerPeriodsDto; isGoalie: boolean }) {
  const { periods, totals } = data;
  if (periods.length === 0)
    return <p className="muted player-periods-empty">No weeks scored for this player yet.</p>;

  return (
    <div className="player-periods">
      <table className="player-periods-table">
        <thead>
          <tr>
            <th>Week</th>
            <th aria-label="In the lineup" />
            <th>GP</th>
            {isGoalie ? <><th>W</th><th>SV</th></> : <><th>G</th><th>A</th></>}
            <th className="player-periods-pts">PT</th>
          </tr>
        </thead>
        <tbody>
          {periods.map((p) => (
            <tr key={p.periodIndex} className={p.active ? "" : "benched"}>
              <td>
                <span className="player-periods-week">W{String(p.periodIndex).padStart(2, "0")}</span>
                <span className="muted"> {shortDate(p.startDate)}</span>
              </td>
              <td>
                {p.active
                  ? <CircleCheckIcon size={13} className="player-periods-in" />
                  : <CircleIcon size={13} className="player-periods-out" />}
              </td>
              {/* A break week has no games at all; a zero there is the schedule,
                  not the player. */}
              <td>{p.gameCount === 0 ? <span className="muted">—</span> : p.gamesPlayed}</td>
              {isGoalie ? <><td>{p.wins}</td><td>{p.saves}</td></> : <><td>{p.goals}</td><td>{p.assists}</td></>}
              <td className="player-periods-pts">{p.points}</td>
            </tr>
          ))}
        </tbody>
      </table>

      <div className="player-periods-totals">
        <span>
          <strong>{totals.activePoints}</strong> pts over {totals.activeWeeks} week
          {totals.activeWeeks === 1 ? "" : "s"} in the lineup
        </span>
        {totals.benchPoints > 0 && (
          /* The number this panel exists to surface: what he scored while his
             GM had him benched. */
          <span className="player-periods-bench">
            {totals.benchPoints} left on the bench over {totals.benchedWeeks} week
            {totals.benchedWeeks === 1 ? "" : "s"}
          </span>
        )}
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
  // Never played an NHL game. Pinned below the roster and held out of the sort
  // entirely — see `withProspectsLast`. Computed by the API, not here: it is a
  // fact about the player's career, not about this screen.
  isProspect: boolean;
  // The Équipe slot: one row per team, holding an NHL franchise instead of a
  // player. Only the three Record columns and the Fantasy point PTS carry a
  // number (Nick, 2026-08-05) — a franchise has no goals, no cap hit and no
  // injury, and the row shows a dash for each rather than a zero that would
  // read as "played and produced nothing".
  isFranchise: boolean;
  logoUrl: string | null;
  teamWins: number;
  teamLosses: number;
  teamOtLosses: number;
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
  // Out right now, per the news sources — null when he is fit. Not a stat, so
  // it sorts nowhere and totals nothing; it only marks the row.
  injuryStatus: InjuryStatus | null;
  injuryType: string | null;
  // Moving via an accepted trade not yet executed by the nightly job. "out" on
  // the current roster (leaving), "in" on the Incoming grid (arriving). Never
  // derived from `engaged` alone — that flag is symmetric across both sides of
  // a trade, so the caller building the row says which grid it's for.
  tradeMark: "out" | "in" | null;
  // Cap hit — the contract figure for the *league's* season, never a later
  // year's. Contracts run years ahead (Eichel is $10M in 2025-26 and $13.5M
  // from 2026-27), so taking the newest one on file made this disagree with
  // the player card about the same player.
  capHit: number | null;
  // Against NHL points, not Fantasy point — the question this answers is
  // "was he worth his real production", which a fantasy-scoped denominator
  // (already this team's own rule config, already shrunk by benched weeks)
  // would double up on.
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
/** F, then D, then G — the order a GM reads his own bench in. */
const PROSPECT_POSITION_ORDER = ["F", "D", "G"];

/**
 * Prospects last, always, in a fixed order the sort never touches (Nick,
 * 2026-08-25).
 *
 * They are held out of the sort rather than merely sorted to the bottom: a GM
 * ranking his roster by points is not ranking twenty players who have never
 * dressed, and a zone that keeps exactly the same shape whatever the sort is
 * the thing the accent border can honestly delimit. Sorting inside it would
 * make that border move under the reader's thumb.
 *
 * Position group first, then name — and never the current sort key, which is
 * the whole point.
 */
function withProspectsLast(rows: PlayerRow[]): PlayerRow[] {
  const roster = rows.filter((r) => !r.isProspect);
  const prospects = rows
    .filter((r) => r.isProspect)
    .sort((a, b) => {
      const byPosition =
        PROSPECT_POSITION_ORDER.indexOf(posGroup(a.position)) -
        PROSPECT_POSITION_ORDER.indexOf(posGroup(b.position));
      return byPosition !== 0 ? byPosition : a.name.localeCompare(b.name);
    });
  return [...roster, ...prospects];
}


/** Top row of the two-row grouped header: a label spanning the columns
 * belonging to one data source (Fantasy point / NHL season totals / Extra
 * counting stats / Cap hit), so the grid reads as "where did this number come
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
  /** First column of a group (Fantasy point/NHL/Extra/Cap hit) — draws the
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


/**
 * The Roster/Departed/Incoming grids are three separate `<table>`s, and
 * `table-layout: auto` sizes each one's player column from *its own* content
 * alone. Structurally identical rows (a toggle-or-spacer, the name, the
 * calendar button, the position pill) still land at different widths across
 * tables whenever the widest name differs between the lists — which it
 * usually does — so the sticky column's right edge visibly jumps between
 * sections (Nick, 2026-08-25, confirmed by measuring the live DOM: Roster's
 * column rendered 211px, Departed's 188px, on identical markup).
 *
 * Fixed by measuring the widest cell across *all* currently-mounted grids and
 * forcing every `.stats-col-player` to that one width via `--stats-player-w`
 * — see the `width: var(--stats-player-w)` rule in App.css, which only
 * engages once this has a value; unset, cells keep sizing off `min-width` the
 * way the measurement below needs them to.
 *
 * Runs once per data load (mount, refetch after a trade or a test-mode
 * advance) rather than continuously: the inputs that change a name's width —
 * which players are on the grid — are exactly `players`/`departed`/`incoming`
 * changing, and re-measuring on every incidental re-render (opening a
 * periods panel, ticking `saving`) would be pure waste.
 */
function useSharedPlayerColumnWidth(deps: readonly unknown[]) {
  const ref = useRef<HTMLElement>(null);

  useLayoutEffect(() => {
    const root = ref.current;
    if (!root) return;
    const cells = root.querySelectorAll<HTMLElement>("tbody .stats-col-player, tfoot .stats-col-player");
    if (cells.length === 0) return;
    let widest = 0;
    for (const cell of cells) widest = Math.max(widest, cell.getBoundingClientRect().width);
    root.style.setProperty("--stats-player-w", `${widest}px`);
    // deps is the caller's contract (which data actually changes a name's
    // width), not a literal array this closure reads from directly.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps);

  return ref;
}

/** The roster grid: one sortable table with its own totals footer.
 *
 * A component rather than two copies of the markup, because this table changes
 * often — twice on 2026-08-02 alone (short names, then Salary renamed to Cap
 * hit), and before that the skater/goalie merge and several column revisions.
 * Twenty-one columns and a derived footer duplicated would mean every future
 * change is two edits, and forgetting one drifts silently rather than breaking.
 *
 * Used for the current roster and for departed players, whose banked points
 * this team keeps. The lineup props are optional: a player who has left has no
 * lineup state, so his rows simply carry no toggle.
 */
function RosterGrid({
  rows, title, subtitle, emptyLabel,
  lineupByPlayer, pendingFor, lineupEditable, saving, onToggleLineup,
  onOpenPlayer, openPeriodsFor, periodsByPlayer, onTogglePeriods,
}: {
  rows: PlayerRow[];
  title: string;
  subtitle?: string;
  emptyLabel: string;
  lineupByPlayer?: Map<number, LineupEntry>;
  pendingFor?: (entry: LineupEntry) => PendingChange;
  lineupEditable?: boolean;
  saving: boolean;
  onToggleLineup?: (spotId: string) => void;
  onOpenPlayer: (playerId: number) => void;
  openPeriodsFor: number | null;
  periodsByPlayer: Record<number, PlayerPeriodsDto>;
  onTogglePeriods: (playerId: number) => void;
}) {
  // Each grid sorts independently — that is the main thing two instances need
  // that one shared table could not give.
  const sort = useSort<PlayerRow>(rows, "poolPoints");

  // The order actually rendered: whatever the sort produced, then the prospect
  // zone bolted to the bottom. `firstProspectId` is only ever used to hang the
  // divider on the row that opens that zone.
  const ordered = withProspectsLast(sort.sorted);
  const firstProspectId = ordered.find((r) => r.isProspect)?.id ?? null;

  // Totals are derived from this grid's own rows, so the departed grid foots to
  // what those players actually banked rather than repeating the roster's.
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

  return (
    <div>
      <span className="stats-table-title">
        {title}
        {subtitle != null && <span className="stats-table-title-sub"> {subtitle}</span>}
      </span>
      {rows.length === 0 ? (
        <p className="empty-state">{emptyLabel}</p>
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
              {/* "Record", not "Goalie", since 2026-08-05: the Équipe slot
                  reports W/L/OTL in the same three columns, and a goalie's
                  W/OTL/SO was always a record anyway. L is filled for the
                  franchise alone — no goalie loss total is stored. */}
              <GroupHead label="Record" span={4} />
              <GroupHead label="NHL" span={5} />
              <GroupHead label="Extra" span={5} />
              <GroupHead label="Cap hit" span={2} />
            </tr>
            <tr>
              <SortableHead label="GP" colKey="poolGamesPlayed" active={sort.key === "poolGamesPlayed"} dir={sort.dir} onSort={sort.toggle} accent groupStart />
              <SortableHead label="G" colKey="poolGoals" active={sort.key === "poolGoals"} dir={sort.dir} onSort={sort.toggle} accent />
              <SortableHead label="A" colKey="poolAssists" active={sort.key === "poolAssists"} dir={sort.dir} onSort={sort.toggle} accent />
              <SortableHead label="PT" colKey="poolPoints" active={sort.key === "poolPoints"} dir={sort.dir} onSort={sort.toggle} accent spotlight />
              <SortableHead label="PT/G" colKey="poolPtsPerGame" active={sort.key === "poolPtsPerGame"} dir={sort.dir} onSort={sort.toggle} accent />
              <SortableHead label="W" colKey="wins" active={sort.key === "wins"} dir={sort.dir} onSort={sort.toggle} groupStart />
              <SortableHead label="L" colKey="teamLosses" active={sort.key === "teamLosses"} dir={sort.dir} onSort={sort.toggle} />
              <SortableHead label="OTL" colKey="otLosses" active={sort.key === "otLosses"} dir={sort.dir} onSort={sort.toggle} />
              <SortableHead label="SO" colKey="shutouts" active={sort.key === "shutouts"} dir={sort.dir} onSort={sort.toggle} />
              <SortableHead label="GP" colKey="gamesPlayed" active={sort.key === "gamesPlayed"} dir={sort.dir} onSort={sort.toggle} groupStart />
              <SortableHead label="G" colKey="goals" active={sort.key === "goals"} dir={sort.dir} onSort={sort.toggle} />
              <SortableHead label="A" colKey="assists" active={sort.key === "assists"} dir={sort.dir} onSort={sort.toggle} />
              <SortableHead label="PT" colKey="nhlPoints" active={sort.key === "nhlPoints"} dir={sort.dir} onSort={sort.toggle} spotlight />
              <SortableHead label="PT/G" colKey="nhlPtsPerGame" active={sort.key === "nhlPtsPerGame"} dir={sort.dir} onSort={sort.toggle} />
              <SortableHead label="+/-" colKey="plusMinus" active={sort.key === "plusMinus"} dir={sort.dir} onSort={sort.toggle} groupStart />
              <SortableHead label="PIM" colKey="pim" active={sort.key === "pim"} dir={sort.dir} onSort={sort.toggle} />
              <SortableHead label="SOG" colKey="shots" active={sort.key === "shots"} dir={sort.dir} onSort={sort.toggle} />
              <SortableHead label="GAA" colKey="gaa" active={sort.key === "gaa"} dir={sort.dir} onSort={sort.toggle} />
              <SortableHead label="SV%" colKey="svPct" active={sort.key === "svPct"} dir={sort.dir} onSort={sort.toggle} />
              <SortableHead label="Cap hit" colKey="capHit" active={sort.key === "capHit"} dir={sort.dir} onSort={sort.toggle} groupStart />
              <SortableHead label="$/PT" colKey="costPerPoint" active={sort.key === "costPerPoint"} dir={sort.dir} onSort={sort.toggle} />
            </tr>
          </thead>
          <tbody>
            {ordered.map((r) => {
              const lineupEntry = lineupByPlayer?.get(r.id);
              return (
              <Fragment key={r.id}>
              {/* Opens the prospect zone: a section header, not just a border,
                  at the same tiny/wide-letter-spaced proportions as the
                  ticker's "Trade Alert" strip (Nick, 2026-08-25) — the two
                  read as the same *kind* of mark, a small label, even though
                  they mean different things. Muted rather than gold or ice:
                  both those colours already carry a meaning elsewhere on this
                  screen (gold = trade, ice = headline number), and borrowing
                  one here would misattribute it. This replaces the earlier
                  border-only divider; there is now one mechanism, not two
                  stacked on top of each other. */}
              {r.id === firstProspectId && (
                <tr className="stats-prospect-label-row">
                  {/* The label rides the same sticky `.stats-col-player` cell
                      every name does, rather than a `position: sticky` of its
                      own — that stopped working once nested inside a
                      `colSpan`-ing cell (sticky's containing-block search
                      apparently doesn't reach through one reliably), and this
                      column is already proven to stick, at the exact width
                      `--stats-player-w` gives every other row. */}
                  <td className="stats-col-player stats-prospect-label-cell">
                    Prospects
                  </td>
                  <td colSpan={21} />
                </tr>
              )}
              {/* The coloured edge rides the sticky identity cell, so it stays
                  on screen while the twenty numeric columns scroll under it —
                  a border on the row itself would scroll away with them. A row
                  that is both injured and leaving via trade carries both
                  classes; they share the same rose tone, so nothing collides. */}
              <tr
                className={
                  [
                    r.injuryStatus && "stats-row-out",
                    r.tradeMark === "out" && "stats-row-trade-out",
                    r.tradeMark === "in" && "stats-row-trade-in",
                  ]
                    .filter(Boolean)
                    .join(" ") || undefined
                }
              >
                <td className="stats-col-player">
                  {/* The franchise's logo sits where a player's active/bench
                      control does — not a button, because there is nothing to
                      decide: one franchise, one seat, always in the lineup. */}
                  {r.isFranchise ? (
                    <img className="stats-franchise-logo" src={r.logoUrl ?? ""} alt={r.name} />
                  ) : lineupEntry ? (
                    <LineupToggle
                      entry={lineupEntry}
                      // Editable whenever *next* week is: this week's own
                      // lock is irrelevant, since a tap never touches it.
                      editable={!!lineupEditable}
                      busy={saving}
                      pending={pendingFor?.(lineupEntry) ?? null}
                      onToggle={(spotId) => onToggleLineup?.(spotId)}
                    />
                  ) : (
                    // Departed and Incoming carry no lineup entry, so no grid
                    // in either ever shows a toggle. An empty box of the same
                    // size, not nothing: the Roster grid's rows all show one,
                    // and column width is per-table, so a bare absence here
                    // would leave the name starting ~23px further left than
                    // it does one section up.
                    <span className="stats-toggle-spacer" aria-hidden="true" />
                  )}
                  {r.isFranchise ? (
                    /* Full name, not "M. Canadiens" (Nick, 2026-08-05): there
                       is one of these per team, so it can afford the width —
                       and a franchise's name does not split into initial and
                       surname the way formatShortName assumes. Plain text: it
                       opens no player card. */
                    <span className="stats-player-btn stats-player-name">{r.name}</span>
                  ) : (
                    <button type="button" className="stats-player-btn" onClick={() => onOpenPlayer(r.id)}>
                      <span className="stats-player-name">{formatShortName(r.name)}</span>
                      {r.injuryStatus && <InjuryMark status={r.injuryStatus} type={r.injuryType} />}
                      {r.tradeMark && <TradeMarkIcon direction={r.tradeMark} />}
                    </button>
                  )}
                  {/* No week-by-week for a franchise: the breakdown is served
                      per player id, and a franchise has none. */}
                  {!r.isFranchise && (
                    <button
                      type="button"
                      className={`stats-periods-btn${openPeriodsFor === r.id ? " open" : ""}`}
                      onClick={() => onTogglePeriods(r.id)}
                      aria-expanded={openPeriodsFor === r.id}
                      aria-label={`${r.name} — week by week`}
                      title="Week by week"
                    >
                      <CalendarIcon size={13} />
                    </button>
                  )}
                  <span className={`stats-player-pos pos-compact-${posGroupClass(r.position)}`}>
                    {posGroup(r.position)}
                  </span>
                </td>
                {/* A franchise has none of these. It scored, so PTS is real;
                    everything else is a dash rather than a zero, which would
                    read as "took the ice and produced nothing". */}
                <td className="accent stats-group-start">{r.isFranchise ? "—" : r.poolGamesPlayed}</td>
                <td className="accent">{r.isFranchise ? "—" : r.poolGoals}</td>
                <td className="accent">{r.isFranchise ? "—" : r.poolAssists}</td>
                <td className="accent stats-col-spotlight">{r.poolPoints}</td>
                <td className="accent">{r.isFranchise ? "—" : displayRate(r.poolPtsPerGame, 2)}</td>
                <td className="stats-group-start">{r.isFranchise ? r.teamWins : r.isGoalie ? r.wins : "—"}</td>
                <td>{r.isFranchise ? r.teamLosses : "—"}</td>
                <td>{r.isFranchise ? r.teamOtLosses : r.isGoalie ? r.otLosses : "—"}</td>
                <td>{r.isGoalie ? r.shutouts : "—"}</td>
                <td className="stats-group-start">{r.isFranchise ? "—" : r.gamesPlayed}</td>
                <td>{r.isFranchise ? "—" : r.goals}</td>
                <td>{r.isFranchise ? "—" : r.assists}</td>
                <td className="stats-col-spotlight">{r.isFranchise ? "—" : r.nhlPoints}</td>
                <td>{r.isFranchise ? "—" : displayRate(r.nhlPtsPerGame, 2)}</td>
                <td className="stats-group-start">{r.isFranchise ? "—" : signed(r.plusMinus)}</td>
                <td>{r.isFranchise ? "—" : r.pim}</td>
                <td>{r.isFranchise ? "—" : r.shots}</td>
                <td>{displayRate(r.gaa, 2)}</td>
                <td>{displayRate(r.svPct, 3, true)}</td>
                <td className="stats-group-start">{r.capHit != null ? formatMoneyCompact(r.capHit) : "—"}</td>
                <td>{r.costPerPoint != null ? formatMoneyCompact(r.costPerPoint) : "—"}</td>
              </tr>
              {openPeriodsFor === r.id && (
                /* Spans the whole grid: the breakdown is its own small
                   table, and lining its columns up with the season
                   grid's twenty-one would make both unreadable. */
                <tr className="player-periods-row">
                  <td colSpan={23}>
                    {periodsByPlayer[r.id] ? (
                      <PlayerPeriods data={periodsByPlayer[r.id]} isGoalie={r.isGoalie} />
                    ) : (
                      <p className="muted player-periods-empty">Loading…</p>
                    )}
                  </td>
                </tr>
              )}
              </Fragment>
              );
            })}
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
              {/* Only the franchise fills this column, and one row's number is
                  not a total. */}
              <td>—</td>
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
              <td>{nhlPtsTotal > 0 ? formatMoneyCompact(capTotal / nhlPtsTotal) : "—"}</td>
            </tr>
          </tfoot>
        </table>
      </div>
      )}
    </div>
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
  const [departed, setDeparted] = useState<PlayerSeasonStatsRow[] | null>(null);
  const [incoming, setIncoming] = useState<PlayerSeasonStatsRow[] | null>(null);
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
  // Which player's week-by-week panel is open, and what has been fetched so
  // far. Cached per player: reopening a row should not re-ask.
  const [openPeriodsFor, setOpenPeriodsFor] = useState<number | null>(null);
  const [periodsByPlayer, setPeriodsByPlayer] = useState<Record<number, PlayerPeriodsDto>>({});

  // Keeps the Roster/Departed/Incoming grids' sticky columns the same width as
  // each other — see the hook's own comment. Scoped to the whole screen (not
  // one grid) because that's the only vantage point that can see all three
  // tables at once. Declared up here with the other hooks, unconditionally:
  // `viewedTeam`'s early return below would otherwise skip it on some
  // renders and not others, which is exactly what Rules of Hooks forbids.
  const gridsRef = useSharedPlayerColumnWidth([players, departed, incoming]);

  useEffect(() => {
    let ignore = false;
    setLoading(true);
    setError("");
    api
      .teamSeasonStats(league.id, targetUsername)
      .then((res) => {
        if (ignore) return;
        setPlayers(res.players);
        setDeparted(res.departed ?? []);
        setIncoming(res.incoming ?? []);
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

  /** Opens or closes a player's week-by-week panel, fetching on first open.
   *
   * One player at a time, and cached once fetched: a roster is twenty-odd
   * players and nobody compares two breakdowns side by side, so loading them
   * all up front would be twenty requests for the one that gets read. */
  async function togglePeriods(playerId: number) {
    if (openPeriodsFor === playerId) {
      setOpenPeriodsFor(null);
      return;
    }
    setOpenPeriodsFor(playerId);
    if (periodsByPlayer[playerId]) return;
    try {
      const data = await api.playerPeriods(league.id, targetUsername, playerId);
      setPeriodsByPlayer((prev) => ({ ...prev, [playerId]: data }));
    } catch {
      // Closing again is a truer signal than an error banner over a grid: the
      // panel is supplementary, and the row it belongs to is still correct.
      setOpenPeriodsFor(null);
    }
  }

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
  // `tradeMark` is a parameter, never derived from `p.engaged` here: that flag
  // is symmetric (true on both sides of an accepted trade), so only the caller
  // — which grid this row is being built for — knows whether it means "leaving"
  // or "arriving".
  const toRow = (p: PlayerSeasonStatsRow, tradeMark: PlayerRow["tradeMark"]): PlayerRow => {
    const isGoalie = p.isGoalie;
    const isFranchise = posGroup(p.position) === "T";
    const nhlPoints = p.goals + p.assists;
    return {
      id: p.id,
      name: p.name,
      position: p.position,
      isGoalie,
      // `?? false` is the same deploy-skew guard as the pool columns below: an
      // API that predates this field sends undefined, and undefined would read
      // as "not a prospect", which is the harmless way round.
      isProspect: p.isProspect ?? false,
      isFranchise,
      logoUrl: p.logoUrl ?? null,
      // Left out of the W/OTL columns' own fields on purpose: those totals are
      // the team's *goalies'* record, and a franchise's is not additive with
      // it. The cells below read these three directly.
      teamWins: p.teamWins ?? 0,
      teamLosses: p.teamLosses ?? 0,
      teamOtLosses: p.teamOtLosses ?? 0,
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
      // `?? null` rather than trusting the field: same deploy-skew guard as
      // the pool columns above — an API that predates these sends undefined,
      // and `undefined` would make the mark render for everybody.
      injuryStatus: p.injuryStatus ?? null,
      injuryType: p.injuryType ?? null,
      tradeMark,
      capHit: p.capHit,
      costPerPoint: p.capHit != null && nhlPoints > 0 ? p.capHit / nhlPoints : null,
    };
  };

  const rows = (players ?? []).map((p) => toRow(p, p.engaged ? "out" : null));
  // Never marked — he's already gone, and "departed but was also engaged at
  // the moment he left" isn't a state worth surfacing on a row about history.
  const departedRows = (departed ?? []).map((p) => toRow(p, null));
  const incomingRows = (incoming ?? []).map((p) => toRow(p, "in"));

  // The grid is keyed by playerId; the lineup by roster-spot id. One open spot
  // per player per team, so this mapping is unambiguous.
  // Player entries only: the Équipe entry has no player id, and no lineup
  // control for this map to feed.
  const lineupBySpotPlayer = new Map<number, LineupEntry>(
    (lineup?.hidden ? [] : (lineup?.entries ?? []))
      .filter((e): e is LineupEntry & { playerId: number } => e.playerId != null)
      .map((e) => [e.playerId, e]),
  );

  /** Next week's entry per spot. Keyed by spot rather than by player: a player
   * traded away and back would be two spots, and only the one still held
   * should be compared. */
  const nextBySpot = new Map<string, LineupEntry>(
    (nextLineup?.hidden ? [] : (nextLineup?.entries ?? [])).map((e) => [e.spotId, e]),
  );

  /** In, out, gone, or unchanged when the week rolls over. Null while next week
   * has not loaded — a mark that guessed would be worse than no mark. */
  const pendingFor = (entry: LineupEntry): PendingChange => {
    const next = nextBySpot.get(entry.spotId);
    if (!next) return null;
    // Checked before the active comparison: he is not dropping out of a
    // lineup, he is leaving the roster, and only one of those is the GM's to
    // undo.
    if (next.leaving) return "gone";
    return next.active === entry.active ? null : next.active ? "in" : "out";
  };


  const maxRosterSize = league.ruleConfig.rosterSize.max;

  return (
    <section
      className="fade-in"
      style={{ display: "flex", flexDirection: "column", gap: "1rem" }}
      ref={gridsRef}
    >
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
        <>
          <RosterGrid
            rows={rows}
            title="Roster"
            subtitle={`${rows.length}${maxRosterSize != null ? ` / ${maxRosterSize}` : ""} player`}
            emptyLabel="No players on this roster."
            lineupByPlayer={lineupBySpotPlayer}
            pendingFor={pendingFor}
            lineupEditable={!!nextLineup && !nextLineup.locked && nextLineup.isOwner}
            saving={saving}
            onToggleLineup={setPickerSpotId}
            onOpenPlayer={setOpenPlayerId}
            openPeriodsFor={openPeriodsFor}
            periodsByPlayer={periodsByPlayer}
            onTogglePeriods={togglePeriods}
          />

          {/* Players this team no longer holds but whose banked points it
              keeps — a trade cannot move history, so the standings figure is
              not explained by the roster above alone. Hidden entirely when
              there are none, which is every team until its first trade. */}
          {departedRows.length > 0 && (
            <RosterGrid
              rows={departedRows}
              title="Departed"
              subtitle={`${departedRows.length} player, points kept`}
              emptyLabel="Nobody has left this roster."
              saving={saving}
              onOpenPlayer={setOpenPlayerId}
              openPeriodsFor={openPeriodsFor}
              periodsByPlayer={periodsByPlayer}
              onTogglePeriods={togglePeriods}
            />
          )}

          {/* Acquired in a trade that has not reached its effective date. They
              have a roster spot, and a lineup row for the week they arrive in —
              which is set from the picker, not from here, because this grid is
              the week in progress and they are not in it yet. Hidden entirely
              when there are none, same as Departed. */}
          {incomingRows.length > 0 && (
            <RosterGrid
              rows={incomingRows}
              title="Incoming"
              subtitle={`${incomingRows.length} player, on the roster when the week turns`}
              emptyLabel="Nobody arriving."
              saving={saving}
              onOpenPlayer={setOpenPlayerId}
              openPeriodsFor={openPeriodsFor}
              periodsByPlayer={periodsByPlayer}
              onTogglePeriods={togglePeriods}
            />
          )}
        </>
      )}
      {/* Everything in here is **next week's** lineup — the one being written.
          It used to read from the week on screen, which is the same roster
          right up until a trade lands, and a different one exactly when it
          matters. */}
      {pickerSpotId && nextLineup && (() => {
        const entry = nextLineup.entries.find((e) => e.spotId === pickerSpotId);
        if (!entry) return null;
        const max =
          entry.positionGroup === "F" ? nextLineup.slots.forwards
          : entry.positionGroup === "D" ? nextLineup.slots.defense
          : nextLineup.slots.goalies;
        const close = () => setPickerSpotId(null);
        return (
          <LineupPicker
            entry={entry}
            entries={nextLineup.entries}
            slotIsFree={(nextLineup.used[entry.positionGroup] ?? 0) < max}
            busy={saving}
            periodIndex={nextLineup.periodIndex}
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
