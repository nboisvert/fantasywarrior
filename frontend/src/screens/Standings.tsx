import type { LeagueDetail } from "../api";
import { ArrowDownIcon, ArrowUpIcon, TrophyIcon } from "../components/Icons";
import { useLanguage } from "../i18n/LanguageContext";

/** The rank-movement pill next to a team's name: up/down since last night's
 * games, from the two most recent nightly snapshots. `null` ("nothing to
 * compare yet") and `0` ("compared, no movement") render the same dash but
 * carry different aria-labels — two different facts, not one collapsed
 * into the other. */
function RankPill({ change }: { change: number | null }) {
  const { t } = useLanguage();
  if (change === null)
    return (
      <span className="standings-rank-pill neutral" aria-hidden="true">
        —
      </span>
    );
  if (change === 0)
    return (
      <span className="standings-rank-pill neutral" aria-label={t("standings.rankSame")}>
        —
      </span>
    );
  const up = change > 0;
  const label = up
    ? t("standings.rankUp", { spots: change })
    : t("standings.rankDown", { spots: Math.abs(change) });
  return (
    <span className={`standings-rank-pill ${up ? "up" : "down"}`} aria-label={label} title={label}>
      {up ? <ArrowUpIcon size={12} /> : <ArrowDownIcon size={12} />}
      {Math.abs(change)}
    </span>
  );
}

// Teams arrive sorted by score from the API — that order is the rank. A row
// is a shortcut to that team's Stats screen (no inline roster expansion).
export function Standings({
  league,
  username,
  onOpenTeamStats,
  onOpenPalmares,
}: {
  league: LeagueDetail;
  username: string;
  onOpenTeamStats: (ownerUsername: string) => void;
  onOpenPalmares: () => void;
}) {
  const { t } = useLanguage();
  return (
    <section className="fade-in" style={{ display: "flex", flexDirection: "column", gap: "0.75rem" }}>
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: "0.6rem" }}>
        <span className="section-title">{t("standings.title", { season: league.season })}</span>
        <button
          type="button"
          className="icon-btn"
          onClick={onOpenPalmares}
          aria-label={t("standings.viewPalmares")}
        >
          <TrophyIcon size={20} />
        </button>
      </div>
      {league.teams.length === 0 && <p className="empty-state">{t("standings.noTeamYet")}</p>}
      {league.teams.length > 0 && (
        <div className="standings-grid-scroll">
          <table className="standings-grid">
            <thead>
              <tr className="standings-group-row">
                <th className="standings-col-team" rowSpan={2} scope="col" />
                <th colSpan={5} className="standings-group-th accent">
                  {t("standings.groupFantasy")}
                </th>
                <th colSpan={2} className="standings-group-th">
                  {t("standings.groupRecent")}
                </th>
              </tr>
              <tr>
                <th className="standings-group-start" scope="col">
                  GP
                </th>
                <th scope="col">G</th>
                <th scope="col">A</th>
                <th className="standings-col-spotlight" scope="col">
                  PTS
                </th>
                <th scope="col">PTS/G</th>
                <th className="standings-group-start" scope="col">
                  {t("standings.colLastNight")}
                </th>
                <th scope="col">{t("standings.colThisWeek")}</th>
              </tr>
            </thead>
            <tbody>
              {league.teams.map((team, i) => (
                <tr key={team.ownerUsername} className={team.ownerUsername === username ? "mine" : undefined}>
                  <td className="standings-col-team">
                    <button
                      type="button"
                      onClick={() => onOpenTeamStats(team.ownerUsername)}
                      aria-label={t("standings.viewStats", { team: team.name })}
                    >
                      <span className={`rank r${i + 1}`}>{i + 1}</span>
                      <span className="standings-team-name">{team.name}</span>
                      <RankPill change={team.rankChange} />
                    </button>
                  </td>
                  <td className="standings-group-start">{team.gamesPlayed}</td>
                  <td>{team.goals}</td>
                  <td>{team.assists}</td>
                  <td className="standings-col-spotlight">{team.score}</td>
                  <td>{team.ptsPerGame != null ? team.ptsPerGame.toFixed(2) : t("standings.noStats")}</td>
                  <td className="standings-group-start">
                    {team.lastNightPoints != null ? team.lastNightPoints : t("standings.noStats")}
                  </td>
                  <td>{league.currentPeriod ? team.periodPoints : t("standings.noStats")}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
