// Shared leaderboard-card grid for the two GM Office "wow" sections (Top
// Reserve, Top Free Agents) — same card format for both, per Nick, so the
// only thing that differs between the two call sites is which stat headlines
// the card and what the secondary line says.

import { formatShortName, posGroup, posGroupClass } from "../api";
import "./TopPlayerGrid.css";

export interface TopPlayerCard {
  playerId: number;
  name: string;
  team: string | null;
  headshotUrl: string | null;
  position: string;
  /** The headline number, and the unit it is counted in — "pts" for a skater,
   * "W" for a goalie, so the figure is never ambiguous (2026-08-04, per Nick). */
  statValue: number;
  statUnit: string;
  /** The window the figure covers, under it. Omitted when the section's title
   * already says — Top Free Agents is the season, and saying so on every card
   * would be four cards repeating their own heading. */
  statWindow?: string;
  /** One short line of supporting stats, already formatted by the caller —
   * the two sections show different stats here (bench/season points vs.
   * raw production), so this stays a plain string rather than a shape. */
  secondaryLine: string;
}

export function TopPlayerGrid({
  title, icon, cards, emptyMessage, onOpenPlayer,
}: {
  title: string;
  icon: React.ReactNode;
  cards: TopPlayerCard[];
  emptyMessage: string;
  onOpenPlayer: (playerId: number) => void;
}) {
  return (
    <div className="card top-grid">
      <span className="section-title top-grid-title">
        {icon}
        {title}
      </span>
      {cards.length === 0 ? (
        <p className="empty-state">{emptyMessage}</p>
      ) : (
        <div className="top-grid-cards">
          {cards.map((c, i) => (
            <button
              key={c.playerId}
              type="button"
              className={`top-card top-card-${posGroupClass(c.position)}`}
              style={{ animationDelay: `${i * 80}ms` }}
              onClick={() => onOpenPlayer(c.playerId)}
            >
              <span className="top-card-headshot-wrap">
                <img className="top-card-headshot" src={c.headshotUrl ?? ""} alt="" />
              </span>
              <span className="top-card-name">{formatShortName(c.name)}</span>
              {/* Compact position — the bare coloured letter, per CLAUDE.md's
                  two-pattern rule. These cards are a dense grid, so the pill
                  they used to carry was the wrong half of it. */}
              <span className="top-card-meta">
                <span className={`pos-compact-${posGroupClass(c.position)}`}>
                  {posGroup(c.position)}
                </span>
                {c.team && <span className="top-card-team">{c.team}</span>}
              </span>
              <span className="top-card-stat">
                <span className="top-card-stat-line">
                  <span className="top-card-stat-value">{c.statValue}</span>
                  <span className="top-card-stat-unit">{c.statUnit}</span>
                </span>
                {c.statWindow && <span className="top-card-stat-window">{c.statWindow}</span>}
              </span>
              <span className="top-card-secondary muted">{c.secondaryLine}</span>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
