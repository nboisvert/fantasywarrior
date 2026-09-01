// CockmanCampaignPopup — the notification a CockmanCampaignGate shows once
// per user per campaign: a message, and (for a future question-bearing
// campaign) a quiz with a cockcoin reward. Reuses CockmanChat.tsx's modal
// shell mechanics (Escape, focus trap, scroll lock, focus restore) but reads
// as a single centered announcement card, same shell shape as
// CockcoinInfo.tsx's dialog, rather than the docked chat widget — this is an
// interrupt, not a conversation.
//
// The message is three short beats (2026-09-01, per Nick — reworked from the
// original one-liner, based on CockmanChat's own intro): an in-character
// intro naming the league (same "President of {league}" line CockmanChat
// opens with), a stats line naming this GM's actual league — how many GMs,
// who's running it — and a call to action naming Trades and the weekly
// lineup, with the jersey icon inline the same way CockmanChat inlines the
// cockcoin icon into its own copy. Every campaign is expected to supply
// `${key}Intro` / `${key}Stats` / `${key}Cta` in the cockmanCampaigns
// dictionary.

import { useEffect, useRef } from "react";
import { createPortal } from "react-dom";
import type { KeyboardEvent as ReactKeyboardEvent } from "react";
import type { CockmanCampaignDto, LeagueDetail } from "../api";
import cockmanAvatar from "../assets/cockman.png";
import { useLanguage } from "../i18n/LanguageContext";
import { JerseyIcon, XIcon } from "./Icons";
import "./CockmanCampaignPopup.css";

/** Renders the CTA line with the jersey icon inlined at the `%jersey%`
 * token — the token is language-neutral, so the icon lands correctly
 * regardless of where each translation puts it. */
function CtaText({ text }: { text: string }) {
  const parts = text.split("%jersey%");
  return (
    <>
      {parts.map((part, i) => (
        <span key={i}>
          {part}
          {i < parts.length - 1 && <JerseyIcon size={16} className="gcc-cta-icon" />}
        </span>
      ))}
    </>
  );
}

export function CockmanCampaignPopup({
  campaign,
  league,
  onDismiss,
  onAnswer,
}: {
  campaign: CockmanCampaignDto;
  league: LeagueDetail;
  onDismiss: () => void;
  onAnswer: (choiceKey: string) => void;
}) {
  const { t } = useLanguage();
  const cardRef = useRef<HTMLDivElement>(null);
  const closeRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onDismiss();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onDismiss]);

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
    if (e.key !== "Tab" || !cardRef.current) return;
    const focusables = cardRef.current.querySelectorAll<HTMLElement>(
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

  const admin = league.commissionerUsername.charAt(0).toUpperCase() + league.commissionerUsername.slice(1);
  const intro = t(`cockmanCampaigns.${campaign.key}Intro`, { league: league.name });
  const stats = t(`cockmanCampaigns.${campaign.key}Stats`, { gmCount: league.teams.length, admin, league: league.name });
  const cta = t(`cockmanCampaigns.${campaign.key}Cta`, { trades: t("app.navTrades") });

  return createPortal(
    <div className="gcc-overlay">
      <div ref={cardRef} className="gcc-card" role="dialog" aria-modal="true" aria-labelledby="gcc-title" onKeyDown={trapFocus}>
        <button ref={closeRef} className="gcc-close" onClick={onDismiss} aria-label={t("cockmanCampaigns.closeAria")}>
          <XIcon size={18} />
        </button>
        <img className="gcc-avatar" src={cockmanAvatar} alt="" />
        <span id="gcc-title" className="gcc-title">
          {t("cockmanCampaigns.title")}
        </span>
        <p className="gcc-message">{intro}</p>
        <p className="gcc-message gcc-message-stats">{stats}</p>
        <p className="gcc-message gcc-message-cta">
          <CtaText text={cta} />
        </p>

        {campaign.hasQuestion && campaign.choiceKeys && (
          <>
            <p className="gcc-question">{t(`cockmanCampaigns.${campaign.key}Question`)}</p>
            <div className="gcc-choices">
              {campaign.choiceKeys.map((choiceKey) => (
                <button key={choiceKey} type="button" className="btn-outline" onClick={() => onAnswer(choiceKey)}>
                  {t(`cockmanCampaigns.${campaign.key}Choice_${choiceKey}`)}
                </button>
              ))}
            </div>
          </>
        )}

        {!campaign.hasQuestion && (
          <button type="button" className="btn-outline gcc-dismiss" onClick={onDismiss}>
            {t("cockmanCampaigns.gotIt")}
          </button>
        )}
      </div>
    </div>,
    document.body,
  );
}
