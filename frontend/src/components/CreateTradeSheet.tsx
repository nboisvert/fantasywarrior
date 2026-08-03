// CreateTradeSheet — bottom-sheet (mobile) / centered modal (desktop) for
// proposing a trade. Reuses PlayerCard's overlay/sheet/focus-trap mechanics
// wholesale (same "pc-*" classes from PlayerCard.css — purely structural
// dialog chrome, no player-specific styling in there) with different body
// content: pick a counterparty team, then multi-select players and picks each
// way, with a live read on what it does to both teams.

import { useEffect, useRef, useState } from "react";
import type { KeyboardEvent as ReactKeyboardEvent, MouseEvent as ReactMouseEvent } from "react";
import { api, formatCapCompact, posGroup, posGroupClass } from "../api";
import type { DraftPickDto, LeagueDetail, TeamDto } from "../api";
import { XIcon } from "./Icons";
import "./PlayerCard.css";
import "./CreateTradeSheet.css";

/** Minimal shape both sides of the trade picker need — the signed-in user's
 * roster comes from `league.myRoster` (RosterPlayer, a superset), the
 * counterparty's is fetched on demand. */
interface PickPlayer {
  id: number;
  name: string;
  position: string;
  capHit: number | null;
  /** Already moving in an accepted trade; offering it again would be refused. */
  engaged: boolean;
}

/* ---------- the recap, and the arithmetic behind it ----------
 * This mirrors TradeRules.Impact/Validate in FantasyWarrior.Core, deliberately.
 * The duplication buys a recap that moves on every checkbox without a round
 * trip; the server stays the authority and will refuse anything this lets
 * through. Keep the two in step — the rules are four lines each.
 *
 * The baseline is the ENGAGED figure, never `capTotal`/`playerCount`: an
 * accepted trade already commits a team, and validating against today's roster
 * is exactly how a GM ends up busting the cap with two individually-legal
 * trades.
 */
interface SideImpact {
  teamName: string;
  spaceBefore: number | null;
  spaceAfter: number | null;
  countBefore: number;
  countAfter: number;
  unknownContracts: number;
  violations: string[];
}

function impactOf(
  team: TeamDto | undefined,
  outgoing: PickPlayer[],
  incoming: PickPlayer[],
  capAmount: number | null,
  rosterMin: number | null,
  rosterMax: number | null,
): SideImpact | null {
  if (!team) return null;

  const sum = (players: PickPlayer[]) => players.reduce((t, p) => t + (p.capHit ?? 0), 0);
  const capAfter = team.engagedCapTotal - sum(outgoing) + sum(incoming);
  const countAfter = team.engagedPlayerCount - outgoing.length + incoming.length;

  const violations: string[] = [];
  if (capAmount != null && capAfter > capAmount) violations.push("cap");
  if (rosterMax != null && countAfter > rosterMax) violations.push("max");
  if (rosterMin != null && countAfter < rosterMin) violations.push("min");

  return {
    teamName: team.name,
    spaceBefore: capAmount == null ? null : capAmount - team.engagedCapTotal,
    spaceAfter: capAmount == null ? null : capAmount - capAfter,
    countBefore: team.engagedPlayerCount,
    countAfter,
    unknownContracts:
      outgoing.filter((p) => p.capHit == null).length + incoming.filter((p) => p.capHit == null).length,
    violations,
  };
}

