export const en = {
  // "Palmarès" is the screen's own name (already French in the English UI,
  // same flavour as the login screen's "pool") — kept identical in both
  // languages, not translated as a common noun.
  title: "Palmarès",
  back: "Back to standings",
  loading: "Loading…",
  noSeason: "No season recorded yet.",
  seasonLine: (v: { number: number | string; formatted: string }) => `Season ${v.number} · ${v.formatted}`,
  phasePreparing: "Preparing",
  phaseProtecting: "Protection window open",
  phaseDrafting: "Draft in progress",
  phasePreSeason: "Pre-season",
  phaseInSeason: "In season",
  phaseComplete: "Complete",
};

export const fr = {
  title: "Palmarès",
  back: "Retour au classement",
  loading: "Chargement…",
  noSeason: "Aucune saison enregistrée pour l'instant.",
  seasonLine: (v: { number: number | string; formatted: string }) => `Saison ${v.number} · ${v.formatted}`,
  phasePreparing: "Préparation",
  phaseProtecting: "Fenêtre de protection ouverte",
  phaseDrafting: "Repêchage en cours",
  phasePreSeason: "Avant-saison",
  phaseInSeason: "Saison en cours",
  phaseComplete: "Terminée",
};
