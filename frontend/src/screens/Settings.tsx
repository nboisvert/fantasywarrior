import { useCallback, useEffect, useState } from "react";
import { api, formatCap } from "../api";
import type { LeagueDetail, LeagueSummary } from "../api";
import { LogOutIcon, MessageSquareIcon, SettingsIcon, ShieldIcon } from "../components/Icons";
import { CockmanChat } from "../components/CockmanChat";
import { LoadingLogo } from "../components/LoadingLogo";
import { RulesPanel } from "./RulesPanel";
import { TestModePanel } from "./TestModePanel";
import { useLanguage } from "../i18n/LanguageContext";

export function Settings({
  username,
  league,
  onOpen,
  onLogout,
  onRulesSaved,
}: {
  username: string;
  league: LeagueDetail | null;
  onOpen: (id: string) => void;
  onLogout: () => void;
  onRulesSaved: () => void;
}) {
  const { lang, setLang, t } = useLanguage();
  const [leagues, setLeagues] = useState<LeagueSummary[] | null>(null);
  const [name, setName] = useState("");
  const [cap, setCap] = useState("");
  const [joinCode, setJoinCode] = useState("");
  const [error, setError] = useState("");
  const [showRules, setShowRules] = useState(false);
  const [showCockman, setShowCockman] = useState(false);
  const [showTestMode, setShowTestMode] = useState(false);

  const refresh = useCallback(() => {
    api.myLeagues(username).then(setLeagues).catch((e) => setError((e as Error).message));
  }, [username]);
  useEffect(refresh, [refresh]);

  const create = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      setError("");
      const { id } = await api.createLeague(name, username, cap ? Number(cap) : null);
      setName("");
      setCap("");
      refresh();
      onOpen(id);
    } catch (err) {
      setError((err as Error).message);
    }
  };

  const join = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      setError("");
      const { id } = await api.joinLeague(joinCode.trim(), username);
      setJoinCode("");
      refresh();
      onOpen(id);
    } catch (err) {
      setError((err as Error).message);
    }
  };

  const isCommissioner = league != null && league.commissionerUsername === username;
  // Global, not per-league — the simulation applies to every league at once,
  // so this can't be folded into isCommissioner. Enforced again server-side
  // (TestModeEndpoints.cs); this gate is just what decides whether to show
  // the button at all.
  const isNick = username === "nick";

  return (
    <section className="fade-in" style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
      <span className="section-title">{t("settings.title")}</span>
      {error && <p className="error-banner">{error}</p>}

      <div style={{ display: "flex", flexDirection: "column", gap: "0.6rem" }}>
        <span className="section-title">{t("settings.myLeagues")}</span>
        {leagues === null ? (
          <LoadingLogo />
        ) : leagues.length === 0 ? (
          <p className="empty-state">{t("settings.noLeagueYet")}</p>
        ) : (
          <ul className="league-list">
            {leagues.map((l) => (
              <li key={l.id}>
                <button
                  className={`league-card${league?.id === l.id ? " active" : ""}`}
                  onClick={() => onOpen(l.id)}
                  aria-current={league?.id === l.id ? "true" : undefined}
                >
                  <span>
                    <strong>{l.name}</strong>
                    <small>{t("settings.leagueCardMeta", { members: l.members, cap: formatCap(l.capAmount) })}</small>
                  </span>
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>

      <div style={{ display: "flex", flexDirection: "column", gap: "0.6rem" }}>
        <div className="dashboard-section-head">
          <span className="section-title">{t("settings.language")}</span>
          <div className="lang-switch" role="group" aria-label="Language / Langue">
            <button
              type="button"
              className={`lang-switch-btn${lang === "en" ? " active" : ""}`}
              onClick={() => setLang("en", username)}
              aria-pressed={lang === "en"}
            >
              {t("common.languageEn")}
            </button>
            <button
              type="button"
              className={`lang-switch-btn${lang === "fr" ? " active" : ""}`}
              onClick={() => setLang("fr", username)}
              aria-pressed={lang === "fr"}
            >
              {t("common.languageFr")}
            </button>
          </div>
        </div>
      </div>

      <form onSubmit={create} className="card" style={{ display: "flex", flexDirection: "column", gap: "0.6rem" }}>
        <span className="section-title">{t("settings.createLeague")}</span>
        <input
          className="field"
          value={name}
          onChange={(e) => setName(e.target.value)}
          placeholder={t("settings.leagueNamePlaceholder")}
          aria-label={t("settings.leagueNameAria")}
        />
        <input
          className="field"
          value={cap}
          onChange={(e) => setCap(e.target.value.replace(/\D/g, ""))}
          placeholder={t("settings.capPlaceholder")}
          inputMode="numeric"
          aria-label={t("settings.capAria")}
        />
        <button type="submit" className="btn" disabled={!name.trim()}>
          {t("settings.create")}
        </button>
      </form>

      <form onSubmit={join} className="card" style={{ display: "flex", flexDirection: "column", gap: "0.6rem" }}>
        <span className="section-title">{t("settings.joinLeague")}</span>
        <input
          className="field"
          value={joinCode}
          onChange={(e) => setJoinCode(e.target.value)}
          placeholder={t("settings.inviteCodePlaceholder")}
          aria-label={t("settings.inviteCodeAria")}
        />
        <button type="submit" className="btn" disabled={!joinCode.trim()}>
          {t("settings.join")}
        </button>
      </form>

      {isCommissioner && league && (
        <div style={{ display: "flex", flexDirection: "column", gap: "0.6rem" }}>
          <div className="dashboard-section-head">
            <span className="section-title">
              <SettingsIcon size={14} className="inline-icon" /> {t("settings.leagueRules", { name: league.name })}
            </span>
            <button
              type="button"
              className="btn-ghost"
              onClick={() => setShowRules(!showRules)}
              aria-expanded={showRules}
            >
              {showRules ? t("common.hide") : t("common.edit")}
            </button>
          </div>
          {showRules && (
            <RulesPanel
              league={league}
              username={username}
              onSaved={onRulesSaved}
              onClose={() => setShowRules(false)}
            />
          )}
        </div>
      )}

      {isCommissioner && league && (
        <div style={{ display: "flex", flexDirection: "column", gap: "0.6rem" }}>
          <div className="dashboard-section-head">
            <span className="section-title">
              <MessageSquareIcon size={14} className="inline-icon" /> {t("settings.commissionerSupport")}
            </span>
            <button type="button" className="btn-ghost" onClick={() => setShowCockman(true)}>
              {t("settings.chatWithCockman")}
            </button>
          </div>
        </div>
      )}

      {showCockman && league && (
        <CockmanChat league={league} onClose={() => setShowCockman(false)} />
      )}

      {isNick && (
        <div style={{ display: "flex", flexDirection: "column", gap: "0.6rem" }}>
          <div className="dashboard-section-head">
            <span className="section-title">
              <ShieldIcon size={14} className="inline-icon" /> {t("settings.testMode")}
            </span>
            <button
              type="button"
              className="btn-ghost"
              onClick={() => setShowTestMode(!showTestMode)}
              aria-expanded={showTestMode}
            >
              {showTestMode ? t("common.hide") : t("common.open")}
            </button>
          </div>
          {showTestMode && (
            <TestModePanel username={username} onClose={() => setShowTestMode(false)} />
          )}
        </div>
      )}

      <button className="btn-ghost" onClick={onLogout} style={{ alignSelf: "flex-start" }}>
        <LogOutIcon size={16} /> {t("settings.logOut", { username })}
      </button>
    </section>
  );
}
