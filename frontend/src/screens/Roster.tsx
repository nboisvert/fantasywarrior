import { useEffect, useState } from "react";
import { api, formatCap } from "../api";
import type { LeagueDetail, PlayerDto } from "../api";
import { PlusIcon, SearchIcon, XIcon } from "../components/Icons";
import { PlayerCard } from "../components/PlayerCard";

export function Roster({
  league,
  username,
  onChanged,
}: {
  league: LeagueDetail;
  username: string;
  onChanged: () => void;
}) {
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<PlayerDto[]>([]);
  const [error, setError] = useState("");
  const [openPlayerId, setOpenPlayerId] = useState<number | null>(null);

  const myTeam = league.teams.find((t) => t.ownerUsername === username);

  useEffect(() => {
    if (query.trim().length < 2) {
      setResults([]);
      return;
    }
    const t = setTimeout(() => api.searchPlayers(query).then(setResults).catch(() => {}), 250);
    return () => clearTimeout(t);
  }, [query]);

  if (!myTeam) return <p className="empty-state">You don't have a team in this league.</p>;

  const capUsed = myTeam.capTotal;
  const capMax = league.capAmount;
  const over = capMax != null && capUsed > capMax;
  const pct = capMax ? Math.min(100, (capUsed / capMax) * 100) : 0;

  const add = async (playerId: number) => {
    try {
      setError("");
      await api.addPlayer(league.id, username, playerId);
      setQuery("");
      onChanged();
    } catch (err) {
      setError((err as Error).message);
    }
  };

  const remove = async (playerId: number) => {
    try {
      setError("");
      await api.removePlayer(league.id, username, playerId);
      onChanged();
    } catch (err) {
      setError((err as Error).message);
    }
  };

  return (
    <section className="fade-in" style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
      <div className="card cap-meter">
        <div className="cap-labels">
          <span>
            <span className={`used${over ? " over" : ""}`}>{formatCap(capUsed)}</span>
            {capMax != null && <> / {formatCap(capMax)}</>}
          </span>
          <span>
            {myTeam.name} · <span className="pts-small">{myTeam.score} pts</span>
          </span>
        </div>
        {capMax != null && (
          <div className="cap-track" role="progressbar" aria-valuenow={Math.round(pct)} aria-valuemin={0} aria-valuemax={100} aria-label="Salary cap used">
            <div className={`cap-fill${over ? " over" : ""}`} style={{ width: `${pct}%` }} />
          </div>
        )}
        {myTeam.adjustmentsTotal !== 0 && (
          <small className="muted">
            Raw top-X {myTeam.rawTopXScore} pts · adjustments {myTeam.adjustmentsTotal > 0 ? "+" : ""}
            {myTeam.adjustmentsTotal} pts
          </small>
        )}
      </div>

      {error && <p className="error-banner">{error}</p>}

      <div className="search-wrap">
        <SearchIcon size={18} className="search-icon" />
        <input
          className="field"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder="Search a player to add…"
          aria-label="Search players"
        />
      </div>

      {results.length > 0 && (
        <ul className="player-list">
          {results.map((p) => (
            <li key={p.id}>
              <button className="player-row clickable" onClick={() => add(p.id)}>
                <img className="headshot" src={p.headshotUrl ?? ""} alt="" loading="lazy" />
                <span className="player-info">
                  <span className="name">{p.name}</span>
                  <small>
                    <span className="pos-badge">{p.position}</span>
                    {p.team} · {p.status}
                  </small>
                </span>
                <span className="player-cap">{formatCap(p.capHit)}</span>
                <PlusIcon size={18} />
              </button>
            </li>
          ))}
        </ul>
      )}

      <span className="section-title">My roster ({myTeam.players.length})</span>
      {myTeam.players.length === 0 && (
        <p className="empty-state">Empty roster — search a player above to get started.</p>
      )}
      <ul className="player-list">
        {myTeam.players.map((p) => (
          <li key={p.id} className={`player-row${p.counted ? " counted" : ""}`}>
            <button
              className="player-hit"
              onClick={() => setOpenPlayerId(p.id)}
              aria-label={`Open ${p.name} card`}
            >
              <img className="headshot" src={p.headshotUrl ?? ""} alt="" loading="lazy" />
              <span className="player-info">
                <span className="name">{p.name}</span>
                <small>
                  <span className="pos-badge">{p.position}</span>
                  {p.team} · {formatCap(p.capHit)}
                </small>
              </span>
              <span className="pts-small">{p.points} pts</span>
            </button>
            <button className="icon-btn" onClick={() => remove(p.id)} aria-label={`Remove ${p.name}`}>
              <XIcon size={18} />
            </button>
          </li>
        ))}
      </ul>
      {openPlayerId != null && (
        <PlayerCard playerId={openPlayerId} onClose={() => setOpenPlayerId(null)} />
      )}
    </section>
  );
}
