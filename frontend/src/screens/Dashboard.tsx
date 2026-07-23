import { useEffect, useState } from "react";
import { api, formatCap } from "../api";
import type { ActivityEntry, LeagueDetail } from "../api";
import { ActivityIcon, ArrowLeftRightIcon, PlusIcon, XIcon } from "../components/Icons";
import { PlayerCard } from "../components/PlayerCard";

const TOP_STANDINGS = 5;
const TOP_SCORERS = 7;
const TICKER_INTERVAL_MS = 3600;

function capitalize(s: string): string {
  return s.length === 0 ? s : s[0].toUpperCase() + s.slice(1);
}

function formatRelativeTime(dateUtc: string): string {
  const d = new Date(dateUtc);
  if (Number.isNaN(d.getTime())) return dateUtc;
  const diffMs = Date.now() - d.getTime();
  const diffMin = Math.floor(diffMs / 60000);
  if (diffMin < 1) return "Just now";
  if (diffMin < 60) return `${diffMin}m ago`;
  const diffHours = Math.floor(diffMin / 60);
  if (diffHours < 24) return `${diffHours}h ago`;
  const diffDays = Math.floor(diffHours / 24);
  if (diffDays === 1) return "Yesterday";
  if (diffDays < 7) return `${diffDays}d ago`;
  return d.toLocaleDateString("en-US", { month: "short", day: "numeric" });
}

function activityText(entry: ActivityEntry): { verb: string; suffix: string } {
  const verb = entry.type === "add" ? "added" : "dropped";
  const suffix = entry.source === "trade" ? " (trade)" : entry.source === "draft" ? " (draft)" : "";
  return { verb, suffix };
}

/** Tracks `prefers-reduced-motion`, live — auto-advance must react if the OS setting flips mid-session. */
function usePrefersReducedMotion(): boolean {
  const [reduced, setReduced] = useState(
    () => typeof window !== "undefined" && window.matchMedia("(prefers-reduced-motion: reduce)").matches,
  );
  useEffect(() => {
    const mql = window.matchMedia("(prefers-reduced-motion: reduce)");
    const onChange = () => setReduced(mql.matches);
    mql.addEventListener("change", onChange);
    return () => mql.removeEventListener("change", onChange);
  }, []);
  return reduced;
}

/** Single-item auto-looping activity carousel. Reserves a fixed-height slot so
 * loading/empty/error/entry states never shift surrounding layout. */
function ActivityTicker({
  activity,
  error,
  username,
}: {
  activity: ActivityEntry[] | null;
  error: string;
  username: string;
}) {
  const [index, setIndex] = useState(0);
  const [paused, setPaused] = useState(false);
  const reducedMotion = usePrefersReducedMotion();

  useEffect(() => {
    setIndex(0);
  }, [activity]);

  useEffect(() => {
    if (reducedMotion || paused || !activity || activity.length <= 1) return;
    const t = setInterval(() => {
      setIndex((i) => (i + 1) % activity.length);
    }, TICKER_INTERVAL_MS);
    return () => clearInterval(t);
  }, [activity, paused, reducedMotion]);

  const entry = activity && activity.length > 0 ? activity[index % activity.length] : null;
  const { verb, suffix } = entry ? activityText(entry) : { verb: "", suffix: "" };

  return (
    <div
      className="card ticker"
      onMouseEnter={() => setPaused(true)}
      onMouseLeave={() => setPaused(false)}
      onFocus={() => setPaused(true)}
      onBlur={() => setPaused(false)}
      tabIndex={0}
      role="group"
      aria-label="Recent pool activity, auto-updating. Pauses on hover or focus."
    >
      <div className="ticker-viewport" aria-live="polite" aria-atomic="true">
        {error ? (
          <p className="empty-state ticker-empty">{error}</p>
        ) : activity === null ? (
          <p className="empty-state ticker-empty">Loading…</p>
        ) : !entry ? (
          <p className="empty-state ticker-empty">No transactions yet this season.</p>
        ) : (
          <div className={`activity-row ticker-row${entry.teamUsername === username ? " mine" : ""}`}>
            <span className={`activity-icon${entry.type === "drop" ? " drop" : ""}`}>
              {entry.source === "trade" ? (
                <ArrowLeftRightIcon size={16} />
              ) : entry.type === "add" ? (
                <PlusIcon size={16} />
              ) : (
                <XIcon size={16} />
              )}
            </span>
            <span className="activity-text">
              <strong>{capitalize(entry.teamUsername)}</strong> {verb} {entry.playerName}
              {suffix && <span className="muted">{suffix}</span>}
            </span>
            <span className="activity-time">{formatRelativeTime(entry.dateUtc)}</span>
          </div>
        )}
      </div>
    </div>
  );
}

