import { useEffect, useState } from "react";
import { api } from "../api";
import type { LeagueSeasonSummary } from "../api";
import { ArrowLeftIcon, TrophyIcon } from "../components/Icons";
import { useLanguage } from "../i18n/LanguageContext";

// "20262027" -> "2026-27". Same small formatter PlayerCard.tsx carries for its
// own career rows — deliberately not shared, since two characters of slicing
// is not worth a module boundary.
function formatSeason(season: string): string {
  if (season.length !== 8) return season;
  return `${season.slice(0, 4)}-${season.slice(6)}`;
}

const PHASE_KEY: Record<LeagueSeasonSummary["phase"], string> = {
  Preparing: "phasePreparing",
  Protecting: "phaseProtecting",
  Drafting: "phaseDrafting",
  PreSeason: "phasePreSeason",
  InSeason: "phaseInSeason",
  Complete: "phaseComplete",
};

/**
 * The palmarès — one row per season this league has ever played, newest
 * first. Reachable from a single link at the top of Standings rather than a
 * bottom-nav tab (already full) or a second shortcut anywhere else.
 */
export function Palmares({ leagueId, onClose }: { leagueId: string; onClose: () => void }) {
  const { t } = useLanguage();
  const [seasons, setSeasons] = useState<LeagueSeasonSummary[] | null>(null);
  const [error, setError] = useState("");

  useEffect(() => {
    let cancelled = false;
    api
      .seasons(leagueId)
      .then((rows) => {
        if (!cancelled) setSeasons(rows);
      })
      .catch((err) => {
        if (!cancelled) setError((err as Error).message);
      });
    return () => {
      cancelled = true;
    };
  }, [leagueId]);

  return (
    <section className="fade-in" style={{ display: "flex", flexDirection: "column", gap: "0.75rem" }}>
      <div style={{ display: "flex", alignItems: "center", gap: "0.6rem" }}>
        <button
          type="button"
          className="icon-btn"
          onClick={onClose}
          aria-label={t("palmares.back")}
          style={{ flexShrink: 0 }}
        >
          <ArrowLeftIcon size={20} />
        </button>
        <span className="section-title">{t("palmares.title")}</span>
      </div>

      {error && <p className="error-banner">{error}</p>}
      {seasons === null && !error && <p className="muted">{t("palmares.loading")}</p>}
      {seasons?.length === 0 && <p className="empty-state">{t("palmares.noSeason")}</p>}

      <ol className="standings-list">
        {seasons?.map((s) => (
          <li key={s.number}>
            <div className="standing-row" style={{ cursor: "default" }}>
              <span className="rank r1">{s.number}</span>
              <div className="standing-info">
                <div className="team">
                  {s.championTeamName ?? <span className="muted">{t(`palmares.${PHASE_KEY[s.phase]}`)}</span>}
                </div>
                <small>{t("palmares.seasonLine", { number: s.number, formatted: formatSeason(s.season) })}</small>
              </div>
              {s.phase === "Complete" && (
                <div className="standing-points">
                  <TrophyIcon size={20} />
                </div>
              )}
            </div>
          </li>
        ))}
      </ol>
    </section>
  );
}
