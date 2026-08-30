// PlayerCard — bottom-sheet (mobile) / centered modal (desktop) showing a
// player's photo, bio, season totals and last 10 game lines.
// Self-contained: fetches GET {API_BASE}/api/players/{playerId} on its own.
// Night Arena design system — tokens from index.css, shared classes from App.css.

import { useEffect, useRef, useState } from "react";
import type { JSX, KeyboardEvent as ReactKeyboardEvent, MouseEvent as ReactMouseEvent } from "react";
import { posGroup, posGroupClass } from "../api";
import { CrossIcon, ExternalLinkIcon, GavelIcon, ShieldIcon, XIcon } from "./Icons";
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
  hits: number;
  blockedShots: number;
  wins: number;
  otLosses: number;
  shutouts: number;
  goalsAgainst: number;
  saves: number;
  shotsAgainst: number;
  avgToi: string | null;
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

/** One season/league/team stint from GET /api/players/{id}/career, sourced
 * from career-sync's cache of the NHL's own player-landing data — a
 * player's whole hockey history, not just what this app has tracked since
 * going live. */
interface CareerRow {
  season: string;
  league: string;
  team: string | null;
  gamesPlayed: number;
  goals: number | null;
  assists: number | null;
  points: number | null;
  pim: number | null;
  plusMinus: number | null;
  wins: number | null;
  losses: number | null;
  otLosses: number | null;
  goalsAgainst: number | null;
  goalsAgainstAvg: number | null;
  savePctg: number | null;
  shutouts: number | null;
}

/** How this player is unavailable right now, or null when he is fit. Comes
 * from the injury lists news-sync reads nightly, not from the NHL — so
 * `injuryType` is the source's own short label and can be missing. */
interface Injury {
  status: "Injured" | "Suspended";
  injuryType: string | null;
  since: string;
}

/** One item from GET /api/players/{id}/news — every source, newest first.
 * `body` is the factual paragraph; Rotowire's subscription-locked ANALYSIS
 * block is never stored, so it can never appear here. */
interface NewsRow {
  id: string;
  source: string;
  headline: string;
  body: string | null;
  url: string | null;
  publishedUtc: string;
}

interface PlayerDetail {
  id: number;
  name: string;
  injury: Injury | null;
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
  // Regular-season NHL games, career to date, and whether that puts him out of
  // reach in the off-season draft. Both null means career-sync has never
  // reached him — not that he has never played. The card shows nothing on
  // null: an AUTO badge on a veteran whose sync failed would be a lie about a
  // real person, and no badge at all is merely a gap.
  careerNhlGames: number | null;
  autoProtected: boolean | null;
  isGoalie: boolean;
  // Null for a player with no NHL game on record at all — every true
  // prospect. `seasonTotals` below is never null from the API even then
  // (it sums zero rows to zero, not to nothing), so this is the field that
  // actually says whether there is a season to report.
  season: string | null;
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

/** "Jul 19, 2026" — news carries a real date, and a headline with no date is
 * worth very little when a standing injury sits on a page for months. */
function formatNewsDate(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return d.toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric" });
}

/** "rotowire_html" -> "Rotowire". The stored value is a sync key, not a label,
 * and three of them collapse to two sites. */
const SOURCE_LABELS: Record<string, string> = {
  rotowire_rss: "Rotowire",
  rotowire_html: "Rotowire",
  fantasysp: "FantasySP",
};

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

/** Full NHL team name -> city, for the Career tab (2026-08-02, per Nick: NHL
 * rows show just the city, to help the table fit without horizontal
 * scroll). Junior/college/European team names are already short, so this
 * only applies to `league === "NHL"` rows — anything not in the map (an old
 * relocated/renamed franchise this list doesn't cover) just falls back to
 * the full name rather than showing something wrong. */
const NHL_TEAM_CITIES: Record<string, string> = {
  "Boston Bruins": "Boston",
  "Buffalo Sabres": "Buffalo",
  "Detroit Red Wings": "Detroit",
  "Florida Panthers": "Florida",
  "Montreal Canadiens": "Montreal",
  "Ottawa Senators": "Ottawa",
  "Tampa Bay Lightning": "Tampa Bay",
  "Toronto Maple Leafs": "Toronto",
  "Carolina Hurricanes": "Carolina",
  "Columbus Blue Jackets": "Columbus",
  "New Jersey Devils": "New Jersey",
  "New York Islanders": "New York",
  "New York Rangers": "New York",
  "Philadelphia Flyers": "Philadelphia",
  "Pittsburgh Penguins": "Pittsburgh",
  "Washington Capitals": "Washington",
  "Chicago Blackhawks": "Chicago",
  "Colorado Avalanche": "Colorado",
  "Dallas Stars": "Dallas",
  "Minnesota Wild": "Minnesota",
  "Nashville Predators": "Nashville",
  "St. Louis Blues": "St. Louis",
  "Utah Mammoth": "Utah",
  "Winnipeg Jets": "Winnipeg",
  "Anaheim Ducks": "Anaheim",
  "Calgary Flames": "Calgary",
  "Edmonton Oilers": "Edmonton",
  "Los Angeles Kings": "Los Angeles",
  "Seattle Kraken": "Seattle",
  "San Jose Sharks": "San Jose",
  "Vancouver Canucks": "Vancouver",
  "Vegas Golden Knights": "Vegas",
  "Arizona Coyotes": "Arizona",
  "Phoenix Coyotes": "Phoenix",
};

function formatCareerTeam(team: string | null, league: string): string {
  if (team == null) return "—";
  return league === "NHL" ? (NHL_TEAM_CITIES[team] ?? team) : team;
}

const formatGaa = (t: SeasonTotals) =>
  t.gamesPlayed > 0 ? (t.goalsAgainst / t.gamesPlayed).toFixed(2) : "—";

const formatSvPct = (t: SeasonTotals) =>
  t.shotsAgainst > 0 ? (t.saves / t.shotsAgainst).toFixed(3).replace(/^0\./, ".") : "—";

const formatPtsPerGame = (t: SeasonTotals) =>
  t.gamesPlayed > 0 ? (t.points / t.gamesPlayed).toFixed(2) : "—";

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
  { label: "PTS/G", value: formatPtsPerGame(t) },
  { label: "+/-", value: signed(t.plusMinus) },
  { label: "PIM", value: String(t.pim) },
  { label: "SOG", value: String(t.shots) },
  { label: "HIT", value: String(t.hits) },
  { label: "BLK", value: String(t.blockedShots) },
  { label: "TOI", value: t.avgToi ?? "—" },
];

