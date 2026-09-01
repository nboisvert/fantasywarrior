// DoneDealRewardGate — invisible orchestrator, same shape as
// CockmanCampaignGate: fetches whatever done-deal cockcoin is pending once
// per mount and shows the celebration popup when there is any. A fact about
// the user, not any one league — no league prop, no live subscription (the
// award happened while the GM was offline, so there's nothing to push).

import { useEffect, useState } from "react";
import { api } from "../api";
import { DoneDealRewardPopup } from "./DoneDealRewardPopup";

export function DoneDealRewardGate({ username }: { username: string }) {
  const [amount, setAmount] = useState<number | null>(null);

  useEffect(() => {
    let cancelled = false;
    api
      .pendingCockcoinReward(username)
      .then((r) => {
        if (!cancelled && r) setAmount(r.amount);
      })
      .catch(() => {
        // No pending reward is not an error worth surfacing to the GM.
      });
    return () => {
      cancelled = true;
    };
  }, [username]);

  if (amount == null) return null;

  return (
    <DoneDealRewardPopup
      amount={amount}
      onDone={() => {
        api.ackPendingCockcoinReward(username).catch(() => {});
        setAmount(null);
      }}
    />
  );
}
