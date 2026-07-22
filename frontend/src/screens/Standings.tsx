import { formatCap } from "../api";
import type { LeagueDetail } from "../api";

// Points come from the scoring engine (Phase 4); until then every team
// shows 0 pts and ties are broken by cap total then name.
export function Standings({ league, username }: { league: LeagueDetail; username: string }) {
  const ranked = [...league.teams].sort(
    (a, b) => b.capTotal - a.capTotal || a.name.localeCompare(b.name),
  );

  return (
    <section className="fade-in" style={{ display: "flex", flexDirection: "column", gap: "0.75rem" }}>
      <span className="section-title">Standings — Season {league.season}</span>
      {ranked.length === 0 && <p className="empty-state">No team in this league yet.</p>}
      <ol className="standings-list">
        {ranked.map((team, i) => (
          <li
            key={team.ownerUsername}
            className={`standing-row${team.ownerUsername === username ? " mine" : ""}`}
          >
            <span className={`rank r${i + 1}`}>{i + 1}</span>
            <div className="standing-info">
              <div className="team">{team.name}</div>
              <small>
                @{team.ownerUsername} · {team.players.length} player
                {team.players.length === 1 ? "" : "s"}
              </small>
            </div>
            <div className="standing-points">
              <span className="pts">0 pts</span>
              <small>{formatCap(team.capTotal)}</small>
            </div>
          </li>
        ))}
      </ol>
    </section>
  );
}
