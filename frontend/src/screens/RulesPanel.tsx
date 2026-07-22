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
    ["forwards", "Top forwards"],
    ["defense", "Top defense"],
    ["goalies", "Top goalies"],
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
        Players counting toward the score (empty = all)
      </span>
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

      <p className="muted" style={{ margin: 0, fontSize: "0.8rem" }}>
        Scores refresh at the next nightly calculation.
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
