// CockmanChat — a pure UI/visual mock (2026-07-27, per Nick): "Garry Cockman,"
// a parody AI-mascot chatbot, "President" of the league, produced and
// sponsored by Fantasy Warrior. No backend, no real AI, no real token system —
// everything below is scripted/local state, evaluated on visual quality only.
//
// The whole point of this component is to deliberately CLASH with the rest of
// the app's "Night Arena" theme: it's meant to read as a real embedded 3rd-party
// corporate helpdesk widget (Intercom/Zendesk/Drift-style) bolted onto the app,
// not a native screen. CockmanChat.css is fully self-contained — it never
// references index.css's custom properties, on purpose.
//
// Reuses two existing patterns rather than inventing new ones:
//   - the modal shell mechanics of PlayerCard.tsx (overlay, focus trap,
//     Escape/backdrop close, scroll lock, focus restore) — behavior only.
//   - the scripted mock-chat state of ProfileMenu.tsx (local-only thread,
//     typed replies appended locally, a visible "not real" disclosure note).

import { useEffect, useRef, useState } from "react";
import type { KeyboardEvent as ReactKeyboardEvent, MouseEvent as ReactMouseEvent } from "react";
import type { LeagueDetail } from "../api";
import cockmanAvatar from "../assets/cockman.png";
import { useLanguage } from "../i18n/LanguageContext";
import { CockcoinIcon, SendIcon, XIcon } from "./Icons";
import "./CockmanChat.css";

interface ChatMessage {
  id: string;
  fromCockman: boolean;
  text: string;
  withCoin?: boolean;
}

/** Fixed intro script — one Cockman, one script, always the same order
 * (unlike ProfileMenu's per-GM hash-seeded pick, there's nothing to vary
 * here). The last line is the "bonus entry" hook (2026-07-27, per Nick):
 * cockcoin is earned toward in-app interaction and unlocks exclusive content
 * — the three-word question is the first of what should become a small
 * library of bonus-entry prompts. Script text itself comes from the
 * cockmanChat dictionary (gamer-tone flavour text, translated in full, not
 * external content) — `t()` is threaded through instead of `{league}`/
 * `{pooler}` placeholders now that substitution happens per-language. */
function buildInitialThread(
  leagueName: string,
  poolerName: string,
  t: (key: string, vars?: Record<string, string>) => string,
): ChatMessage[] {
  return [
    { id: "script-0", fromCockman: true, text: t("cockmanChat.intro", { league: leagueName }) },
    { id: "script-1", fromCockman: true, text: t("cockmanChat.reminder"), withCoin: true },
    { id: "script-2", fromCockman: true, text: t("cockmanChat.balance"), withCoin: true },
    { id: "script-3", fromCockman: true, text: t("cockmanChat.howToEarn"), withCoin: true },
    { id: "script-4", fromCockman: true, text: t("cockmanChat.bonusEntry", { pooler: poolerName }), withCoin: true },
  ];
}

/** Picks a stand-in fellow pooler name for the bonus-entry question. Falls
 * back to `t("cockmanChat.yourself")` if the league has no teams yet (e.g.
 * brand-new league). */
function pickPoolerName(league: LeagueDetail, yourself: string): string {
  const names = league.teams.map((t) => t.ownerUsername).filter(Boolean);
  if (names.length === 0) return yourself;
  const raw = names[Math.floor(Math.random() * names.length)];
  return raw.charAt(0).toUpperCase() + raw.slice(1);
}

/** Renders message text with every "cockcoin" mention followed by the coin
 * icon inline — keeps the SCRIPT array as plain strings while still letting
 * the icon sit right next to the word, per the "candy crush gold coin next
 * to token mentions" ask. */
function MessageText({ text, withCoin }: { text: string; withCoin?: boolean }) {
  if (!withCoin) return <>{text}</>;
  const parts = text.split(/(cockcoin)/g);
  return (
    <>
      {parts.map((part, i) =>
        part === "cockcoin" ? (
          <span key={i} className="gc-coin-mention">
            cockcoin <CockcoinIcon size={16} className="gc-coin-icon" />
          </span>
        ) : (
          <span key={i}>{part}</span>
        ),
      )}
    </>
  );
}

