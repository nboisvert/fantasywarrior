import { useCallback, useEffect, useState } from "react";
import { api, formatCap } from "../api";
import type { LeagueSummary } from "../api";
import { LogOutIcon } from "../components/Icons";

export function LeagueGate({
  username,
  onOpen,
  onLogout,
}: {
  username: string;
  onOpen: (id: string) => void;
  onLogout: () => void;
}) {
  const [leagues, setLeagues] = useState<LeagueSummary[] | null>(null);
  const [name, setName] = useState("");
  const [cap, setCap] = useState("");
  const [joinCode, setJoinCode] = useState("");
  const [error, setError] = useState("");

  const refresh = useCallback(() => {
    api.myLeagues(username).then(setLeagues).catch((e) => setError(e.message));
  }, [username]);
  useEffect(refresh, [refresh]);

  const create = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      const { id } = await api.createLeague(name, username, cap ? Number(cap) : null);
      onOpen(id);
    } catch (err) {
      setError((err as Error).message);
    }
  };

  const join = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      const { id } = await api.joinLeague(joinCode.trim(), username);
      onOpen(id);
    } catch (err) {
      setError((err as Error).message);
    }
  };

  return (
    <div className="shell fade-in">
      <header className="topbar">
        <span className="brand">
          Fantasy <span className="accent">Warrior</span>
        </span>
        <button className="btn-ghost" onClick={onLogout}>
          <LogOutIcon size={16} /> {username}
        </button>
      </header>
      <main className="shell-content" style={{ paddingTop: "1rem" }}>
        {error && <p className="error-banner">{error}</p>}

        <span className="section-title">My leagues</span>
        {leagues === null ? (
          <p className="empty-state">Loading…</p>
        ) : leagues.length === 0 ? (
          <p className="empty-state">No league yet — create one or join with an invite code.</p>
        ) : (
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
        )}

        <form onSubmit={create} className="card" style={{ display: "flex", flexDirection: "column", gap: "0.6rem" }}>
          <span className="section-title">Create a league</span>
          <input
            className="field"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="League name"
          />
          <input
            className="field"
            value={cap}
            onChange={(e) => setCap(e.target.value.replace(/\D/g, ""))}
            placeholder="Salary cap in $ (optional)"
            inputMode="numeric"
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
          />
          <button type="submit" className="btn" disabled={!joinCode.trim()}>
            Join
          </button>
        </form>
      </main>
    </div>
  );
}
