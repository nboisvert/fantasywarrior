import { useState } from "react";
import { api } from "../api";
import type { LeagueDetail, RuleConfig } from "../api";

/**
 * Commissioner-only league scoring settings. Point values + top X per
 * position group (empty = every player counts).
 */
export function RulesPanel({
  league,
  username,
  onSaved,
  onClose,
}: {
  league: LeagueDetail;
  username: string;
  onSaved: () => void;
  onClose: () => void;
}) {
  const [config, setConfig] = useState<RuleConfig>(structuredClone(league.ruleConfig));
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  const setPoint = (key: keyof RuleConfig["pointValues"], value: string) =>
    setConfig({
      ...config,
      pointValues: { ...config.pointValues, [key]: Number(value) || 0 },
    });

  const setTop = (key: keyof RuleConfig["topCount"], value: string) =>
    setConfig({
      ...config,
      topCount: { ...config.topCount, [key]: value === "" ? null : Math.max(0, Number(value) || 0) },
    });

  const setRosterSize = (key: keyof RuleConfig["rosterSize"], value: string) =>
    setConfig({
      ...config,
      rosterSize: { ...config.rosterSize, [key]: value === "" ? null : Math.max(0, Number(value) || 0) },
    });

  const save = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError("");
    try {
      await api.updateRules(league.id, username, config);
      onSaved();
      onClose();
    } catch (err) {
      setError((err as Error).message);
      setBusy(false);
    }
  };

  const points: [keyof RuleConfig["pointValues"], string][] = [
    ["goal", "Goal"],
    ["assist", "Assist"],
    ["goalieWin", "Goalie win"],
    ["goalieOtLoss", "Goalie OT loss"],
    ["shutout", "Shutout"],
  ];
  const tops: [keyof RuleConfig["topCount"], string][] = [
    ["forwards", "Active forwards"],
    ["defense", "Active defense"],
    ["goalies", "Active goalies"],
  ];
  const rosterSizes: [keyof RuleConfig["rosterSize"], string][] = [
    ["min", "Min roster size"],
    ["max", "Max roster size"],
  ];

  return (
    <form onSubmit={save} className="card fade-in" style={{ display: "flex", flexDirection: "column", gap: "0.9rem" }}>
      <span className="section-title">League rules (commissioner)</span>
      {error && <p className="error-banner">{error}</p>}

      <span className="section-title" style={{ letterSpacing: "0.05em" }}>Point values</span>
      <div className="rules-grid">
        {points.map(([key, label]) => (
          <label key={key}>
            {label}
            <input
              className="field"
              inputMode="decimal"
              value={config.pointValues[key]}
              onChange={(e) => setPoint(key, e.target.value)}
            />
          </label>
        ))}
      </div>

      <span className="section-title" style={{ letterSpacing: "0.05em" }}>
        Weekly lineup slots
      </span>
      <small className="muted">
        How many players each GM may activate per position. Only active players score.
      </small>
      <div className="rules-grid">
        {tops.map(([key, label]) => (
          <label key={key}>
            {label}
            <input
              className="field"
              inputMode="numeric"
              placeholder="all"
              value={config.topCount[key] ?? ""}
              onChange={(e) => setTop(key, e.target.value)}
            />
          </label>
        ))}
      </div>

      <span className="section-title" style={{ letterSpacing: "0.05em" }}>
        Roster size (empty = no limit)
      </span>
      <small className="muted">Enforced on trades, when proposed and when accepted.</small>
      <div className="rules-grid">
        {rosterSizes.map(([key, label]) => (
          <label key={key}>
            {label}
            <input
              className="field"
              inputMode="numeric"
              placeholder="no limit"
              value={config.rosterSize[key] ?? ""}
              onChange={(e) => setRosterSize(key, e.target.value)}
            />
          </label>
        ))}
      </div>

      <span className="section-title" style={{ letterSpacing: "0.05em" }}>
        Unsigned players
      </span>
      <small className="muted">
        What a player with no contract on file counts against the cap. Unsigned free
        agents and undrafted prospects never get a salary — set 0 to carry them for free.
      </small>
      <div className="rules-grid">
        <label>
          Default cap hit
          <input
            className="field"
            inputMode="numeric"
            value={config.defaultCapHit}
            onChange={(e) =>
              setConfig({ ...config, defaultCapHit: Math.max(0, Number(e.target.value) || 0) })
            }
          />
        </label>
      </div>

      <p className="muted" style={{ margin: 0, fontSize: "0.8rem" }}>
        Applies from the next nightly scoring run. Weeks already scored keep the scale
        they were played under — changing the rules mid-season does not restate history.
      </p>
      <div style={{ display: "flex", gap: "0.6rem" }}>
        <button type="submit" className="btn" disabled={busy} style={{ flex: 1 }}>
          {busy ? "Saving…" : "Save rules"}
        </button>
        <button type="button" className="btn-ghost" onClick={onClose}>
          Cancel
        </button>
      </div>
    </form>
  );
}
