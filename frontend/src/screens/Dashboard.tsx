// GM Dashboard — the landing tab. Full restart (2026-07-22, per Nick): the
// previous 2-column dense-grid version read like a desktop BI panel crammed
// onto a phone. This is a single vertical stack of full-width cards, styled
// with PlayerCard's own spacing/type-scale — `.pc-tiles`/`.pc-tile` are
// reused verbatim (that CSS ships in the bundle already, via the PlayerCard
// import below) so the numbers match PlayerCard exactly instead of being
// re-derived and re-tightened.

import { useEffect, useMemo, useState } from "react";
import { api, topPlayersByNhlPoints } from "../api";
import type { LeagueDetail, Trade, TradePlayer } from "../api";
import { ArrowLeftRightIcon } from "../components/Icons";
import { PlayerCard } from "../components/PlayerCard";

/** Same window the news ticker uses for its "hot" gold glow — a trade is
 * fresh news for 30 minutes after its own stage timestamp. */
const HOT_WINDOW_MS = 30 * 60 * 1000;

/** Small local helper, not shared — same duplicated-per-file convention as
 * NewsTicker's own timeAgo (and Stats.tsx's formatMoneyCompact). */
function timeAgo(dateUtc: string): string {
  const ms = Date.now() - new Date(dateUtc).getTime();
  const minutes = Math.floor(ms / 60_000);
  if (minutes < 1) return "now";
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  return `${Math.floor(hours / 24)}d ago`;
}

/** The trade's own "just happened" timestamp and status label — same
 * priority Trades.tsx's primaryDate uses (processed beats accepted beats
 * proposed), but only the two history-worthy stages ever reach this card. */
function tradeTimestamp(t: Trade): { label: string; iso: string } {
  if (t.status === "processed" && t.processedUtc) return { label: "Processed", iso: t.processedUtc };
  if (t.status === "accepted" && t.respondedUtc) return { label: "Accepted", iso: t.respondedUtc };
  return { label: "Proposed", iso: t.createdUtc };
}

/** One team's column: "To {team}" on top, the headliners (by NHL points) it
 * *acquired* below, "+N more" for the rest — the collapsed view Trades.tsx's
 * own trade cards use, reused verbatim (same classes) so this reads as the
 * same visual language, not a smaller copy of it. No expand affordance
 * here: this is a glance at what happened, not the place to act on or rate
 * a trade — that's what the Trades tab already is.
 *
 * Callers pass the *other* side's assets here — see teamsSplit in
 * Trades.tsx for why "To {team}" pairs with what it acquired, not what it
 * gave (2026-08-03, per Nick). */
function TradeSide({
  teamName, acquires, pointsById, align, onOpenPlayer,
}: {
  teamName: string;
  acquires: TradePlayer[];
  pointsById: Map<number, number>;
  align: "left" | "right";
  onOpenPlayer: (playerId: number) => void;
}) {
  const top = topPlayersByNhlPoints(acquires, pointsById, 2);
  const topIds = new Set(top.map((p) => p.id));
  const rest = acquires.filter((p) => !topIds.has(p.id));
  return (
    <div className={`trade-side trade-side-${align}`}>
      <span className="trade-side-name">To {teamName}</span>
      <div className="trade-side-acquired">
        {top.length === 0 ? (
          <span className="muted">nothing</span>
        ) : (
          top.map((p) => (
            <button
              key={p.id}
              type="button"
              className="dash-news-player"
              onClick={() => onOpenPlayer(p.id)}
            >
              {p.name}
            </button>
          ))
        )}
        {rest.length > 0 && <span className="muted">+{rest.length} more</span>}
      </div>
    </div>
  );
}

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
  const [trades, setTrades] = useState<Trade[] | null>(null);

  // Ticks every 30s purely to re-evaluate "is this trade still within its
  // 30-minute hot window" — same reasoning as NewsTicker's own clock.
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), 30_000);
    return () => clearInterval(id);
  }, []);

  useEffect(() => {
    let ignore = false;
    api
      .trades(league.id, username)
      .then((list) => {
        if (!ignore) setTrades(list);
      })
      .catch(() => {
        if (!ignore) setTrades([]);
      });
    return () => {
      ignore = true;
    };
  }, [league.id, username]);

  const pointsById = useMemo(() => {
    const map = new Map<number, number>();
    for (const team of league.teams)
      for (const [id, pts] of Object.entries(team.playerNhlPoints)) map.set(Number(id), pts);
    return map;
  }, [league]);

  // Same "history" scope Trades.tsx shows (processed, plus accepted trades
  // still awaiting tonight's processing) — declined/cancelled/pending offers
  // aren't news yet. Most recent stage first.
  const recentTrades = (trades ?? [])
    .filter((t) => t.status === "processed" || t.status === "accepted")
    .map((t) => ({ trade: t, ...tradeTimestamp(t) }))
    .sort((a, b) => new Date(b.iso).getTime() - new Date(a.iso).getTime());

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

  return (
    <section className="fade-in dash-stack">
      <div className="card dash-glance">
        <span className="section-title">At a glance</span>
        <div className="pc-tiles">
          <div className="pc-tile">
            <span className="pc-tile-value">{league.myRoster.length}</span>
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
          {league.currentPeriod && (
            <>
              {" · "}
              {league.currentPeriod.gameCount === 0
                ? `Week ${league.currentPeriod.index}: league break`
                : `Week ${league.currentPeriod.index}: +${myTeam.periodPoints ?? 0} pts`}
              {/* Bench regret is the point of a weekly lineup — surface it. */}
              {(myTeam.benchScore ?? 0) > 0 && `, ${myTeam.benchScore} benched`}
            </>
          )}
        </p>
      </div>

      <div className="card dash-news">
        <span className="section-title">League News</span>
        {trades == null ? null : recentTrades.length === 0 ? (
          <p className="empty-state">No trades yet.</p>
        ) : (
          <ul className="dash-news-list">
            {recentTrades.map(({ trade, label, iso }, i) => {
              const isHot = now - new Date(iso).getTime() < HOT_WINDOW_MS;
              return (
                <li
                  key={trade.id}
                  className={`dash-news-card${isHot ? " hot" : ""}`}
                  style={{ animationDelay: `${Math.min(i, 5) * 60}ms` }}
                >
                  <div className="trade-row-head">
                    <span className="trade-row-date">
                      <span className="trade-row-date-label">{label}</span> {timeAgo(iso)}
                    </span>
                    {trade.status === "accepted" && (
                      <span className="trade-awaiting-pill">Awaiting processing</span>
                    )}
                  </div>
                  <div className="trade-teams-split">
                    <TradeSide
                      teamName={trade.proposerTeamName}
                      acquires={trade.playersFromCounterparty}
                      pointsById={pointsById}
                      align="left"
                      onOpenPlayer={setOpenPlayerId}
                    />
                    <span className="dash-news-icon" aria-hidden="true">
                      <ArrowLeftRightIcon size={16} />
                    </span>
                    <TradeSide
                      teamName={trade.counterpartyTeamName}
                      acquires={trade.playersFromProposer}
                      pointsById={pointsById}
                      align="right"
                      onOpenPlayer={setOpenPlayerId}
                    />
                  </div>
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
