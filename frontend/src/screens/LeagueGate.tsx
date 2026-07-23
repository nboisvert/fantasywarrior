// Full-screen league picker. Shown either as the first-time landing when the
// user has no remembered league, or transiently when the topbar "switch
// league" control is used. Create/join forms live in Settings now.

import { useEffect, useState } from "react";
import { api, formatCap } from "../api";
import type { LeagueSummary } from "../api";
import { LogOutIcon, XIcon } from "../components/Icons";
import logo from "../assets/logo.webp";

export function LeagueGate({
  username,
  onOpen,
  onLogout,
  onGoSettings,
  onClose,
}: {
  username: string;
  onOpen: (id: string) => void;
  onLogout: () => void;
  onGoSettings: () => void;
  /** Present only when there's already an active league to return to (i.e. this is a "switch", not the first-time gate). */
  onClose?: () => void;
}) {
  const [leagues, setLeagues] = useState<LeagueSummary[] | null>(null);
  const [error, setError] = useState("");

  useEffect(() => {
    api
      .myLeagues(username)
      .then(setLeagues)
      .catch((e) => setError((e as Error).message));
  }, [username]);

  return (
    <div className="shell fade-in">
      <header className="topbar">
        <span className="brand">
          <img className="topbar-logo" src={logo} alt="" />
          Fantasy <span className="accent">Warrior</span>
        </span>
        <span style={{ display: "inline-flex", alignItems: "center", gap: "0.35rem" }}>
          {onClose && (
            <button className="icon-btn" onClick={onClose} aria-label="Cancel switching league">
              <XIcon size={18} />
            </button>
          )}
          <button className="btn-ghost" onClick={onLogout}>
            <LogOutIcon size={16} /> {username}
          </button>
        </span>
      </header>
      <main className="shell-content" style={{ paddingTop: "1rem" }}>
        {error && <p className="error-banner">{error}</p>}

        <span className="section-title">Choose a league</span>
        {leagues === null ? (
          <p className="empty-state">Loading…</p>
        ) : leagues.length === 0 ? (
          <div className="empty-state" style={{ display: "flex", flexDirection: "column", gap: "0.9rem", alignItems: "center" }}>
            <p style={{ margin: 0 }}>No league yet — create one or join with an invite code.</p>
            <button className="btn" onClick={onGoSettings}>
              Go to Settings
            </button>
          </div>
        ) : (
          <>
            <ul className="league-list">
              {leagues.map((l) => (
                <li key={l.id}>
                  <button className="league-card" onClick={() => onOpen(l.id)}>
                    <span>
                      <strong>{l.name}</strong>
                      <small>
                        {l.members} member{l.members > 1 ? "s" : ""} · cap {formatCap(l.capAmount)}
                      </small>
                    </span>
                  </button>
                </li>
              ))}
            </ul>
            <button className="btn-ghost" onClick={onGoSettings} style={{ alignSelf: "flex-start" }}>
              Create or join another league in Settings
            </button>
          </>
        )}
      </main>
    </div>
  );
}
