// Thin client for the Fantasy Warrior API.
// TEMPORARY auth model: the API trusts the username we send.

const BASE = import.meta.env.VITE_API_URL || "http://localhost:5099";

export interface PlayerDto {
  id: number;
  name: string;
  position: string;
  team: string;
  status: string;
  capHit: number | null;
  headshotUrl: string | null;
}

export interface LeagueSummary {
  id: string;
  name: string;
  season: string;
  capAmount: number | null;
  members: number;
}

export interface RosterPlayer extends PlayerDto {
  /** Fantasy points banked for this team. */
  points: number;
  nhlPoints: number;
}

/** Light team row from league-detail — team-level display only, no roster.
 * Rosters/stats for a team are fetched on demand (Stats, CreateTradeSheet).
 * `playerNhlPoints` maps playerId (string) -> season goals+assists, used to
 * rank players in Trades/NewsTicker without shipping full rosters. */
export interface TeamDto {
  name: string;
  ownerUsername: string;
  /** Season total = finalizedScore + periodPoints. */
  score: number;
  ptsPerGame: number | null;
  capTotal: number;
  playerCount: number;
  playerNhlPoints: Record<string, number>;
  /** The NHL franchise this GM owns for life, when the league uses them. */
  franchiseAbbrev: string | null;
  /** Points from this week's active players. */
  periodPoints: number;
  /** What this week's benched players scored — "left on the bench". */
  benchScore: number;
  /** Banked from every finished week; never moves again. */
  finalizedScore: number;
}

export interface RuleConfig {
  pointValues: {
    goal: number;
    assist: number;
    goalieWin: number;
    goalieOtLoss: number;
    shutout: number;
  };
  /** Anything scored beyond the five above, keyed by stat name (goals,
   * assists, plusMinus, pim, shots, hits, blockedShots, wins, otLosses,
   * shutouts, saves, goalsAgainst, shotsAgainst, gamesPlayed). Lets a league
   * score a stat the app never anticipated without a schema change. */
  extraPointValues: Record<string, number>;
  /** Active lineup slots per position — how many players count each week. */
  topCount: {
    forwards: number | null;
    defense: number | null;
    goalies: number | null;
  };
  rosterSize: {
    min: number | null;
    max: number | null;
  };
}

/** One scoring week. Weeks run Monday-Sunday on the NHL's Eastern game date. */
export interface PeriodDto {
  index: number;
  startDate: string;
  endDate: string;
  /** 0 means a break week (Olympics, All-Star) — say so rather than showing 0 pts. */
  gameCount: number;
  locked: boolean;
  finalized: boolean;
}

/** One roster spot's row in the weekly lineup. */
export interface LineupEntry {
  spotId: string;
  playerId: number;
  name: string;
  position: string;
  positionGroup: 'F' | 'D' | 'G';
  team: string | null;
  headshotUrl: string | null;
  capHit: number | null;
  active: boolean;
  points: number;
  gamesPlayed: number;
  /** The days of the week this spot owned — set when a player was acquired mid-week. */
  fromDate: string | null;
  toDate: string | null;
  seasonPoints: number;
}

export interface LineupDto {
  periodIndex: number;
  startDate: string;
  endDate: string;
  gameCount: number;
  locked: boolean;
  finalized: boolean;
  isOwner: boolean;
  /** A rival's lineup stays hidden until the week locks. */
  hidden: boolean;
  setBy?: string;
  submittedUtc?: string;
  activePoints: number;
  benchPoints: number;
  slots: { forwards: number; defense: number; goalies: number };
  used: Record<string, number>;
  entries: LineupEntry[];
  periods: PeriodDto[];
}

export interface LeagueDetail {
  id: string;
  name: string;
  season: string;
  capAmount: number | null;
  commissionerUsername: string;
  ruleConfig: RuleConfig;
  members: string[];
  /** Null before a season's period calendar has been generated. */
  currentPeriod: PeriodDto | null;
  teams: TeamDto[];
  /** The requesting user's own roster (empty if they have no team here).
   * Other teams' rosters are fetched on demand. */
  myRoster: RosterPlayer[];
}

export interface PlayerSeasonStatsRow {
  id: number;
  name: string;
  position: string;
  team: string;
  capHit: number | null;
  headshotUrl: string | null;
  isGoalie: boolean;
  gamesPlayed: number;
  goals: number;
  assists: number;
  points: number;
  plusMinus: number;
  pim: number;
  shots: number;
  hits: number;
  blockedShots: number;
  wins: number;
  otLosses: number;
  shutouts: number;
  goalsAgainst: number;
  saves: number;
  shotsAgainst: number;
  /** Weekly-lineup scoring: totals for this player's stint with this team,
   * counting only the weeks he was in the active lineup. */
  spotStartDate: string | null;
  spotActiveGamesPlayed: number;
  spotActiveGoals: number;
  spotActiveAssists: number;
  spotActivePoints: number;
  spotBenchPoints: number;
  /** Null while he is still on the roster; the date he left otherwise. */
  spotEndDate: string | null;
}

