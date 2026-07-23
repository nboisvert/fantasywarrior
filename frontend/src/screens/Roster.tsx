import { useState } from "react";
import { formatCap } from "../api";
import type { LeagueDetail } from "../api";
import { PlayerCard } from "../components/PlayerCard";

// Read-only roster view: rosters are season-long (see project docs) —
// add/remove is intentionally not exposed here.

/** Compact headline cap format: millions with one decimal ($9.2M), thousands
 * under a million. Used only for the gauge's big "available"/"over budget"
 * figure — the sub-line below it still uses api.ts's long-form `formatCap`
 * for the exact committed/cap numbers, so the header gives both a
 * glance-friendly figure and the precise one. No sign is embedded here;
 * callers already know which side (available vs. over) they're formatting. */
function formatCapCompact(amount: number): string {
  const abs = Math.abs(amount);
  if (abs >= 1_000_000) return `$${(abs / 1_000_000).toFixed(1)}M`;
  if (abs >= 1_000) return `$${Math.round(abs / 1_000)}K`;
  return `$${abs}`;
}

type PosGroup = "F" | "D" | "G";

/** Collapse a raw NHL position code (C, L, R, LW, RW, D, G, ...) to the three
 * roster groups: forward, defense, goalie. Anything not D/G defaults to F. */
function posGroup(position: string): PosGroup {
  if (position === "D") return "D";
  if (position === "G") return "G";
  return "F";
}

export function Roster({ league, username }: { league: LeagueDetail; username: string }) {
  const [openPlayerId, setOpenPlayerId] = useState<number | null>(null);

  const myTeam = league.teams.find((t) => t.ownerUsername === username);
  if (!myTeam) return <p className="empty-state">You don't have a team in this league.</p>;

  const capUsed = myTeam.capTotal;
  const capMax = league.capAmount;
  const over = capMax != null && capUsed > capMax;
  // Uncapped percentage (can exceed 100 when over the cap) drives the text;
  // the bar's own width is clamped to 100% so it never renders "broken".
  const pctRaw = capMax ? (capUsed / capMax) * 100 : 0;
  const pctBarWidth = Math.min(100, Math.max(0, pctRaw));
  const pctDisplay = Math.round(pctRaw);
  const capAvailable = capMax != null ? capMax - capUsed : null;

  return (
    <section className="fade-in" style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
      <div className="card roster-header">
        <span className="roster-team-name">{myTeam.name}</span>

        <div className="roster-score-block">
          <div className="roster-score-main">
            <span className="roster-score-value">{myTeam.score}</span>
            {myTeam.adjustmentsTotal !== 0 && (
              <span
                className={`roster-adj-pill ${
                  myTeam.adjustmentsTotal > 0 ? "roster-adj-pill-pos" : "roster-adj-pill-neg"
                }`}
              >
                {myTeam.adjustmentsTotal > 0 ? "+" : ""}
                {myTeam.adjustmentsTotal}
              </span>
            )}
          </div>
          <span className="roster-score-label">Points</span>
          {myTeam.adjustmentsTotal !== 0 && (
            <small className="muted roster-score-detail">Raw top-X score: {myTeam.rawTopXScore} pts</small>
          )}
        </div>

        {capMax != null && capAvailable != null ? (
          <div className="roster-cap">
            <div className="pc-tiles roster-cap-tiles">
              <div className={`pc-tile${over ? " danger" : " accent"}`}>
                <span className="pc-tile-value">{formatCapCompact(Math.abs(capAvailable))}</span>
                <span className="pc-tile-label">{over ? "Over budget" : "Available"}</span>
              </div>
              <div className={`pc-tile${over ? " danger" : ""}`}>
                <span className="pc-tile-value">{pctDisplay}%</span>
                <span className="pc-tile-label">Used</span>
              </div>
            </div>
            <div
              className="cap-track"
              role="progressbar"
              aria-valuenow={Math.round(pctBarWidth)}
              aria-valuemin={0}
              aria-valuemax={100}
              aria-valuetext={`${pctDisplay}% of cap used, ${formatCapCompact(Math.abs(capAvailable))} ${
                over ? "over budget" : "available"
              }`}
              aria-label="Salary cap used"
            >
              <div className={`cap-fill${over ? " over" : ""}`} style={{ width: `${pctBarWidth}%` }} />
            </div>
            <small className="muted roster-cap-sub">
              {formatCap(capUsed)} committed of {formatCap(capMax)} cap
            </small>
          </div>
        ) : (
          <p className="muted roster-cap-none">No salary cap set for this league.</p>
        )}
      </div>

      <span className="section-title">My roster ({myTeam.players.length})</span>
      {myTeam.players.length === 0 && <p className="empty-state">Empty roster.</p>}
      <ul className="player-list">
        {myTeam.players.map((p) => (
          <li key={p.id} className={`player-row${p.counted ? " counted" : ""}`}>
            <button
              className="player-hit"
              onClick={() => setOpenPlayerId(p.id)}
              aria-label={`Open ${p.name} card`}
            >
              <img className="headshot" src={p.headshotUrl ?? ""} alt="" loading="lazy" />
              <span className="player-info">
                <span className="name">{p.name}</span>
                <small className="player-sub">
                  <span className={`roster-pos-pill roster-pos-pill-${posGroup(p.position).toLowerCase()}`}>
                    {posGroup(p.position)}
                  </span>
                  <span>{p.team}</span>
                  {p.capHit != null && <span className="player-cap-hit">{formatCap(p.capHit)}</span>}
                </small>
              </span>
              <span className="pts-small">{p.points} pts</span>
            </button>
          </li>
        ))}
      </ul>
      {openPlayerId != null && (
        <PlayerCard playerId={openPlayerId} onClose={() => setOpenPlayerId(null)} />
      )}
    </section>
  );
}
