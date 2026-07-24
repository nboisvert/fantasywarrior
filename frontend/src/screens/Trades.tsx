// Trades — propose trades, act on offers you've received or sent, and browse
// the league's public trade history (with a star-based who-won rating on each
// processed trade). Reached via the bottom nav.
//
// Two areas, by intent rather than by raw status:
//   • Pending — offers awaiting action, split into Received (accept/decline)
//     and Sent (cancel). An offer leaves this area the moment it's answered.
//   • History — the whole league's processed trades, plus accepted ones still
//     awaiting tonight's processing (marked with the only status pill left in
//     the UI). Declined/cancelled trades are never shown anywhere.
//
// Privacy: the server already filters out pending/declined/cancelled trades
// the viewer isn't party to (see TradeValidation.IsVisibleTo) — everything
// this screen receives is safe to render.

import { useEffect, useMemo, useState } from "react";
import { api, topPlayersByNhlPoints } from "../api";
import type { LeagueDetail, Trade, TradePlayer } from "../api";
import { ArrowLeftRightIcon } from "../components/Icons";
import { CreateTradeSheet } from "../components/CreateTradeSheet";
import { TradeRatingWidget } from "../components/TradeRatingWidget";
import { LoadingLogo } from "../components/LoadingLogo";

const formatDateTime = (iso: string) =>
  new Date(iso).toLocaleString("en-US", {
    month: "short",
    day: "numeric",
    year: "numeric",
    hour: "numeric",
    minute: "2-digit",
  });

const playersLabel = (players: TradePlayer[]) =>
  players.length === 0 ? "nothing" : players.map((p) => p.name).join(", ");

/** The one date worth putting at the top of a card, labelled by what it marks:
 * when the offer was proposed (still pending), accepted (awaiting tonight's
 * processing), or actually processed. */
function primaryDate(t: Trade): { label: string; date: string } {
  if (t.status === "processed" && t.processedUtc) return { label: "Processed", date: formatDateTime(t.processedUtc) };
  if (t.status === "accepted" && t.respondedUtc) return { label: "Accepted", date: formatDateTime(t.respondedUtc) };
  return { label: "Proposed", date: formatDateTime(t.createdUtc) };
}