export interface TeamSeasonStats {
  season: string;
  players: PlayerSeasonStatsRow[];
  /** Players no longer on the roster whose banked points this team keeps — a
   * trade cannot move history. Only those who were in the lineup at least once;
   * empty for a team that has traded nobody away. */
  departed: PlayerSeasonStatsRow[];
}

/** One week of a player's season with this team. */
export interface PlayerPeriodRow {
  periodIndex: number;
  startDate: string;
  endDate: string;
  gameCount: number;
  /** The week's points are banked and can never move again. */
  finalized: boolean;
  /** Whether the GM had him in the lineup — the reason this view exists. */
  active: boolean;
  points: number;
  gamesPlayed: number;
  goals: number;
  assists: number;
  plusMinus: number;
  pim: number;
  shots: number;
  hits: number;
  blockedShots: number;
  wins: number;
  otLosses: number;
  shutouts: number;
  saves: number;
  goalsAgainst: number;
  shotsAgainst: number;
  /** The days this roster spot actually owned — narrower than the week only
   * when he arrived or left part-way through it. */
  from: string;
  to: string;
}

export interface PlayerPeriods {
  playerId: number;
  periods: PlayerPeriodRow[];
  totals: {
    activePoints: number;
    benchPoints: number;
    activeWeeks: number;
    benchedWeeks: number;
    gamesPlayed: number;
  };
}

export interface NewsArticle {
  id: string;
  source: "rotowire_rss" | "rotowire_html" | "fantasysp";
  headline: string;
  url: string;
  playerId: number | null;
  playerName: string | null;
  publishedUtc: string;
}

export interface TradePlayer {
  id: number;
  name: string;
  position: string | null;
}

export type TradeStatus = "pending" | "declined" | "cancelled" | "accepted" | "processed";

export interface TradeVoteTally {
  proposerClear: number;
  proposerLean: number;
  fair: number;
  counterpartyLean: number;
  counterpartyClear: number;
  total: number;
}

export interface TradeMyVote {
  favoredUsername: string | null;
  magnitude: number;
}

