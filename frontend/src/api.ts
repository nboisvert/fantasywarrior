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
  points: number;
  counted: boolean;
}

export interface TeamDto {
  name: string;
  ownerUsername: string;
  score: number;
  rawTopXScore: number;
  adjustmentsTotal: number;
  capTotal: number;
  players: RosterPlayer[];
}

export interface RuleConfig {
  pointValues: {
    goal: number;
    assist: number;
    goalieWin: number;
    goalieOtLoss: number;
    shutout: number;
  };
  topCount: {
    forwards: number | null;
    defense: number | null;
    goalies: number | null;
  };
}

export interface LeagueDetail {
  id: string;
  name: string;
  season: string;
  capAmount: number | null;
  commissionerUsername: string;
  ruleConfig: RuleConfig;
  members: string[];
  teams: TeamDto[];
}

export interface ActivityEntry {
  type: "add" | "drop";
  dateUtc: string;
  playerId: number;
  playerName: string;
  position: string;
  teamUsername: string;
  teamName: string;
  source: "initial" | "free_agency" | "trade" | "draft";
  sourceRefId: string | null;
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
  league: (leagueId: string) => request<LeagueDetail>(`/api/leagues/${encodeURIComponent(leagueId)}`),
  activity: (leagueId: string, limit = 15) =>
    request<ActivityEntry[]>(
      `/api/leagues/${encodeURIComponent(leagueId)}/activity?limit=${encodeURIComponent(String(limit))}`,
    ),
  updateRules: (leagueId: string, username: string, ruleConfig: RuleConfig) =>
    request<{ ok: boolean }>(`/api/leagues/${encodeURIComponent(leagueId)}/rules`, {
      method: "PATCH",
      body: JSON.stringify({ username, ruleConfig }),
    }),
  searchPlayers: (q: string) => request<PlayerDto[]>(`/api/players?q=${encodeURIComponent(q)}`),
  addPlayer: (leagueId: string, username: string, playerId: number) =>
    request<PlayerDto>(`/api/leagues/${encodeURIComponent(leagueId)}/teams/${encodeURIComponent(username)}/roster`, {
      method: "POST",
      body: JSON.stringify({ playerId }),
    }),
  removePlayer: (leagueId: string, username: string, playerId: number) =>
    request<void>(
      `/api/leagues/${encodeURIComponent(leagueId)}/teams/${encodeURIComponent(username)}/roster/${playerId}`,
      { method: "DELETE" },
    ),
};

export const formatCap = (amount: number | null | undefined) =>
  amount == null ? "—" : `$${amount.toLocaleString("en-US")}`;

/** "20262027" -> "2026-27" */
export const formatSeason = (season: string): string =>
  season.length === 8 ? `${season.slice(0, 4)}-${season.slice(6)}` : season;
