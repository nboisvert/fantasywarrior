// Copy for CockmanCampaignPopup. Keys are flat and built per-campaign as
// `${campaign.key}Message` (and, for a future question-bearing campaign,
// `${key}Question` / `${key}Choice_${choiceKey}`) — t() only resolves one
// namespace + one leaf, so nothing here can nest. The welcome campaign pulls
// the live nav labels from the `app` dictionary rather than repeating them
// here, so this line stays correct if the nav copy ever changes.

export const en = {
  title: "Garry Cockman",
  closeAria: "Close",
  gotIt: "Got it",
  welcomeMessage: (v: { office: string; standings: string; team: string; trades: string }) =>
    `Welcome aboard. Everything lives in four spots: ${v.office}, ${v.standings}, ${v.team} and ${v.trades} — go poke around.`,
};

export const fr = {
  title: "Garry Cockman",
  closeAria: "Fermer",
  gotIt: "Compris",
  welcomeMessage: (v: { office: string; standings: string; team: string; trades: string }) =>
    `Bienvenue à bord. Tout se trouve à quatre endroits : ${v.office}, ${v.standings}, ${v.team} et ${v.trades} — va explorer.`,
};