export function Trades({ league, username }: { league: LeagueDetail; username: string }) {
  const [trades, setTrades] = useState<Trade[] | null>(null);
  const [error, setError] = useState("");
  const [showCreate, setShowCreate] = useState(false);
  const [expanded, setExpanded] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);

  const load = () => {
    api
      .trades(league.id, username)
      .then((list) => {
        setTrades(list);
        setError("");
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : "Could not load trades."));
  };

  useEffect(load, [league.id, username]);

  const pointsById = useMemo(() => {
    const map = new Map<number, number>();
    for (const team of league.teams) for (const p of team.players) map.set(p.id, p.nhlPoints);
    return map;
  }, [league]);

  const myTeam = league.teams.find((t) => t.ownerUsername === username);

  // Split by intent, not raw status. Declined/cancelled are dropped entirely.
  const received = (trades ?? []).filter((t) => t.status === "pending" && t.counterpartyUsername === username);
  const sent = (trades ?? []).filter((t) => t.status === "pending" && t.proposerUsername === username);
  const history = (trades ?? []).filter((t) => t.status === "processed" || t.status === "accepted");

  const respond = async (tradeId: string, accept: boolean) => {
    setBusyId(tradeId);
    try {
      await api.respondTrade(league.id, tradeId, username, accept);
      load();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not respond to trade.");
    } finally {
      setBusyId(null);
    }
  };

  /** One team's column: name on top, its 2 headliners (by NHL points) below,
   * "+N more" if it's giving up more than two. */
  const tradeSide = (teamName: string, gives: TradePlayer[], align: "left" | "right" = "left") => {
    const top = topPlayersByNhlPoints(gives, pointsById, 2);
    const extra = gives.length - top.length;
    return (
      <div className={`trade-side trade-side-${align}`}>
        <span className="trade-side-name">{teamName}</span>
        <div className="trade-side-given">
          {top.length === 0 ? (
            <span className="muted">nothing</span>
          ) : (
            top.map((p) => <span key={p.id}>{p.name}</span>)
          )}
          {extra > 0 && <span className="muted">+{extra} more</span>}
        </div>
      </div>
    );
  };

  /** The card's top line: the relevant date (prominent, left) and, for an
   * accepted-not-yet-processed trade, the awaiting pill (right). */
  const cardHead = (trade: Trade) => {
    const { label, date } = primaryDate(trade);
    return (
      <div className="trade-row-head">
        <span className="trade-row-date">
          <span className="trade-row-date-label">{label}</span> {date}
        </span>
        {trade.status === "accepted" && <span className="trade-awaiting-pill">Awaiting processing</span>}
      </div>
    );
  };

  /** The full-width visual shared by every trade card: two team-name-topped
   * columns with each side's headliners underneath, split by the ⇄ icon. */
  const teamsSplit = (trade: Trade) => (
    <div className="trade-teams-split">
      {tradeSide(trade.proposerTeamName, trade.playersFromProposer)}
      <ArrowLeftRightIcon size={16} className="trade-row-arrow" />
      {tradeSide(trade.counterpartyTeamName, trade.playersFromCounterparty, "right")}
    </div>
  );

  /** A history card: the teams/headliners visual, an expand toggle, and, once
   * open, the full player lists plus the rating (processed) or an awaiting
   * note (accepted). */
  const historyCard = (trade: Trade) => (
    <li key={trade.id} className="card trade-row">
      {cardHead(trade)}
      <button className="trade-row-toggle" onClick={() => setExpanded(expanded === trade.id ? null : trade.id)}>
        {teamsSplit(trade)}
      </button>
      {expanded === trade.id && (
        <div className="trade-row-expanded">
          <div className="trade-row-players">
            <div>{trade.proposerTeamName} gave: {playersLabel(trade.playersFromProposer)}</div>
            <div>{trade.counterpartyTeamName} gave: {playersLabel(trade.playersFromCounterparty)}</div>
          </div>
          {trade.status === "processed" ? (
            <TradeRatingWidget leagueId={league.id} trade={trade} username={username} onVoted={load} />
          ) : (
            <small className="muted">Accepted — the rosters swap with tonight's scoring update.</small>
          )}
        </div>
      )}
    </li>
  );

  return (
    <section className="fade-in" style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
      <div className="trades-header">
        <span className="section-title">Trades</span>
        <button className="btn-ghost" onClick={() => setShowCreate(true)}>
          <ArrowLeftRightIcon size={16} />
          Propose trade
        </button>
      </div>

      {error && <p className="error-banner">{error}</p>}
      {trades === null && !error && <LoadingLogo label="Loading trades…" />}

      {trades !== null && (
        <>
          {(received.length > 0 || sent.length > 0) && (
            <div>
              <span className="section-title">Pending</span>

              {received.length > 0 && (
                <>
                  <span className="trade-subhead">Received</span>
                  <ul className="trade-list">
                    {received.map((t) => (
                      <li key={t.id} className="card trade-row trade-row-received">
                        {cardHead(t)}
                        {teamsSplit(t)}
                        <div className="trade-actions">
                          <button className="btn" disabled={busyId === t.id} onClick={() => respond(t.id, true)}>
                            Accept
                          </button>
                          <button className="btn-ghost" disabled={busyId === t.id} onClick={() => respond(t.id, false)}>
                            Decline
                          </button>
                        </div>
                      </li>
                    ))}
                  </ul>
                </>
              )}

              {sent.length > 0 && (
                <>
                  <span className="trade-subhead">Sent</span>
                  <ul className="trade-list">
                    {sent.map((t) => (
                      <li key={t.id} className="card trade-row">
                        {cardHead(t)}
                        {teamsSplit(t)}
                        <div className="trade-actions">
                          <button className="btn-ghost" disabled={busyId === t.id} onClick={() => respond(t.id, false)}>
                            Cancel offer
                          </button>
                        </div>
                      </li>
                    ))}
                  </ul>
                </>
              )}
            </div>
          )}

          <div>
            <span className="section-title">League trades</span>
            {history.length === 0 ? (
              <p className="empty-state">No trades yet.</p>
            ) : (
              <ul className="trade-list">{history.map((t) => historyCard(t))}</ul>
            )}
          </div>
        </>
      )}

      {showCreate && myTeam && (
        <CreateTradeSheet
          league={league}
          username={username}
          onClose={() => setShowCreate(false)}
          onCreated={load}
        />
      )}
    </section>
  );
}
