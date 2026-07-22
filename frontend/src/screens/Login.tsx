import { useState } from "react";
import { api } from "../api";
import { ShieldIcon } from "../components/Icons";

export function Login({ onLogin }: { onLogin: (username: string) => void }) {
  const [value, setValue] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError("");
    try {
      const user = await api.login(value);
      onLogin(user.username);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <main className="login fade-in">
      <div className="login-logo">
        <div className="icon-ring">
          <ShieldIcon size={40} />
        </div>
        <div>
          <h1>
            Fantasy <span className="accent">Warrior</span>
          </h1>
          <p className="tagline">Your pool. Your ice. Your bragging rights.</p>
        </div>
      </div>
      <form onSubmit={submit}>
        <label htmlFor="username" className="section-title">
          Who's playing?
        </label>
        <input
          id="username"
          className="field"
          value={value}
          onChange={(e) => setValue(e.target.value)}
          placeholder="Username"
          autoComplete="username"
          autoFocus
        />
        <button type="submit" className="btn" disabled={busy || value.trim().length < 2}>
          {busy ? "Entering…" : "Hit the ice"}
        </button>
        {error && <p className="error-banner">{error}</p>}
      </form>
    </main>
  );
}
