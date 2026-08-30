// CockcoinReward — the "wow" feedback for any action that earns cockcoin: a
// floating "+N cockcoin" pop, mobile-game-style (pop in, drift up, fade out).
// First trigger: TradeVoteWidget's vote confirmation. Meant to be reused by
// every future earning action (cockman-concept.md's "how it's earned" list),
// so this stays generic — just an amount and a callback for when it's done.
//
// Absolutely positioned; the caller supplies a `position: relative` anchor
// (TradeVoteWidget wraps its own root) so this centers over whatever earned
// it rather than the page.

import { CockcoinIcon } from "./Icons";
import { useLanguage } from "../i18n/LanguageContext";
import "./CockcoinReward.css";

export function CockcoinReward({ amount, onDone }: { amount: number; onDone: () => void }) {
  const { t } = useLanguage();
  return (
    <span className="cockcoin-reward" aria-live="polite" onAnimationEnd={onDone}>
      <CockcoinIcon size={36} />
      <span className="cockcoin-reward-amount">+{amount} CK</span>
      <span className="cockcoin-reward-label">{t("cockcoinReward.label")}</span>
    </span>
  );
}