const goalieTiles = (t: SeasonTotals): Tile[] => [
  { label: "GP", value: String(t.gamesPlayed) },
  { label: "W", value: String(t.wins), accent: true },
  { label: "OTL", value: String(t.otLosses) },
  { label: "SO", value: String(t.shutouts) },
  { label: "GAA", value: formatGaa(t) },
  { label: "SV%", value: formatSvPct(t) },
  { label: "TOI", value: t.avgToi ?? "—" },
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

/* ---------- career table ---------- */

/** One row per season/league/team stint, most recent season first (already
 * sorted server-side). Column set depends on isGoalie, same branch pattern
 * as the tiles and GamesTable above. */
function CareerTable({ rows, isGoalie }: { rows: CareerRow[]; isGoalie: boolean }) {
  return (
    <table className="pc-career-table">
      <thead>
        <tr>
          <th scope="col">Season</th>
          <th scope="col" aria-label="League" />
          <th scope="col">Team</th>
          <th scope="col">GP</th>
          {isGoalie ? (
            <>
              <th scope="col">W</th>
              <th scope="col">L</th>
              <th scope="col">OTL</th>
              <th scope="col">GAA</th>
              <th scope="col">SV%</th>
              <th scope="col">SO</th>
            </>
          ) : (
            <>
              <th scope="col">G</th>
              <th scope="col">A</th>
              <th scope="col">PTS</th>
            </>
          )}
        </tr>
      </thead>
      <tbody>
        {rows.map((r, i) => (
          <tr key={`${r.season}-${r.league}-${r.team ?? ""}-${i}`}>
            <td>{formatSeason(r.season)}</td>
            <td>{r.league}</td>
            <td>{formatCareerTeam(r.team, r.league)}</td>
            <td>{r.gamesPlayed}</td>
            {isGoalie ? (
              <>
                <td>{r.wins ?? 0}</td>
                <td>{r.losses ?? 0}</td>
                <td>{r.otLosses ?? 0}</td>
                <td>{r.goalsAgainstAvg != null ? r.goalsAgainstAvg.toFixed(2) : "—"}</td>
                <td>{r.savePctg != null ? r.savePctg.toFixed(3).replace(/^0\./, ".") : "—"}</td>
                <td>{r.shutouts ?? 0}</td>
              </>
            ) : (
              <>
                <td>{r.goals ?? 0}</td>
                <td>{r.assists ?? 0}</td>
                <td>{r.points ?? 0}</td>
              </>
            )}
          </tr>
        ))}
      </tbody>
    </table>
  );
}

/* ---------- injury banner ---------- */

/** Why this player's name is marked on the Team grid, said in words.
 *
 * A full-width strip rather than a badge in the header meta: that row is
 * nowrap and already carries position, team and number, so a fourth item
 * would push the team name out. This is also the only place the *date* fits,
 * and "out since Jul 19" is most of what a GM wants — the label alone does
 * not say whether he is about to come back. */
function InjuryBanner({ injury }: { injury: Injury }) {
  const suspended = injury.status === "Suspended";
  return (
    <div className="pc-injury">
      {suspended ? <GavelIcon size={15} /> : <CrossIcon size={13} />}
      <span className="pc-injury-label">
        {suspended ? "Suspended" : "Injured"}
        {injury.injuryType != null && <span className="pc-injury-type">{injury.injuryType}</span>}
      </span>
      <span className="pc-injury-since">since {formatNewsDate(injury.since)}</span>
    </div>
  );
}

/* ---------- news list ---------- */

/** Every item we hold about this player, newest first — a contract signing and
 * a knee sit in the same list, because a GM wants both and would only open one
 * tab. The source is named on each item: two sites word the same event
 * differently, and there is no point pretending they are one voice. */
function NewsList({ rows }: { rows: NewsRow[] }) {
  return (
    <ul className="pc-news">
      {rows.map((n) => (
        <li key={n.id} className="pc-news-item">
          <div className="pc-news-head">
            <span className="pc-news-date">{formatNewsDate(n.publishedUtc)}</span>
            <span className="pc-news-source">{SOURCE_LABELS[n.source] ?? n.source}</span>
          </div>
          <p className="pc-news-headline">{n.headline}</p>
          {n.body != null && <p className="pc-news-body">{n.body}</p>}
          {n.url != null && (
            <a className="pc-news-link" href={n.url} target="_blank" rel="noopener noreferrer">
              Read at the source
              <ExternalLinkIcon size={12} />
            </a>
          )}
        </li>
      ))}
    </ul>
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

/**
 * @param leagueId Whose rules decide "auto-protected". The bars are a league rule,
 *   so a card opened outside any league gets no answer and shows no badge —
 *   better than reporting one pool's threshold as a fact about the player.
 */
export function PlayerCard(
  { playerId, leagueId, onClose }:
  { playerId: number; leagueId?: string; onClose: () => void },
): JSX.Element {
  const [player, setPlayer] = useState<PlayerDetail | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const [activeTab, setActiveTab] = useState<"stats" | "last10" | "career" | "news">("stats");
  // Fetched lazily on first Career tab open, not bundled into the main
  // player fetch — career history is a few dozen rows nobody reads on every
  // card open. null means "not fetched yet", not "fetched, empty".
  const [careerRows, setCareerRows] = useState<CareerRow[] | null>(null);
  const [careerLoading, setCareerLoading] = useState(false);
  const [careerError, setCareerError] = useState("");
  // Same lazy pattern as Career. Most players have no news at all, so paying
  // for it on every card open would be a request that returns [] nine times
  // in ten.
  const [newsRows, setNewsRows] = useState<NewsRow[] | null>(null);
  const [newsLoading, setNewsLoading] = useState(false);
  const [newsError, setNewsError] = useState("");
  const sheetRef = useRef<HTMLDivElement>(null);
  const closeRef = useRef<HTMLButtonElement>(null);

  // Fetch player detail.
  useEffect(() => {
    const ctrl = new AbortController();
    setLoading(true);
    setError("");
    setPlayer(null);
    setActiveTab("stats");
    setCareerRows(null);
    setCareerError("");
    setNewsRows(null);
    setNewsError("");
    const scoped = leagueId ? `?league=${encodeURIComponent(leagueId)}` : "";
    fetch(`${API_BASE}/api/players/${playerId}${scoped}`, { signal: ctrl.signal })
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
  }, [playerId, leagueId]);

  /** Opens the Career tab, fetching on first open and caching after —
   * mirrors Stats.tsx's togglePeriods (per-player data nobody needs until
   * they ask for it). */
  async function openCareerTab() {
    setActiveTab("career");
    if (careerRows !== null || careerLoading) return;
    setCareerLoading(true);
    setCareerError("");
    try {
      const res = await fetch(`${API_BASE}/api/players/${playerId}/career`);
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      setCareerRows((await res.json()) as CareerRow[]);
    } catch {
      setCareerError("Could not load career stats.");
    } finally {
      setCareerLoading(false);
    }
  }

  /** Opens the News tab, fetching on first open and caching after — same
   * reasoning as openCareerTab. */
  async function openNewsTab() {
    setActiveTab("news");
    if (newsRows !== null || newsLoading) return;
    setNewsLoading(true);
    setNewsError("");
    try {
      const res = await fetch(`${API_BASE}/api/players/${playerId}/news`);
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      setNewsRows((await res.json()) as NewsRow[]);
    } catch {
      setNewsError("Could not load news.");
    } finally {
      setNewsLoading(false);
    }
  }

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
                    {/* Explicitly `=== true`: null means career-sync has never
                        looked, which is not the same as "not protected". */}
                    {player.autoProtected === true && (
                      <span
                        className="pc-protect-pill"
                        title={`Auto-protected — ${player.careerNhlGames} NHL ${
                          player.careerNhlGames === 1 ? "game" : "games"
                        }, too few to be drafted away`}
                      >
                        <ShieldIcon size={12} />
                        AUTO
                      </span>
                    )}
                  </div>
                </div>
              </div>

              {player.injury != null && <InjuryBanner injury={player.injury} />}

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

              {/* Stats (season totals) / Last 10 games / Career (embedded
                  Hockey-Reference) — Stats is the default tab, since it's
                  the number most people open the card to check. */}
              <div className="pc-tabs" role="tablist" aria-label="Player stats">
                <button
                  id="pc-tab-stats"
                  role="tab"
                  type="button"
                  aria-selected={activeTab === "stats"}
                  aria-controls="pc-panel-stats"
                  className={`pc-tab${activeTab === "stats" ? " active" : ""}`}
                  onClick={() => setActiveTab("stats")}
                >
                  Stats
                </button>
                <button
                  id="pc-tab-last10"
                  role="tab"
                  type="button"
                  aria-selected={activeTab === "last10"}
                  aria-controls="pc-panel-last10"
                  className={`pc-tab${activeTab === "last10" ? " active" : ""}`}
                  onClick={() => setActiveTab("last10")}
                >
                  Last 10
                </button>
                <button
                  id="pc-tab-career"
                  role="tab"
                  type="button"
                  aria-selected={activeTab === "career"}
                  aria-controls="pc-panel-career"
                  className={`pc-tab${activeTab === "career" ? " active" : ""}`}
                  onClick={() => void openCareerTab()}
                >
                  Career
                </button>
                <button
                  id="pc-tab-news"
                  role="tab"
                  type="button"
                  aria-selected={activeTab === "news"}
                  aria-controls="pc-panel-news"
                  className={`pc-tab${activeTab === "news" ? " active" : ""}`}
                  onClick={() => void openNewsTab()}
                >
                  News
                </button>
              </div>

              <div
                id="pc-panel-stats"
                role="tabpanel"
                aria-labelledby="pc-tab-stats"
                hidden={activeTab !== "stats"}
                className="pc-tabpanel"
              >
                <div className="pc-stats-panel">
                  <span className="section-title">
                    {player.season ? `Season ${formatSeason(player.season)}` : "Season"}
                  </span>
                  {/* `season == null` means no NHL game on record at all — a
                      prospect. `seasonTotals` is never actually null from the
                      API (it sums zero rows to zero, not to nothing), so a
                      bare truthiness check here rendered a wall of "0"
                      tiles for exactly the players this message is for. */}
                  {player.season && player.seasonTotals ? (
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
                </div>
              </div>

              <div
                id="pc-panel-last10"
                role="tabpanel"
                aria-labelledby="pc-tab-last10"
                hidden={activeTab !== "last10"}
                className="pc-tabpanel"
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
                hidden={activeTab !== "career"}
                className="pc-tabpanel"
              >
                {careerLoading && <p className="pc-empty muted">Loading…</p>}
                {!careerLoading && careerError && <p className="error-banner">{careerError}</p>}
                {!careerLoading && !careerError && careerRows != null && (
                  careerRows.length === 0 ? (
                    <p className="pc-empty muted">No career stats on file yet.</p>
                  ) : (
                    <div className="pc-career-wrap">
                      <CareerTable rows={careerRows} isGoalie={player.isGoalie} />
                    </div>
                  )
                )}
              </div>

              <div
                id="pc-panel-news"
                role="tabpanel"
                aria-labelledby="pc-tab-news"
                hidden={activeTab !== "news"}
                className="pc-tabpanel"
              >
                {newsLoading && <p className="pc-empty muted">Loading…</p>}
                {!newsLoading && newsError && <p className="error-banner">{newsError}</p>}
                {!newsLoading && !newsError && newsRows != null && (
                  newsRows.length === 0 ? (
                    <p className="pc-empty muted">No news about this player.</p>
                  ) : (
                    <NewsList rows={newsRows} />
                  )
                )}
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
