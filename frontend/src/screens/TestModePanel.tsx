import { useEffect, useState } from "react";
import { api } from "../api";
import type { ClockStatus } from "../api";
import { useLanguage } from "../i18n/LanguageContext";

/**
 * Nick-only. Runs the sim-advance job from the phone instead of a PowerShell
 * prompt on C:\Nick\fw — see .claude/doc/testmode.md. The backend enforces
 * username == "nick" independently; this panel is reached only through
 * Settings' own isNick gate, so no second check is needed here.
 */
export function TestModePanel({
  username,
  onClose,
}: {
  username: string;
  onClose: () => void;
}) {
  const { t } = useLanguage();
  const [clock, setClock] = useState<ClockStatus | null>(null);
  const [to, setTo] = useState("");
  const [dryRun, setDryRun] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [result, setResult] = useState<{ ok: boolean; output: string } | null>(null);

  useEffect(() => {
    api.clock().then((c) => {
      setClock(c);
      setTo((prev) => prev || c.asOfDate || c.todayEt);
    });
  }, []);

  const isMultiWeekJump = (() => {
    const from = clock?.asOfDate ?? clock?.lastStatDate;
    if (!from || !to) return false;
    const days = (new Date(to).getTime() - new Date(from).getTime()) / 86_400_000;
    return days > 7;
  })();

  const advance = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!dryRun && !confirming && isMultiWeekJump) {
      setConfirming(true);
      return;
    }
    setBusy(true);
    setError("");
    setConfirming(false);
    try {
      const r = await api.testModeAdvance(username, to, dryRun);
      setResult(r);
      if (!dryRun) setClock(await api.clock());
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <form onSubmit={advance} className="card fade-in" style={{ display: "flex", flexDirection: "column", gap: "0.6rem" }}>
      <span className="section-title">{t("testModePanel.title")}</span>
      {error && <p className="error-banner">{error}</p>}

      <small className="muted">
        {clock === null
          ? t("testModePanel.loadingClock")
          : clock.simulated
            ? t("testModePanel.simulatedStatus", { date: clock.asOfDate })
            : t("testModePanel.realTime")}
      </small>

      <label>
        {t("testModePanel.advanceTo")}
        <input
          type="date"
          className="field"
          value={to}
          min={clock?.asOfDate ?? undefined}
          onChange={(e) => {
            setTo(e.target.value);
            setConfirming(false);
          }}
        />
      </label>

      <label style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
        <input
          type="checkbox"
          checked={dryRun}
          onChange={(e) => {
            setDryRun(e.target.checked);
            setConfirming(false);
          }}
        />
        {t("testModePanel.previewOnly")}
      </label>

      <small className="muted">{t("testModePanel.advanceNote")}</small>

      {confirming && <p className="error-banner">{t("testModePanel.confirmJumpWarning")}</p>}

      <div style={{ display: "flex", gap: "0.6rem" }}>
        <button type="submit" className="btn" disabled={busy || !to} style={{ flex: 1 }}>
          {busy ? t("testModePanel.advancing") : confirming ? t("testModePanel.confirmAdvance") : t("testModePanel.advance")}
        </button>
        <button type="button" className="btn-ghost" onClick={onClose}>
          {t("common.close")}
        </button>
      </div>

      {result && (
        <pre
          style={{
            whiteSpace: "pre-wrap",
            fontFamily: "ui-monospace, SFMono-Regular, monospace",
            fontSize: "0.75rem",
            maxHeight: 280,
            overflowY: "auto",
            background: "var(--surface)",
            border: "1px solid var(--border)",
            borderRadius: 12,
            padding: "0.6rem",
            margin: 0,
          }}
        >
          {result.output}
        </pre>
      )}
    </form>
  );
}
