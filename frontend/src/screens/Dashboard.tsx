// GM Dashboard — the landing tab. Full restart (2026-07-22, per Nick): the
// previous 2-column dense-grid version read like a desktop BI panel crammed
// onto a phone. This is a single vertical stack of full-width cards, styled
// with PlayerCard's own spacing/type-scale — `.pc-tiles`/`.pc-tile` are
// reused verbatim (that CSS ships in the bundle already, via the PlayerCard
// import below) so the numbers match PlayerCard exactly instead of being
// re-derived and re-tightened.

import { useState } from "react";
import type { LeagueDetail } from "../api";
import { PlayerCard } from "../components/PlayerCard";

const TOP_SCORERS = 5;

/** Compact cap-space format for the "at a glance" tile: millions with one
 * decimal ($9.2M), thousands in $K under a million, sign preserved so an
 * over-cap (negative remaining room) team reads as "-$1.3M". Distinct from
 * api.ts's `formatCap` (long-form "$X,XXX,XXX" used on Roster/Settings) —
 * that one stays as-is, this is just for this one small tile. */
function formatCapCompact(amount: number): string {
  const sign = amount < 0 ? "-" : "";
  const abs = Math.abs(amount);
  if (abs >= 1_000_000) return `${sign}$${(abs / 1_000_000).toFixed(1)}M`;
  if (abs >= 1_000) return `${sign}$${Math.round(abs / 1_000)}K`;
  return `${sign}$${abs}`;
}

type PosGroup = "F" | "D" | "G";

/** Collapse a raw NHL position code to the three roster groups — same
 * helper as Roster.tsx/Stats.tsx (kept duplicated on purpose, per project
 * convention of small screen-local helpers rather than a shared util). */
function posGroup(position: string): PosGroup {
  if (position === "D") return "D";
  if (position === "G") return "G";
  return "F";
}

/** "Sidney Crosby" -> "S. Crosby" — used only for this screen's compact
 * scorer list (Roster/Stats keep full names). Falls back to the plain
 * string when there's no space to split on (single-word name). */
function formatShortName(name: string): string {
  const spaceIndex = name.indexOf(" ");
  if (spaceIndex <= 0) return name;
  return `${name[0]}. ${name.slice(spaceIndex + 1)}`;
}

/** 1 -> "1st", 3 -> "3rd", 11 -> "11th", etc. */
function ordinal(n: number): string {
  const rem100 = n % 100;
  if (rem100 >= 11 && rem100 <= 13) return `${n}th`;
  switch (n % 10) {
    case 1:
      return `${n}st`;
    case 2:
      return `${n}nd`;
    case 3:
      return `${n}rd`;
    default:
      return `${n}th`;
  }
}

export function Dashboard({ league, username }: { league: LeagueDetail; username: string }) {
  const [openPlayerId, setOpenPlayerId] = useState<number | null>(null);

  const myIndex = league.teams.findIndex((t) => t.ownerUsername === username);
  const myTeam = myIndex >= 0 ? league.teams[myIndex] : undefined;
  const myRank = myIndex >= 0 ? myIndex + 1 : null;

  if (!myTeam) {
    return (
      <section className="fade-in dash-stack">
        <p className="empty-state">You don't have a team in this league.</p>
      </section>
    );
  }

  const leaderScore = league.teams.reduce((max, t) => Math.max(max, t.score), -Infinity);
  const isLeading = myTeam.score >= leaderScore;
  const pointsBehind = isLeading ? null : leaderScore - myTeam.score;

  const capOver = league.capAmount != null && league.capAmount - myTeam.capTotal < 0;
  const capValue =
    league.capAmount == null ? "No cap" : formatCapCompact(league.capAmount - myTeam.capTotal);

  const topScorers = myTeam.players.slice(0, TOP_SCORERS);

  return (
    <section className="fade-in dash-stack">
      <div className="card dash-glance">
        <span className="section-title">At a glance</span>
        <div className="pc-tiles">
          <div className="pc-tile">
            <span className="pc-tile-value">{myTeam.players.length}</span>
            <span className="pc-tile-label">Players</span>
          </div>
          <div className={`pc-tile${capOver ? " danger" : ""}`}>
            <span className="pc-tile-value">{capValue}</span>
            <span className="pc-tile-label">Cap Space</span>
          </div>
          <div className="pc-tile">
            <span className="pc-tile-value">{myRank != null ? ordinal(myRank) : "—"}</span>
            <span className="pc-tile-label">Rank</span>
          </div>
          <div className="pc-tile accent">
            <span className="pc-tile-value">{myTeam.score}</span>
            <span className="pc-tile-label">Points</span>
          </div>
        </div>
        <p className="dash-leader-note muted">
          {isLeading ? "Leading the pool" : `-${pointsBehind} vs leader`}
        </p>
      </div>

      <div className="card dash-scorers">
        <span className="section-title">Top scorers</span>
        {topScorers.length === 0 ? (
          <p className="empty-state">Empty roster.</p>
        ) : (
          <ul className="player-list">
            {topScorers.map((p) => (
              <li key={p.id} className="player-row">
                <button
                  className="player-hit"
                  onClick={() => setOpenPlayerId(p.id)}
                  aria-label={`Open ${p.name} card`}
                >
                  <img className="headshot" src={p.headshotUrl ?? ""} alt="" loading="lazy" />
                  <span className="player-info">
                    <span className="name">{formatShortName(p.name)}</span>
                    <small>
                      <span className={`roster-pos-pill roster-pos-pill-${posGroup(p.position).toLowerCase()}`}>
                        {posGroup(p.position)}
                      </span>
                      {p.team}
                    </small>
                  </span>
                  <span className="pts-small">{p.points} pts</span>
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>

      {openPlayerId != null && <PlayerCard playerId={openPlayerId} onClose={() => setOpenPlayerId(null)} />}
    </section>
  );
}
