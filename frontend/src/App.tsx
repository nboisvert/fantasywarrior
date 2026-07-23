import { useCallback, useEffect, useState } from "react";
import { api, formatSeason } from "./api";
import type { LeagueDetail } from "./api";
import {
  ActivityIcon,
  ArrowLeftRightIcon,
  ChevronDownIcon,
  HomeIcon,
  SettingsIcon,
  TrophyIcon,
  UsersIcon,
} from "./components/Icons";
import { LoadingLogo } from "./components/LoadingLogo";
import logo from "./assets/logo.webp";
import { Login } from "./screens/Login";
import { LeagueGate } from "./screens/LeagueGate";
import { Dashboard } from "./screens/Dashboard";
import { Standings } from "./screens/Standings";
import { Roster } from "./screens/Roster";
import { Stats } from "./screens/Stats";
import { Trades } from "./screens/Trades";
import { Settings } from "./screens/Settings";
import { NewsTicker } from "./components/NewsTicker";
import "./App.css";

type Tab = "dashboard" | "standings" | "roster" | "stats" | "trades" | "settings";

export default function App() {
  const [username, setUsername] = useState<string | null>(localStorage.getItem("fw-username"));
  const [leagueId, setLeagueId] = useState<string | null>(localStorage.getItem("fw-league"));
  const [league, setLeague] = useState<LeagueDetail | null>(null);
  const [tab, setTab] = useState<Tab>("dashboard");
  const [showPicker, setShowPicker] = useState(false);
  const [defaultChecked, setDefaultChecked] = useState(false);
  const [error, setError] = useState("");

  const openLeague = (id: string) => {
    localStorage.setItem("fw-league", id);
    setLeague(null);
    setLeagueId(id);
    setTab("dashboard");
    setShowPicker(false);
  };

  const logout = () => {
    localStorage.removeItem("fw-username");
    localStorage.removeItem("fw-league");
    setUsername(null);
    setLeagueId(null);
    setLeague(null);
    setShowPicker(false);
    setDefaultChecked(false);
    setTab("dashboard");
  };

  const refreshLeague = useCallback(() => {
    if (!leagueId) return;
    api
      .league(leagueId)
      .then((detail) => {
        setLeague(detail);
        setError("");
      })
      .catch((e) => setError((e as Error).message));
  }, [leagueId]);
  useEffect(refreshLeague, [refreshLeague]);

  // First-time landing logic: with no remembered league, decide where returning
  // vs. brand-new users go — one league auto-selects it, several open the
  // picker, zero sends the user straight to Settings (create/join lives there).
  useEffect(() => {
    if (!username || leagueId || defaultChecked) return;
    api
      .myLeagues(username)
      .then((list) => {
        if (list.length === 0) {
          setTab("settings");
        } else if (list.length === 1) {
          openLeague(list[0].id);
        } else {
          setShowPicker(true);
        }
        setDefaultChecked(true);
      })
      .catch((e) => {
        setError((e as Error).message);
        setDefaultChecked(true);
      });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [username, leagueId, defaultChecked]);

  if (!username)
    return (
      <Login
        onLogin={(u) => {
          localStorage.setItem("fw-username", u);
          setUsername(u);
        }}
      />
    );

  if (showPicker)
    return (
      <LeagueGate
        username={username}
        onOpen={openLeague}
        onLogout={logout}
        onGoSettings={() => {
          setShowPicker(false);
          setTab("settings");
        }}
        onClose={leagueId ? () => setShowPicker(false) : undefined}
      />
    );

  return (
    <div className="shell">
      <header className="topbar">
        <img className="topbar-logo" src={logo} alt="" />
        <button className="league-switch" onClick={() => setShowPicker(true)} aria-label="Switch league">
          <span className="league-switch-text">
            <span className="name">{league?.name ?? (leagueId ? "…" : "Select league")}</span>
            {league && <span className="season">{formatSeason(league.season)}</span>}
          </span>
          <ChevronDownIcon size={16} />
        </button>
        <span className="muted" style={{ fontSize: "0.85rem" }}>
          {username}
        </span>
        <button className="topbar-icon-btn" onClick={() => setTab("settings")} aria-label="Settings">
          <SettingsIcon size={20} />
        </button>
      </header>

      <main className="shell-content" style={{ paddingTop: "1rem" }}>
        {error && <p className="error-banner">{error}</p>}

        {tab === "settings" && (
          <Settings
            username={username}
            league={league}
            onOpen={openLeague}
            onLogout={logout}
            onRulesSaved={refreshLeague}
          />
        )}

        {tab !== "settings" && !leagueId && (
          <p className="empty-state">
            No league selected yet. Head to{" "}
            <button className="link-btn" onClick={() => setTab("settings")}>
              Settings
            </button>{" "}
            to create or join one.
          </p>
        )}
        {tab !== "settings" && leagueId && !league && !error && <LoadingLogo label="Loading league…" />}
        {league && tab === "dashboard" && <Dashboard league={league} username={username} />}
        {league && tab === "standings" && <Standings league={league} username={username} />}
        {league && tab === "roster" && (
          <Roster league={league} username={username} />
        )}
        {league && tab === "stats" && <Stats league={league} username={username} />}
        {league && tab === "trades" && <Trades league={league} username={username} />}
      </main>

      <NewsTicker leagueId={leagueId} />

      <nav className="bottom-nav" aria-label="Main navigation">
        <button
          className={`nav-tab${tab === "dashboard" ? " active" : ""}`}
          onClick={() => setTab("dashboard")}
          aria-current={tab === "dashboard" ? "page" : undefined}
        >
          <HomeIcon size={22} />
          Dashboard
        </button>
        <button
          className={`nav-tab${tab === "standings" ? " active" : ""}`}
          onClick={() => setTab("standings")}
          aria-current={tab === "standings" ? "page" : undefined}
        >
          <TrophyIcon size={22} />
          Standings
        </button>
        <button
          className={`nav-tab${tab === "roster" ? " active" : ""}`}
          onClick={() => setTab("roster")}
          aria-current={tab === "roster" ? "page" : undefined}
        >
          <UsersIcon size={22} />
          Roster
        </button>
        <button
          className={`nav-tab${tab === "stats" ? " active" : ""}`}
          onClick={() => setTab("stats")}
          aria-current={tab === "stats" ? "page" : undefined}
        >
          <ActivityIcon size={22} />
          Stats
        </button>
        <button
          className={`nav-tab${tab === "trades" ? " active" : ""}`}
          onClick={() => setTab("trades")}
          aria-current={tab === "trades" ? "page" : undefined}
        >
          <ArrowLeftRightIcon size={22} />
          Trades
        </button>
      </nav>
    </div>
  );
}
