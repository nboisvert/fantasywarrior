// Copy for CockmanCampaignPopup. Keys are flat and built per-campaign as
// `${campaign.key}Intro` / `${key}Stats` / `${key}Cta` (and, for a future
// question-bearing campaign, `${key}Question` / `${key}Choice_${choiceKey}`)
// — t() only resolves one namespace + one leaf, so nothing here can nest.
//
// The welcome campaign (2026-09-01, per Nick — reworked from the original
// one-liner, based on CockmanChat's own intro): an in-character opener
// naming the league, a stats line naming this GM's actual league (GM count,
// commissioner), then a call to action on Trades and the weekly lineup. The
// CTA's `%jersey%` token marks where CockmanCampaignPopup inlines the jersey
// icon — language-neutral, so it lands correctly in either translation.
// `trades` is threaded through from the live `app.navTrades` label rather
// than repeated here, same reasoning as the original welcome one-liner.

export const en = {
  title: "Garry Cockman",
  closeAria: "Close",
  gotIt: "Got it",
  welcomeIntro: (v: { league: string }) =>
    `Hi there. Garry Cockman here — President of ${v.league}, produced and sponsored by Fantasy Warrior. Great to e-meet you.`,
  welcomeStats: (v: { gmCount: number; admin: string; league: string }) =>
    `${v.gmCount} GMs strong in ${v.league}, run by commissioner ${v.admin} — welcome to the show.`,
  welcomeCta: (v: { trades: string }) =>
    `Go make some noise: propose ${v.trades}, and set your %jersey% lineup every week.`,
};

export const fr = {
  title: "Garry Cockman",
  closeAria: "Fermer",
  gotIt: "Compris",
  welcomeIntro: (v: { league: string }) =>
    `Allô. Garry Cockman ici — président de ${v.league}, produit et commandité par Fantasy Warrior. Content de te e-rencontrer.`,
  welcomeStats: (v: { gmCount: number; admin: string; league: string }) =>
    `${v.gmCount} DG dans ${v.league}, menée par le commissaire ${v.admin} — bienvenue dans le show.`,
  welcomeCta: (v: { trades: string }) =>
    `Brasse un peu : propose des ${v.trades}, pis prépare ta %jersey% formation chaque semaine.`,
};
