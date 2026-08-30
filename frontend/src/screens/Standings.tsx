import type { LeagueDetail } from "../api";
import { TrophyIcon } from "../components/Icons";
import { useLanguage } from "../i18n/LanguageContext";

// Teams arrive sorted by score from the API. A row is a shortcut to that
// team's Stats screen (no inline roster expansion anymore).
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
      <ol className="standings-list">
        {league.teams.map((team, i) => (
          <li key={team.ownerUsername}>
            <button
              type="button"
              className={`standing-row${team.ownerUsername === username ? " mine" : ""}`}
              onClick={() => onOpenTeamStats(team.ownerUsername)}
              aria-label={t("standings.viewStats", { team: team.name })}
            >
              <span className={`rank r${i + 1}`}>{i + 1}</span>
              <div className="standing-info">
                <div className="team">{team.name}</div>
                <small>
                  @{team.ownerUsername} · {t("standings.playerCount", { count: team.playerCount })}
                </small>
              </div>
              <div className="standing-points">
                <span className="pts">{team.score} pts</span>
                {/* This week's take, which is what actually moves during a
                    week — the season total barely budges day to day. */}
                <small>
                  {league.currentPeriod
                    ? t("standings.thisWeek", { points: team.periodPoints ?? 0 })
                    : team.ptsPerGame != null
                      ? t("standings.ptsPerGame", { value: team.ptsPerGame })
                      : t("standings.noStats")}
                </small>
              </div>
            </button>
          </li>
        ))}
      </ol>
    </section>
  );
}
