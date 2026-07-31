// PlayerCard — bottom-sheet (mobile) / centered modal (desktop) showing a
// player's photo, bio, season totals and last 10 game lines.
// Self-contained: fetches GET {API_BASE}/api/players/{playerId} on its own.
// Night Arena design system — tokens from index.css, shared classes from App.css.

import { useEffect, useRef, useState } from "react";
import type { JSX, KeyboardEvent as ReactKeyboardEvent, MouseEvent as ReactMouseEvent } from "react";
import { posGroup, posGroupClass } from "../api";
import { ExternalLinkIcon, XIcon } from "./Icons";
import "./PlayerCard.css";

const API_BASE = import.meta.env.VITE_API_URL || "http://localhost:5099";

/* ---------- data contract (GET /api/players/{id}) ---------- */

interface SeasonTotals {
  gamesPlayed: number;
  goals: number;
  assists: number;
  points: number;
  plusMinus: number;
  pim: number;
  shots: number;
  wins: number;
  otLosses: number;
  shutouts: number;
  goalsAgainst: number;
  saves: number;
  shotsAgainst: number;
}

interface RecentGame {
  date: string;
  gameId: number;
  opponent: string;
  isHome: boolean;
  goals: number | null;
  assists: number | null;
  points: number | null;
  plusMinus: number | null;
  pim: number | null;
  shots: number | null;
  toi: string | null;
  decision: string | null; // "W" | "L" | "O" for goalies
  saves: number | null;
  shotsAgainst: number | null;
  goalsAgainst: number | null;
  shutout: boolean | null;
}

interface PlayerDetail {
  id: number;
  name: string;
  position: string;
  team: string;
  status: string;
  sweaterNumber: number | null;
  shootsCatches: string | null;
  birthDate: string | null;
  birthCountry: string | null;
  heightCm: number | null;
  weightKg: number | null;
  headshotUrl: string | null;
  capHit: number | null;
  draftYear: number | null;
  draftRound: number | null;
  draftOverall: number | null;
  draftTeamAbbrev: string | null;
  isGoalie: boolean;
  season: string;
  seasonTotals: SeasonTotals | null;
  recentGames: RecentGame[] | null;
}

/* ---------- formatting helpers ---------- */

const formatCapHit = (amount: number | null | undefined) =>
  amount == null ? "—" : `$${amount.toLocaleString("en-US")}`;

const signed = (n: number) => (n > 0 ? `+${n}` : String(n));

function computeAge(birthDate: string): number | null {
  const b = new Date(`${birthDate}T00:00:00`);
  if (Number.isNaN(b.getTime())) return null;
  const now = new Date();
  let age = now.getFullYear() - b.getFullYear();
  const m = now.getMonth() - b.getMonth();
  if (m < 0 || (m === 0 && now.getDate() < b.getDate())) age--;
  return age;
}

/** Ordinal suffix: 1 -> "1st", 3 -> "3rd", 11 -> "11th", 123 -> "123rd". */
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

/** "Dec 25th" — no year, month/day only (2026-07-27, per Nick: bio row
 * collapsed to one line per cell, age is now the headline value). */
function formatBirthDayMonth(birthDate: string): string {
  const d = new Date(`${birthDate}T00:00:00`);
  if (Number.isNaN(d.getTime())) return birthDate;
  const month = d.toLocaleDateString("en-US", { month: "short" });
  return `${month} ${ordinal(d.getDate())}`;
}

/** "1st 2005" main + "PIT" team, rendered as two segments so the team can
 * get the muted de-emphasized treatment instead of parens (2026-07-27, per
 * Nick — no more "()", just a lighter/muted font for the secondary bit). */
function formatDraft(p: PlayerDetail): { main: string; team: string | null } {
  if (p.draftYear == null || p.draftOverall == null) return { main: "Undrafted", team: null };
  return { main: `${ordinal(p.draftOverall)} ${p.draftYear}`, team: p.draftTeamAbbrev };
}

function formatGameDate(date: string): string {
  const d = new Date(`${date}T00:00:00`);
  if (Number.isNaN(d.getTime())) return date;
  return d.toLocaleDateString("en-US", { month: "short", day: "numeric" });
}

/** "20252026" -> "2025-26" */
function formatSeason(season: string): string {
  if (season.length !== 8) return season;
  return `${season.slice(0, 4)}-${season.slice(6)}`;
}