export function CockmanChat({ league, onClose }: { league: LeagueDetail; onClose: () => void }) {
  const { t } = useLanguage();
  const sheetRef = useRef<HTMLDivElement>(null);
  const closeRef = useRef<HTMLButtonElement>(null);
  const threadRef = useRef<HTMLUListElement>(null);
  const [poolerName] = useState(() => pickPoolerName(league, t("cockmanChat.yourself")));
  const [thread, setThread] = useState<ChatMessage[]>(() => buildInitialThread(league.name, poolerName, t));
  const [draft, setDraft] = useState("");
  const [busy, setBusy] = useState(false);
  // True until the bonus-entry question (last script line) gets its first
  // reply — that one reply is coin-themed; every message after reverts to
  // the generic deflection.
  const [bonusPending, setBonusPending] = useState(true);

  // Keep the thread scrolled to its latest message — including the initial
  // scripted script itself, so the bonus-entry question at the end (the
  // whole point of this chat) is visible without the user having to scroll.
  useEffect(() => {
    const el = threadRef.current;
    if (el) el.scrollTop = el.scrollHeight;
  }, [thread]);

  // Escape closes.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  // Lock body scroll while open; move focus in, restore it on close.
  useEffect(() => {
    const prevOverflow = document.body.style.overflow;
    const prevFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    document.body.style.overflow = "hidden";
    closeRef.current?.focus();
    return () => {
      document.body.style.overflow = prevOverflow;
      prevFocus?.focus();
    };
  }, []);

  const trapFocus = (e: ReactKeyboardEvent<HTMLDivElement>) => {
    if (e.key !== "Tab" || !sheetRef.current) return;
    const focusables = sheetRef.current.querySelectorAll<HTMLElement>(
      'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])',
    );
    if (focusables.length === 0) return;
    const first = focusables[0];
    const last = focusables[focusables.length - 1];
    if (e.shiftKey && document.activeElement === first) {
      e.preventDefault();
      last.focus();
    } else if (!e.shiftKey && document.activeElement === last) {
      e.preventDefault();
      first.focus();
    }
  };

  const onBackdrop = (e: ReactMouseEvent<HTMLDivElement>) => {
    if (e.target === e.currentTarget) onClose();
  };

  const sendMessage = () => {
    const text = draft.trim();
    if (!text || busy) return;
    setThread((prev) => [...prev, { id: `mine-${Date.now()}`, fromCockman: false, text }]);
    setDraft("");
    setBusy(true);
    const replyText = bonusPending ? t("cockmanChat.bonusReply") : t("cockmanChat.autoReply");
    window.setTimeout(() => {
      setThread((prev) => [...prev, { id: `reply-${Date.now()}`, fromCockman: true, text: replyText, withCoin: true }]);
      setBusy(false);
      setBonusPending(false);
    }, 900);
  };

  return (
    <div className="gc-overlay" onClick={onBackdrop}>
      <div
        ref={sheetRef}
        className="gc-sheet"
        role="dialog"
        aria-modal="true"
        aria-labelledby="gc-header-name"
        onKeyDown={trapFocus}
      >
        <div className="gc-header">
          <img className="gc-avatar" src={cockmanAvatar} alt="" />
          <div className="gc-header-info">
            <span id="gc-header-name" className="gc-header-name">
              Garry Cockman
              <span className="gc-online-dot" aria-hidden="true" />
            </span>
            <span className="gc-header-sub">{t("cockmanChat.headerSub", { league: league.name })}</span>
          </div>
          <button ref={closeRef} className="gc-close" onClick={onClose} aria-label={t("cockmanChat.closeAria")}>
            <XIcon size={18} />
          </button>
        </div>

        <p className="gc-mock-note">{t("cockmanChat.mockNote")}</p>

        <ul className="gc-thread" ref={threadRef}>
          {thread.map((msg) => (
            <li key={msg.id} className={`gc-bubble${msg.fromCockman ? "" : " gc-bubble-mine"}`}>
              <MessageText text={msg.text} withCoin={msg.withCoin} />
            </li>
          ))}
        </ul>

        <div className="gc-composer">
          <input
            className="gc-input"
            placeholder={t("cockmanChat.messagePlaceholder")}
            value={draft}
            onChange={(e) => setDraft(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter") sendMessage();
            }}
            aria-label={t("cockmanChat.messageAria")}
          />
          <button className="gc-send" onClick={sendMessage} disabled={!draft.trim()} aria-label={t("cockmanChat.send")}>
            <SendIcon size={16} />
          </button>
        </div>

        <div className="gc-footer">{t("cockmanChat.footer")}</div>
      </div>
    </div>
  );
}