export interface Trade {
  id: string;
  proposerUsername: string;
  proposerTeamName: string;
  counterpartyUsername: string;
  counterpartyTeamName: string;
  playersFromProposer: TradePlayer[];
  playersFromCounterparty: TradePlayer[];
  status: TradeStatus;
  createdUtc: string;
  respondedUtc: string | null;
  processedUtc: string | null;
  votes: TradeVoteTally;
  myVote: TradeMyVote | null;
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE}${path}`, {
    headers: { "Content-Type": "application/json" },
    ...init,
  });
  const body = await res.json().catch(() => ({}));
  if (!res.ok) throw new Error(body.error ?? `HTTP ${res.status}`);
  return body as T;
}

export const api = {
  login: (username: string) =>
    request<{ username: string; displayName: string }>("/api/login", {
      method: "POST",
      body: JSON.stringify({ username }),
    }),
  myLeagues: (username: string) =>
    request<LeagueSummary[]>(`/api/users/${encodeURIComponent(username)}/leagues`),
  createLeague: (name: string, username: string, capAmount: number | null) =>
    request<{ id: string }>("/api/leagues", {
      method: "POST",
      body: JSON.stringify({ name, username, capAmount }),
    }),
  joinLeague: (leagueId: string, username: string) =>
    request<{ id: string }>(`/api/leagues/${encodeURIComponent(leagueId)}/join`, {
      method: "POST",
      body: JSON.stringify({ username }),
    }),
  league: (leagueId: string, username: string) =>
    request<LeagueDetail>(
      `/api/leagues/${encodeURIComponent(leagueId)}?username=${encodeURIComponent(username)}`,
    ),
  news: (limit = 30) =>
    request<NewsArticle[]>(`/api/news?limit=${encodeURIComponent(String(limit))}`),
  updateRules: (leagueId: string, username: string, ruleConfig: RuleConfig) =>
    request<{ ok: boolean }>(`/api/leagues/${encodeURIComponent(leagueId)}/rules`, {
      method: "PATCH",
      body: JSON.stringify({ username, ruleConfig }),
    }),
  teamSeasonStats: (leagueId: string, username: string) =>
    request<TeamSeasonStats>(
      `/api/leagues/${encodeURIComponent(leagueId)}/teams/${encodeURIComponent(username)}/season-stats`,
    ),
  /** One player's season with this team, week by week. Fetched on demand —
   * a roster is twenty-odd players and this is only ever wanted for one. */
  playerPeriods: (leagueId: string, username: string, playerId: number) =>
    request<PlayerPeriods>(
      `/api/leagues/${encodeURIComponent(leagueId)}/teams/${encodeURIComponent(username)}` +
        `/players/${playerId}/periods`,
    ),
  /** A team's week: roster, slot usage and per-player results. Omit `period`
   * for the current one. `viewer` gates a rival's lineup until it locks. */
  lineup: (leagueId: string, username: string, viewer: string, period?: number) =>
    request<LineupDto>(
      `/api/leagues/${encodeURIComponent(leagueId)}/teams/${encodeURIComponent(username)}/lineup` +
        `?viewer=${encodeURIComponent(viewer)}${period ? `&period=${period}` : ""}`,
    ),
  /** Replaces the whole active set — the write is atomic, so two tabs can't
   * race into an illegal roster. */
  setLineup: (leagueId: string, username: string, periodIndex: number, activeSpotIds: string[]) =>
    request<{ ok: boolean; periodIndex: number; active: number }>(
      `/api/leagues/${encodeURIComponent(leagueId)}/teams/${encodeURIComponent(username)}/lineup`,
      { method: "PUT", body: JSON.stringify({ username, periodIndex, activeSpotIds }) },
    ),
  trades: (leagueId: string, username: string) =>
    request<Trade[]>(
      `/api/leagues/${encodeURIComponent(leagueId)}/trades?username=${encodeURIComponent(username)}`,
    ),
  proposeTrade: (
    leagueId: string,
    username: string,
    counterpartyUsername: string,
    playersFromProposer: number[],
    playersFromCounterparty: number[],
  ) =>
    request<{ id: string }>(`/api/leagues/${encodeURIComponent(leagueId)}/trades`, {
      method: "POST",
      body: JSON.stringify({ username, counterpartyUsername, playersFromProposer, playersFromCounterparty }),
    }),
  respondTrade: (leagueId: string, tradeId: string, username: string, accept: boolean) =>
    request<{ ok: boolean; status: TradeStatus }>(
      `/api/leagues/${encodeURIComponent(leagueId)}/trades/${encodeURIComponent(tradeId)}/respond`,
      { method: "POST", body: JSON.stringify({ username, accept }) },
    ),
  voteTrade: (leagueId: string, tradeId: string, username: string, favoredUsername: string | null, magnitude: number) =>
    request<{ ok: boolean }>(
      `/api/leagues/${encodeURIComponent(leagueId)}/trades/${encodeURIComponent(tradeId)}/vote`,
      { method: "POST", body: JSON.stringify({ username, favoredUsername, magnitude }) },
    ),
};

export const formatCap = (amount: number | null | undefined) =>
  amount == null ? "—" : `$${amount.toLocaleString("en-US")}`;

/** "20262027" -> "2026-27" */
export const formatSeason = (season: string): string =>
  season.length === 8 ? `${season.slice(0, 4)}-${season.slice(6)}` : season;

/** "Sidney Crosby" -> "S. Crosby". Falls back to the plain string when there
 * is no space to split on (a single-word name).
 *
 * Shared rather than per-screen since 2026-08-02: the Team grid adopted it to
 * free the width the lineup controls needed, so it now has two callers and
 * belongs in one place — same reasoning as `posGroup`. */
export function formatShortName(name: string): string {
  const spaceIndex = name.indexOf(" ");
  if (spaceIndex <= 0) return name;
  return `${name[0]}. ${name.slice(spaceIndex + 1)}`;
}

export type PosGroup = "F" | "D" | "G";

/** Collapses a raw NHL position code to the three roster groups everywhere
 * the app displays a position — C/L/R always read as "F", at all times, no
 * per-screen exceptions (the raw code is still what's stored/returned by the
 * API; this is a display-only rule). Single source of truth: every screen
 * (Roster, Stats, Dashboard, PlayerCard) imports this instead of keeping its
 * own copy. */
export function posGroup(position: string): PosGroup {
  if (position === "D") return "D";
  if (position === "G") return "G";
  return "F";
}

/** Lowercase class suffix ("f"/"d"/"g") for the app-wide position-indicator
 * color convention (see CLAUDE.md's "Position indicator pattern") — combine
 * with `.roster-pos-pill-` (pill) or `.pos-compact-` (bare letter) depending
 * on the screen's data density. Never hardcode `.toLowerCase()` inline. */
export function posGroupClass(position: string): string {
  return posGroup(position).toLowerCase();
}

/** The N most notable players on one side of a trade, ranked by NHL points
 * (looked up from already-loaded roster data — the trade endpoint carries no
 * stats). Shared by the Trades screen (N=2 headliners per side) and the
 * NewsTicker (N=1), so the ranking rule lives in exactly one place. */
export function topPlayersByNhlPoints(
  players: TradePlayer[],
  pointsById: Map<number, number>,
  n: number,
): TradePlayer[] {
  return [...players]
    .sort((a, b) => (pointsById.get(b.id) ?? 0) - (pointsById.get(a.id) ?? 0))
    .slice(0, n);
}
