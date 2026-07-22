import { useCallback, useEffect, useState } from "react";
import { api, formatCap } from "./api";
import type { LeagueDetail, LeagueSummary, PlayerDto } from "./api";
import "./App.css";

export default function App() {
  const [username, setUsername] = useState<string | null>(localStorage.getItem("fw-username"));
  const [leagueId, setLeagueId] = useState<string | null>(null);

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
    return (
      <Leagues
        username={username}
        onOpen={setLeagueId}
        onLogout={() => {
          localStorage.removeItem("fw-username");
          setUsername(null);
        }}
      />
    );
  return <LeagueView leagueId={leagueId} username={username} onBack={() => setLeagueId(null)} />;
}

function Login({ onLogin }: { onLogin: (username: string) => void }) {
  const [value, setValue] = useState("");
  const [error, setError] = useState("");

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      const user = await api.login(value);
      onLogin(user.username);
    } catch (err) {
      setError((err as Error).message);
    }
  };

  return (
    <main className="screen">
      <h1>Fantasy Warrior</h1>
      <form onSubmit={submit} className="stack">
        <input
          value={value}
          onChange={(e) => setValue(e.target.value)}
          placeholder="Username"
          autoFocus
        />
        <button type="submit" disabled={value.trim().length < 2}>
          Enter
        </button>
        {error && <p className="error">{error}</p>}
      </form>
    </main>
  );
}

function Leagues({
  username,
  onOpen,
  onLogout,
}: {
  username: string;
  onOpen: (id: string) => void;
  onLogout: () => void;
}) {
  const [leagues, setLeagues] = useState<LeagueSummary[]>([]);
  const [name, setName] = useState("");
  const [cap, setCap] = useState("");
  const [joinCode, setJoinCode] = useState("");
  const [error, setError] = useState("");

  const refresh = useCallback(() => {
    api.myLeagues(username).then(setLeagues).catch((e) => setError(e.message));
  }, [username]);
  useEffect(refresh, [refresh]);

  const create = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      const { id } = await api.createLeague(name, username, cap ? Number(cap) : null);
      onOpen(id);
    } catch (err) {
      setError((err as Error).message);
    }
  };

  const join = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      const { id } = await api.joinLeague(joinCode.trim(), username);
      onOpen(id);
    } catch (err) {
      setError((err as Error).message);
    }
  };

  return (
    <main className="screen">
      <header className="bar">
        <h1>My leagues</h1>
        <button className="link" onClick={onLogout}>
          {username} · logout
        </button>
      </header>
      {error && <p className="error">{error}</p>}
      <ul className="list">
        {leagues.map((l) => (
          <li key={l.id}>
            <button className="card" onClick={() => onOpen(l.id)}>
              <strong>{l.name}</strong>
              <span>
                {l.members} member{l.members > 1 ? "s" : ""} · cap {formatCap(l.capAmount)}
              </span>
            </button>
          </li>
        ))}
        {leagues.length === 0 && <li className="muted">No league yet — create or join one.</li>}
      </ul>
      <form onSubmit={create} className="stack boxed">
        <h2>Create a league</h2>
        <input value={name} onChange={(e) => setName(e.target.value)} placeholder="League name" />
        <input
          value={cap}
          onChange={(e) => setCap(e.target.value.replace(/\D/g, ""))}
          placeholder="Salary cap in $ (optional)"
          inputMode="numeric"
        />
        <button type="submit" disabled={!name.trim()}>
          Create
        </button>
      </form>
      <form onSubmit={join} className="stack boxed">
        <h2>Join a league</h2>
        <input
          value={joinCode}
          onChange={(e) => setJoinCode(e.target.value)}
          placeholder="Invite code"
        />
        <button type="submit" disabled={!joinCode.trim()}>
          Join
        </button>
      </form>
    </main>
  );
}

function LeagueView({
  leagueId,
  username,
  onBack,
}: {
  leagueId: string;
  username: string;
  onBack: () => void;
}) {
  const [league, setLeague] = useState<LeagueDetail | null>(null);
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<PlayerDto[]>([]);
  const [error, setError] = useState("");

  const refresh = useCallback(() => {
    api.league(leagueId).then(setLeague).catch((e) => setError(e.message));
  }, [leagueId]);
  useEffect(refresh, [refresh]);

  useEffect(() => {
    if (query.trim().length < 2) {
      setResults([]);
      return;
    }
    const t = setTimeout(() => api.searchPlayers(query).then(setResults).catch(() => {}), 250);
    return () => clearTimeout(t);
  }, [query]);

  if (!league)
    return <main className="screen">{error ? <p className="error">{error}</p> : "Loading…"}</main>;

  const overCap = (capTotal: number) => league.capAmount != null && capTotal > league.capAmount;

  const add = async (playerId: number) => {
    try {
      setError("");
      await api.addPlayer(leagueId, username, playerId);
      setQuery("");
      refresh();
    } catch (err) {
      setError((err as Error).message);
    }
  };

  const remove = async (playerId: number) => {
    try {
      await api.removePlayer(leagueId, username, playerId);
      refresh();
    } catch (err) {
      setError((err as Error).message);
    }
  };

  return (
    <main className="screen">
      <header className="bar">
        <button className="link" onClick={onBack}>
          ← Leagues
        </button>
        <h1>{league.name}</h1>
      </header>
      <p className="muted">
        Season {league.season} · cap {formatCap(league.capAmount)} · invite code:{" "}
        <code>{league.id}</code>
      </p>
      {error && <p className="error">{error}</p>}

      <div className="stack boxed">
        <h2>Add a player to my team</h2>
        <input
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder="Search players (e.g. Suzuki)"
        />
        {results.length > 0 && (
          <ul className="list">
            {results.map((p) => (
              <li key={p.id}>
                <button className="card row" onClick={() => add(p.id)}>
                  <span>
                    {p.name}{" "}
                    <small>
                      ({p.position} · {p.team})
                    </small>
                  </span>
                  <span>{formatCap(p.capHit)}</span>
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>

      {league.teams.map((team) => (
        <section key={team.ownerUsername} className="boxed">
          <h2 className="row">
            <span>
              {team.name} <small>@{team.ownerUsername}</small>
            </span>
            <span className={overCap(team.capTotal) ? "error" : ""}>{formatCap(team.capTotal)}</span>
          </h2>
          {team.players.length === 0 && <p className="muted">Empty roster.</p>}
          <ul className="list">
            {team.players.map((p) => (
              <li key={p.id} className="card row">
                <span>
                  {p.name}{" "}
                  <small>
                    ({p.position} · {p.team})
                  </small>
                </span>
                <span>
                  {formatCap(p.capHit)}
                  {team.ownerUsername === username && (
                    <button className="link danger" onClick={() => remove(p.id)}>
                      ✕
                    </button>
                  )}
                </span>
              </li>
            ))}
          </ul>
        </section>
      ))}
    </main>
  );
}
