// Thin client for the Fantasy Warrior API.
// TEMPORARY auth model: the API trusts the username we send.

const BASE = import.meta.env.VITE_API_URL || "http://localhost:5099";

/** Exported for the SignalR client, which builds its own URL rather than going
 * through `request`. */
export const API_BASE = BASE;

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

/** Whether a season replay is running, and where its cursor sits. */
export interface ClockStatus {
  simulated: boolean;
  asOfDate: string | null;
  season: string | null;
  todayEt: string;
  lastStatDate: string;
}

/** The console output of a sim-advance run, same text the CLI job prints. */
export interface TestModeAdvanceResult {
  ok: boolean;
  output: string;
}

/** One unused draft pick a team holds. Picks exist one season ahead and only
 * one, so the year is shown but never chosen. */
export interface DraftPickDto {
  id: number;
  year: number;
  round: number;
  /** Whose pick it originally was — "Martin's 2nd, via Boston" needs it. */
  originalTeamName: string;
  viaTrade: boolean;
  /** Already moving in an accepted trade, so it cannot be offered again. */
  engaged: boolean;
}

/** Already moving in an accepted trade, so it cannot be offered again — the
 * trade sheet greys these out rather than letting a GM build a doomed offer. */
export interface Engageable {
  engaged?: boolean;
}