export function Dashboard({
  league,
  username,
}: {
  league: LeagueDetail;
  username: string;
}) {
  const [openPlayerId, setOpenPlayerId] = useState<number | null>(null);
  const [activity, setActivity] = useState<ActivityEntry[] | null>(null);
  const [activityError, setActivityError] = useState("");

  useEffect(() => {
    let cancelled = false;
    setActivity(null);
    setActivityError("");
    api
      .activity(league.id, 15)
      .then((data) => {
        if (!cancelled) setActivity(data);
      })
      .catch((e) => {
        if (!cancelled) setActivityError((e as Error).message);
      });
    return () => {
      cancelled = true;
    };
  }, [league.id]);

  const myIndex = league.teams.findIndex((t) => t.ownerUsername === username);
  const myTeam = myIndex >= 0 ? league.teams[myIndex] : undefined;
  const myRank = myIndex >= 0 ? myIndex + 1 : null;
  const topScorers = myTeam ? myTeam.players.slice(0, TOP_SCORERS) : [];

  // Standings: show top-5 normally, unless my team falls outside the top 5 —
  // then show top-4 + a bold gap divider + my team's true rank as the 5th row.
  const showGap = myRank != null && myRank > TOP_STANDINGS;
  const topTeams = league.teams.slice(0, showGap ? TOP_STANDINGS - 1 : Math.min(TOP_STANDINGS, league.teams.length));

  const capUsed = myTeam?.capTotal ?? null;
  const capMax = league.capAmount;
  const over = capMax != null && capUsed != null && capUsed > capMax;
  const pct = capMax != null && capUsed != null ? Math.min(100, (capUsed / capMax) * 100) : 0;

  return (
    <section className="fade-in dash-grid">
      <div className="dash-standings">
        <span className="section-title">Top of the pool</span>
        {topTeams.length === 0 ? (
          <p className="empty-state">No team in this league yet.</p>
        ) : (
          <div className="mini-table mini-standings">
            {topTeams.map((team, i) => (
              <div
                key={team.ownerUsername}
                className={`mini-row${team.ownerUsername === username ? " mine" : ""}`}
              >
                <span className={`msr-rank r${i + 1}`}>{i + 1}</span>
                <span className="msr-team">
                  <span className="msr-name">{team.name}</span>
                </span>
                <span className="msr-pts">{team.score}</span>
              </div>
            ))}
            {showGap && myTeam && myRank != null && (
              <>
                <div className="standings-gap" aria-hidden="true" />
                <div className="mini-row mine">
                  <span className="msr-rank">{myRank}</span>
                  <span className="msr-team">
                    <span className="msr-name">{myTeam.name}</span>
                  </span>
                  <span className="msr-pts">{myTeam.score}</span>
                </div>
              </>
            )}
          </div>
        )}
      </div>

      <div className="dash-cap">
        <span className="section-title">Cap space</span>
        <div className="card cap-meter dash-cap-meter">
          {!myTeam ? (
            <p className="empty-state dash-cap-empty">No team in this league.</p>
          ) : (
            <>
              <div className="cap-labels">
                <span>
                  <span className={`used${over ? " over" : ""}`}>{formatCap(capUsed)}</span>
                  {capMax != null && <> / {formatCap(capMax)}</>}
                </span>
              </div>
              {capMax != null && (
                <div
                  className="cap-track"
                  role="progressbar"
                  aria-valuenow={Math.round(pct)}
                  aria-valuemin={0}
                  aria-valuemax={100}
                  aria-label="Salary cap used"
                >
                  <div className={`cap-fill${over ? " over" : ""}`} style={{ width: `${pct}%` }} />
                </div>
              )}
            </>
          )}
        </div>
      </div>

      <div className="dash-activity">
        <span className="section-title">
          <ActivityIcon size={14} className="inline-icon" /> Recent activity
        </span>
        <ActivityTicker activity={activity} error={activityError} username={username} />
      </div>

      <div className="dash-scorers">
        <span className="section-title">My top scorers</span>
        {!myTeam ? (
          <p className="empty-state">You don't have a team in this league.</p>
        ) : topScorers.length === 0 ? (
          <p className="empty-state">Empty roster — add players from the Roster tab.</p>
        ) : (
          <div className="mini-table mini-scorers">
            {topScorers.map((p) => (
              <button key={p.id} className="mini-row clickable" onClick={() => setOpenPlayerId(p.id)}>
                <span className="msr-name">{p.name}</span>
                <span className="msr-pos">
                  {p.position} <span className="msr-team-abbr">{p.team}</span>
                </span>
                <span className="msr-pts">{p.points}</span>
              </button>
            ))}
          </div>
        )}
      </div>

      {openPlayerId != null && <PlayerCard playerId={openPlayerId} onClose={() => setOpenPlayerId(null)} />}
    </section>
  );
}
