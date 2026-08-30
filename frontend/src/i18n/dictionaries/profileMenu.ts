export const en = {
  triggerOnline: (v: { username: string; count: number | string }) =>
    `Profile menu — ${v.username}, ${v.count} other GM${Number(v.count) > 1 ? "s" : ""} online`,
  triggerAlone: (v: { username: string }) => `Profile menu — ${v.username}, nobody else online`,
  dialogLabel: "Profile menu",
  cockcoinAria: (v: { amount: number | string }) => `${v.amount} CK cockcoin`,
  cockcoinLabel: "cockcoin",
  leagueGms: (v: { count: number | string }) => `League GMs (${v.count} online)`,
  noOtherPoolers: "No other poolers yet.",
  messageAria: (v: { member: string }) => `Message ${v.member}`,
  online: "Online",
  lastSeen: (v: { time: string }) => `last seen ${v.time}`,
  settings: "Settings",
  logOut: "Log out",
};

export const fr = {
  triggerOnline: (v: { username: string; count: number | string }) =>
    `Menu du profil — ${v.username}, ${v.count} autre${Number(v.count) > 1 ? "s" : ""} DG en ligne`,
  triggerAlone: (v: { username: string }) => `Menu du profil — ${v.username}, personne d'autre en ligne`,
  dialogLabel: "Menu du profil",
  cockcoinAria: (v: { amount: number | string }) => `${v.amount} CK cockcoin`,
  cockcoinLabel: "cockcoin",
  leagueGms: (v: { count: number | string }) => `DG de la ligue (${v.count} en ligne)`,
  noOtherPoolers: "Encore aucun autre DG.",
  messageAria: (v: { member: string }) => `Écrire à ${v.member}`,
  online: "En ligne",
  lastSeen: (v: { time: string }) => `vu ${v.time}`,
  settings: "Réglages",
  logOut: "Déconnexion",
};
