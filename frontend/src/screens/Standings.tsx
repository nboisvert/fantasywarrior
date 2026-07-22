import { useState } from "react";
import { formatCap } from "../api";
import type { LeagueDetail } from "../api";
import { PlayerCard } from "../components/PlayerCard";

// Teams arrive sorted by score from the API.
export function Standings({ league, username }: { league: LeagueDetail; username: string }) {
  const [openPlayerId, setOpenPlayerId] = useState<number | null>(null);
  const [expanded, setExpanded] = useState<string | null>(null);

  return (
    <section className="fade-in" style={{ display: "flex", flexDirection: "column", gap: "0.75rem" }}>
      <span className="section-title">Standings — Season {league.season}</span>
      {league.teams.length === 0 && <p className="empty-state">No team in this league yet.</p>}
      <ol className="standings-list">
        {league.teams.map((team, i) => (
          <li key={team.ownerUsername}>
            <div
              className={`standing-row${team.ownerUsername === username ? " mine" : ""}`}
              onClick={() => setExpanded(expanded === team.ownerUsername ? null : team.ownerUsername)}
              style={{ cursor: "pointer" }}
            >
              <span className={`rank r${i + 1}`}>{i + 1}</span>
              <div className="standing-info">
                <div className="team">{team.name}</div>
                <small>
                  @{team.ownerUsername} · {team.players.length} player
                  {team.players.length === 1 ? "" : "s"}
                  {team.adjustmentsTotal !== 0 && (
                    <> · adj {team.adjustmentsTotal > 0 ? "+" : ""}{team.adjustmentsTotal}</>
                  )}
                </small>
              </div>
              <div className="standing-points">
                <span className="pts">{team.score} pts</span>
                <small>{formatCap(team.capTotal)}</small>
              </div>
            </div>
            {expanded === team.ownerUsername && (
              <ul className="player-list" style={{ margin: "0.5rem 0 0.25rem 2.6rem" }}>
                {team.players.map((p) => (
                  <li key={p.id}>
                    <button className="player-row clickable" onClick={() => setOpenPlayerId(p.id)}>
                      <span className="player-info">
                        <span className="name">
                          {p.name}
                          {p.counted && <span className="counted-badge">TOP</span>}
                        </span>
                        <small>
                          <span className="pos-badge">{p.position}</span>
                          {p.team}
                        </small>
                      </span>
                      <span className="pts-small">{p.points} pts</span>
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </li>
        ))}
      </ol>
      {openPlayerId != null && (
        <PlayerCard playerId={openPlayerId} onClose={() => setOpenPlayerId(null)} />
      )}
    </section>
  );
}
