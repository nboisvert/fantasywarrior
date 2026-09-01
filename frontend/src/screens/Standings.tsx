import { Fragment, useState } from "react";
import type { ReactNode } from "react";
import { api, formatCapCompact } from "../api";
import type { LeagueDetail, TeamDto, TeamPeriodRow } from "../api";
import { ArrowDownIcon, ArrowUpIcon, CalendarIcon, ChevronDownIcon, CrossIcon, TrophyIcon } from "../components/Icons";
import { useLanguage } from "../i18n/LanguageContext";

/** The rank-movement pill, now its own "Last Night" sub-column rather than a
 * mark next to the team name (2026-09-01, per Nick). `null` ("nothing to
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

/* ---------- sorting — same shape as Stats.tsx's own useSort, duplicated
   rather than shared: a screen-local concern, not a cross-screen one. ---------- */

type SortDir = "asc" | "desc";

function compareNullable(a: number | null, b: number | null, dir: SortDir): number {
  if (a == null && b == null) return 0;
  if (a == null) return 1;
  if (b == null) return -1;
  return dir === "asc" ? a - b : b - a;
}

function useSort<T extends object>(rows: T[], initialKey: keyof T) {
  const [key, setKey] = useState<keyof T>(initialKey);
  const [dir, setDir] = useState<SortDir>("desc");

  const toggle = (k: string) => {
    const typedKey = k as keyof T;
    if (typedKey === key) setDir((d) => (d === "asc" ? "desc" : "asc"));
    else {
      setKey(typedKey);
      setDir("desc");
    }
  };

  const sorted = [...rows].sort((a, b) => {
    const av = a[key];
    const bv = b[key];
    if (typeof av === "number" || av == null || typeof bv === "number" || bv == null) {
      return compareNullable(av as number | null, bv as number | null, dir);
    }
    const cmp = String(av).localeCompare(String(bv));
    return dir === "asc" ? cmp : -cmp;
  });

  return { sorted, key: key as string, dir, toggle };
}

function GroupHead({ label, span, accent }: { label: string; span: number; accent?: boolean }) {
  return (
    <th colSpan={span} className={`standings-group-th${accent ? " accent" : ""}`} scope="colgroup">
      {label}
    </th>
  );
}

function SortableHead({
  label,
  ariaLabel,
  colKey,
  active,
  dir,
  onSort,
  accent,
  spotlight,
  groupStart,
}: {
  label: ReactNode;
  /** Only needed when `label` isn't plain text (the icon-only Injury header). */
  ariaLabel?: string;
  colKey: string;
  active: boolean;
  dir: SortDir;
  onSort: (k: string) => void;
  accent?: boolean;
  spotlight?: boolean;
  groupStart?: boolean;
}) {
  return (
    <th
      scope="col"
      className={`standings-sortable${accent ? " accent" : ""}${spotlight ? " standings-col-spotlight" : ""}${groupStart ? " standings-group-start" : ""}`}
      aria-sort={active ? (dir === "asc" ? "ascending" : "descending") : "none"}
    >
      <button type="button" className="standings-sort-btn" aria-label={ariaLabel} onClick={() => onSort(colKey)}>
        {label}
        {active && (
          <ChevronDownIcon size={12} className={`standings-sort-icon${dir === "asc" ? " asc" : ""}`} />
        )}
      </button>
    </th>
  );
}

/** A team's whole season, week by week — same shape as Stats.tsx's
 * PlayerPeriods, minus the active/bench-jersey split: there's no single
 * "in the lineup" flag for a whole team, since different spots can be
 * active or benched the same week. */
