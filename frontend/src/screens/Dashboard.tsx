// GM Dashboard — the landing tab. Full restart (2026-07-22, per Nick): the
// previous 2-column dense-grid version read like a desktop BI panel crammed
// onto a phone. This is a single vertical stack of full-width cards, styled
// with PlayerCard's own spacing/type-scale — `.pc-tiles`/`.pc-tile` are
// reused verbatim (that CSS ships in the bundle already, via the PlayerCard
// import below) so the numbers match PlayerCard exactly instead of being
// re-derived and re-tightened.

import { useEffect, useState } from "react";
import { api } from "../api";
import type { LeagueDetail } from "../api";
import { ActivityIcon, UsersIcon } from "../components/Icons";
import { PlayerCard } from "../components/PlayerCard";
import { TopPlayerGrid } from "../components/TopPlayerGrid";
import type { TopPlayerCard } from "../components/TopPlayerGrid";

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

      <TopReserve league={league} username={username} onOpenPlayer={setOpenPlayerId} />
      <TopFreeAgents league={league} onOpenPlayer={setOpenPlayerId} />

      {openPlayerId != null && <PlayerCard playerId={openPlayerId} onClose={() => setOpenPlayerId(null)} />}
    </section>
  );
}

/** Currently benched (as of *this* week's lineup), ranked by what they
 * scored *last* week — two different periods, so two lineup fetches joined
 * on `spotId` (stable across periods for the same held roster spot):
 * one for who's benched now, one for last week's points (2026-08-02, per
 * Nick — "top réserve" is who's on the bench today, not who was benched
 * last week). Both reuse the existing lineup endpoint; no new API needed. */
function TopReserve({
  league, username, onOpenPlayer,
}: {
  league: LeagueDetail;
  username: string;
  onOpenPlayer: (playerId: number) => void;
}) {
  const [cards, setCards] = useState<TopPlayerCard[] | null>(null);
  const previousIndex = league.currentPeriod ? league.currentPeriod.index - 1 : null;

  useEffect(() => {
    if (previousIndex == null || previousIndex < 1) {
      setCards([]);
      return;
    }
    let ignore = false;
    Promise.all([
      api.lineup(league.id, username, username),
      api.lineup(league.id, username, username, previousIndex),
    ])
      .then(([current, previous]) => {
        if (ignore) return;
        const benchedNow = new Set(current.entries.filter((e) => !e.active).map((e) => e.spotId));
        const ranked = previous.entries
          .filter((e) => benchedNow.has(e.spotId))
          .sort((a, b) => b.points - a.points)
          .slice(0, 4)
          .map((e): TopPlayerCard => ({
            playerId: e.playerId,
            name: e.name,
            team: e.team,
            headshotUrl: e.headshotUrl,
            position: e.position,
            statValue: e.points,
            statLabel: `Wk ${previousIndex} pts`,
            secondaryLine: `${e.gamesPlayed} GP · ${e.seasonPoints} season`,
          }));
        setCards(ranked);
      })
      .catch(() => {
        if (!ignore) setCards([]);
      });
    return () => {
      ignore = true;
    };
  }, [league.id, username, previousIndex]);

  if (cards === null) return null;
  return (
    <TopPlayerGrid
      title="Top Reserve"
      icon={<ActivityIcon size={16} />}
      cards={cards}
      emptyMessage={
        previousIndex == null || previousIndex < 1
          ? "No previous week yet."
          : "No bench standouts last week."
      }
      onOpenPlayer={onOpenPlayer}
    />
  );
}

/** League-wide unrostered players, ranked by fantasy points from *last*
 * week under the league's own scoring scale — same "previous period" window
 * as Top Reserve, applied to the whole player pool instead of just the
 * viewer's own bench. Scoring through the league's scale (not raw NHL
 * points) is what puts goalies in the mix (2026-08-02, per Nick). */
function TopFreeAgents({
  league, onOpenPlayer,
}: {
  league: LeagueDetail;
  onOpenPlayer: (playerId: number) => void;
}) {
  const [cards, setCards] = useState<TopPlayerCard[] | null>(null);
  const previousIndex = league.currentPeriod ? league.currentPeriod.index - 1 : null;

  useEffect(() => {
    if (previousIndex == null || previousIndex < 1) {
      setCards([]);
      return;
    }
    let ignore = false;
    api
      .freeAgents(league.id, previousIndex)
      .then((rows) => {
        if (ignore) return;
        setCards(
          rows.map((r): TopPlayerCard => ({
            playerId: r.playerId,
            name: r.name,
            team: r.team,
            headshotUrl: r.headshotUrl,
            position: r.position,
            statValue: r.points,
            statLabel: `Wk ${previousIndex} pts`,
            secondaryLine:
              r.positionGroup === "G"
                ? `${r.wins}-${r.otLosses} · ${r.saves} SV`
                : `${r.goals}G ${r.assists}A`,
          })),
        );
      })
      .catch(() => {
        if (!ignore) setCards([]);
      });
    return () => {
      ignore = true;
    };
  }, [league.id, previousIndex]);

  if (cards === null) return null;
  return (
    <TopPlayerGrid
      title="Top Free Agents"
      icon={<UsersIcon size={16} />}
      cards={cards}
      emptyMessage={
        previousIndex == null || previousIndex < 1
          ? "No previous week yet."
          : "No standout free agents last week."
      }
      onOpenPlayer={onOpenPlayer}
    />
  );
}
