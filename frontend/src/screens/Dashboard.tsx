// GM Dashboard — the landing tab. Full restart (2026-07-22, per Nick): the
// previous 2-column dense-grid version read like a desktop BI panel crammed
// onto a phone. This is a single vertical stack of full-width cards, styled
// with PlayerCard's own spacing/type-scale — `.pc-tiles`/`.pc-tile` are
// reused verbatim (that CSS ships in the bundle already, via the PlayerCard
// import below) so the numbers match PlayerCard exactly instead of being
// re-derived and re-tightened.

import { useEffect, useState } from "react";
import { api, formatCapCompact } from "../api";
import type { LeagueDetail, LineupEntry } from "../api";
import { ActivityIcon, UsersIcon } from "../components/Icons";
import { PlayerCard } from "../components/PlayerCard";
import { TopPlayerGrid } from "../components/TopPlayerGrid";
import type { TopPlayerCard } from "../components/TopPlayerGrid";
import { useLanguage } from "../i18n/LanguageContext";
import type { Language } from "../i18n/LanguageContext";

/** A lineup entry that holds a player, as against the Équipe slot's, which
 * holds a franchise and has no player id or position group to rank by. */
type PlayerLineupEntry = LineupEntry & { playerId: number; positionGroup: "F" | "D" | "G" };

const isPlayerEntry = (e: LineupEntry): e is PlayerLineupEntry =>
  e.playerId != null && e.positionGroup !== "T";

/** The raw line under a leaderboard card: "3GP · 2G · 3A", over whatever
 * window the caller summed.
 *
 * Goalies get their own shape, because 0G 0A is technically true and entirely
 * useless — a goalie's week is wins and saves. Shared by both sections so the
 * two cannot drift into describing the same stats differently. */
function rawLine(
  positionGroup: "F" | "D" | "G",
  s: { gamesPlayed: number; goals: number; assists: number; wins: number; otLosses: number; saves: number },
): string {
  if (positionGroup === "G")
    return `${s.gamesPlayed}GP · ${s.wins}-${s.otLosses} · ${s.saves}SV`;
  return `${s.gamesPlayed}GP · ${s.goals}G · ${s.assists}A`;
}

/** The headline number on a leaderboard card: NHL points for a skater, wins
 * for a goalie (2026-08-04, per Nick).
 *
 * Deliberately *not* the fantasy score the list is ranked by: the ranking has
 * to be able to compare a goalie with a winger, but the number a GM reads on
 * the card should be the one he already knows from a box score. */
function nhlHeadline(
  positionGroup: "F" | "D" | "G",
  s: { goals: number; assists: number; wins: number },
): { value: number; unit: string } {
  return positionGroup === "G"
    ? { value: s.wins, unit: "W" }
    : { value: s.goals + s.assists, unit: "pts" };
}

/* formatCapCompact moved to api.ts (2026-08-03) — the trade sheet's cap recap
 * needs the identical wording, and two copies of a money formatter is how the
 * same number starts reading two ways on two screens. */


/** 1 -> "1st", 3 -> "3rd", 11 -> "11th", etc. French has no teens exception —
 * every rank but 1st takes a bare "e" (1er, 2e, 3e, 11e...). */
