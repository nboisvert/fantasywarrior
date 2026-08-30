// TradeRatingInfo — the small "i" trigger next to a trade rating number, and
// the popup it opens explaining what the number means (2026-08-05, per
// Nick). Self-contained: owns its own open/closed state so it can sit next
// to any rating number without the parent tracking a modal.
//
// stopPropagation on both the trigger and the overlay's own click matters
// here — this button is meant to live inside a larger clickable row (the
// collapsed trade recap expands the card on tap), and tapping it must open
// the popup instead of also toggling that row.
//
// Portaled to document.body rather than rendered in place: every screen root
// carries the app-wide `.fade-in` class, and an element with an animation
// targeting `transform` is a containing block for its `position: fixed`
// descendants for as long as that animation property is set — which for
// `.fade-in` is forever, not just while it's actually playing. Left in
// place, this popup would be sized against the Trades screen's section
// instead of the viewport.

import { useState } from "react";
import { createPortal } from "react-dom";
import { InfoIcon } from "./Icons";
import { useLanguage } from "../i18n/LanguageContext";
import "./TradeRatingInfo.css";

export function TradeRatingInfo() {
  const { t } = useLanguage();
  const [open, setOpen] = useState(false);

  return (
    <>
      <button
        type="button"
        className="trade-rating-info-trigger"
        aria-label={t("tradeRatingInfo.triggerAria")}
        onClick={(e) => {
          e.stopPropagation();
          setOpen(true);
        }}
      >
        <InfoIcon size={12} />
      </button>
      {open &&
        createPortal(
          <div
            className="trade-rating-info-overlay"
            role="dialog"
            aria-modal="true"
            onClick={(e) => {
              e.stopPropagation();
              setOpen(false);
            }}
          >
            <div className="trade-rating-info" onClick={(e) => e.stopPropagation()}>
              <span className="trade-rating-info-title">{t("tradeRatingInfo.title")}</span>
              <p>{t("tradeRatingInfo.intro")}</p>
              <ul>
                <li>
                  <strong className="rating-tier-even">50</strong> — {t("tradeRatingInfo.tierEven")}
                </li>
                <li>
                  <strong className="rating-tier-lean">50–65</strong> — {t("tradeRatingInfo.tierLean")}
                </li>
                <li>
                  <strong className="rating-tier-clear">65–85</strong> — {t("tradeRatingInfo.tierClear")}
                </li>
                <li>
                  <strong className="rating-tier-consensus">85–100</strong> — {t("tradeRatingInfo.tierConsensus")}
                </li>
              </ul>
              <p className="muted">{t("tradeRatingInfo.footnote")}</p>
              <button type="button" className="btn-outline" onClick={() => setOpen(false)}>
                {t("tradeRatingInfo.gotIt")}
              </button>
            </div>
          </div>,
          document.body,
        )}
    </>
  );
}