export interface RosterPlayer extends PlayerDto, Engageable {
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
  /** Cap and headcount including accepted-but-unexecuted trades. These are what
   * the server validates a new trade against, so they are what the trade sheet
   * must show — the two above describe the roster as it stands today. */
  engagedCapTotal: number;
  engagedPlayerCount: number;
  playerNhlPoints: Record<string, number>;
  /** The team's own identity in the pool. Never moves. */
  franchiseAbbrev: string | null;
  /** The franchise this team *holds* — the Équipe roster spot, which is what a
   * trade can move. Starts equal to `franchiseAbbrev` and is meant to be able
   * to diverge from it: the club you are is not the club you own (Nick,
   * 2026-08-05). Null in a league that does not use the slot. */
  franchise: {
    abbrev: string;
    name: string;
    logoUrl: string | null;
    /** Already promised away by an accepted trade — the sheet greys it out,
     * same as a player or a pick. */
    engaged: boolean;
  } | null;
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
  /** Picks per team per year — one per round. Null = no draft. */
  draftRounds: number | null;
  /** Roster spots a GM may protect before the off-season steal draft. Null = not configured. */
  protectionSlots: number | null;
  /**
   * What a player with no contract on file costs against the cap, in whole
   * dollars. $1M by default; 0 carries unsigned players for free.
   *
   * Not nullable: an unsigned free agent is a permanent state in a keeper
   * pool, not a data gap, so every league needs an answer.
   */
  defaultCapHit: number;
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
export interface LineupEntry extends Engageable {
  spotId: string;
  /** Null on an Équipe entry, which holds a franchise rather than a player. */
  playerId: number | null;
  name: string;
  position: string;
  positionGroup: PosGroup;
  team: string | null;
  headshotUrl: string | null;
  /** The franchise's crest, on an Équipe entry only. */
  logoUrl: string | null;
  capHit: number | null;
  /** Always true on an Équipe entry — one franchise, one seat, nothing to
   * toggle. */
  active: boolean;
  /** He is not on this week's roster: he leaves before it opens. Present only
   * on a week that has not begun, and never selectable — there is no lineup row
   * to set. Shown anyway so the GM sees where his slot went rather than watch a
   * player vanish. */
  leaving?: boolean;
  /** Acquired in a trade that has not taken effect. He is genuinely on this
   * week's roster and can be activated; he is simply not on today's. */
  arriving?: boolean;
  points: number;
  gamesPlayed: number;
  /** The week's raw line, for cards that say what happened rather than only
   * what it scored. Goalie fields are 0 for skaters and vice versa. */
  goals: number;
  assists: number;
  wins: number;
  otLosses: number;
  saves: number;
  teamWins: number;
  teamLosses: number;
  teamOtLosses: number;
  /** The days of the week this spot owned — set when a player was acquired mid-week. */
  fromDate: string | null;
  toDate: string | null;
  seasonPoints: number;
}

/** One unrostered player's season to date: the raw NHL line, plus the fantasy
 * score it earns under the league's own scoring rules (see `api.freeAgents`).
 * The card shows the raw line; the score is only what ranks the list, and is
 * what lets a goalie's wins/saves compete with a skater's goals/assists. */
export interface FreeAgentRow {
  playerId: number;
  name: string;
  team: string | null;
  headshotUrl: string | null;
  position: string;
  positionGroup: 'F' | 'D' | 'G';
  points: number;
  gamesPlayed: number;
  goals: number;
  assists: number;
  wins: number;
  otLosses: number;
  shutouts: number;
  goalsAgainst: number;
  saves: number;
  shotsAgainst: number;
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

/** One row of the palmarès — one season this league has played. */
export interface LeagueSeasonSummary {
  number: number;
  /** The NHL season this row plays, e.g. "20262027". */
  season: string;
  phase: "Preparing" | "Protecting" | "Drafting" | "PreSeason" | "InSeason" | "Complete";
  /** Null until the season closes and a champion is written. */
  championTeamName: string | null;
  completedUtc: string | null;
}

/** How a player is unavailable right now, per the news sources. Null on both
 * fields means he is fit. `injuryType` is the source's own short label
 * ("Achilles", "Suspension") and can be missing even when the status is not —
 * a site sometimes lists a player with no cause given. */
export interface InjuryFields {
  injuryStatus: "Injured" | "Suspended" | null;
  injuryType: string | null;
}

export interface PlayerSeasonStatsRow extends Engageable, InjuryFields {
  id: number;
  name: string;
  position: string;
  team: string;
  capHit: number | null;
  headshotUrl: string | null;
  isGoalie: boolean;
  /** No NHL game ever played. Not `Player.Status === "prospect"`, which means
   * something else (not on an NHL club's season roster) and catches a
   * different set — the grid keeps these players below the roster and out of
   * the sort. */
  isProspect: boolean;
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
  /** The Équipe slot's own record — filled only when `position` is "T", where
   * every player stat above is zero and the grid shows a dash for it. The
   * franchise's crest, shown where a player's lineup toggle sits. */
  logoUrl: string | null;
  teamWins: number;
  teamLosses: number;
  teamOtLosses: number;
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
  /** Arriving via an accepted trade not yet executed by the nightly job — no
   * roster spot exists for them here yet, so their "Fantasy point" columns
   * read zero; NHL season stats and cap hit are real. Empty for a team with no
   * pending incoming trade. */
  incoming: PlayerSeasonStatsRow[];
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
  proposer: number;
  fair: number;
  counterparty: number;
  total: number;
}

export interface TradeMyVote {
  favoredUsername: string | null; // null = voted fair
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
  /** Withheld by the server until the viewer has cast their own vote — a
   * blind ballot, so nobody's pick leans on the crowd's before they've
   * formed their own — except a party to the trade, who is never made to
   * wait since they can never vote at all. */
  votes: TradeVoteTally | null;
  myVote: TradeMyVote | null;
  /** False for either party to the trade (self-interest) or a trade that
   * isn't processed yet. */
  canVote: boolean;
}

/** A GM's presence as the server resolved it. `label` is rendered verbatim —
 * the wording lives in FantasyWarrior.Core so there is one tested
 * implementation of it rather than a second timeAgo drifting on this side.
 *
 * `online` means one thing: a live connection. There is no "seen recently so
 * probably still around" — `lastSeenUtc` only ever words the label. */
export interface MemberPresence {
  username: string;
  online: boolean;
  lastSeenUtc: string | null;
  label: string;
}

/** One row of the chat sheet's conversation list: a league-mate, their
 * presence, and the last thing said if anything ever was. */
export interface Conversation extends MemberPresence {
  preview: string | null;
  lastSentUtc: string | null;
  lastFromMe: boolean;
  unreadCount: number;
}

export interface ChatMessage {
  id: string;
  fromMe: boolean;
  body: string;
  sentUtc: string;
}

/** A message as it arrives on the live channel — addressed by username, since
 * the recipient has no idea what our local `fromMe` framing would mean. */
export interface LiveMessage {
  id: string;
  leagueId: string;
  from: string;
  to: string;
  body: string;
  sentUtc: string;
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
  /** The palmarès: one row per season this league has ever played, newest first. */
  seasons: (leagueId: string) =>
    request<LeagueSeasonSummary[]>(`/api/leagues/${encodeURIComponent(leagueId)}/seasons`),
  news: (limit = 30) =>
    request<NewsArticle[]>(`/api/news?limit=${encodeURIComponent(String(limit))}`),
  updateRules: (leagueId: string, username: string, ruleConfig: RuleConfig) =>
    request<{ ok: boolean }>(`/api/leagues/${encodeURIComponent(leagueId)}/rules`, {
      method: "PATCH",
      body: JSON.stringify({ username, ruleConfig }),
    }),
  clock: () => request<ClockStatus>("/api/clock"),
  /** Nick-only — see TestModeEndpoints.cs. Runs sim-advance up to `to`. */
  testModeAdvance: (username: string, to: string, dryRun: boolean) =>
    request<TestModeAdvanceResult>(
      `/api/testmode/advance?username=${encodeURIComponent(username)}` +
        `&to=${encodeURIComponent(to)}&dryRun=${dryRun}`,
      { method: "POST" },
    ),
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
  /** League-wide unrostered players, ranked by fantasy points under the
   * league's own scale, over the whole season to date — there is no week to
   * ask for (2026-08-04, per Nick). */
  freeAgents: (leagueId: string, limit?: number) =>
    request<FreeAgentRow[]>(
      `/api/leagues/${encodeURIComponent(leagueId)}/free-agents` +
        `${limit ? `?limit=${limit}` : ""}`,
    ),
  trades: (leagueId: string, username: string) =>
    request<Trade[]>(
      `/api/leagues/${encodeURIComponent(leagueId)}/trades?username=${encodeURIComponent(username)}`,
    ),
  /** Unused picks a team still holds. Only one draft year ever exists, so
   * there is no year to ask for. */
  picks: (leagueId: string, username: string) =>
    request<DraftPickDto[]>(
      `/api/leagues/${encodeURIComponent(leagueId)}/teams/${encodeURIComponent(username)}/picks`,
    ),
  proposeTrade: (
    leagueId: string,
    username: string,
    counterpartyUsername: string,
    playersFromProposer: number[],
    playersFromCounterparty: number[],
    picksFromProposer: number[] = [],
    picksFromCounterparty: number[] = [],
    // The Équipe slot. Abbrevs rather than flags so the server can refuse an
    // offer built on a stale screen. Only ever both or neither: a franchise is
    // tradable only against another franchise.
    franchiseFromProposer: string | null = null,
    franchiseFromCounterparty: string | null = null,
  ) =>
    request<{ id: string }>(`/api/leagues/${encodeURIComponent(leagueId)}/trades`, {
      method: "POST",
      body: JSON.stringify({
        username,
        counterpartyUsername,
        playersFromProposer,
        playersFromCounterparty,
        picksFromProposer,
        picksFromCounterparty,
        franchiseFromProposer,
        franchiseFromCounterparty,
      }),
    }),
  respondTrade: (leagueId: string, tradeId: string, username: string, accept: boolean) =>
    request<{ ok: boolean; status: TradeStatus }>(
      `/api/leagues/${encodeURIComponent(leagueId)}/trades/${encodeURIComponent(tradeId)}/respond`,
      { method: "POST", body: JSON.stringify({ username, accept }) },
    ),
  voteTrade: (leagueId: string, tradeId: string, username: string, favoredUsername: string | null) =>
    request<{ ok: boolean; cockcoinAwarded: number; cockcoinBalance: number }>(
      `/api/leagues/${encodeURIComponent(leagueId)}/trades/${encodeURIComponent(tradeId)}/vote`,
      { method: "POST", body: JSON.stringify({ username, favoredUsername }) },
    ),
  /** Everyone starts at 0 — no row for a user who's never earned any, the
   * server already turns that into 0 rather than an absent value. */
  cockcoinBalance: (username: string) =>
    request<{ balance: number }>(`/api/users/${encodeURIComponent(username)}/cockcoin`),