function ordinal(n: number, lang: Language): string {
  if (lang === "fr") return n === 1 ? "1er" : `${n}e`;
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

export function Dashboard(
  { league, username, onRosterChanged }:
  { league: LeagueDetail; username: string; onRosterChanged?: () => void },
) {
  const { lang, t } = useLanguage();
  // Which section opened the card decides whether it may offer "Add to my
  // team" — a Top Reserve player is already on this roster, a Top Free
  // Agents one never is, and the card itself has no way to tell the two
  // apart on its own.
  const [openPlayer, setOpenPlayer] = useState<{ id: number; isFreeAgent: boolean } | null>(null);

  const myIndex = league.teams.findIndex((t) => t.ownerUsername === username);
  const myTeam = myIndex >= 0 ? league.teams[myIndex] : undefined;
  const myRank = myIndex >= 0 ? myIndex + 1 : null;

  if (!myTeam) {
    return (
      <section className="fade-in dash-stack">
        <p className="empty-state">{t("dashboard.noTeam")}</p>
      </section>
    );
  }

  const leaderScore = league.teams.reduce((max, tm) => Math.max(max, tm.score), -Infinity);
  const isLeading = myTeam.score >= leaderScore;
  const pointsBehind = isLeading ? null : leaderScore - myTeam.score;

  const capOver = league.capAmount != null && league.capAmount - myTeam.capTotal < 0;
  const capValue =
    league.capAmount == null ? t("dashboard.noCap") : formatCapCompact(league.capAmount - myTeam.capTotal);

  return (
    <section className="fade-in dash-stack">
      <div className="card dash-glance">
        <span className="section-title">{t("dashboard.atAGlance")}</span>
        <div className="pc-tiles">
          <div className="pc-tile">
            <span className="pc-tile-value">{league.myRoster.length}</span>
            <span className="pc-tile-label">{t("dashboard.players")}</span>
          </div>
          <div className={`pc-tile${capOver ? " danger" : ""}`}>
            <span className="pc-tile-value">{capValue}</span>
            <span className="pc-tile-label">{t("dashboard.capSpace")}</span>
          </div>
          <div className="pc-tile">
            <span className="pc-tile-value">{myRank != null ? ordinal(myRank, lang) : "—"}</span>
            <span className="pc-tile-label">{t("dashboard.rank")}</span>
          </div>
          <div className="pc-tile accent">
            <span className="pc-tile-value">{myTeam.score}</span>
            <span className="pc-tile-label">{t("dashboard.points")}</span>
          </div>
        </div>
        <p className="dash-leader-note muted">
          {isLeading ? t("dashboard.leadingThePool") : t("dashboard.behindLeader", { points: pointsBehind })}
          {league.currentPeriod && (
            <>
              {" · "}
              {league.currentPeriod.gameCount === 0
                ? t("dashboard.weekBreak", { index: league.currentPeriod.index })
                : t("dashboard.weekPoints", { index: league.currentPeriod.index, points: myTeam.periodPoints ?? 0 })}
              {/* Bench regret is the point of a weekly lineup — surface it. */}
              {(myTeam.benchScore ?? 0) > 0 && t("dashboard.benchedSuffix", { count: myTeam.benchScore })}
            </>
          )}
        </p>
      </div>

      <TopReserve
        league={league}
        username={username}
        onOpenPlayer={(id) => setOpenPlayer({ id, isFreeAgent: false })}
      />
      <TopFreeAgents league={league} onOpenPlayer={(id) => setOpenPlayer({ id, isFreeAgent: true })} />

      {openPlayer != null && (
        <PlayerCard
          playerId={openPlayer.id}
          leagueId={league.id}
          username={username}
          canAddToRoster={openPlayer.isFreeAgent}
          onAdded={onRosterChanged}
          onClose={() => setOpenPlayer(null)}
        />
      )}
    </section>
  );
}

/** The two completed weeks behind the one being played, most recent last.
 * Week 2 has only one week behind it — show that one rather than nothing, and
 * before week 2 there is nothing to show at all. */
function lastTwoWeeks(previousIndex: number | null): number[] {
  if (previousIndex == null) return [];
  return [previousIndex - 1, previousIndex].filter((i) => i >= 1);
}

/** Currently benched (as of *this* week's lineup), ranked by what they scored
 * over the *last two* weeks — different periods, so one lineup fetch each,
 * joined on `spotId` (stable across periods for the same held roster spot):
 * one for who's benched now, the others for the points and the raw line
 * (2026-08-02, per Nick — "top réserve" is who's on the bench today, not who
 * was benched last week; two-week window 2026-08-04, so one quiet week does
 * not bury a player who has been producing). All reuse the existing lineup
 * endpoint; no new API needed. */
function TopReserve({
  league, username, onOpenPlayer,
}: {
  league: LeagueDetail;
  username: string;
  onOpenPlayer: (playerId: number) => void;
}) {
  const { t } = useLanguage();
  const [cards, setCards] = useState<TopPlayerCard[] | null>(null);
  const previousIndex = league.currentPeriod ? league.currentPeriod.index - 1 : null;
  const hasHistory = lastTwoWeeks(previousIndex).length > 0;

  useEffect(() => {
    const weeks = lastTwoWeeks(previousIndex);
    if (weeks.length === 0) {
      setCards([]);
      return;
    }
    let ignore = false;
    Promise.all([
      api.lineup(league.id, username, username),
      ...weeks.map((index) => api.lineup(league.id, username, username, index)),
    ])
      .then(([current, ...prior]) => {
        if (ignore) return;
        const benchedNow = new Set(current.entries.filter((e) => !e.active).map((e) => e.spotId));

        // Summed per roster spot, not per player: a spot holds one player for
        // its whole life, and the spot id is what both weeks agree on.
        // Players only. A franchise is never benched, so it could not reach
        // this list anyway — but "Top Reserve" is a question about players, and
        // the Équipe slot has no headshot, no position group and no player id
        // to answer it with.
        const totals = new Map<string, PlayerLineupEntry>();
        for (const week of prior)
          for (const e of week.entries) {
            if (!benchedNow.has(e.spotId) || !isPlayerEntry(e)) continue;
            const acc = totals.get(e.spotId);
            totals.set(e.spotId, acc == null ? e : {
              ...e,
              points: acc.points + e.points,
              gamesPlayed: acc.gamesPlayed + e.gamesPlayed,
              goals: acc.goals + e.goals,
              assists: acc.assists + e.assists,
              wins: acc.wins + e.wins,
              otLosses: acc.otLosses + e.otLosses,
              saves: acc.saves + e.saves,
            });
          }

        const ranked = [...totals.values()]
          .sort((a, b) => b.points - a.points)
          .slice(0, 4)
          .map((e): TopPlayerCard => {
            const headline = nhlHeadline(e.positionGroup, e);
            return {
              playerId: e.playerId,
              name: e.name,
              team: e.team,
              headshotUrl: e.headshotUrl,
              position: e.position,
              statValue: headline.value,
              statUnit: headline.unit,
              // The one thing a GM cannot infer from the card — the section's
              // title says nothing about how far back it looks.
              statWindow: t("dashboard.lastTwoWeeks"),
              secondaryLine: rawLine(e.positionGroup, e),
            };
          });
        setCards(ranked);
      })
      .catch(() => {
        if (!ignore) setCards([]);
      });
    return () => {
      ignore = true;
    };
  }, [league.id, username, previousIndex, t]);

  if (cards === null) return null;
  return (
    <TopPlayerGrid
      title={t("dashboard.topReserve")}
      icon={<ActivityIcon size={16} />}
      cards={cards}
      emptyMessage={hasHistory ? t("dashboard.noBenchStandouts") : t("dashboard.noPreviousWeek")}
      onOpenPlayer={onOpenPlayer}
    />
  );
}

/** League-wide unrostered players over the **whole season to date**
 * (2026-08-04, per Nick — a claim is a season-long bet, not a reaction to one
 * good Saturday), applied to the entire player pool instead of just the
 * viewer's own bench. Ranked server-side through the league's own scale (not
 * raw NHL points), which is what puts goalies in the mix; the card itself
 * shows the raw NHL line. */
function TopFreeAgents({
  league, onOpenPlayer,
}: {
  league: LeagueDetail;
  onOpenPlayer: (playerId: number) => void;
}) {
  const { t } = useLanguage();
  const [cards, setCards] = useState<TopPlayerCard[] | null>(null);

  useEffect(() => {
    let ignore = false;
    api
      // Four is the most the grid ever shows (three on a phone, where CSS
      // drops the fourth) — asking for more would just be fetched and hidden.
      .freeAgents(league.id, 4)
      .then((rows) => {
        if (ignore) return;
        setCards(
          rows.map((r): TopPlayerCard => {
            const headline = nhlHeadline(r.positionGroup, r);
            return {
              playerId: r.playerId,
              name: r.name,
              team: r.team,
              headshotUrl: r.headshotUrl,
              position: r.position,
              statValue: headline.value,
              statUnit: headline.unit,
              secondaryLine: rawLine(r.positionGroup, r),
            };
          }),
        );
      })
      .catch(() => {
        if (!ignore) setCards([]);
      });
    return () => {
      ignore = true;
    };
  }, [league.id]);

  if (cards === null) return null;
  return (
    <TopPlayerGrid
      title={t("dashboard.topFreeAgents")}
      icon={<UsersIcon size={16} />}
      cards={cards}
      emptyMessage={t("dashboard.noFreeAgentsYet")}
      onOpenPlayer={onOpenPlayer}
    />
  );
}
