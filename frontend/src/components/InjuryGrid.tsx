// GM Office's Injury Report — same tile shell as TopPlayerGrid's Top Reserve
// and Top Free Agents cards (.top-grid/.top-card, TopPlayerGrid.css), but
// with no stat block: an injury has no scoring number to headline, so the
// ring around the photo carries the one fact that matters instead (rose for
// hurt, slate for suspended, same pairing as PlayerCard/Stats). A separate
// component rather than a TopPlayerGrid variant — the shape genuinely
// differs (no onOpenPlayer, an external article link instead), not just the
// stat it shows.

import { formatShortName } from "../api";
import { CrossIcon, ExternalLinkIcon, GavelIcon } from "./Icons";
import "./TopPlayerGrid.css";

export interface InjuryCard {
  playerId: number;
  name: string;
  team: string | null;
  headshotUrl: string | null;
  status: "Injured" | "Suspended";
  injuryType: string | null;
  /** The injury-list article that reported this — null when the player is
   * marked unavailable but no matching article was found (a source can list
   * someone with no per-item text). The tile still shows; it just isn't a
   * link. */
  articleUrl: string | null;
}

function InjuryTile({ card, delayMs }: { card: InjuryCard; delayMs: number }) {
  const suspended = card.status === "Suspended";
  const inner = (
    <>
      {card.articleUrl && <ExternalLinkIcon size={11} className="injury-card-link" />}
      <span className="top-card-headshot-wrap">
        <img className="top-card-headshot" src={card.headshotUrl ?? ""} alt="" />
        {suspended ? (
          <GavelIcon size={15} className="injury-card-badge injury-card-badge-suspended" />
        ) : (
          <CrossIcon size={15} className="injury-card-badge injury-card-badge-hurt" />
        )}
      </span>
      <span className="top-card-name">{formatShortName(card.name)}</span>
      <span className="top-card-meta">
        {card.team && <span className="top-card-team">{card.team}</span>}
        {card.injuryType && <span className="top-card-team">{card.injuryType}</span>}
      </span>
    </>
  );
  const className = `top-card injury-card${suspended ? " injury-card-suspended" : ""}${
    card.articleUrl ? "" : " injury-card-inert"
  }`;
  const style = { animationDelay: `${delayMs}ms` };

  return card.articleUrl ? (
    <a className={className} style={style} href={card.articleUrl} target="_blank" rel="noopener noreferrer">
      {inner}
    </a>
  ) : (
    <div className={className} style={style}>
      {inner}
    </div>
  );
}

/** Hidden entirely with no injured/suspended roster player — not an
 * emptyMessage card like its siblings, since a clean bill of health isn't
 * news worth a permanent card on the page (Nick, 2026-09-27). */
export function InjuryGrid({ title, cards }: { title: string; cards: InjuryCard[] }) {
  if (cards.length === 0) return null;
  return (
    <div className="card top-grid">
      <span className="section-title top-grid-title">
        <CrossIcon size={16} />
        {title}
      </span>
      <div className="top-grid-cards">
        {cards.map((c, i) => (
          <InjuryTile key={c.playerId} card={c} delayMs={i * 80} />
        ))}
      </div>
    </div>
  );
}
