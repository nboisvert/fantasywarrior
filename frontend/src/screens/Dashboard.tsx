import { useEffect, useMemo, useRef, useState } from "react";
import { api, formatCap } from "../api";
import type { ActivityEntry, LeagueDetail } from "../api";
import { ActivityIcon, ArrowLeftRightIcon, PlusIcon, XIcon } from "../components/Icons";
import { PlayerCard } from "../components/PlayerCard";

const TOP_STANDINGS = 5;
const TOP_SCORERS = 7;
const TICKER_INTERVAL_MS = 3600;
const TICKER_VISIBLE_ROWS = 3;
/** Must match the inline `transitionDuration` set on `.ticker-track` — this is how the
 * "rewind the loop" timer below stays in lockstep with the CSS slide animation it's timing. */
const TICKER_SLIDE_MS = 420;

function capitalize(s: string): string {
  return s.length === 0 ? s : s[0].toUpperCase() + s.slice(1);
}

/** Mirrors the backend's `PositionGroups.From` mapping (FantasyWarrior.Core/Scoring/RuleConfig.cs):
 * "D" -> Defense, "G" -> Goalie, anything else (C/L/R/LW/RW/...) -> Forward. Frontend-only,
 * no backend import — just the same handful of lines. */
function positionGroupLabel(position: string): "F" | "D" | "G" {
  switch (position) {
    case "D":
      return "D";
    case "G":
      return "G";
    default:
      return "F";
  }
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

function ActivityRow({ entry, mine }: { entry: ActivityEntry; mine: boolean }) {
  const { verb, suffix } = activityText(entry);
  return (
    <div className={`activity-row ticker-row${mine ? " mine" : ""}`}>
      <span className={`activity-icon${entry.type === "drop" ? " drop" : ""}`}>
        {entry.source === "trade" ? (
          <ArrowLeftRightIcon size={12} />
        ) : entry.type === "add" ? (
          <PlusIcon size={12} />
        ) : (
          <XIcon size={12} />
        )}
      </span>
      <span className="activity-text">
        <strong>{capitalize(entry.teamUsername)}</strong> {verb} {entry.playerName}
        {suffix && <span className="muted">{suffix}</span>}
      </span>
      <span className="activity-time">{formatRelativeTime(entry.dateUtc)}</span>
    </div>
  );
}

/** Vertical sliding carousel over the full activity list: shows a 3-row window that
 * auto-advances one row at a time on a smooth CSS `translateY` transition (new entry
 * slides in at the bottom, oldest slides out the top). Reserves a fixed 3-row-tall slot
 * so loading/empty/error/entry states never shift surrounding layout. */
function ActivityTicker({
  activity,
  error,
  username,
}: {
  activity: ActivityEntry[] | null;
  error: string;
  username: string;
}) {
  const list = useMemo(() => activity ?? [], [activity]);
  const total = list.length;
  const canSlide = total > TICKER_VISIBLE_ROWS;
  const reducedMotion = usePrefersReducedMotion();
  const showStatic = !canSlide || reducedMotion;

  const [windowStart, setWindowStart] = useState(0);
  const [instant, setInstant] = useState(false);
  const [paused, setPaused] = useState(false);
  const [announcement, setAnnouncement] = useState("");
  const prevWindowStartRef = useRef(0);

  // New data (e.g. a fresh fetch) always restarts the loop from the top.
  useEffect(() => {
    setWindowStart(0);
    setInstant(false);
    setAnnouncement("");
    prevWindowStartRef.current = 0;
  }, [activity]);

  // Auto-advance one row every TICKER_INTERVAL_MS. Off entirely when reduced-motion is
  // requested, when there's nothing to cycle through, or while paused (hover/focus).
  useEffect(() => {
    if (showStatic || paused) return;
    const t = setInterval(() => setWindowStart((i) => i + 1), TICKER_INTERVAL_MS);
    return () => clearInterval(t);
  }, [showStatic, paused]);

  // The visible track renders `list` doubled so the window can slide continuously past
  // the "end" — once a full cycle has played out (windowStart reaches `total`), rewind
  // to 0 with the transition switched off for one frame, which is visually seamless
  // because doubled[i] === doubled[i + total].
  useEffect(() => {
    if (!canSlide || windowStart < total) return;
    const t = setTimeout(() => {
      setInstant(true);
      setWindowStart((i) => i - total);
    }, TICKER_SLIDE_MS);
    return () => clearTimeout(t);
  }, [windowStart, total, canSlide]);

  useEffect(() => {
    if (!instant) return;
    const raf = requestAnimationFrame(() => setInstant(false));
    return () => cancelAnimationFrame(raf);
  }, [instant]);

  // Announce only the single row that just entered the window — not the whole 3-row
  // block — so assistive tech gets one short, meaningful update per advance instead of
  // being re-read the same 3 lines on every tick.
  useEffect(() => {
    const prev = prevWindowStartRef.current;
    prevWindowStartRef.current = windowStart;
    if (!canSlide || windowStart <= prev) return;
    const entering = list[(windowStart + TICKER_VISIBLE_ROWS - 1) % total];
    if (!entering) return;
    const { verb } = activityText(entering);
    setAnnouncement(`${capitalize(entering.teamUsername)} ${verb} ${entering.playerName}`);
  }, [windowStart, canSlide, list, total]);

  const doubled = total > 0 ? [...list, ...list] : [];

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
      <div className="ticker-viewport">
        {error ? (
          <p className="empty-state ticker-empty" aria-live="polite">
            {error}
          </p>
        ) : activity === null ? (
          <p className="empty-state ticker-empty" aria-live="polite">
            Loading…
          </p>
        ) : total === 0 ? (
          <p className="empty-state ticker-empty" aria-live="polite">
            No transactions yet this season.
          </p>
        ) : showStatic ? (
          <div className="ticker-track">
            {list.slice(0, TICKER_VISIBLE_ROWS).map((entry, i) => (
              <ActivityRow
                key={`${entry.playerId}-${entry.dateUtc}-${i}`}
                entry={entry}
                mine={entry.teamUsername === username}
              />
            ))}
          </div>
        ) : (
          <div
            className="ticker-track"
            style={{
              transform: `translateY(calc(var(--ticker-row-h) * ${-windowStart}))`,
              transitionDuration: instant ? "0ms" : `${TICKER_SLIDE_MS}ms`,
            }}
          >
            {doubled.map((entry, i) => (
              <ActivityRow
                key={`${entry.playerId}-${entry.dateUtc}-${i}`}
                entry={entry}
                mine={entry.teamUsername === username}
              />
            ))}
          </div>
        )}
      </div>
      <span className="ticker-sr-status" aria-live="polite" aria-atomic="true">
        {announcement}
      </span>
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
                <span className="msr-name">
                  <span className="msr-name-text">{p.name}</span>
                  <span className="msr-pos-pill">{positionGroupLabel(p.position)}</span>
                </span>
                <span className="msr-team-abbr">{p.team}</span>
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
