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
import { api, topPlayersByNhlPoints, tradeRating } from "../api";
import type { LeagueDetail, Trade, TradePlayer } from "../api";
import { ArrowLeftRightIcon } from "../components/Icons";
import { CockcoinReward } from "../components/CockcoinReward";
import { CreateTradeSheet } from "../components/CreateTradeSheet";
import { TradeVoteWidget } from "../components/TradeVoteWidget";
import { LoadingLogo } from "../components/LoadingLogo";
import { useLanguage } from "../i18n/LanguageContext";

const formatDateTime = (iso: string) =>
  new Date(iso).toLocaleString("en-US", {
    month: "short",
    day: "numeric",
    year: "numeric",
    hour: "numeric",
    minute: "2-digit",
  });

/** The one date worth putting at the top of a card, labelled by what it marks:
 * when the offer was proposed (still pending), accepted (awaiting tonight's
 * processing), or actually processed. */
function primaryDate(trade: Trade, t: (key: string) => string): { label: string; date: string } {
  if (trade.status === "processed" && trade.processedUtc)
    return { label: t("trades.dateProcessed"), date: formatDateTime(trade.processedUtc) };
  if (trade.status === "accepted" && trade.respondedUtc)
    return { label: t("trades.dateAccepted"), date: formatDateTime(trade.respondedUtc) };
  return { label: t("trades.dateProposed"), date: formatDateTime(trade.createdUtc) };
}

