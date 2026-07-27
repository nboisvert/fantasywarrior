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
import cockmanAvatar from "../assets/cockman.png";
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
 * here). `{league}` is substituted with the actual league name. */
const SCRIPT: { text: string; withCoin?: boolean }[] = [
  {
    text:
      "Hi there. Garry Cockman here — President of {league}, produced and sponsored by Fantasy Warrior. Great to e-meet you.",
  },
  {
    text:
      "Before we get into your ticket, a quick reminder that your league runs on cockcoin — our proprietary, 100% fictional token economy. Very real-sounding, very fake.",
    withCoin: true,
  },
  {
    text:
      "You currently have a very respectable amount of cockcoin. I'd tell you exactly how much, but our blockchain is actually just a spreadsheet, and Deb is at lunch.",
    withCoin: true,
  },
  { text: "Anyway — how can I not actually help you today?" },
];

const AUTO_REPLY = "Great question. I'm going to escalate this to myself and get back to you never. Have you tried more cockcoin?";

function buildInitialThread(leagueName: string): ChatMessage[] {
  return SCRIPT.map((line, i) => ({
    id: `script-${i}`,
    fromCockman: true,
    text: line.text.replace("{league}", leagueName),
    withCoin: line.withCoin,
  }));
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

export function CockmanChat({ leagueName, onClose }: { leagueName: string; onClose: () => void }) {
  const sheetRef = useRef<HTMLDivElement>(null);
  const closeRef = useRef<HTMLButtonElement>(null);
  const [thread, setThread] = useState<ChatMessage[]>(() => buildInitialThread(leagueName));
  const [draft, setDraft] = useState("");
  const [busy, setBusy] = useState(false);

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
    window.setTimeout(() => {
      setThread((prev) => [...prev, { id: `reply-${Date.now()}`, fromCockman: true, text: AUTO_REPLY, withCoin: true }]);
      setBusy(false);
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
            <span className="gc-header-sub">President, {leagueName} · Typically replies instantly</span>
          </div>
          <button ref={closeRef} className="gc-close" onClick={onClose} aria-label="Close chat with Garry Cockman">
            <XIcon size={18} />
          </button>
        </div>

        <p className="gc-mock-note">
          This is a UI preview only. Garry Cockman is not a real president, cockcoin is not a real currency, and no
          messages here go anywhere — Fantasy Warrior accepts no liability for Garry's opinions, of which he has many.
        </p>

        <ul className="gc-thread">
          {thread.map((msg) => (
            <li key={msg.id} className={`gc-bubble${msg.fromCockman ? "" : " gc-bubble-mine"}`}>
              <MessageText text={msg.text} withCoin={msg.withCoin} />
            </li>
          ))}
        </ul>

        <div className="gc-composer">
          <input
            className="gc-input"
            placeholder="Message Cockman…"
            value={draft}
            onChange={(e) => setDraft(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter") sendMessage();
            }}
            aria-label="Message Garry Cockman"
          />
          <button className="gc-send" onClick={sendMessage} disabled={!draft.trim()} aria-label="Send">
            <SendIcon size={16} />
          </button>
        </div>

        <div className="gc-footer">⚡ Powered by Fantasy Warrior · Cockcoin™ Support</div>
      </div>
    </div>
  );
}
