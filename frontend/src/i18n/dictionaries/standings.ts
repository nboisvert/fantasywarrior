export const en = {
  title: (v: { season: string }) => `Standings — Season ${v.season}`,
  viewPalmares: "View the league's palmarès",
  noTeamYet: "No team in this league yet.",
  viewStats: (v: { team: string }) => `View ${v.team}'s stats`,
  playerCount: (v: { count: number | string }) => `${v.count} player${Number(v.count) === 1 ? "" : "s"}`,
  thisWeek: (v: { points: number | string }) => `+${v.points} this week`,
  ptsPerGame: (v: { value: number | string }) => `${v.value} pts/gm`,
  noStats: "—",
};

export const fr = {
  title: (v: { season: string }) => `Classement — Saison ${v.season}`,
  viewPalmares: "Voir le palmarès de la ligue",
  noTeamYet: "Encore aucune équipe dans cette ligue.",
  viewStats: (v: { team: string }) => `Voir les statistiques de ${v.team}`,
  playerCount: (v: { count: number | string }) => `${v.count} joueur${Number(v.count) === 1 ? "" : "s"}`,
  thisWeek: (v: { points: number | string }) => `+${v.points} cette semaine`,
  ptsPerGame: (v: { value: number | string }) => `${v.value} pts/match`,
  noStats: "—",
};
