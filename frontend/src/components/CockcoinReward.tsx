// CockcoinReward — the "wow" feedback for any action that earns cockcoin: a
// floating "+N cockcoin" pop, mobile-game-style (pop in, drift up, fade out).
// First trigger: TradeVoteWidget's vote confirmation. Reused by every earning
// action now — chat/trade Fibonacci milestones, the done-deal bonus, votes —
// so this stays generic: an amount, an optional reason line, a callback for
// when it's done.
//
// `reason` (2026-09-01, per Nick: "ajoute la raison en light pour involve le
// user") is a second, quieter line under the amount — already-translated
// text from the caller, not resolved here, since every call site knows its
// own reason (there's nothing to look up).
//
// Absolutely positioned; the caller supplies a `position: relative` anchor
// (TradeVoteWidget wraps its own root) so this centers over whatever earned
// it rather than the page.

import { CockcoinIcon } from "./Icons";
import { useLanguage } from "../i18n/LanguageContext";
import "./CockcoinReward.css";

export function CockcoinReward({ amount, reason, onDone }: { amount: number; reason?: string; onDone: () => void }) {
  const { t } = useLanguage();
  return (
    <span className="cockcoin-reward" aria-live="polite" onAnimationEnd={onDone}>
      <span className="cockcoin-reward-top">
        <CockcoinIcon size={36} />
        <span className="cockcoin-reward-amount">+{amount} CK</span>
        <span className="cockcoin-reward-label">{t("cockcoinReward.label")}</span>
      </span>
      {reason && <span className="cockcoin-reward-reason">{reason}</span>}
    </span>
  );
}
