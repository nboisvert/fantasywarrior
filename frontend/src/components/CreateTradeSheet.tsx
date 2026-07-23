// CreateTradeSheet — bottom-sheet (mobile) / centered modal (desktop) for
// proposing a trade. Reuses PlayerCard's overlay/sheet/focus-trap mechanics
// wholesale (same "pc-*" classes from PlayerCard.css — purely structural
// dialog chrome, no player-specific styling in there) with different body
// content: pick a counterparty team, then multi-select players each way.

import { useEffect, useRef, useState } from "react";
import type { KeyboardEvent as ReactKeyboardEvent, MouseEvent as ReactMouseEvent } from "react";
import { api, posGroup, posGroupClass } from "../api";
import type { LeagueDetail, RosterPlayer } from "../api";
import { XIcon } from "./Icons";
import "./PlayerCard.css";
import "./CreateTradeSheet.css";

function PlayerCheckList({
  players,
  selected,
  onToggle,
}: {
  players: RosterPlayer[];
  selected: Set<number>;
  onToggle: (id: number) => void;
}) {
  if (players.length === 0) return <p className="empty-state cts-empty">No players on this roster.</p>;
  return (
    <ul className="cts-player-list">
      {players.map((p) => (
        <li key={p.id}>
          <label className="cts-player-row">
            <input
              type="checkbox"
              checked={selected.has(p.id)}
              onChange={() => onToggle(p.id)}
            />
            <span className="cts-player-name">{p.name}</span>
            <span className={`cts-player-pos pos-compact-${posGroupClass(p.position)}`}>
              {posGroup(p.position)}
            </span>
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
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState("");

  const counterpartyTeam = league.teams.find((t) => t.ownerUsername === counterparty);

  // Switching the counterparty invalidates whatever was picked from the
  // previous team's roster.
  useEffect(() => {
    setTheirs(new Set());
  }, [counterparty]);

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
      'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])',
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

  const canSubmit = !submitting && counterparty !== "" && (mine.size > 0 || theirs.size > 0);

  const submit = async () => {
    if (!canSubmit) return;
    setSubmitting(true);
    setError("");
    try {
      await api.proposeTrade(league.id, username, counterparty, [...mine], [...theirs]);
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

        <div className="pc-body">
          <h2 className="cts-title">Propose a trade</h2>

          {otherTeams.length === 0 ? (
            <p className="empty-state">No other team to trade with.</p>
          ) : (
            <>
              <label className="cts-field">
                <span className="section-title">Trade with</span>
                <select
                  className="field"
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
                    players={myTeam.players}
                    selected={mine}
                    onToggle={(id) => toggle(mine, setMine, id)}
                  />
                </div>
                <div className="cts-side">
                  <span className="section-title">You get ({counterpartyTeam?.name ?? "—"})</span>
                  <PlayerCheckList
                    players={counterpartyTeam?.players ?? []}
                    selected={theirs}
                    onToggle={(id) => toggle(theirs, setTheirs, id)}
                  />
                </div>
              </div>

              {error && <p className="error-banner">{error}</p>}

              <button className="btn" disabled={!canSubmit} onClick={submit}>
                {submitting ? "Sending…" : "Send trade offer"}
              </button>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
