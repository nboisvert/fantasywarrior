// Trades — propose/accept/decline trades between teams in this league, and
// rate processed ("past") trades on a 5-level who-won scale. Reached via the
// bottom nav (took Settings' old slot — Settings moved to a topbar icon).

import { useEffect, useState } from "react";
import { api } from "../api";
import type { LeagueDetail, Trade, TradePlayer } from "../api";
import { ArrowLeftRightIcon } from "../components/Icons";
import { CreateTradeSheet } from "../components/CreateTradeSheet";
import { TradeRatingWidget } from "../components/TradeRatingWidget";
import { LoadingLogo } from "../components/LoadingLogo";

const playersLabel = (players: TradePlayer[]) =>
  players.length === 0 ? "nothing" : players.map((p) => p.name).join(", ");

const formatDateTime = (iso: string) =>
  new Date(iso).toLocaleString("en-US", {
    month: "short",
    day: "numeric",
    year: "numeric",
    hour: "numeric",
    minute: "2-digit",
  });

/** The most relevant date/time to show per trade, labeled by what it means
 * for that status — proposed/accepted/declined/processed each point at a
 * different timestamp field. */
function tradeDateLabel(t: Trade): string {
  if (t.status === "processed" && t.processedUtc) return `Processed ${formatDateTime(t.processedUtc)}`;
  if (t.status === "declined" && t.respondedUtc) return `Declined ${formatDateTime(t.respondedUtc)}`;
  if (t.status === "accepted" && t.respondedUtc) return `Accepted ${formatDateTime(t.respondedUtc)}`;
  return `Proposed ${formatDateTime(t.createdUtc)}`;
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

  const myTeam = league.teams.find((t) => t.ownerUsername === username);
  const pending = (trades ?? []).filter((t) => t.status === "pending" || t.status === "accepted");
  const past = (trades ?? []).filter((t) => t.status === "processed" || t.status === "declined");

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
          <div>
            <span className="section-title">Pending</span>
            {pending.length === 0 ? (
              <p className="empty-state">No pending trades.</p>
            ) : (
              <ul className="trade-list">
                {pending.map((t) => (
                  <li key={t.id} className="card trade-row">
                    <div className="trade-row-teams">
                      <strong>{t.proposerTeamName}</strong>
                      <ArrowLeftRightIcon size={14} className="trade-row-arrow" />
                      <strong>{t.counterpartyTeamName}</strong>
                      <span className={`trade-status-pill trade-status-${t.status}`}>{t.status}</span>
                    </div>
                    <div className="trade-row-players">
                      <div>{t.proposerTeamName} gives: {playersLabel(t.playersFromProposer)}</div>
                      <div>{t.counterpartyTeamName} gives: {playersLabel(t.playersFromCounterparty)}</div>
                    </div>
                    <small className="muted trade-row-date">{tradeDateLabel(t)}</small>
                    {t.status === "pending" && t.counterpartyUsername === username && (
                      <div className="trade-actions">
                        <button className="btn" disabled={busyId === t.id} onClick={() => respond(t.id, true)}>
                          Accept
                        </button>
                        <button className="btn-ghost" disabled={busyId === t.id} onClick={() => respond(t.id, false)}>
                          Decline
                        </button>
                      </div>
                    )}
                    {t.status === "pending" && t.proposerUsername === username && (
                      <div className="trade-actions">
                        <button className="btn-ghost" disabled={busyId === t.id} onClick={() => respond(t.id, false)}>
                          Cancel offer
                        </button>
                      </div>
                    )}
                    {t.status === "accepted" && (
                      <small className="muted">Accepted — processing with tonight's scoring update.</small>
                    )}
                  </li>
                ))}
              </ul>
            )}
          </div>

          <div>
            <span className="section-title">Past trades</span>
            {past.length === 0 ? (
              <p className="empty-state">No past trades yet.</p>
            ) : (
              <ul className="trade-list">
                {past.map((t) => (
                  <li key={t.id} className="card trade-row">
                    <button
                      className="trade-row-toggle"
                      onClick={() => setExpanded(expanded === t.id ? null : t.id)}
                    >
                      <span className="trade-row-teams">
                        <strong>{t.proposerTeamName}</strong>
                        <ArrowLeftRightIcon size={14} className="trade-row-arrow" />
                        <strong>{t.counterpartyTeamName}</strong>
                      </span>
                      <span className={`trade-status-pill trade-status-${t.status}`}>{t.status}</span>
                    </button>
                    <small className="muted trade-row-date">{tradeDateLabel(t)}</small>
                    {expanded === t.id && (
                      <div className="trade-row-expanded">
                        <div className="trade-row-players">
                          <div>{t.proposerTeamName} gave: {playersLabel(t.playersFromProposer)}</div>
                          <div>{t.counterpartyTeamName} gave: {playersLabel(t.playersFromCounterparty)}</div>
                        </div>
                        {t.status === "processed" && (
                          <TradeRatingWidget leagueId={league.id} trade={t} username={username} onVoted={load} />
                        )}
                      </div>
                    )}
                  </li>
                ))}
              </ul>
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