  /** Every other GM in the league, whether or not anything has ever been said —
   * a pool of fourteen is a contact list and an inbox at once. Already sorted
   * by the server. */
  conversations: (leagueId: string, username: string) =>
    request<Conversation[]>(
      `/api/leagues/${encodeURIComponent(leagueId)}/messages?username=${encodeURIComponent(username)}`,
    ),
  thread: (leagueId: string, username: string, peer: string) =>
    request<ChatMessage[]>(
      `/api/leagues/${encodeURIComponent(leagueId)}/messages/${encodeURIComponent(peer)}` +
        `?username=${encodeURIComponent(username)}`,
    ),
  /** The response is the stored message. It also arrives over the live channel
   * a moment later — the sheet dedupes on `id` rather than guessing which wins. */
  sendMessage: (leagueId: string, username: string, to: string, body: string) =>
    request<LiveMessage>(`/api/leagues/${encodeURIComponent(leagueId)}/messages`, {
      method: "POST",
      body: JSON.stringify({ username, to, body }),
    }),
  markRead: (leagueId: string, username: string, peer: string) =>
    request<{ marked: number }>(
      `/api/leagues/${encodeURIComponent(leagueId)}/messages/${encodeURIComponent(peer)}/read`,
      { method: "POST", body: JSON.stringify({ username }) },
    ),
  unreadCount: (leagueId: string, username: string) =>
    request<{ count: number }>(
      `/api/leagues/${encodeURIComponent(leagueId)}/unread?username=${encodeURIComponent(username)}`,
    ),
};

export const formatCap = (amount: number | null | undefined) =>
  amount == null ? "—" : `$${amount.toLocaleString("en-US")}`;

/** Signed, compact money for tiles and tight rows: `$9.2M`, `-$1.3M`, `$850K`.
 *
 * Shared since 2026-08-03 — it started private to the Dashboard, and the trade
 * sheet needs the identical wording for its cap recap. Three private dollar
 * formatters were already in circulation; this is the one that belongs here. */
export function formatCapCompact(amount: number | null | undefined): string {
  if (amount == null) return "—";
  const sign = amount < 0 ? "-" : "";
  const abs = Math.abs(amount);
  if (abs >= 1_000_000) return `${sign}$${(abs / 1_000_000).toFixed(1)}M`;
  if (abs >= 1_000) return `${sign}$${Math.round(abs / 1_000)}K`;
  return `${sign}$${abs}`;
}

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

/** First letters of up to the first two "words" of a username (usernames in
 * this app are plain handles, not "First Last", but a couple may contain a
 * dot/underscore/dash separator — handle that gracefully, fall back to the
 * first two characters otherwise).
 *
 * Shared rather than living in ProfileMenu since 2026-08-03: the chat sheet
 * draws the same avatar, and two copies would eventually disagree about the
 * same person's initials. */
export function initials(username: string): string {
  const parts = username.split(/[.\-_ ]+/).filter(Boolean);
  if (parts.length >= 2) return (parts[0][0] + parts[1][0]).toUpperCase();
  return username.slice(0, 2).toUpperCase();
}

/** F/D/G are player positions; "T" is the Équipe slot, which holds an NHL
 * franchise rather than a player. It rides in the same indicator because every
 * screen already has one, and a fourth one-off would be exactly the kind of
 * exception CLAUDE.md forbids. */
export type PosGroup = "F" | "D" | "G" | "T";

/** Collapses a raw NHL position code to the roster groups everywhere the app
 * displays a position — C/L/R always read as "F", at all times, no per-screen
 * exceptions (the raw code is still what's stored/returned by the API; this is
 * a display-only rule). Single source of truth: every screen (Roster, Stats,
 * Dashboard, PlayerCard) imports this instead of keeping its own copy. */
export function posGroup(position: string): PosGroup {
  if (position === "D") return "D";
  if (position === "G") return "G";
  if (position === "T") return "T";
  return "F";
}

/** Lowercase class suffix ("f"/"d"/"g"/"t") for the app-wide position-indicator
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

/** The four bands from the rating explainer (see TradeRatingInfo), flat at
 * 50 and escalating toward a landslide — drives the color/glow class
 * (`rating-tier-*` in App.css) wherever a rating renders, so the color
 * itself carries the "how decisive was this" signal before anyone reads
 * the number. */
export type TradeRatingTier = "even" | "lean" | "clear" | "consensus";

function tradeRatingTier(rating: number): TradeRatingTier {
  if (rating <= 50) return "even";
  if (rating <= 65) return "lean";
  if (rating <= 85) return "clear";
  return "consensus";
}

export interface TradeRating {
  /** null only for a dead-even split (rating exactly 50) — nobody "won". */
  winner: "proposer" | "counterparty" | null;
  /** 50–100, always scoped to the winner: how convincingly the crowd sided
   * with them, never how badly the other side "lost". A QB-rating-style
   * single number rather than three buckets to compare by eye. */
  rating: number;
  tier: TradeRatingTier;
}

/** Collapses the proposer/fair/counterparty vote buckets into one QB-rating-
 * style number. A "fair" vote is a vote that both sides did equally well, so
 * it's split half a point to each side rather than sitting in its own
 * bucket — the same weighting either team's raw vote count already gets,
 * just extended to the option that isn't really a third side. Whichever
 * side ends up with more weight "won"; their share of the total is the
 * rating. An exact tie (including an all-fair vote) is 50 with no winner. */
export function tradeRating(votes: TradeVoteTally): TradeRating {
  if (votes.total === 0) return { winner: null, rating: 50, tier: "even" };
  const proposerScore = votes.proposer + votes.fair / 2;
  const counterpartyScore = votes.counterparty + votes.fair / 2;
  if (proposerScore === counterpartyScore) return { winner: null, rating: 50, tier: "even" };
  const winner = counterpartyScore > proposerScore ? "counterparty" : "proposer";
  const rating = Math.round((Math.max(proposerScore, counterpartyScore) / votes.total) * 100);
  return { winner, rating, tier: tradeRatingTier(rating) };
}
