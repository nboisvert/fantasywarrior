export const en = {
  fairTradeOption: "Fair Trade",
  wonTheTrade: "won the trade",
  fairTrade: "Fair trade",
  outOfVotes: (v: { total: number | string }) => `out of ${v.total} vote${Number(v.total) === 1 ? "" : "s"}`,
  tradeRating: "Trade rating",
  whoWonTheTrade: "Who won the trade?",
  optionAria: (v: { label: string; count: number | string; total: number | string }) =>
    `${v.label}: ${v.count} of ${v.total} votes`,
  voteCount: (v: { count: number | string; pct: number | string }) =>
    `${v.count} vote${Number(v.count) === 1 ? "" : "s"} · ${v.pct}%`,
  savingVote: "Saving your vote…",
  cantVoteOwnTrade: "You can't vote on your own trade.",
  votesTotal: (v: { total: number | string }) => `${v.total} vote${Number(v.total) === 1 ? "" : "s"} total`,
  voteToSee: "Vote to see how everyone else voted.",
  confirmText: (v: { label: string }) => `Vote for ${v.label}? This can't be changed once cast.`,
  confirmVote: "Confirm vote",
};

export const fr = {
  fairTradeOption: "Échange équitable",
  wonTheTrade: "a remporté l'échange",
  fairTrade: "Échange équitable",
  outOfVotes: (v: { total: number | string }) => `sur ${v.total} vote${Number(v.total) === 1 ? "" : "s"}`,
  tradeRating: "Cote de l'échange",
  whoWonTheTrade: "Qui a remporté l'échange?",
  optionAria: (v: { label: string; count: number | string; total: number | string }) =>
    `${v.label} : ${v.count} vote${Number(v.count) === 1 ? "" : "s"} sur ${v.total}`,
  voteCount: (v: { count: number | string; pct: number | string }) =>
    `${v.count} vote${Number(v.count) === 1 ? "" : "s"} · ${v.pct}%`,
  savingVote: "Vote en cours d'enregistrement…",
  cantVoteOwnTrade: "Tu ne peux pas voter sur ton propre échange.",
  votesTotal: (v: { total: number | string }) => `${v.total} vote${Number(v.total) === 1 ? "" : "s"} au total`,
  voteToSee: "Vote pour voir ce que le reste de la ligue a choisi.",
  confirmText: (v: { label: string }) => `Voter pour ${v.label}? Impossible de revenir en arrière une fois voté.`,
  confirmVote: "Confirmer le vote",
};
