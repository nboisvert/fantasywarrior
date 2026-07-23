import { useCallback, useEffect, useState } from "react";
import { api, formatCap } from "../api";
import type { LeagueDetail, LeagueSummary } from "../api";
import { LogOutIcon, SettingsIcon } from "../components/Icons";
import { RulesPanel } from "./RulesPanel";

export function Settings({
  username,
  league,
  onOpen,
  onLogout,
  onRulesSaved,
}: {
  username: string;
  league: LeagueDetail | null;
  onOpen: (id: string) => void;
  onLogout: () => void;
  onRulesSaved: () => void;
}) {
  const [leagues, setLeagues] = useState<LeagueSummary[] | null>(null);
  const [name, setName] = useState("");
  const [cap, setCap] = useState("");
  const [joinCode, setJoinCode] = useState("");
  const [error, setError] = useState("");
  const [showRules, setShowRules] = useState(false);

  const refresh = useCallback(() => {
    api.myLeagues(username).then(setLeagues).catch((e) => setError((e as Error).message));
  }, [username]);
  useEffect(refresh, [refresh]);

  const create = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      setError("");
      const { id } = await api.createLeague(name, username, cap ? Number(cap) : null);
      setName("");
      setCap("");
      refresh();
      onOpen(id);
    } catch (err) {
      setError((err as Error).message);
    }
  };

  const join = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      setError("");
      const { id } = await api.joinLeague(joinCode.trim(), username);
      setJoinCode("");
      refresh();
      onOpen(id);
    } catch (err) {
      setError((err as Error).message);
    }
  };

  const isCommissioner = league != null && league.commissionerUsername === username;

  return (
    <section className="fade-in" style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
      <span className="section-title">Settings</span>
      {error && <p className="error-banner">{error}</p>}

      <div style={{ display: "flex", flexDirection: "column", gap: "0.6rem" }}>
        <span className="section-title">My leagues</span>
        {leagues === null ? (
          <p className="empty-state">Loading…</p>
        ) : leagues.length === 0 ? (
          <p className="empty-state">No league yet — create one or join with an invite code below.</p>
        ) : (
          <ul className="league-list">
            {leagues.map((l) => (
              <li key={l.id}>
                <button
                  className={`league-card${league?.id === l.id ? " active" : ""}`}
                  onClick={() => onOpen(l.id)}
                  aria-current={league?.id === l.id ? "true" : undefined}
                >
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
        )}
      </div>

      <form onSubmit={create} className="card" style={{ display: "flex", flexDirection: "column", gap: "0.6rem" }}>
        <span className="section-title">Create a league</span>
        <input
          className="field"
          value={name}
          onChange={(e) => setName(e.target.value)}
          placeholder="League name"
          aria-label="League name"
        />
        <input
          className="field"
          value={cap}
          onChange={(e) => setCap(e.target.value.replace(/\D/g, ""))}
          placeholder="Salary cap in $ (optional)"
          inputMode="numeric"
          aria-label="Salary cap"
        />
        <button type="submit" className="btn" disabled={!name.trim()}>
          Create
        </button>
      </form>

      <form onSubmit={join} className="card" style={{ display: "flex", flexDirection: "column", gap: "0.6rem" }}>
        <span className="section-title">Join a league</span>
        <input
          className="field"
          value={joinCode}
          onChange={(e) => setJoinCode(e.target.value)}
          placeholder="Invite code"
          aria-label="Invite code"
        />
        <button type="submit" className="btn" disabled={!joinCode.trim()}>
          Join
        </button>
      </form>

      {isCommissioner && league && (
        <div style={{ display: "flex", flexDirection: "column", gap: "0.6rem" }}>
          <div className="dashboard-section-head">
            <span className="section-title">
              <SettingsIcon size={14} className="inline-icon" /> League rules — {league.name}
            </span>
            <button
              type="button"
              className="btn-ghost"
              onClick={() => setShowRules(!showRules)}
              aria-expanded={showRules}
            >
              {showRules ? "Hide" : "Edit"}
            </button>
          </div>
          {showRules && (
            <RulesPanel
              league={league}
              username={username}
              onSaved={onRulesSaved}
              onClose={() => setShowRules(false)}
            />
          )}
        </div>
      )}

      <button className="btn-ghost" onClick={onLogout} style={{ alignSelf: "flex-start" }}>
        <LogOutIcon size={16} /> Log out ({username})
      </button>
    </section>
  );
}