function TeamPeriods({ periods }: { periods: TeamPeriodRow[] }) {
  const { t } = useLanguage();
  if (periods.length === 0) return <p className="muted team-periods-empty">{t("standings.noWeeksScored")}</p>;

  const totalActive = periods.reduce((sum, p) => sum + p.activePoints, 0);
  const totalBench = periods.reduce((sum, p) => sum + p.benchPoints, 0);

  return (
    <div className="team-periods">
      <table className="team-periods-table">
        <thead>
          <tr>
            <th>{t("standings.colWeek")}</th>
            <th>GP</th>
            <th className="team-periods-pts">PTS</th>
            <th>{t("standings.colBench")}</th>
          </tr>
        </thead>
        <tbody>
          {periods.map((p) => (
            <tr key={p.periodIndex}>
              <td>
                <span className="team-periods-week">W{String(p.periodIndex).padStart(2, "0")}</span>
                <span className="muted"> {new Date(`${p.startDate}T12:00:00Z`).toLocaleDateString(undefined, { month: "short", day: "numeric", timeZone: "UTC" })}</span>
              </td>
              <td>{p.gameCount === 0 ? <span className="muted">—</span> : p.gamesPlayed}</td>
              <td className="team-periods-pts">{p.activePoints}</td>
              <td className="muted">{p.benchPoints}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <div className="team-periods-totals">
        <span>
          <strong>{totalActive}</strong> {t("standings.periodsActiveTail")}
        </span>
        {totalBench > 0 && <span className="team-periods-bench">{t("standings.periodsBenchTotal", { pts: totalBench })}</span>}
      </div>
    </div>
  );
}

interface StandingsRow extends TeamDto {
  /** Fixed standings position — never changes when the table is sorted by
   * another column, same as a spreadsheet's row number staying put. */
  rank: number;
}

// Teams arrive sorted by score from the API; that order becomes `rank`,
// computed once and carried along regardless of how the table is sorted.
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
  const rows: StandingsRow[] = league.teams.map((team, i) => ({ ...team, rank: i + 1 }));
  const sort = useSort<StandingsRow>(rows, "score");

  // Which team's week-by-week panel is open, and what has been fetched so
  // far. Cached per team: reopening a row should not re-ask.
  const [openPeriodsFor, setOpenPeriodsFor] = useState<string | null>(null);
  const [periodsByTeam, setPeriodsByTeam] = useState<Record<string, TeamPeriodRow[]>>({});

  async function togglePeriods(ownerUsername: string) {
    if (openPeriodsFor === ownerUsername) {
      setOpenPeriodsFor(null);
      return;
    }
    setOpenPeriodsFor(ownerUsername);
    if (periodsByTeam[ownerUsername]) return;
    try {
      const data = await api.teamPeriods(league.id, ownerUsername);
      setPeriodsByTeam((prev) => ({ ...prev, [ownerUsername]: data.periods }));
    } catch {
      // Closing again is a truer signal than an error banner over the grid.
      setOpenPeriodsFor(null);
    }
  }

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
                <GroupHead label={t("standings.groupFantasy")} span={5} accent />
                <GroupHead label={t("standings.groupLastNight")} span={3} />
                <GroupHead label={t("standings.groupThisWeek")} span={2} />
                <GroupHead label={t("standings.groupExtra")} span={2} />
              </tr>
              <tr>
                <SortableHead label="GP" colKey="gamesPlayed" active={sort.key === "gamesPlayed"} dir={sort.dir} onSort={sort.toggle} accent groupStart />
                <SortableHead label="G" colKey="goals" active={sort.key === "goals"} dir={sort.dir} onSort={sort.toggle} accent />
                <SortableHead label="A" colKey="assists" active={sort.key === "assists"} dir={sort.dir} onSort={sort.toggle} accent />
                <SortableHead label="PTS" colKey="score" active={sort.key === "score"} dir={sort.dir} onSort={sort.toggle} accent spotlight />
                <SortableHead label="PTS/G" colKey="ptsPerGame" active={sort.key === "ptsPerGame"} dir={sort.dir} onSort={sort.toggle} accent />
                <SortableHead label="GP" colKey="lastNightGamesPlayed" active={sort.key === "lastNightGamesPlayed"} dir={sort.dir} onSort={sort.toggle} groupStart />
                <SortableHead label="PTS" colKey="lastNightPoints" active={sort.key === "lastNightPoints"} dir={sort.dir} onSort={sort.toggle} />
                <SortableHead label="±" colKey="rankChange" active={sort.key === "rankChange"} dir={sort.dir} onSort={sort.toggle} />
                <SortableHead label="GP" colKey="periodGamesPlayed" active={sort.key === "periodGamesPlayed"} dir={sort.dir} onSort={sort.toggle} groupStart />
                <SortableHead label="PTS" colKey="periodPoints" active={sort.key === "periodPoints"} dir={sort.dir} onSort={sort.toggle} />
                <SortableHead
                  label={<CrossIcon size={12} />}
                  ariaLabel={t("standings.colInjured")}
                  colKey="injuredCount"
                  active={sort.key === "injuredCount"}
                  dir={sort.dir}
                  onSort={sort.toggle}
                  groupStart
                />
                <SortableHead label={t("standings.groupCapHit")} colKey="capTotal" active={sort.key === "capTotal"} dir={sort.dir} onSort={sort.toggle} />
              </tr>
            </thead>
            <tbody>
              {sort.sorted.map((team) => (
                <Fragment key={team.ownerUsername}>
                  <tr className={team.ownerUsername === username ? "mine" : undefined}>
                    <td className="standings-col-team">
                      <div className="standings-col-team-inner">
                        <span className="standings-rank-plain">{team.rank}</span>
                        <button
                          type="button"
                          className="standings-team-btn"
                          onClick={() => onOpenTeamStats(team.ownerUsername)}
                          aria-label={t("standings.viewStats", { team: team.name })}
                        >
                          <span className="standings-team-name">{team.name}</span>
                        </button>
                        <button
                          type="button"
                          className={`standings-periods-btn${openPeriodsFor === team.ownerUsername ? " open" : ""}`}
                          onClick={() => void togglePeriods(team.ownerUsername)}
                          aria-expanded={openPeriodsFor === team.ownerUsername}
                          aria-label={t("standings.weekByWeekAria", { team: team.name })}
                          title={t("standings.weekByWeek")}
                        >
                          <CalendarIcon size={13} />
                        </button>
                      </div>
                    </td>
                    <td className="accent standings-group-start">{team.gamesPlayed}</td>
                    <td className="accent">{team.goals}</td>
                    <td className="accent">{team.assists}</td>
                    <td className="accent standings-col-spotlight">{team.score}</td>
                    <td className="accent">{team.ptsPerGame != null ? team.ptsPerGame.toFixed(2) : t("standings.noStats")}</td>
                    <td className="standings-group-start">
                      {team.lastNightGamesPlayed != null ? team.lastNightGamesPlayed : t("standings.noStats")}
                    </td>
                    <td>{team.lastNightPoints != null ? team.lastNightPoints : t("standings.noStats")}</td>
                    <td>
                      <RankPill change={team.rankChange} />
                    </td>
                    <td className="standings-group-start">{league.currentPeriod ? team.periodGamesPlayed : t("standings.noStats")}</td>
                    <td>{league.currentPeriod ? team.periodPoints : t("standings.noStats")}</td>
                    <td className="standings-group-start">
                      {team.injuredCount > 0 ? team.injuredCount : <span className="muted">—</span>}
                    </td>
                    <td>{formatCapCompact(team.capTotal)}</td>
                  </tr>
                  {openPeriodsFor === team.ownerUsername && (
                    <tr className="team-periods-row">
                      <td colSpan={13}>
                        {periodsByTeam[team.ownerUsername] ? (
                          <TeamPeriods periods={periodsByTeam[team.ownerUsername]} />
                        ) : (
                          <p className="muted team-periods-empty">{t("common.loading")}…</p>
                        )}
                      </td>
                    </tr>
                  )}
                </Fragment>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
