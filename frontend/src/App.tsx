import { useCallback, useEffect, useState } from "react";
import { api } from "./api";
import type { LeagueDetail } from "./api";
import { ChevronDownIcon, LogOutIcon, SettingsIcon, TrophyIcon, UsersIcon } from "./components/Icons";
import logo from "./assets/logo.webp";
import { Login } from "./screens/Login";
import { LeagueGate } from "./screens/LeagueGate";
import { Standings } from "./screens/Standings";
import { Roster } from "./screens/Roster";
import { RulesPanel } from "./screens/RulesPanel";
import "./App.css";

type Tab = "standings" | "roster";

export default function App() {
  const [username, setUsername] = useState<string | null>(localStorage.getItem("fw-username"));
  const [leagueId, setLeagueId] = useState<string | null>(localStorage.getItem("fw-league"));
  const [league, setLeague] = useState<LeagueDetail | null>(null);
  const [tab, setTab] = useState<Tab>("standings");
  const [showRules, setShowRules] = useState(false);
  const [error, setError] = useState("");

  const openLeague = (id: string) => {
    localStorage.setItem("fw-league", id);
    setLeague(null);
    setLeagueId(id);
    setTab("standings");
  };

  const closeLeague = () => {
    localStorage.removeItem("fw-league");
    setLeagueId(null);
    setLeague(null);
  };

  const logout = () => {
    localStorage.removeItem("fw-username");
    closeLeague();
    setUsername(null);
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

  if (!username)
    return (
      <Login
        onLogin={(u) => {
          localStorage.setItem("fw-username", u);
          setUsername(u);
        }}
      />
    );

  if (!leagueId)
    return <LeagueGate username={username} onOpen={openLeague} onLogout={logout} />;

  return (
    <div className="shell">
      <header className="topbar">
        <img className="topbar-logo" src={logo} alt="" />
        <button className="league-switch" onClick={closeLeague} aria-label="Switch league">
          <span className="name">{league?.name ?? "…"}</span>
          <ChevronDownIcon size={16} />
        </button>
        <span style={{ display: "inline-flex", alignItems: "center", gap: "0.35rem" }}>
          {league?.commissionerUsername === username && (
            <button
              className="icon-btn"
              onClick={() => setShowRules(!showRules)}
              aria-label="League rules"
              style={showRules ? { color: "var(--ice-bright)" } : undefined}
            >
              <SettingsIcon size={18} />
            </button>
          )}
          <button className="btn-ghost" onClick={logout}>
            <LogOutIcon size={16} /> {username}
          </button>
        </span>
      </header>

      <main className="shell-content" style={{ paddingTop: "1rem" }}>
        {error && <p className="error-banner">{error}</p>}
        {!league && !error && <p className="empty-state">Loading league…</p>}
        {league && showRules && (
          <RulesPanel
            league={league}
            username={username}
            onSaved={refreshLeague}
            onClose={() => setShowRules(false)}
          />
        )}
        {league && tab === "standings" && <Standings league={league} username={username} />}
        {league && tab === "roster" && (
          <Roster league={league} username={username} onChanged={refreshLeague} />
        )}
      </main>

      <nav className="bottom-nav" aria-label="Main navigation">
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
      </nav>
    </div>
  );
}
