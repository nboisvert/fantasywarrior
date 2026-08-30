export const en = {
  title: "Advance the test season",
  loadingClock: "Loading…",
  simulatedStatus: (v: { date: string }) => `Simulated — cursor at ${v.date}`,
  realTime: "Real time — not simulated",
  advanceTo: "Advance to",
  previewOnly: "Preview only (dry run) — don't bank or execute anything",
  advanceNote:
    "Advances only move forward — there is no undo. A week banks one day after it ends (the grace day). Jumping more than one week executes every trade accepted in between at the first week boundary crossed, not spread across them.",
  confirmJumpWarning:
    "This jumps more than a week — trades will execute at the first boundary crossed. Tap Advance again to confirm.",
  advancing: "Advancing…",
  confirmAdvance: "Confirm advance",
  advance: "Advance",
};

export const fr = {
  title: "Avancer la saison test",
  loadingClock: "Chargement…",
  simulatedStatus: (v: { date: string }) => `Simulé — curseur au ${v.date}`,
  realTime: "Temps réel — aucune simulation",
  advanceTo: "Avancer jusqu'au",
  previewOnly: "Aperçu seulement (essai) — ne banque ni n'exécute rien",
  advanceNote:
    "Les avances ne vont que vers l'avant — aucun retour en arrière. Une semaine banque un jour après sa fin (le jour de grâce). Sauter plus d'une semaine exécute tous les échanges acceptés entre-temps à la première limite de semaine franchie, pas répartis entre elles.",
  confirmJumpWarning:
    "Ceci saute plus d'une semaine — les échanges s'exécuteront à la première limite franchie. Retouche Avancer pour confirmer.",
  advancing: "Avance en cours…",
  confirmAdvance: "Confirmer l'avance",
  advance: "Avancer",
};
