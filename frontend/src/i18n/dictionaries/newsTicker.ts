// Headlines themselves come from licensed external feeds (NHL/CapWages/
// FantasySP/Rotowire) and are never translated or rewritten — see
// .claude/doc/integrations.md. Only the ticker's own chrome lives here.

export const en = {
  ariaLabel: "NHL news and league trades",
  tradeAlert: "Trade Alert",
  agreedPrefix: "Agreed — ",
  nothing: "nothing",
  timeNow: "now",
  timeMinutes: (v: { n: number | string }) => `${v.n}m`,
  timeHours: (v: { n: number | string }) => `${v.n}h`,
  timeDays: (v: { n: number | string }) => `${v.n}d`,
};

export const fr = {
  ariaLabel: "Actualités LNH et échanges de la ligue",
  tradeAlert: "Alerte échange",
  agreedPrefix: "Entendu — ",
  nothing: "rien",
  timeNow: "maintenant",
  timeMinutes: (v: { n: number | string }) => `${v.n} min`,
  timeHours: (v: { n: number | string }) => `${v.n} h`,
  timeDays: (v: { n: number | string }) => `${v.n} j`,
};
