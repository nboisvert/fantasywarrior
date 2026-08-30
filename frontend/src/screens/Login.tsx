import { useState } from "react";
import { api } from "../api";
import logo from "../assets/logo.webp";
import { useLanguage } from "../i18n/LanguageContext";

export function Login({ onLogin }: { onLogin: (username: string) => void }) {
  const [value, setValue] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const { lang, setLang, applyAccountLanguage, t } = useLanguage();

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError("");
    try {
      const user = await api.login(value);
      applyAccountLanguage(user.language, user.username);
      onLogin(user.username);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <main className="login fade-in">
      {/* No username yet, so this is the only place a language choice can be
          made outside an account — see LanguageContext for how it's reconciled
          against the account's own saved choice right after login. */}
      <div className="lang-switch" role="group" aria-label="Language / Langue">
        <button
          type="button"
          className={`lang-switch-btn${lang === "en" ? " active" : ""}`}
          onClick={() => setLang("en")}
          aria-pressed={lang === "en"}
        >
          {t("common.languageEn")}
        </button>
        <button
          type="button"
          className={`lang-switch-btn${lang === "fr" ? " active" : ""}`}
          onClick={() => setLang("fr")}
          aria-pressed={lang === "fr"}
        >
          {t("common.languageFr")}
        </button>
      </div>
      <div className="login-logo">
        <img className="hero-logo" src={logo} alt={t("login.logoAlt")} />
        <div>
          <h1>
            Fantasy <span className="accent">Warrior</span>
          </h1>
          <p className="tagline">{t("login.tagline")}</p>
        </div>
      </div>
      <form onSubmit={submit}>
        <label htmlFor="username" className="section-title">
          {t("login.whoPlaying")}
        </label>
        <input
          id="username"
          className="field"
          value={value}
          onChange={(e) => setValue(e.target.value)}
          placeholder={t("login.usernamePlaceholder")}
          autoComplete="username"
          autoFocus
        />
        <button type="submit" className="btn" disabled={busy || value.trim().length < 2}>
          {busy ? t("login.entering") : t("login.hitTheIce")}
        </button>
        {error && <p className="error-banner">{error}</p>}
      </form>
    </main>
  );
}
