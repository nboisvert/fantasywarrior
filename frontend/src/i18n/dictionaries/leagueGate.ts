export const en = {
  cancelSwitching: "Cancel switching league",
  chooseLeague: "Choose a league",
  noLeagueYet: "No league yet — create one or join with an invite code.",
  goToSettings: "Go to Settings",
  memberCap: (v: { members: number | string; cap: string }) =>
    `${v.members} member${Number(v.members) > 1 ? "s" : ""} · cap ${v.cap}`,
  createOrJoinInSettings: "Create or join another league in Settings",
};

export const fr = {
  cancelSwitching: "Annuler le changement de ligue",
  chooseLeague: "Choisir une ligue",
  noLeagueYet: "Encore aucune ligue — crées-en une ou embarque avec un code d'invitation.",
  goToSettings: "Aller aux réglages",
  memberCap: (v: { members: number | string; cap: string }) =>
    `${v.members} membre${Number(v.members) > 1 ? "s" : ""} · plafond ${v.cap}`,
  createOrJoinInSettings: "Crée ou rejoins une autre ligue dans les réglages",
};
