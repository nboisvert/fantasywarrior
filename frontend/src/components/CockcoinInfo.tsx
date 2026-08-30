// CockcoinInfo — the small "i" trigger next to the cockcoin bank in
// ProfileMenu, and the popup it opens explaining what cockcoin is
// (2026-08-30, per Nick). Same trigger/portal/backdrop mechanics as
// TradeRatingInfo — copy that pattern rather than inventing a new one — but
// "the message" here is Cockman's own voice (his avatar + a quote), not a
// numeric tier legend.
//
// Visible to every GM, not just the commissioner: the cockcoin balance
// itself shows to everyone in ProfileMenu, so the explainer for it can't
// assume the commissioner-only Cockman chat is reachable — this popup is
// fully standalone, no link into that screen.

import { useState } from "react";
import { createPortal } from "react-dom";
import cockmanAvatar from "../assets/cockman.png";
import { InfoIcon } from "./Icons";
import { useLanguage } from "../i18n/LanguageContext";
import "./CockcoinInfo.css";

export function CockcoinInfo() {
  const { t } = useLanguage();
  const [open, setOpen] = useState(false);

  return (
    <>
      <button
        type="button"
        className="cockcoin-info-trigger"
        aria-label={t("cockcoinInfo.triggerAria")}
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
            className="cockcoin-info-overlay"
            role="dialog"
            aria-modal="true"
            onClick={(e) => {
              e.stopPropagation();
              setOpen(false);
            }}
          >
            <div className="cockcoin-info" onClick={(e) => e.stopPropagation()}>
              <span className="cockcoin-info-title">{t("cockcoinInfo.title")}</span>
              <div className="cockcoin-info-quote">
                <img className="cockcoin-info-avatar" src={cockmanAvatar} alt="" />
                <div className="cockcoin-info-quote-text">
                  <p>{t("cockcoinInfo.line1")}</p>
                  <p>{t("cockcoinInfo.line2")}</p>
                  <span className="cockcoin-info-attribution">{t("cockcoinInfo.quoteAttribution")}</span>
                </div>
              </div>
              <p className="muted cockcoin-info-footnote">{t("cockcoinInfo.footnote")}</p>
              <button type="button" className="btn-outline" onClick={() => setOpen(false)}>
                {t("cockcoinInfo.gotIt")}
              </button>
            </div>
          </div>,
          document.body,
        )}
    </>
  );
}