/** No per-player Hockey-Reference ID mapping exists, so we route through
 * their name search — Sports Reference's search redirects straight to the
 * player page on a unique name match. */
function hockeyReferenceUrl(playerName: string): string {
  return `https://www.hockey-reference.com/search/search.fcgi?search=${encodeURIComponent(playerName)}`;
}

const formatGaa = (t: SeasonTotals) =>
  t.gamesPlayed > 0 ? (t.goalsAgainst / t.gamesPlayed).toFixed(2) : "—";

const formatSvPct = (t: SeasonTotals) =>
  t.shotsAgainst > 0 ? (t.saves / t.shotsAgainst).toFixed(3).replace(/^0\./, ".") : "—";

/* ---------- stat tiles ---------- */

interface Tile {
  label: string;
  value: string;
  accent?: boolean;
}

const skaterTiles = (t: SeasonTotals): Tile[] => [
  { label: "GP", value: String(t.gamesPlayed) },
  { label: "G", value: String(t.goals) },
  { label: "A", value: String(t.assists) },
  { label: "PTS", value: String(t.points), accent: true },
  { label: "+/-", value: signed(t.plusMinus) },
  { label: "PIM", value: String(t.pim) },
  { label: "SOG", value: String(t.shots) },
];

const goalieTiles = (t: SeasonTotals): Tile[] => [
  { label: "GP", value: String(t.gamesPlayed) },
  { label: "W", value: String(t.wins), accent: true },
  { label: "OTL", value: String(t.otLosses) },
  { label: "SO", value: String(t.shutouts) },
  { label: "GAA", value: formatGaa(t) },
  { label: "SV%", value: formatSvPct(t) },
];

/* ---------- game log table ---------- */

const isHotGame = (g: RecentGame, isGoalie: boolean): boolean =>
  isGoalie ? g.decision === "W" : (g.goals ?? 0) > 0;

/** Aligned columns instead of a single concatenated stat string, so the 10
 * rows read as a scannable grid (2026-07-31, per Nick). */
function GamesTable({ games, isGoalie }: { games: RecentGame[]; isGoalie: boolean }) {
  return (
    <table className="pc-games-table">
      <colgroup>
        <col style={{ width: "3rem" }} />
        <col style={{ width: "3.4rem" }} />
        <col />
        <col />
        <col />
        <col />
      </colgroup>
      <thead>
        <tr>
          <th scope="col">Date</th>
          <th scope="col">Opp</th>
          {isGoalie ? (
            <>
              <th scope="col">Dec</th>
              <th scope="col">SV</th>
              <th scope="col">GA</th>
              <th scope="col">SO</th>
            </>
          ) : (
            <>
              <th scope="col">G</th>
              <th scope="col">A</th>
              <th scope="col">+/-</th>
              <th scope="col">TOI</th>
            </>
          )}
        </tr>
      </thead>
      <tbody>
        {games.map((g) => (
          <tr key={g.gameId} className={isHotGame(g, isGoalie) ? "hot" : undefined}>
            <td>{formatGameDate(g.date)}</td>
            <td>
              {g.isHome ? "vs" : "@"} {g.opponent}
            </td>
            {isGoalie ? (
              <>
                <td>{g.decision ?? "—"}</td>
                <td>
                  {g.saves ?? 0}/{g.shotsAgainst ?? 0}
                </td>
                <td>{g.goalsAgainst ?? 0}</td>
                <td>{g.shutout ? "SO" : "—"}</td>
              </>
            ) : (
              <>
                <td>{g.goals ?? 0}</td>
                <td>{g.assists ?? 0}</td>
                <td>{signed(g.plusMinus ?? 0)}</td>
                <td>{g.toi ?? "--:--"}</td>
              </>
            )}
          </tr>
        ))}
      </tbody>
    </table>
  );
}

/* ---------- skeleton (loading state, no spinner) ---------- */

function Skeleton() {
  return (
    <div aria-hidden="true">
      <div className="pc-header">
        <span className="pc-skel pc-skel-headshot" />
        <div className="pc-header-info">
          <span className="pc-skel pc-skel-line" style={{ width: "60%" }} />
          <span className="pc-skel pc-skel-line" style={{ width: "40%" }} />
        </div>
      </div>
      <div className="pc-bio-grid">
        {Array.from({ length: 3 }, (_, i) => (
          <span key={i} className="pc-skel pc-skel-bio" />
        ))}
      </div>
      <div className="pc-tiles">
        {Array.from({ length: 4 }, (_, i) => (
          <span key={i} className="pc-skel pc-skel-tile" />
        ))}
      </div>
      <div className="pc-games">
        {Array.from({ length: 3 }, (_, i) => (
          <span key={i} className="pc-skel pc-skel-row" />
        ))}
      </div>
    </div>
  );
}