function RecapRow({ impact }: { impact: SideImpact }) {
  const bad = impact.violations.length > 0;
  return (
    <li className={`cts-recap-row${bad ? " bad" : ""}`}>
      <span className="cts-recap-team">{impact.teamName}</span>
      <span className="cts-recap-cap">
        {impact.spaceBefore == null ? (
          <span className="muted">no cap</span>
        ) : (
          <>
            <span className="muted">{formatCapCompact(impact.spaceBefore)}</span>
            <span className="cts-recap-arrow" aria-hidden="true">
              ▸
            </span>
            <span className={impact.violations.includes("cap") ? "cts-recap-over" : ""}>
              {formatCapCompact(impact.spaceAfter)}
            </span>
          </>
        )}
      </span>
      <span className="cts-recap-roster">
        <span className="muted">{impact.countBefore}</span>
        <span className="cts-recap-arrow" aria-hidden="true">
          ▸
        </span>
        <span
          className={
            impact.violations.includes("max") || impact.violations.includes("min")
              ? "cts-recap-over"
              : ""
          }
        >
          {impact.countAfter}
        </span>
      </span>
      <span className="cts-recap-verdict" aria-hidden="true">
        {bad ? "✕" : "✓"}
      </span>
    </li>
  );
}

function PlayerCheckList({
  players,
  selected,
  onToggle,
}: {
  players: PickPlayer[];
  selected: Set<number>;
  onToggle: (id: number) => void;
}) {
  if (players.length === 0) return <p className="empty-state cts-empty">No players on this roster.</p>;
  return (
    <ul className="cts-player-list">
      {players.map((p) => (
        <li key={p.id}>
          <label className={`cts-player-row${p.engaged ? " engaged" : ""}`}>
            <input
              type="checkbox"
              checked={selected.has(p.id)}
              disabled={p.engaged}
              onChange={() => onToggle(p.id)}
            />
            <span className="cts-player-name">{p.name}</span>
            <span className={`cts-player-pos pos-compact-${posGroupClass(p.position)}`}>
              {posGroup(p.position)}
            </span>
            {/* Shown rather than hidden (2026-08-03): a player who simply
                vanished from the list would read as a bug. */}
            <span className="cts-player-cap muted">
              {p.engaged ? "in a trade" : formatCapCompact(p.capHit)}
            </span>
          </label>
        </li>
      ))}
    </ul>
  );
}

function PickCheckList({
  picks,
  selected,
  onToggle,
}: {
  picks: DraftPickDto[];
  selected: Set<number>;
  onToggle: (id: number) => void;
}) {
  if (picks.length === 0) return null;
  return (
    <ul className="cts-player-list cts-pick-list">
      {picks.map((p) => (
        <li key={p.id}>
          <label className={`cts-player-row${p.engaged ? " engaged" : ""}`}>
            <input
              type="checkbox"
              checked={selected.has(p.id)}
              disabled={p.engaged}
              onChange={() => onToggle(p.id)}
            />
            <span className="cts-player-name">
              {p.year} round {p.round}
              {/* "Martin's 2nd, via Boston" is only expressible because the
                  original owner survives every trade. */}
              {p.viaTrade && <span className="muted"> · via {p.originalTeamName}</span>}
            </span>
            {p.engaged && <span className="cts-player-cap muted">in a trade</span>}
          </label>
        </li>
      ))}
    </ul>
  );
}

