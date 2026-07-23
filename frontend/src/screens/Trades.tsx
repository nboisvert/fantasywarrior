// Trades — propose/accept/decline trades between teams in this league, and
// rate processed ("past") trades on a star-based who-won scale. Reached via
// the bottom nav (took Settings' old slot — Settings moved to a topbar icon).
//
// Privacy: the server already filters out pending/declined/cancelled trades
// the viewer isn't party to (see TradeValidation.IsVisibleTo) — everything
// this screen receives is safe to render without further filtering.

import { useEffect, useMemo, useState } from "react";
import { api } from "../api";
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

interface TimelineStop {
  label: string;
  date: string | null;
  state: "done" | "upcoming";
  tone: "positive" | "negative";
}

/** Every stage of a trade's life gets its own stop — proposed always happens;
 * what follows depends on how it was resolved (or whether it still hasn't
 * been). Declined/cancelled are terminal, so they never grow a Processed
 * stop; accepted-but-not-yet-processed shows a muted "Processing" stop so
 * the timeline still communicates what's coming next. */
function timelineStops(t: Trade): TimelineStop[] {
  const stops: TimelineStop[] = [{ label: "Proposed", date: formatDateTime(t.createdUtc), state: "done", tone: "positive" }];
  if (t.status === "pending") {
    stops.push({ label: "Awaiting response", date: null, state: "upcoming", tone: "positive" });
    return stops;
  }
  if (t.status === "declined") {
    stops.push({ label: "Declined", date: t.respondedUtc ? formatDateTime(t.respondedUtc) : null, state: "done", tone: "negative" });
    return stops;
  }
  if (t.status === "cancelled") {
    stops.push({ label: "Cancelled", date: t.respondedUtc ? formatDateTime(t.respondedUtc) : null, state: "done", tone: "negative" });
    return stops;
  }
  stops.push({ label: "Accepted", date: t.respondedUtc ? formatDateTime(t.respondedUtc) : null, state: "done", tone: "positive" });
  if (t.status === "processed") {
    stops.push({ label: "Processed", date: t.processedUtc ? formatDateTime(t.processedUtc) : null, state: "done", tone: "positive" });
  } else {
    stops.push({ label: "Processing", date: null, state: "upcoming", tone: "positive" });
  }
  return stops;
}

function TradeTimeline({ trade }: { trade: Trade }) {
  const stops = timelineStops(trade);
  return (
    <ol className="trade-timeline">
      {stops.map((s, i) => (
        <li key={i} className={`trade-timeline-stop trade-timeline-${s.state} trade-timeline-${s.tone}`}>
          <span className="trade-timeline-dot" />
          <span className="trade-timeline-label">{s.label}</span>
          {s.date && <span className="trade-timeline-date">{s.date}</span>}
        </li>
      ))}
    </ol>
  );
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

  /** Collapsed-row recap: the 2-3 best players by NHL points per side, plus
   * a "+N others" tail — full lists are still available in the expanded
   * view via playersLabel below. */
  const sideRecap = (players: TradePlayer[]): string => {
    if (players.length === 0) return "nothing";
    const sorted = [...players].sort((a, b) => (pointsById.get(b.id) ?? 0) - (pointsById.get(a.id) ?? 0));
    if (sorted.length <= 3) return sorted.map((p) => p.name).join(", ");
    const shown = sorted.slice(0, 2);
    return `${shown.map((p) => p.name).join(", ")} +${sorted.length - shown.length} others`;
  };

  const playersLabel = (players: TradePlayer[]) =>
    players.length === 0 ? "nothing" : players.map((p) => p.name).join(", ");

  const myTeam = league.teams.find((t) => t.ownerUsername === username);
  const pending = (trades ?? []).filter((t) => t.status === "pending" || t.status === "accepted");
  const past = (trades ?? []).filter((t) => t.status === "processed" || t.status === "declined" || t.status === "cancelled");

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
                    <TradeTimeline trade={t} />
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
                    <div className="trade-row-recap">
                      <div>{t.proposerTeamName}: {sideRecap(t.playersFromProposer)}</div>
                      <div>{t.counterpartyTeamName}: {sideRecap(t.playersFromCounterparty)}</div>
                    </div>
                    {expanded === t.id && (
                      <div className="trade-row-expanded">
                        <div className="trade-row-players">
                          <div>{t.proposerTeamName} gave: {playersLabel(t.playersFromProposer)}</div>
                          <div>{t.counterpartyTeamName} gave: {playersLabel(t.playersFromCounterparty)}</div>
                        </div>
                        <TradeTimeline trade={t} />
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
