// Stat-code columns (GP, G, A, PT, PT/G, W, L, OTL, SO, +/-, PIM, SOG, GAA,
// SV%, $/PT) are left identical in both languages on purpose — Québécois
// hockey coverage reads these in their English short form same as everywhere
// else, and re-abbreviating them into French would just be a second code to
// learn for no gain. Full labels (Player, Record, Cap hit, position-filter
// text) are translated; F/D/G position letters follow the app-wide convention
// of staying as-is (see `.pos-compact-f/d/g`) in both languages.

export const en = {
  // ---- roster grid: titles / subtitles / empty states ----
  rosterTitle: "Roster",
  rosterSubtitle: (v: { count: number | string; max: number | string | null }) =>
    `${v.count}${v.max != null ? ` / ${v.max}` : ""} player`,
  rosterEmpty: "No players on this roster.",
  departedTitle: "Departed",
  departedSubtitle: (v: { count: number | string }) => `${v.count} player, points kept`,
  departedEmpty: "Nobody has left this roster.",
  incomingTitle: "Incoming",
  incomingSubtitle: (v: { count: number | string }) => `${v.count} player, on the roster when the week turns`,
  incomingEmpty: "Nobody arriving.",
  positionEmpty: (v: { noun: string }) => `No ${v.noun} here.`,
  positionEmptyForwards: "forwards",
  positionEmptyDefensemen: "defensemen",
  positionEmptyGoalies: "goalies",
  prospects: "Prospects",
  total: "Total",

  // ---- grid headers ----
  colPlayer: "Player",
  groupFantasyPoint: "Fantasy point",
  groupRecord: "Record",
  groupNhl: "NHL",
  groupExtra: "Extra",
  groupCapHit: "Cap hit",
  colWeek: "Week",
  colInLineupAria: "In the lineup",

  // ---- position filter ----
  filterAll: "All",
  filterAria: "Filter roster by position",

  // ---- player periods (week-by-week panel) ----
  noWeeksScored: "No weeks scored for this player yet.",
  weekByWeek: "Week by week",
  weekByWeekAria: (v: { name: string }) => `${v.name} — week by week`,
  periodsActiveTail: (v: { weeks: number | string }) =>
    `pts over ${v.weeks} week${Number(v.weeks) === 1 ? "" : "s"} in the lineup`,
  periodsBenchTotal: (v: { pts: number | string; weeks: number | string }) =>
    `${v.pts} left on the bench over ${v.weeks} week${Number(v.weeks) === 1 ? "" : "s"}`,

  // ---- lineup toggle (active/bench control) ----
  lineupActive: "active",
  lineupBenched: "benched",
  lineupChangeIn: ", coming into next week's lineup",
  lineupChangeOut: ", dropping out of next week's lineup",
  lineupChangeGone: ", leaving the team before next week",
  lineupTapHint: ", tap to change next week",

  // ---- lineup picker sheet ----
  pickerReplaceTitle: (v: { name: string; week: number | string }) => `Replace ${v.name} for week ${v.week}`,
  pickerBringInTitle: (v: { name: string; week: number | string }) => `Bring in ${v.name} for week ${v.week}`,
  pickerBench: "Bench, leave the slot open",
  pickerOrSwapIn: "or swap in",
  pickerActivate: (v: { pos: string }) => `Activate — a ${v.pos} slot is open`,
  pickerAllTaken: (v: { pos: string }) => `Every ${v.pos} slot is taken. Choose who sits out.`,
  pickerLeavingAria: (v: { name: string; week: number | string }) =>
    `${v.name} — leaves the team before week ${v.week}, cannot be selected`,
  pickerLeavesTeam: "leaves the team",
  pickerArrivingViaTrade: "arriving via trade",
  pickerNobodyPlays: (v: { pos: string }) => `Nobody on the bench plays ${v.pos}.`,

  // ---- injury / trade marks ----
  injuredKind: "Injured",
  suspendedKind: "Suspended",
  injuryLabel: (v: { kind: string; type: string | null }) => (v.type ? `${v.kind} — ${v.type}` : v.kind),
  leavingViaTrade: "Leaving via trade",
  arrivingViaTrade: "Arriving via trade",

  // ---- screen chrome ----
  backToStandings: "Back to standings",
  points: "Points",
  teamNotFound: "Team not found in this league.",
  loadingStats: "Loading stats…",
  couldNotLoadStats: "Could not load stats.",
  couldNotSaveLineup: "Could not save the lineup.",

  // ---- cap gauge ----
  capSummary: (v: { used: string; max: string }) => `Cap ${v.used} / ${v.max}`,
  capOverBudget: "Over budget",
  capAvailable: "Available",
  capUsed: "Used",
  capValueText: (v: { pct: number | string; amount: string; state: string }) =>
    `${v.pct}% of cap used, ${v.amount} ${v.state}`,
  capOverBudgetState: "over budget",
  capAvailableState: "available",
  capAria: "Salary cap used",
  capCommitted: (v: { used: string; max: string }) => `${v.used} committed of ${v.max} cap`,
};

