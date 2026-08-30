// Hand-rolled i18n: no react-i18next, no router — this repo has four
// dependencies and no state library, so a dictionary + a context matches the
// rest of it instead of pulling in an ecosystem for two languages.
//
// Language lives on the *account* (User.Language, backend), not the browser:
// a GM who logs in from a second device should see their own last choice,
// not that device's. Pre-login (Login.tsx, before a username exists) there is
// nothing to key an account lookup on, so the guess is localStorage → the
// browser's language → English, and that guess is written to the account the
// first time a username is known (see `applyAccountLanguage`).

import { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";
import type { ReactNode } from "react";
import { api } from "../api";
import { dictionaries } from "./dictionaries";

export type Language = "en" | "fr";

type Vars = Record<string, string | number>;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export type DictValue = string | ((vars: any) => string);

const STORAGE_KEY = "fw-lang";

function detectLanguage(): Language {
  const stored = localStorage.getItem(STORAGE_KEY);
  if (stored === "en" || stored === "fr") return stored;
  return navigator.language?.toLowerCase().startsWith("fr") ? "fr" : "en";
}

function resolve(lang: Language, key: string, vars?: Vars): string {
  const [ns, leaf] = key.split(".");
  const fromLang = (dictionaries[lang] as Record<string, Record<string, DictValue> | undefined>)[ns]?.[leaf];
  const fromEn = (dictionaries.en as Record<string, Record<string, DictValue> | undefined>)[ns]?.[leaf];
  const value = fromLang ?? fromEn;
  if (value === undefined) return key;
  if (typeof value === "function") return value(vars ?? {});
  return vars ? value.replace(/\{\{(\w+)\}\}/g, (_, name) => String(vars[name] ?? "")) : value;
}

interface LanguageContextValue {
  lang: Language;
  /** Manual switch (login screen pre-account, or the Settings toggle). Persists to the account when `username` is known. */
  setLang: (lang: Language, username?: string | null) => void;
  /** Called once login succeeds. A stored account language wins over the local guess; a never-set account adopts the current guess. */
  applyAccountLanguage: (accountLanguage: string | null, username: string) => void;
  t: (key: string, vars?: Vars) => string;
}

const LanguageContext = createContext<LanguageContextValue | null>(null);

export function LanguageProvider({ children }: { children: ReactNode }) {
  const [lang, setLangState] = useState<Language>(detectLanguage);

  useEffect(() => {
    localStorage.setItem(STORAGE_KEY, lang);
  }, [lang]);

  const setLang = useCallback((next: Language, username?: string | null) => {
    setLangState(next);
    if (username) {
      // Fire-and-forget, same pattern as the presence fetch in ProfileMenu —
      // the UI already switched, nothing here should block on the network.
      api.setLanguage(username, next).catch(() => {});
    }
  }, []);

  const applyAccountLanguage = useCallback(
    (accountLanguage: string | null, username: string) => {
      if (accountLanguage === "en" || accountLanguage === "fr") {
        setLangState(accountLanguage);
      } else {
        // First login on this account: seed it with whatever we're already showing.
        setLangState((current) => {
          api.setLanguage(username, current).catch(() => {});
          return current;
        });
      }
    },
    [],
  );

  const t = useCallback((key: string, vars?: Vars) => resolve(lang, key, vars), [lang]);

  const value = useMemo(
    () => ({ lang, setLang, applyAccountLanguage, t }),
    [lang, setLang, applyAccountLanguage, t],
  );

  return <LanguageContext.Provider value={value}>{children}</LanguageContext.Provider>;
}

export function useLanguage() {
  const ctx = useContext(LanguageContext);
  if (!ctx) throw new Error("useLanguage must be used within a LanguageProvider");
  return ctx;
}
