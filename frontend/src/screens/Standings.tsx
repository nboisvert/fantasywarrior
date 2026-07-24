import type { LeagueDetail } from "../api";

// Teams arrive sorted by score from the API. A row is a shortcut to that
// team's Stats screen (no inline roster expansion anymore).
export function Standings({
  league,
  username,
  onOpenTeamStats,
}: {
  league: LeagueDetail;
  username: string;
  onOpenTeamStats: (ownerUsername: string) => void;
}) {
  return (
    <section className="fade-in" style={{ display: "flex", flexDirection: "column", gap: "0.75rem" }}>
      <span className="section-title">Standings — Season {league.season}</span>
      {league.teams.length === 0 && <p className="empty-state">No team in this league yet.</p>}
      <ol className="standings-list">
        {league.teams.map((team, i) => (
          <li key={team.ownerUsername}>
            <button
              type="button"
              className={`standing-row${team.ownerUsername === username ? " mine" : ""}`}
              onClick={() => onOpenTeamStats(team.ownerUsername)}
              aria-label={`View ${team.name}'s stats`}
            >
              <span className={`rank r${i + 1}`}>{i + 1}</span>
              <div className="standing-info">
                <div className="team">{team.name}</div>
                <small>
                  @{team.ownerUsername} · {team.playerCount} player
                  {team.playerCount === 1 ? "" : "s"}
                  {team.adjustmentsTotal !== 0 && (
                    <> · adj {team.adjustmentsTotal > 0 ? "+" : ""}{team.adjustmentsTotal}</>
                  )}
                </small>
              </div>
              <div className="standing-points">
                <span className="pts">{team.score} pts</span>
                <small>{team.ptsPerGame != null ? `${team.ptsPerGame} pts/gm` : "—"}</small>
              </div>
            </button>
          </li>
        ))}
      </ol>
    </section>
  );
}