export const fr = {
  rosterTitle: "Alignement",
  rosterSubtitle: (v: { count: number | string; max: number | string | null }) =>
    `${v.count}${v.max != null ? ` / ${v.max}` : ""} joueur${Number(v.count) > 1 ? "s" : ""}`,
  rosterEmpty: "Aucun joueur dans cet alignement.",
  departedTitle: "Partis",
  departedSubtitle: (v: { count: number | string }) =>
    `${v.count} joueur${Number(v.count) > 1 ? "s" : ""}, points conservés`,
  departedEmpty: "Personne n'a quitté cet alignement.",
  incomingTitle: "Arrivées",
  incomingSubtitle: (v: { count: number | string }) =>
    `${v.count} joueur${Number(v.count) > 1 ? "s" : ""}, sur l'alignement au tournant de la semaine`,
  incomingEmpty: "Personne en approche.",
  positionEmpty: (v: { noun: string }) => `Aucun ${v.noun} ici.`,
  positionEmptyForwards: "attaquant",
  positionEmptyDefensemen: "défenseur",
  positionEmptyGoalies: "gardien",
  prospects: "Espoirs",
  total: "Total",

  colPlayer: "Joueur",
  groupFantasyPoint: "Point ligue",
  groupRecord: "Fiche",
  groupNhl: "LNH",
  groupExtra: "Extra",
  groupCapHit: "Plafond",
  colWeek: "Semaine",
  colInLineupAria: "Dans la formation",

  filterAll: "Tous",
  filterAria: "Filtrer l'alignement par position",

  noWeeksScored: "Encore aucune semaine comptée pour ce joueur.",
  weekByWeek: "Semaine par semaine",
  weekByWeekAria: (v: { name: string }) => `${v.name} — semaine par semaine`,
  periodsActiveTail: (v: { weeks: number | string }) =>
    `pts sur ${v.weeks} semaine${Number(v.weeks) === 1 ? "" : "s"} dans la formation`,
  periodsBenchTotal: (v: { pts: number | string; weeks: number | string }) =>
    `${v.pts} laissés sur le banc sur ${v.weeks} semaine${Number(v.weeks) === 1 ? "" : "s"}`,

  lineupActive: "actif",
  lineupBenched: "sur le banc",
  lineupChangeIn: ", entre dans la formation la semaine prochaine",
  lineupChangeOut: ", sort de la formation la semaine prochaine",
  lineupChangeGone: ", quitte l'équipe avant la semaine prochaine",
  lineupTapHint: ", touche pour changer la semaine prochaine",

  pickerReplaceTitle: (v: { name: string; week: number | string }) =>
    `Remplacer ${v.name} pour la semaine ${v.week}`,
  pickerBringInTitle: (v: { name: string; week: number | string }) =>
    `Faire embarquer ${v.name} pour la semaine ${v.week}`,
  pickerBench: "Mettre sur le banc, laisser la place vide",
  pickerOrSwapIn: "ou faire embarquer",
  pickerActivate: (v: { pos: string }) => `Activer — une place ${v.pos} est libre`,
  pickerAllTaken: (v: { pos: string }) => `Toutes les places ${v.pos} sont prises. Choisis qui reste sur le banc.`,
  pickerLeavingAria: (v: { name: string; week: number | string }) =>
    `${v.name} — quitte l'équipe avant la semaine ${v.week}, ne peut pas être choisi`,
  pickerLeavesTeam: "quitte l'équipe",
  pickerArrivingViaTrade: "arrive par échange",
  pickerNobodyPlays: (v: { pos: string }) => `Personne sur le banc ne joue ${v.pos}.`,

  injuredKind: "Blessé",
  suspendedKind: "Suspendu",
  injuryLabel: (v: { kind: string; type: string | null }) => (v.type ? `${v.kind} — ${v.type}` : v.kind),
  leavingViaTrade: "Quitte par échange",
  arrivingViaTrade: "Arrive par échange",

  backToStandings: "Retour au classement",
  points: "Points",
  teamNotFound: "Équipe introuvable dans cette ligue.",
  loadingStats: "Chargement des statistiques…",
  couldNotLoadStats: "Impossible de charger les statistiques.",
  couldNotSaveLineup: "Impossible d'enregistrer la formation.",

  capSummary: (v: { used: string; max: string }) => `Plafond ${v.used} / ${v.max}`,
  capOverBudget: "Au-dessus du budget",
  capAvailable: "Disponible",
  capUsed: "Utilisé",
  capValueText: (v: { pct: number | string; amount: string; state: string }) =>
    `${v.pct}% du plafond utilisé, ${v.amount} ${v.state}`,
  capOverBudgetState: "au-dessus du budget",
  capAvailableState: "disponible",
  capAria: "Plafond salarial utilisé",
  capCommitted: (v: { used: string; max: string }) => `${v.used} engagés sur ${v.max} de plafond`,
};
