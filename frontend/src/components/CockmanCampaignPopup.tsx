// CockmanCampaignPopup — the notification a CockmanCampaignGate shows once
// per user per campaign: a message, and (for a future question-bearing
// campaign) a quiz with a cockcoin reward. Reuses CockmanChat.tsx's modal
// shell mechanics (Escape, focus trap, scroll lock, focus restore) but reads
// as a single centered announcement card, same shell shape as
// CockcoinInfo.tsx's dialog, rather than the docked chat widget — this is an
// interrupt, not a conversation.

import { useEffect, useRef } from "react";
import { createPortal } from "react-dom";
import type { KeyboardEvent as ReactKeyboardEvent } from "react";
import type { CockmanCampaignDto } from "../api";
import cockmanAvatar from "../assets/cockman.png";
import { useLanguage } from "../i18n/LanguageContext";
import { XIcon } from "./Icons";
import "./CockmanCampaignPopup.css";

export function CockmanCampaignPopup({
  campaign,
  onDismiss,
  onAnswer,
}: {
  campaign: CockmanCampaignDto;
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

  const message = t(`cockmanCampaigns.${campaign.key}Message`, {
    office: t("app.navGmOffice"),
    standings: t("app.navStandings"),
    team: t("app.navTeam"),
    trades: t("app.navTrades"),
  });

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
        <p className="gcc-message">{message}</p>

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
