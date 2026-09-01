// DoneDealRewardPopup — the celebration for the done-deal bonus (+10 CK per
// GM, awarded overnight in WeekAheadJob while nobody's watching). Nick:
// "l'effet wow du UI!!! Le +5 +10 devrait être incroyable" — this is the one
// that needs to feel biggest, since it's the only earning moment with no
// live action to attach itself to.
//
// Same modal shell mechanics as CockmanCampaignPopup.tsx (portal, Escape,
// focus trap, scroll lock/restore) — reads as a celebration card rather than
// a question. Reuses CockcoinReward as-is (scaled up via CSS transform)
// rather than forking its keyframes/sizing into a drifting second copy.

import { useEffect, useRef } from "react";
import { createPortal } from "react-dom";
import type { KeyboardEvent as ReactKeyboardEvent } from "react";
import { useLanguage } from "../i18n/LanguageContext";
import { CockcoinReward } from "./CockcoinReward";
import { XIcon } from "./Icons";
import "./DoneDealRewardPopup.css";

export function DoneDealRewardPopup({ amount, onDone }: { amount: number; onDone: () => void }) {
  const { t } = useLanguage();
  const cardRef = useRef<HTMLDivElement>(null);
  const closeRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onDone();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onDone]);

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

  return createPortal(
    <div className="ddr-overlay">
      <div ref={cardRef} className="ddr-card" role="dialog" aria-modal="true" aria-labelledby="ddr-title" onKeyDown={trapFocus}>
        <button ref={closeRef} className="ddr-close" onClick={onDone} aria-label={t("doneDealReward.closeAria")}>
          <XIcon size={18} />
        </button>
        <span id="ddr-title" className="ddr-title">
          {t("doneDealReward.title")}
        </span>
        <div className="ddr-stage">
          <CockcoinReward amount={amount} reason={t("cockcoinReward.reasonDoneDeal")} onDone={onDone} />
        </div>
      </div>
    </div>,
    document.body,
  );
}
