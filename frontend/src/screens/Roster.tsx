import { useState } from "react";
import { formatCap } from "../api";
import type { LeagueDetail } from "../api";
import { PlayerCard } from "../components/PlayerCard";

// Read-only roster view: rosters are season-long (see project docs) —
// add/remove is intentionally not exposed here.
export function Roster({ league, username }: { league: LeagueDetail; username: string }) {
  const [openPlayerId, setOpenPlayerId] = useState<number | null>(null);

  const myTeam = league.teams.find((t) => t.ownerUsername === username);
  if (!myTeam) return <p className="empty-state">You don't have a team in this league.</p>;

  const capUsed = myTeam.capTotal;
  const capMax = league.capAmount;
  const over = capMax != null && capUsed > capMax;
  const pct = capMax ? Math.min(100, (capUsed / capMax) * 100) : 0;

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

      <span className="section-title">My roster ({myTeam.players.length})</span>
      {myTeam.players.length === 0 && <p className="empty-state">Empty roster.</p>}
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
          </li>
        ))}
      </ul>
      {openPlayerId != null && (
        <PlayerCard playerId={openPlayerId} onClose={() => setOpenPlayerId(null)} />
      )}
    </section>
  );
}
