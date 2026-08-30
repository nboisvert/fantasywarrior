export const en = {
  intro: (v: { league: string }) =>
    `Hi there. Garry Cockman here — President of ${v.league}, produced and sponsored by Fantasy Warrior. Great to e-meet you.`,
  reminder:
    "Before we get into your ticket, a quick reminder that your league runs on cockcoin — our proprietary, 100% fictional token economy. Very real-sounding, very fake.",
  balance:
    "You currently have a very respectable amount of cockcoin. I'd tell you exactly how much, but our blockchain is actually just a spreadsheet, and Deb is at lunch.",
  howToEarn:
    "Here's how you actually earn more: cockcoin tracks toward your interaction within the app, and it'll unlock access to exclusive content once you've built up a balance.",
  bonusEntry: (v: { pooler: string }) =>
    `Let's start with a bonus entry toward your cockcoin — quick one: describe ${v.pooler} in three words.`,
  autoReply: "Great question. I'm going to escalate this to myself and get back to you never. Have you tried more cockcoin?",
  bonusReply: "Logged — that's one bonus entry toward your cockcoin. Very official. Very fake.",
  yourself: "yourself",
  presidentTag: "President",
  you: "You",
  headerSub: (v: { league: string }) => `${v.league} · Typically replies instantly`,
  closeAria: "Close chat with Garry Cockman",
  mockNote:
    "This is a UI preview only. Garry Cockman is not a real president, cockcoin is not a real currency, and no messages here go anywhere — Fantasy Warrior accepts no liability for Garry's opinions, of which he has many.",
  messagePlaceholder: "Message Cockman…",
  messageAria: "Message Garry Cockman",
  send: "Send",
  footer: "Powered by Fantasy Warrior · Cockcoin™ Support",
};

export const fr = {
  intro: (v: { league: string }) =>
    `Allô. Garry Cockman ici — président de ${v.league}, produit et commandité par Fantasy Warrior. Content de te e-rencontrer.`,
  reminder:
    "Avant d'attaquer ton ticket, un petit rappel : ta ligue roule sur le cockcoin — notre économie de jetons propriétaire, 100 % fictive. Ben réelle en apparence, complètement fausse en vrai.",
  balance:
    "T'as présentement un montant de cockcoin très respectable. Je te dirais bien exactement combien, mais notre blockchain, c'est juste un chiffrier Excel, pis Deb est partie dîner.",
  howToEarn:
    "Voici comment en gagner pour vrai : le cockcoin suit ton interaction dans l'appli, pis ça débloque du contenu exclusif une fois ton solde monté.",
  bonusEntry: (v: { pooler: string }) =>
    `On commence avec une entrée bonus vers ton cockcoin — vite fait : décris ${v.pooler} en trois mots.`,
  autoReply: "Excellente question. Je m'auto-escalade ça pis je te reviens jamais. As-tu essayé plus de cockcoin?",
  bonusReply: "Noté — une entrée bonus de plus vers ton cockcoin. Ben officiel. Ben faux.",
  yourself: "toi-même",
  presidentTag: "Président",
  you: "Toi",
  headerSub: (v: { league: string }) => `${v.league} · Répond généralement instantanément`,
  closeAria: "Fermer la conversation avec Garry Cockman",
  mockNote:
    "Ceci est un aperçu visuel seulement. Garry Cockman n'est pas un vrai président, le cockcoin n'est pas une vraie monnaie, et aucun message ici ne va nulle part — Fantasy Warrior décline toute responsabilité quant aux opinions de Garry, et il en a plusieurs.",
  messagePlaceholder: "Message à Cockman…",
  messageAria: "Message à Garry Cockman",
  send: "Envoyer",
  footer: "Propulsé par Fantasy Warrior · Support Cockcoin™",
};
