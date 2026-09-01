// CockmanCampaignGate — invisible orchestrator, same shape as App.tsx's
// UnreadBridge/DraftBridge: fetches the one Cockman campaign due for this
// user once per mount and renders the popup when one comes back. Campaigns
// are a fact about the user, not any one league — the "due or not" check
// needs no league — but the message itself now quotes this GM's actual
// league (name, GM count, commissioner), so the popup needs one once it has
// something to show.

import { useEffect, useState } from "react";
import { api } from "../api";
import type { CockmanCampaignDto, LeagueDetail } from "../api";
import { CockmanCampaignPopup } from "./CockmanCampaignPopup";

export function CockmanCampaignGate({ username, league }: { username: string; league: LeagueDetail }) {
  const [campaign, setCampaign] = useState<CockmanCampaignDto | null>(null);

  useEffect(() => {
    let cancelled = false;
    api
      .cockmanCampaign(username)
      .then((c) => {
        if (!cancelled) setCampaign(c);
      })
      .catch(() => {
        // No campaign is not an error worth surfacing to the GM.
      });
    return () => {
      cancelled = true;
    };
  }, [username]);

  if (!campaign) return null;

  return (
    <CockmanCampaignPopup
      campaign={campaign}
      league={league}
      onDismiss={() => {
        api.dismissCockmanCampaign(username, campaign.id).catch(() => {});
        setCampaign(null);
      }}
      onAnswer={(choiceKey) => {
        api.answerCockmanCampaign(username, campaign.id, choiceKey).catch(() => {});
        setCampaign(null);
      }}
    />
  );
}
