import { useEffect, useState } from "react";
import { api, formatCap } from "../api";
import type { ActivityEntry, LeagueDetail } from "../api";
import { ActivityIcon, ArrowLeftRightIcon, PlusIcon, XIcon } from "../components/Icons";
import { PlayerCard } from "../components/PlayerCard";

const TOP_STANDINGS = 4;
const TOP_SCORERS = 8;

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

  const myTeam = league.teams.find((t) => t.ownerUsername === username);
  const rank = myTeam ? league.teams.findIndex((t) => t.ownerUsername === username) + 1 : null;
  const topTeams = league.teams.slice(0, TOP_STANDINGS);
  const topScorers = myTeam ? myTeam.players.slice(0, TOP_SCORERS) : [];

  return (
    <section className="fade-in" style={{ display: "flex", flexDirection: "column", gap: "0.85rem" }}>
      <div className="dashboard-stats">
        <div className="stat-chip">
          <span className="value">{rank ? `#${rank}` : "—"}</span>
          <span className="label">Your rank</span>
        </div>
        <div className="stat-chip">
          <span className="value">{myTeam ? myTeam.score : "—"}</span>
          <span className="label">Your score</span>
        </div>
        <div className="stat-chip">
          <span className="value">{myTeam ? formatCap(myTeam.capTotal) : "—"}</span>
          <span className="label">Cap used{league.capAmount != null ? ` / ${formatCap(league.capAmount)}` : ""}</span>
        </div>
      </div>

      <div style={{ display: "flex", flexDirection: "column", gap: "0.6rem" }}>
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
                  <span className="msr-handle">@{team.ownerUsername}</span>
                </span>
                <span className="msr-pts">{team.score}</span>
              </div>
            ))}
          </div>
        )}
      </div>

      <div style={{ display: "flex", flexDirection: "column", gap: "0.6rem" }}>
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

      <div style={{ display: "flex", flexDirection: "column", gap: "0.6rem" }}>
        <span className="section-title">
          <ActivityIcon size={14} className="inline-icon" /> Recent pool activity
        </span>
        {activityError && <p className="error-banner">{activityError}</p>}
        {!activityError && activity === null && <p className="empty-state">Loading…</p>}
        {!activityError && activity !== null && activity.length === 0 && (
          <p className="empty-state">No transactions yet this season.</p>
        )}
        {activity !== null && activity.length > 0 && (
          <ul className="activity-list">
            {activity.map((entry, i) => {
              const { verb, suffix } = activityText(entry);
              return (
                <li
                  key={`${entry.playerId}-${entry.dateUtc}-${i}`}
                  className={`activity-row${entry.teamUsername === username ? " mine" : ""}`}
                >
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
                </li>
              );
            })}
          </ul>
        )}
      </div>

      {openPlayerId != null && <PlayerCard playerId={openPlayerId} onClose={() => setOpenPlayerId(null)} />}
    </section>
  );
}
