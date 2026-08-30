export const en = {
  noTeam: "You don't have a team in this league.",
  atAGlance: "At a glance",
  players: "Players",
  capSpace: "Cap Space",
  noCap: "No cap",
  rank: "Rank",
  points: "Points",
  leadingThePool: "Leading the pool",
  behindLeader: (v: { points: number | string }) => `-${v.points} vs leader`,
  weekBreak: (v: { index: number | string }) => `Week ${v.index}: league break`,
  weekPoints: (v: { index: number | string; points: number | string }) =>
    `Week ${v.index}: +${v.points} pts`,
  benchedSuffix: (v: { count: number | string }) => `, ${v.count} benched`,
  topReserve: "Top Reserve",
  topFreeAgents: "Top Free Agents",
  lastTwoWeeks: "last 2 weeks",
  noBenchStandouts: "No bench standouts in the last 2 weeks.",
  noPreviousWeek: "No previous week yet.",
  noFreeAgentsYet: "No free agents have played yet this season.",
};

export const fr = {
  noTeam: "T'as pas d'équipe dans cette ligue.",
  atAGlance: "Coup d'œil",
  players: "Joueurs",
  capSpace: "Espace sous le plafond",
  noCap: "Aucun plafond",
  rank: "Rang",
  points: "Points",
  // glossary rule: "pool" is promo-only — this is a functional screen, so "ligue".
  leadingThePool: "En tête de la ligue",
  behindLeader: (v: { points: number | string }) => `-${v.points} sur le meneur`,
  weekBreak: (v: { index: number | string }) => `Semaine ${v.index} : pause de la ligue`,
  weekPoints: (v: { index: number | string; points: number | string }) =>
    `Semaine ${v.index} : +${v.points} pts`,
  benchedSuffix: (v: { count: number | string }) => `, ${v.count} au banc`,
  topReserve: "Top réserve",
  topFreeAgents: "Top joueurs autonomes",
  lastTwoWeeks: "2 dernières semaines",
  noBenchStandouts: "Aucun exploit du banc dans les 2 dernières semaines.",
  noPreviousWeek: "Pas encore de semaine précédente.",
  noFreeAgentsYet: "Aucun joueur autonome n'a encore joué cette saison.",
};
