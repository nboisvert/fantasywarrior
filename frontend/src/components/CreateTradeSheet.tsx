// CreateTradeSheet — bottom-sheet (mobile) / centered modal (desktop) for
// proposing a trade. Reuses PlayerCard's overlay/sheet/focus-trap mechanics
// wholesale (same "pc-*" classes from PlayerCard.css — purely structural
// dialog chrome, no player-specific styling in there) with different body
// content: pick a counterparty team, then multi-select players and picks each
// way, with a live read on what it does to both teams.

import { useEffect, useRef, useState } from "react";
import type { KeyboardEvent as ReactKeyboardEvent, MouseEvent as ReactMouseEvent } from "react";
import { api, formatCapCompact, formatShortName, posGroup, posGroupClass } from "../api";
import type { DraftPickDto, LeagueDetail, TeamDto } from "../api";
import { ArrowRightIcon, ChevronDownIcon, CircleCheckIcon, CircleXIcon, XIcon } from "./Icons";
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

/**
 * One team's cap and roster, before and after this trade — sitting inside that
 * team's own card.
 *
 * It used to be a single table under both cards, listing "Colorado" and
 * "Montreal" while the cards above said "you give" and "you get". That left the
 * reader to join the two by hand, which is what made the screen hard to read
 * (2026-08-03, per Nick). A card that carries its own team's numbers needs no
 * such bridge.
 */
function SideBoard({ impact }: { impact: SideImpact }) {
  const bad = impact.violations.length > 0;
  const capBust = impact.violations.includes("cap");
  const rosterBust = impact.violations.includes("max") || impact.violations.includes("min");

  return (
    <div className={`cts-board${bad ? " bad" : ""}`}>
      <span className="cts-board-metric">
        <span className="cts-board-key">Cap</span>
        {impact.spaceBefore == null ? (
          <span className="muted">none</span>
        ) : (
          <>
            <span className="muted">{formatCapCompact(impact.spaceBefore)}</span>
            {/* An arrow, not a chevron: this is "becomes", not navigation. */}
            <ArrowRightIcon size={11} className="cts-board-arrow" />
            <span className={capBust ? "cts-board-over" : "cts-board-now"}>
              {formatCapCompact(impact.spaceAfter)}
            </span>
          </>
        )}
      </span>

      <span className="cts-board-metric">
        <span className="cts-board-key">Roster</span>
        <span className="muted">{impact.countBefore}</span>
        <ArrowRightIcon size={11} className="cts-board-arrow" />
        <span className={rosterBust ? "cts-board-over" : "cts-board-now"}>{impact.countAfter}</span>
      </span>

      {impact.unknownContracts > 0 && (
        // Counted as $0 by the standings and by the server alike. Saying so is
        // the only honest option — we cannot validate a salary nobody has.
        <span className="cts-board-unknown muted">{impact.unknownContracts} with no contract</span>
      )}

      <span className="cts-board-verdict">
        {bad ? <CircleXIcon size={15} /> : <CircleCheckIcon size={15} />}
      </span>
    </div>
  );
}

const pickLabel = (p: DraftPickDto) => `${p.year} rd ${p.round}`;

/**
 * One side of the trade: a card that is either the scrolling picker or, when
 * collapsed, the summary of what has been taken from it.
 *
 * Only one is open at a time (2026-08-03, per Nick). That is what removes the
 * nested scrolling the old layout had — four scroll areas inside a body that
 * scrolled too — and it roughly doubles how many rows fit, because the open
 * card gets the height the other three were spending.
 *
 * The cost is that balancing an offer means toggling back and forth, which is
 * exactly what makes the recap below load-bearing rather than decorative: it
 * shows the net effect without reopening the other side.
 */