export function Trades({ league, username }: { league: LeagueDetail; username: string }) {
  const { t } = useLanguage();
  const [trades, setTrades] = useState<Trade[] | null>(null);
  const [error, setError] = useState("");
  const [showCreate, setShowCreate] = useState(false);
  // The proposal sheet closes the instant it submits, before the cockcoin
  // reward pop could ever finish its animation — so the reward is lifted
  // here instead of shown inside the sheet.
  const [reward, setReward] = useState<number | null>(null);
  // A set, not a single id — expanding one card no longer collapses another
  // (2026-08-03, per Nick: comparing two trades meant losing your place in
  // the first one every time you opened a second).
  const [expanded, setExpanded] = useState<Set<string>>(new Set());
  const [busyId, setBusyId] = useState<string | null>(null);

  const load = () => {
    api
      .trades(league.id, username)
      .then((list) => {
        setTrades(list);
        setError("");
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : t("trades.couldNotLoad")));
  };

  useEffect(load, [league.id, username, t]);

  const pointsById = useMemo(() => {
    const map = new Map<number, number>();
    for (const team of league.teams)
      for (const [id, pts] of Object.entries(team.playerNhlPoints)) map.set(Number(id), pts);
    return map;
  }, [league]);

  const myTeam = league.teams.find((t) => t.ownerUsername === username);

  // Split by intent, not raw status. Declined/cancelled are dropped entirely.
  const received = (trades ?? []).filter((t) => t.status === "pending" && t.counterpartyUsername === username);
  const sent = (trades ?? []).filter((t) => t.status === "pending" && t.proposerUsername === username);
  const history = (trades ?? []).filter((t) => t.status === "processed" || t.status === "accepted");

  const toggleExpanded = (tradeId: string) =>
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(tradeId)) next.delete(tradeId);
      else next.add(tradeId);
      return next;
    });

  const respond = async (tradeId: string, accept: boolean) => {
    setBusyId(tradeId);
    try {
      await api.respondTrade(league.id, tradeId, username, accept);
      load();
    } catch (e) {
      setError(e instanceof Error ? e.message : t("trades.couldNotRespond"));
    } finally {
      setBusyId(null);
    }
  };

  /** One team's column: "To {team}" on top, the 2 headliners (by NHL points)
   * it *acquired* below. Collapsed: "+N more" for anyone beyond the top 2.
   * Expanded: those extra players are listed right below the headliners
   * instead — no "+N more", and the headliners are never repeated a second
   * time anywhere else on the card (2026-07-27, per Nick — the old expanded
   * view duplicated the top players by also dumping the full list
   * underneath).
   *
   * "To {team}" plus which players sit under it (2026-08-03, per Nick): a
   * column used to be headed by the team's own name and list what *it* gave
   * up — reading a card meant mentally swapping the two columns to see who
   * actually got the better end of it. Heading each column with the team the
   * players are headed *to* removes that step. */
  const tradeSide = (
    teamName: string,
    acquires: TradePlayer[],
    expanded: boolean,
    align: "left" | "right" = "left",
  ) => {
    const top = topPlayersByNhlPoints(acquires, pointsById, 2);
    const topIds = new Set(top.map((p) => p.id));
    const rest = acquires.filter((p) => !topIds.has(p.id));
    return (
      <div className={`trade-side trade-side-${align}`}>
        <span className="trade-side-name">{t("trades.toTeam", { team: teamName })}</span>
        <div className="trade-side-acquired">
          {top.length === 0 ? (
            <span className="muted">{t("trades.nothing")}</span>
          ) : (
            top.map((p) => <span key={p.id}>{p.name}</span>)
          )}
          {!expanded && rest.length > 0 && <span className="muted">{t("trades.more", { count: rest.length })}</span>}
          {expanded && rest.map((p) => <span key={p.id}>{p.name}</span>)}
        </div>
      </div>
    );
  };

  /** The card's top line: the relevant date (prominent, left) and, for an
   * accepted-not-yet-processed trade, the awaiting pill (right). */
  const cardHead = (trade: Trade) => {
    const { label, date } = primaryDate(trade, t);
    return (
      <div className="trade-row-head">
        <span className="trade-row-date">
          <span className="trade-row-date-label">{label}</span> {date}
        </span>
        {trade.status === "accepted" && <span className="trade-awaiting-pill">{t("trades.awaitingProcessing")}</span>}
      </div>
    );
  };

  /** The full-width visual shared by every trade card: two "To {team}"
   * columns with each side's *acquired* headliners underneath, split by the
   * ⇄ icon. Each column takes the *other* side's assets — what proposer
   * gave is what lands under "To {counterparty}", and vice versa. */
  const teamsSplit = (trade: Trade, expanded = false) => (
    <div className="trade-teams-split">
      {tradeSide(trade.proposerTeamName, trade.playersFromCounterparty, expanded)}
      <ArrowLeftRightIcon size={16} className="trade-row-arrow" />
      {tradeSide(trade.counterpartyTeamName, trade.playersFromProposer, expanded, "right")}
    </div>
  );

  /** The teams visual, tappable to show every player rather than "+N more".
   *
   * Every card that can say "+N more" needs this, which is all of them —
   * pending offers used to render the same visual with no way to open it, so
   * an offer of more than two players a side could not be read in full before
   * accepting it. Shared here so a fourth caller cannot reintroduce that. */
  const teamsSplitToggle = (trade: Trade) => {
    const isOpen = expanded.has(trade.id);
    return (
      <button
        className="trade-row-toggle"
        onClick={() => toggleExpanded(trade.id)}
        aria-expanded={isOpen}
      >
        {teamsSplit(trade, isOpen)}
      </button>
    );
  };

  /** The collapsed card's own pitch — expanding is no longer required to see
   * where a trade's vote stands (2026-08-03, per Nick). Three states: a
   * flashy nudge when the viewer can vote and hasn't; the QB-rating-style
   * winner call once there's a real vote to report; a quiet "no votes yet"
   * when a party to the trade (who can never vote themselves) is looking at
   * one nobody has weighed in on yet. Tapping any of them expands the card,
   * same as tapping the teams themselves.
   *
   * A small pill for the two states with no rating to show (2026-08-05, per
   * Nick: this is a redundant shortcut — tapping the teams-split above
   * already expands the card — so it shouldn't cost more than a glance).
   * Once there's a rating: the winner sentence centered, vote count muted,
   * and the same small rating readout the expanded widget uses parked on
   * the right (2026-08-05, per Nick — the sentence was missing again once
   * it moved into the expanded widget; the mini rating itself is unchanged,
   * just given a sibling instead of standing alone). */
  const voteTeaser = (trade: Trade) => {
    if (trade.status !== "processed") return null;
    const votes = trade.votes;

    if (votes == null) {
      if (!trade.canVote) return null;
      return (
        <div className="trade-vote-teaser-wrap">
          <button type="button" className="trade-vote-teaser cast" onClick={() => toggleExpanded(trade.id)}>
            {t("trades.castYourVote")}
          </button>
        </div>
      );
    }

    if (votes.total === 0) {
      return (
        <div className="trade-vote-teaser-wrap">
          <button type="button" className="trade-vote-teaser empty" onClick={() => toggleExpanded(trade.id)}>
            {t("trades.noVotesYet")}
          </button>
        </div>
      );
    }

    const { winner, rating, tier } = tradeRating(votes);
    const winnerName = winner === "proposer" ? trade.proposerTeamName : winner === "counterparty" ? trade.counterpartyTeamName : null;
    return (
      <div className="trade-vote-collapsed">
        <button type="button" className="trade-vote-collapsed-text" onClick={() => toggleExpanded(trade.id)}>
          {winnerName ? (
            <>
              <strong>{winnerName}</strong> {t("trades.wonTheTrade")}
            </>
          ) : (
            <strong>{t("trades.fairTrade")}</strong>
          )}{" "}
          <span className="muted">{t("trades.outOfVotes", { total: votes.total })}</span>
        </button>
        <button type="button" className="trade-vote-rating-mini" onClick={() => toggleExpanded(trade.id)}>
          <strong className={`rating-tier-${tier}`}>{rating}</strong> {t("trades.ratingLabel")}
        </button>
      </div>
    );
  };

  /** A history card: the teams/headliners visual (expanding in place to show
   * every player, no separate duplicate list), the collapsed vote teaser
   * when it's shut, and the full widget (processed) or an awaiting note
   * (accepted) once open. */
  const historyCard = (trade: Trade) => {
    const isOpen = expanded.has(trade.id);
    return (
      <li key={trade.id} className="card trade-row">
        {cardHead(trade)}
        {teamsSplitToggle(trade)}
        {!isOpen && voteTeaser(trade)}
        {isOpen && (
          <div className="trade-row-expanded">
            {trade.status === "processed" ? (
              <TradeVoteWidget leagueId={league.id} trade={trade} username={username} onVoted={load} />
            ) : (
              <small className="muted">{t("trades.acceptedNote")}</small>
            )}
          </div>
        )}
      </li>
    );
  };

  return (
    <section className="fade-in" style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
      <div className="trades-header">
        <span className="section-title">{t("trades.title")}</span>
        <button className="btn-ghost" onClick={() => setShowCreate(true)}>
          <ArrowLeftRightIcon size={16} />
          {t("trades.proposeTrade")}
        </button>
        {reward != null && (
          <CockcoinReward amount={reward} reason={t("cockcoinReward.reasonTradeOffer")} onDone={() => setReward(null)} />
        )}
      </div>

      {error && <p className="error-banner">{error}</p>}
      {trades === null && !error && <LoadingLogo label={t("trades.loadingTrades")} />}

      {trades !== null && (
        <>
          {(received.length > 0 || sent.length > 0) && (
            <div>
              <span className="section-title">{t("trades.pending")}</span>

              {received.length > 0 && (
                <>
                  <span className="trade-subhead">{t("trades.received")}</span>
                  <ul className="trade-list">
                    {received.map((rt) => (
                      <li key={rt.id} className="card trade-row trade-row-received">
                        {cardHead(rt)}
                        {teamsSplitToggle(rt)}
                        <div className="trade-actions">
                          <button className="btn" disabled={busyId === rt.id} onClick={() => respond(rt.id, true)}>
                            {t("trades.accept")}
                          </button>
                          <button className="btn-outline danger" disabled={busyId === rt.id} onClick={() => respond(rt.id, false)}>
                            {t("trades.decline")}
                          </button>
                        </div>
                      </li>
                    ))}
                  </ul>
                </>
              )}

              {sent.length > 0 && (
                <>
                  <span className="trade-subhead">{t("trades.sent")}</span>
                  <ul className="trade-list">
                    {sent.map((st) => (
                      <li key={st.id} className="card trade-row">
                        {cardHead(st)}
                        {teamsSplitToggle(st)}
                        <div className="trade-actions">
                          <button className="btn-outline danger" disabled={busyId === st.id} onClick={() => respond(st.id, false)}>
                            {t("trades.cancelOffer")}
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
            <span className="section-title">{t("trades.leagueTrades")}</span>
            {history.length === 0 ? (
              <p className="empty-state">{t("trades.noTradesYet")}</p>
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
          onCreated={(cockcoinAwarded) => {
            load();
            if (cockcoinAwarded > 0) setReward(cockcoinAwarded);
          }}
        />
      )}
    </section>
  );
}