/* ---------- main component ---------- */

export function PlayerCard({ playerId, onClose }: { playerId: number; onClose: () => void }): JSX.Element {
  const [player, setPlayer] = useState<PlayerDetail | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const [gameLogTab, setGameLogTab] = useState<"last10" | "career">("last10");
  const [careerVisited, setCareerVisited] = useState(false);
  const sheetRef = useRef<HTMLDivElement>(null);
  const closeRef = useRef<HTMLButtonElement>(null);

  // Fetch player detail.
  useEffect(() => {
    const ctrl = new AbortController();
    setLoading(true);
    setError("");
    setPlayer(null);
    setGameLogTab("last10");
    setCareerVisited(false);
    fetch(`${API_BASE}/api/players/${playerId}`, { signal: ctrl.signal })
      .then(async (res) => {
        const body: unknown = await res.json().catch(() => ({}));
        if (!res.ok) {
          const msg = (body as { error?: string }).error ?? `HTTP ${res.status}`;
          throw new Error(msg);
        }
        return body as PlayerDetail;
      })
      .then((p) => {
        setPlayer(p);
        setLoading(false);
      })
      .catch((err: unknown) => {
        if (ctrl.signal.aborted) return;
        setError(err instanceof Error ? err.message : "Could not load player.");
        setLoading(false);
      });
    return () => ctrl.abort();
  }, [playerId]);

  // Escape closes.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  // Lock body scroll while open; move focus in, restore it on close.
  useEffect(() => {
    const prevOverflow = document.body.style.overflow;
    const prevFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    document.body.style.overflow = "hidden";
    closeRef.current?.focus();
    return () => {
      document.body.style.overflow = prevOverflow;
      prevFocus?.focus();
    };
  }, []);

  // Minimal focus trap: keep Tab cycling inside the dialog.
  const trapFocus = (e: ReactKeyboardEvent<HTMLDivElement>) => {
    if (e.key !== "Tab" || !sheetRef.current) return;
    const focusables = sheetRef.current.querySelectorAll<HTMLElement>(
      'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])',
    );
    if (focusables.length === 0) return;
    const first = focusables[0];
    const last = focusables[focusables.length - 1];
    if (e.shiftKey && document.activeElement === first) {
      e.preventDefault();
      last.focus();
    } else if (!e.shiftKey && document.activeElement === last) {
      e.preventDefault();
      first.focus();
    }
  };

  const onBackdrop = (e: ReactMouseEvent<HTMLDivElement>) => {
    if (e.target === e.currentTarget) onClose();
  };

  const games = (player?.recentGames ?? [])
    .slice()
    .sort((a, b) => b.date.localeCompare(a.date))
    .slice(0, 10);

  const age = player?.birthDate != null ? computeAge(player.birthDate) : null;
  const draft = player != null ? formatDraft(player) : { main: "", team: null };

  return (
    <div className="pc-overlay" onClick={onBackdrop}>
      <div
        ref={sheetRef}
        className="pc-sheet"
        role="dialog"
        aria-modal="true"
        aria-labelledby="pc-player-name"
        aria-label="Player card"
        onKeyDown={trapFocus}
      >
        <div className="pc-top">
          <span className="pc-handle" aria-hidden="true" />
          <button ref={closeRef} className="pc-close" onClick={onClose} aria-label="Close player card">
            <XIcon size={20} />
          </button>
        </div>

        <div className="pc-body">
          {loading && <Skeleton />}

          {!loading && error && <p className="error-banner">{error}</p>}

          {!loading && !error && player && (
            <>
              {/* header */}
              <div className="pc-header">
                <img
                  className="headshot pc-headshot"
                  src={player.headshotUrl ?? ""}
                  alt=""
                  loading="lazy"
                />
                <div className="pc-header-info">
                  <h2 id="pc-player-name" className="pc-name">
                    {player.name}
                  </h2>
                  <div className="pc-header-meta">
                    <span className={`roster-pos-pill roster-pos-pill-${posGroupClass(player.position)}`}>
                      {posGroup(player.position)}
                    </span>
                    <span className="pc-team">{player.team}</span>
                    {player.sweaterNumber != null && (
                      <span className="pc-number">#{player.sweaterNumber}</span>
                    )}
                  </div>
                </div>
              </div>

              {/* bio */}
              <div className="pc-bio-grid">
                <div className="pc-bio-item">
                  <span className="pc-bio-label">Age</span>
                  <span className="pc-bio-value">
                    {age != null ? String(age) : "—"}
                    {player.birthDate != null && (
                      <span className="pc-bio-inline-sub pc-bio-inline-sub-birthday">
                        {formatBirthDayMonth(player.birthDate)}
                      </span>
                    )}
                  </span>
                </div>
                <div className="pc-bio-item">
                  <span className="pc-bio-label">Draft</span>
                  <span className="pc-bio-value">
                    {draft.main}
                    {draft.team != null && <span className="pc-bio-inline-sub">{draft.team}</span>}
                  </span>
                </div>
                <div className="pc-bio-item">
                  <span className="pc-bio-label">Cap hit</span>
                  <span className="pc-bio-value">{formatCapHit(player.capHit)}</span>
                </div>
              </div>

              {/* season totals */}
              <span className="section-title">Season {formatSeason(player.season)}</span>
              {player.seasonTotals ? (
                <div className={`pc-tiles${player.isGoalie ? " pc-tiles-goalie" : ""}`}>
                  {(player.isGoalie
                    ? goalieTiles(player.seasonTotals)
                    : skaterTiles(player.seasonTotals)
                  ).map((tile) => (
                    <div key={tile.label} className={`pc-tile${tile.accent ? " accent" : ""}`}>
                      <span className="pc-tile-value">{tile.value}</span>
                      <span className="pc-tile-label">{tile.label}</span>
                    </div>
                  ))}
                </div>
              ) : (
                <p className="pc-empty muted">No stats this season.</p>
              )}

              {/* game log: Last 10 games / Career (embedded Hockey-Reference) */}
              <div className="pc-tabs" role="tablist" aria-label="Game log">
                <button
                  id="pc-tab-last10"
                  role="tab"
                  type="button"
                  aria-selected={gameLogTab === "last10"}
                  aria-controls="pc-panel-last10"
                  className={`pc-tab${gameLogTab === "last10" ? " active" : ""}`}
                  onClick={() => setGameLogTab("last10")}
                >
                  Last 10
                </button>
                <button
                  id="pc-tab-career"
                  role="tab"
                  type="button"
                  aria-selected={gameLogTab === "career"}
                  aria-controls="pc-panel-career"
                  className={`pc-tab${gameLogTab === "career" ? " active" : ""}`}
                  onClick={() => {
                    setGameLogTab("career");
                    setCareerVisited(true);
                  }}
                >
                  Career
                </button>
              </div>

              <div
                id="pc-panel-last10"
                role="tabpanel"
                aria-labelledby="pc-tab-last10"
                hidden={gameLogTab !== "last10"}
              >
                {games.length === 0 ? (
                  <p className="pc-empty muted">No games played yet.</p>
                ) : (
                  <div className="pc-games">
                    <GamesTable games={games} isGoalie={player.isGoalie} />
                  </div>
                )}
              </div>

              <div
                id="pc-panel-career"
                role="tabpanel"
                aria-labelledby="pc-tab-career"
                hidden={gameLogTab !== "career"}
              >
                <div className="pc-career">
                  <div className="pc-career-header">
                    <span className="pc-career-source">Hockey-Reference</span>
                    <a
                      className="pc-career-link"
                      href={hockeyReferenceUrl(player.name)}
                      target="_blank"
                      rel="noopener noreferrer"
                    >
                      Open <ExternalLinkIcon size={14} />
                    </a>
                  </div>
                  {careerVisited && (
                    <iframe
                      className="pc-career-frame"
                      src={hockeyReferenceUrl(player.name)}
                      title={`${player.name} — career stats on Hockey-Reference`}
                      loading="lazy"
                      referrerPolicy="no-referrer"
                    />
                  )}
                  <p className="pc-career-note muted">
                    External data from Hockey-Reference.com — if it doesn't load below, use "Open" above.
                  </p>
                </div>
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