export function CreateTradeSheet({
  league,
  username,
  onClose,
  onCreated,
}: {
  league: LeagueDetail;
  username: string;
  onClose: () => void;
  onCreated: () => void;
}) {
  const sheetRef = useRef<HTMLDivElement>(null);
  const closeRef = useRef<HTMLButtonElement>(null);

  const myTeam = league.teams.find((t) => t.ownerUsername === username);
  const otherTeams = league.teams.filter((t) => t.ownerUsername !== username);

  const [counterparty, setCounterparty] = useState(otherTeams[0]?.ownerUsername ?? "");
  const [mine, setMine] = useState<Set<number>>(new Set());
  const [theirs, setTheirs] = useState<Set<number>>(new Set());
  const [myPicks, setMyPicks] = useState<Set<number>>(new Set());
  const [theirPicks, setTheirPicks] = useState<Set<number>>(new Set());
  const [theirPlayers, setTheirPlayers] = useState<PickPlayer[]>([]);
  const [myPickList, setMyPickList] = useState<DraftPickDto[]>([]);
  const [theirPickList, setTheirPickList] = useState<DraftPickDto[]>([]);
  const [theirLoading, setTheirLoading] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState("");

  const counterpartyTeam = league.teams.find((t) => t.ownerUsername === counterparty);

  const myPlayers: PickPlayer[] = league.myRoster.map((p) => ({
    id: p.id,
    name: p.name,
    position: p.position,
    capHit: p.capHit,
    engaged: p.engaged ?? false,
  }));

  // My own picks don't change with the counterparty, so they load once.
  useEffect(() => {
    let ignore = false;
    api
      .picks(league.id, username)
      .then((p) => {
        if (!ignore) setMyPickList(p);
      })
      // A league with no draft configured simply has none — not an error worth
      // a banner over a trade that can still be made in players.
      .catch(() => {});
    return () => {
      ignore = true;
    };
  }, [league.id, username]);

  // Switching the counterparty invalidates the previous team's picks and
  // fetches the newly selected team's roster on demand (rosters other than the
  // signed-in user's aren't shipped with the league payload anymore).
  useEffect(() => {
    setTheirs(new Set());
    setTheirPicks(new Set());
    setTheirPlayers([]);
    setTheirPickList([]);
    if (!counterparty) return;
    let ignore = false;
    setTheirLoading(true);
    Promise.all([
      api.teamSeasonStats(league.id, counterparty),
      api.picks(league.id, counterparty).catch(() => [] as DraftPickDto[]),
    ])
      .then(([res, picks]) => {
        if (ignore) return;
        setTheirPlayers(
          res.players.map((p) => ({
            id: p.id,
            name: p.name,
            position: p.position,
            capHit: p.capHit,
            engaged: p.engaged ?? false,
          })),
        );
        setTheirPickList(picks);
      })
      .catch(() => {
        if (!ignore) setError("Could not load that team's roster.");
      })
      .finally(() => {
        if (!ignore) setTheirLoading(false);
      });
    return () => {
      ignore = true;
    };
  }, [counterparty, league.id]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  useEffect(() => {
    const prevOverflow = document.body.style.overflow;
    const prevFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    document.body.style.overflow = "hidden";
    closeRef.current?.focus();
    return () => {
      document.body.style.overflow = prevOverflow;
      prevFocus?.focus();
    };
  }, []);

  const trapFocus = (e: ReactKeyboardEvent<HTMLDivElement>) => {
    if (e.key !== "Tab" || !sheetRef.current) return;
    const focusables = sheetRef.current.querySelectorAll<HTMLElement>(
      'button:not([disabled]), [href], input:not([disabled]), select, textarea, [tabindex]:not([tabindex="-1"])',
    );
    if (focusables.length === 0) return;
    const first = focusables[0];
    const last = focusables[focusables.length - 1];
    if (e.shiftKey && document.activeElement === first) {
      e.preventDefault();
      last.focus();
    } else if (!e.shiftKey && document.activeElement === last) {
      e.preventDefault();
      first.focus();
    }
  };

  const onBackdrop = (e: ReactMouseEvent<HTMLDivElement>) => {
    if (e.target === e.currentTarget) onClose();
  };

  const toggle = (set: Set<number>, setter: (s: Set<number>) => void, id: number) => {
    const next = new Set(set);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    setter(next);
  };

  const outgoing = myPlayers.filter((p) => mine.has(p.id));
  const incoming = theirPlayers.filter((p) => theirs.has(p.id));

  const cap = league.capAmount;
  const { min: rosterMin, max: rosterMax } = league.ruleConfig.rosterSize;

  const myImpact = impactOf(myTeam, outgoing, incoming, cap, rosterMin, rosterMax);
  const theirImpact = impactOf(counterpartyTeam, incoming, outgoing, cap, rosterMin, rosterMax);

  const impacts = [myImpact, theirImpact].filter((i): i is SideImpact => i !== null);
  const illegal = impacts.some((i) => i.violations.length > 0);
  const unknownContracts = myImpact?.unknownContracts ?? 0;

  const anySelected = mine.size > 0 || theirs.size > 0 || myPicks.size > 0 || theirPicks.size > 0;
  const canSubmit = !submitting && counterparty !== "" && anySelected && !illegal;

  const submit = async () => {
    if (!canSubmit) return;
    setSubmitting(true);
    setError("");
    try {
      await api.proposeTrade(
        league.id,
        username,
        counterparty,
        [...mine],
        [...theirs],
        [...myPicks],
        [...theirPicks],
      );
      onCreated();
      onClose();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not propose trade.");
      setSubmitting(false);
    }
  };

  if (!myTeam) return null;

  return (
    <div className="pc-overlay" onClick={onBackdrop}>
      <div
        ref={sheetRef}
        className="pc-sheet"
        role="dialog"
        aria-modal="true"
        aria-label="Propose a trade"
        onKeyDown={trapFocus}
      >
        <div className="pc-top">
          <span className="pc-handle" aria-hidden="true" />
          <button ref={closeRef} className="pc-close" onClick={onClose} aria-label="Close">
            <XIcon size={20} />
          </button>
        </div>

        {/* No title (2026-08-03, per Nick): the dialog's aria-label already
            carries it, and the height it was spending is what the recap needs. */}
        <div className="pc-body">
          {otherTeams.length === 0 ? (
            <p className="empty-state">No other team to trade with.</p>
          ) : (
            <>
              <label className="cts-field">
                <span className="section-title">Trade with</span>
                <select
                  className="field cts-select"
                  value={counterparty}
                  onChange={(e) => setCounterparty(e.target.value)}
                >
                  {otherTeams.map((t) => (
                    <option key={t.ownerUsername} value={t.ownerUsername}>
                      {t.name}
                    </option>
                  ))}
                </select>
              </label>

              <div className="cts-sides">
                <div className="cts-side">
                  <span className="section-title">You give ({myTeam.name})</span>
                  <PlayerCheckList
                    players={myPlayers}
                    selected={mine}
                    onToggle={(id) => toggle(mine, setMine, id)}
                  />
                  <PickCheckList
                    picks={myPickList}
                    selected={myPicks}
                    onToggle={(id) => toggle(myPicks, setMyPicks, id)}
                  />
                </div>
                <div className="cts-side">
                  <span className="section-title">You get ({counterpartyTeam?.name ?? "—"})</span>
                  {theirLoading ? (
                    <p className="empty-state cts-empty">Loading roster…</p>
                  ) : (
                    <>
                      <PlayerCheckList
                        players={theirPlayers}
                        selected={theirs}
                        onToggle={(id) => toggle(theirs, setTheirs, id)}
                      />
                      <PickCheckList
                        picks={theirPickList}
                        selected={theirPicks}
                        onToggle={(id) => toggle(theirPicks, setTheirPicks, id)}
                      />
                    </>
                  )}
                </div>
              </div>

              {impacts.length === 2 && (
                <div className="cts-recap">
                  <div className="cts-recap-head">
                    <span />
                    <span>Cap space</span>
                    <span>Roster</span>
                    <span />
                  </div>
                  <ul className="cts-recap-rows">
                    {impacts.map((i) => (
                      <RecapRow key={i.teamName} impact={i} />
                    ))}
                  </ul>
                  {unknownContracts > 0 && (
                    // Counted as $0 by the standings and by the server alike.
                    // Saying so is the only honest option — we cannot validate
                    // a salary nobody has on file.
                    <p className="cts-recap-note muted">
                      {unknownContracts} player{unknownContracts > 1 ? "s" : ""} with no contract on
                      file, counted as $0.
                    </p>
                  )}
                </div>
              )}

              {error && <p className="error-banner">{error}</p>}

              <button className="btn" disabled={!canSubmit} onClick={submit}>
                {submitting ? "Sending…" : illegal ? "Over the limit" : "Send trade offer"}
              </button>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