function SideCard({
  title,
  open,
  onOpen,
  players,
  picks,
  franchise,
  franchiseSelected,
  onToggleFranchise,
  selectedPlayers,
  selectedPicks,
  onTogglePlayer,
  onTogglePick,
  loading,
  impact,
}: {
  title: string;
  open: boolean;
  onOpen: () => void;
  players: PickPlayer[];
  picks: DraftPickDto[];
  /** The Équipe slot this team holds. Null in a league that has none. */
  franchise: TeamDto["franchise"];
  franchiseSelected: boolean;
  onToggleFranchise: () => void;
  selectedPlayers: Set<number>;
  selectedPicks: Set<number>;
  onTogglePlayer: (id: number) => void;
  onTogglePick: (id: number) => void;
  loading?: boolean;
  /** This card's own team. Null only before a counterparty is resolved. */
  impact: SideImpact | null;
}) {
  const chosenPlayers = players.filter((p) => selectedPlayers.has(p.id));
  const chosenPicks = picks.filter((p) => selectedPicks.has(p.id));
  const names = [
    ...chosenPlayers.map((p) => formatShortName(p.name)),
    ...chosenPicks.map(pickLabel),
    ...(franchiseSelected && franchise ? [franchise.name] : []),
  ];

  return (
    <section className={`cts-card${open ? " open" : ""}`}>
      {/* No money in the header (2026-08-03, per Nick). The board's cap
          before/after already carries what this side costs, and two dollar
          figures on one card invited the wrong comparison. */}
      <button type="button" className="cts-card-head" aria-expanded={open} onClick={onOpen}>
        <span className="cts-card-title">{title}</span>
        <ChevronDownIcon size={15} className={`cts-card-chevron${open ? " up" : ""}`} />
      </button>

      {/* Every asset, never "+2" (2026-08-03, per Nick): the point of a closed
          card is to answer "what did I put in" without reopening it, and a
          truncated answer sends you back in. Wraps rather than clips. */}
      <div className="cts-card-assets">
        {names.length === 0 ? (
          <span className="muted">Nothing selected</span>
        ) : (
          names.map((n) => (
            <span key={n} className="cts-asset-tag">
              {n}
            </span>
          ))
        )}
      </div>

      {open && (
        <div className="cts-card-body">
          {loading ? (
            <p className="empty-state cts-empty">Loading roster…</p>
          ) : players.length === 0 && picks.length === 0 && franchise == null ? (
            <p className="empty-state cts-empty">Nothing on this roster.</p>
          ) : (
            <ul className="cts-asset-list">
              {players.map((p) => (
                <li key={`p${p.id}`}>
                  <label className={`cts-row${p.engaged ? " engaged" : ""}`}>
                    <input
                      type="checkbox"
                      checked={selectedPlayers.has(p.id)}
                      disabled={p.engaged}
                      onChange={() => onTogglePlayer(p.id)}
                    />
                    <span className="cts-row-name">{p.name}</span>
                    <span className={`cts-row-pos pos-compact-${posGroupClass(p.position)}`}>
                      {posGroup(p.position)}
                    </span>
                    {/* Shown rather than hidden: a player who simply vanished
                        from the list would read as a bug. */}
                    <span className="cts-row-cap muted">
                      {p.engaged ? "in a trade" : formatCapCompact(p.capHit)}
                    </span>
                  </label>
                </li>
              ))}

              {picks.length > 0 && (
                <li className="cts-row-divider" aria-hidden="true">
                  Draft picks
                </li>
              )}
              {picks.map((p) => (
                <li key={`d${p.id}`}>
                  <label className={`cts-row${p.engaged ? " engaged" : ""}`}>
                    <input
                      type="checkbox"
                      checked={selectedPicks.has(p.id)}
                      disabled={p.engaged}
                      onChange={() => onTogglePick(p.id)}
                    />
                    <span className="cts-row-name">
                      {pickLabel(p)}
                      {/* "Martin's 2nd, via Boston" is only expressible because
                          the original owner survives every trade. */}
                      {p.viaTrade && <span className="muted"> · via {p.originalTeamName}</span>}
                    </span>
                    {p.engaged && <span className="cts-row-cap muted">in a trade</span>}
                  </label>
                </li>
              ))}

              {/* Last, and under its own divider: it is not a player and not a
                  pick, it costs no cap, and it only ever moves against the
                  other side's — which the send button enforces rather than
                  this checkbox, so the reason is stated in words. */}
              {franchise && (
                <>
                  <li className="cts-row-divider" aria-hidden="true">
                    Franchise
                  </li>
                  <li>
                    <label className={`cts-row${franchise.engaged ? " engaged" : ""}`}>
                      <input
                        type="checkbox"
                        checked={franchiseSelected}
                        disabled={franchise.engaged}
                        onChange={onToggleFranchise}
                      />
                      <span className="cts-row-name">{franchise.name}</span>
                      <span className="cts-row-pos pos-compact-t">T</span>
                      {/* Shown rather than hidden, same as a player: a
                          franchise that simply vanished would read as a bug. */}
                      <span className="cts-row-cap muted">
                        {franchise.engaged ? "in a trade" : "no cap"}
                      </span>
                    </label>
                  </li>
                </>
              )}
            </ul>
          )}
        </div>
      )}

      {/* Always the card's last band, open or closed (2026-08-03, per Nick).
          It used to sit between the tags and the list, so it moved depending on
          the card's state; a number you have to look for is a number you stop
          reading. */}
      {impact && <SideBoard impact={impact} />}
    </section>
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
  const [myFranchise, setMyFranchise] = useState(false);
  const [theirFranchise, setTheirFranchise] = useState(false);
  const [theirPlayers, setTheirPlayers] = useState<PickPlayer[]>([]);
  const [myPickList, setMyPickList] = useState<DraftPickDto[]>([]);
  const [theirPickList, setTheirPickList] = useState<DraftPickDto[]>([]);
  const [theirLoading, setTheirLoading] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState("");
  // "give" first: you start from what you are willing to part with. Null is a
  // real state (2026-08-03, per Nick) — with both shut, the two summaries sit
  // side by side and the whole offer reads at once, which is the one view
  // neither open card can give you.
  const [openSide, setOpenSide] = useState<"give" | "get" | null>("give");
  const toggleSide = (side: "give" | "get") =>
    setOpenSide((current) => (current === side ? null : side));

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
    setTheirFranchise(false);
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
          // Players only. `season-stats` returns the Équipe slot in the same
          // list — the Team grid wants it there, as one row among the rest —
          // but here it has its own section, and it was showing up twice.
          res.players
            .filter((p) => posGroup(p.position) !== "T")
            .map((p) => ({
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

  // Each card reports its own team now, so nothing here needs the pair — only
  // whether either side breaks, which is what gates the send button.
  const illegal = [myImpact, theirImpact].some((i) => i != null && i.violations.length > 0);

  const anySelected =
    mine.size > 0 || theirs.size > 0 || myPicks.size > 0 || theirPicks.size > 0
    || myFranchise || theirFranchise;

  // Every team holds exactly one franchise, so a one-sided swap would leave
  // one with two and the other with none — impossible, not merely unwise. Said
  // here in words rather than by disabling a checkbox, because "why can't I
  // tick this" is the question a disabled control never answers.
  const franchiseUnbalanced = myFranchise !== theirFranchise;
  const canSubmit =
    !submitting && counterparty !== "" && anySelected && !illegal && !franchiseUnbalanced;

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
        myFranchise ? (myTeam?.franchise?.abbrev ?? null) : null,
        theirFranchise ? (counterpartyTeam?.franchise?.abbrev ?? null) : null,
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
        <div className="pc-body cts-body">
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
                <SideCard
                  title={`You give · ${myTeam.name}`}
                  open={openSide === "give"}
                  onOpen={() => toggleSide("give")}
                  players={myPlayers}
                  picks={myPickList}
                  franchise={myTeam.franchise}
                  franchiseSelected={myFranchise}
                  onToggleFranchise={() => setMyFranchise((v) => !v)}
                  selectedPlayers={mine}
                  selectedPicks={myPicks}
                  onTogglePlayer={(id) => toggle(mine, setMine, id)}
                  onTogglePick={(id) => toggle(myPicks, setMyPicks, id)}
                  impact={myImpact}
                />
                <SideCard
                  title={`You get · ${counterpartyTeam?.name ?? "—"}`}
                  open={openSide === "get"}
                  onOpen={() => toggleSide("get")}
                  players={theirPlayers}
                  picks={theirPickList}
                  franchise={counterpartyTeam?.franchise ?? null}
                  franchiseSelected={theirFranchise}
                  onToggleFranchise={() => setTheirFranchise((v) => !v)}
                  selectedPlayers={theirs}
                  selectedPicks={theirPicks}
                  onTogglePlayer={(id) => toggle(theirs, setTheirs, id)}
                  onTogglePick={(id) => toggle(theirPicks, setTheirPicks, id)}
                  loading={theirLoading}
                  impact={theirImpact}
                />
              </div>

              {franchiseUnbalanced && (
                <p className="error-banner">A franchise can only be traded for another franchise.</p>
              )}
              {error && <p className="error-banner">{error}</p>}

              {/* Blocked reads red, not merely dimmed: a greyed button says
                  "not yet", a red one says "this cannot be sent" — and the
                  reason is already in the card whose board turned red. */}
              <button
                className={`btn${illegal ? " btn-blocked" : ""}`}
                disabled={!canSubmit}
                onClick={submit}
              >
                {submitting ? "Sending…" : illegal ? "Over the limit" : "Send trade offer"}
              </button>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
