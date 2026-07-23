// Stats — detailed season performance grid for the signed-in user's own team.
// Split out of Roster (2026-07-23, per Nick): Roster stays cap/team-composition
// only, this screen owns everything points/stats related, including the score
// headline that used to live at the top of Roster (kept here, just visually
// compacted since the main content below is a dense data grid, not a hero).
//
// Fetches its own data (api.teamSeasonStats) rather than reading it off the
// `league` prop, since per-player game-by-game-derived season totals aren't
// part of LeagueDetail/TeamDto — only points/adjustments/cap are, which is why
// the header below still reads those straight off `league`.

import { useEffect, useState } from "react";
import { api, formatSeason } from "../api";
import type { LeagueDetail, PlayerSeasonStatsRow } from "../api";

type PosGroup = "F" | "D" | "G";

/** Collapse a raw NHL position code to the three roster groups — same helper
 * as Roster.tsx (kept duplicated on purpose, per project convention of small
 * screen-local helpers rather than a shared util for a few lines of logic). */
function posGroup(position: string): PosGroup {
  if (position === "D") return "D";
  if (position === "G") return "G";
  return "F";
}

const signed = (n: number) => (n > 0 ? `+${n}` : String(n));

const formatGaa = (goalsAgainst: number, gamesPlayed: number): string =>
  gamesPlayed > 0 ? (goalsAgainst / gamesPlayed).toFixed(2) : "—";

const formatSvPct = (saves: number, shotsAgainst: number): string =>
  shotsAgainst > 0 ? (saves / shotsAgainst).toFixed(3).replace(/^0\./, ".") : "—";

/** Numeric-only stat keys — the footer sums each of these across the roster.
 * Restricting to this union (rather than `keyof PlayerSeasonStatsRow`) keeps
 * `sum()` from being callable with non-numeric fields like `name`/`team`. */
type NumericStatKey =
  | "gamesPlayed"
  | "goals"
  | "assists"
  | "points"
  | "plusMinus"
  | "pim"
  | "shots"
  | "wins"
  | "otLosses"
  | "shutouts"
  | "goalsAgainst"
  | "saves"
  | "shotsAgainst";

function sum(rows: PlayerSeasonStatsRow[], key: NumericStatKey): number {
  return rows.reduce((acc, r) => acc + r[key], 0);
}

function PlayerCell({ p }: { p: PlayerSeasonStatsRow }) {
  const grp = posGroup(p.position);
  return (
    <td className="stats-col-player">
      <span className={`roster-pos-pill roster-pos-pill-${grp.toLowerCase()}`}>{grp}</span>
      <span className="stats-player-name">{p.name}</span>
    </td>
  );
}

export function Stats({ league, username }: { league: LeagueDetail; username: string }) {
  const [players, setPlayers] = useState<PlayerSeasonStatsRow[] | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let ignore = false;
    setLoading(true);
    setError("");
    api
      .teamSeasonStats(league.id, username)
      .then((res) => {
        if (ignore) return;
        setPlayers(res.players);
        setLoading(false);
      })
      .catch((e: unknown) => {
        if (ignore) return;
        setError(e instanceof Error ? e.message : "Could not load stats.");
        setLoading(false);
      });
    return () => {
      ignore = true;
    };
  }, [league.id, username]);

  const myTeam = league.teams.find((t) => t.ownerUsername === username);
  if (!myTeam) return <p className="empty-state">You don't have a team in this league.</p>;

  const skaters = (players ?? []).filter((p) => !p.isGoalie);
  const goalies = (players ?? []).filter((p) => p.isGoalie);

  return (
    <section className="fade-in" style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
      <div className="card stats-header">
        <span className="roster-team-name">{myTeam.name}</span>
        <div className="stats-score-main">
          <span className="stats-score-value">{myTeam.score}</span>
          {myTeam.adjustmentsTotal !== 0 && (
            <span
              className={`stats-adj-pill ${
                myTeam.adjustmentsTotal > 0 ? "stats-adj-pill-pos" : "stats-adj-pill-neg"
              }`}
            >
              {myTeam.adjustmentsTotal > 0 ? "+" : ""}
              {myTeam.adjustmentsTotal}
            </span>
          )}
          <span className="stats-score-label">Points</span>
        </div>
        {myTeam.adjustmentsTotal !== 0 && (
          <small className="muted stats-score-detail">Raw top-X score: {myTeam.rawTopXScore} pts</small>
        )}
      </div>

      <span className="section-title">Season {formatSeason(league.season)} stats</span>

      {loading && <p className="empty-state">Loading stats…</p>}
      {!loading && error && <p className="error-banner">{error}</p>}

      {!loading && !error && (
        <>
          <div>
            <span className="stats-table-title muted">Skaters</span>
            {skaters.length === 0 ? (
              <p className="empty-state">No skaters on this roster.</p>
            ) : (
              <div className="stats-grid-scroll">
                <table className="stats-grid">
                  <thead>
                    <tr>
                      <th className="stats-col-player" scope="col">
                        Player
                      </th>
                      <th scope="col">GP</th>
                      <th scope="col">G</th>
                      <th scope="col">A</th>
                      <th className="accent" scope="col">
                        PTS
                      </th>
                      <th scope="col">+/-</th>
                      <th scope="col">PIM</th>
                      <th scope="col">SOG</th>
                    </tr>
                  </thead>
                  <tbody>
                    {skaters.map((p) => (
                      <tr key={p.id}>
                        <PlayerCell p={p} />
                        <td>{p.gamesPlayed}</td>
                        <td>{p.goals}</td>
                        <td>{p.assists}</td>
                        <td className="accent">{p.points}</td>
                        <td>{signed(p.plusMinus)}</td>
                        <td>{p.pim}</td>
                        <td>{p.shots}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>

          {goalies.length > 0 && (
            <div>
              <span className="stats-table-title muted">Goalies</span>
              <div className="stats-grid-scroll">
                <table className="stats-grid">
                  <thead>
                    <tr>
                      <th className="stats-col-player" scope="col">
                        Player
                      </th>
                      <th scope="col">GP</th>
                      <th className="accent" scope="col">
                        W
                      </th>
                      <th scope="col">OTL</th>
                      <th scope="col">SO</th>
                      <th scope="col">GAA</th>
                      <th scope="col">SV%</th>
                    </tr>
                  </thead>
                  <tbody>
                    {goalies.map((p) => (
                      <tr key={p.id}>
                        <PlayerCell p={p} />
                        <td>{p.gamesPlayed}</td>
                        <td className="accent">{p.wins}</td>
                        <td>{p.otLosses}</td>
                        <td>{p.shutouts}</td>
                        <td>{formatGaa(p.goalsAgainst, p.gamesPlayed)}</td>
                        <td>{formatSvPct(p.saves, p.shotsAgainst)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}

          {(skaters.length > 0 || goalies.length > 0) && (
            <div className="stats-totals-bar">
              {skaters.length > 0 && (
                <div className="stats-totals-row">
                  <span className="stats-totals-label">Skaters total</span>
                  <span className="stats-totals-chip">
                    GP<b>{sum(skaters, "gamesPlayed")}</b>
                  </span>
                  <span className="stats-totals-chip">
                    G<b>{sum(skaters, "goals")}</b>
                  </span>
                  <span className="stats-totals-chip">
                    A<b>{sum(skaters, "assists")}</b>
                  </span>
                  <span className="stats-totals-chip accent">
                    PTS<b>{sum(skaters, "points")}</b>
                  </span>
                  <span className="stats-totals-chip">
                    +/-<b>{signed(sum(skaters, "plusMinus"))}</b>
                  </span>
                  <span className="stats-totals-chip">
                    PIM<b>{sum(skaters, "pim")}</b>
                  </span>
                  <span className="stats-totals-chip">
                    SOG<b>{sum(skaters, "shots")}</b>
                  </span>
                </div>
              )}
              {goalies.length > 0 && (
                <div className="stats-totals-row">
                  <span className="stats-totals-label">Goalies total</span>
                  <span className="stats-totals-chip">
                    GP<b>{sum(goalies, "gamesPlayed")}</b>
                  </span>
                  <span className="stats-totals-chip accent">
                    W<b>{sum(goalies, "wins")}</b>
                  </span>
                  <span className="stats-totals-chip">
                    OTL<b>{sum(goalies, "otLosses")}</b>
                  </span>
                  <span className="stats-totals-chip">
                    SO<b>{sum(goalies, "shutouts")}</b>
                  </span>
                  <span className="stats-totals-chip">
                    GAA<b>{formatGaa(sum(goalies, "goalsAgainst"), sum(goalies, "gamesPlayed"))}</b>
                  </span>
                  <span className="stats-totals-chip">
                    SV%<b>{formatSvPct(sum(goalies, "saves"), sum(goalies, "shotsAgainst"))}</b>
                  </span>
                </div>
              )}
            </div>
          )}
        </>
      )}
    </section>
  );
}
