# Phase 4 — Moteur de scoring configurable avec top-X et ajustements de transaction

## Context

Les stats quotidiennes (Phase 2) sont en place; le classement affiche « 0 pts ». Nick a spécifié les règles du moteur de scoring. Objectif : de vrais points au classement, recalculés chaque nuit après le sync des stats, avec des règles **paramétrables par ligue par l'admin (commissioner)**.

## Règles confirmées par Nick (2026-07-22)

1. **Valeurs de points paramétrables** — défauts : but = 1, passe = 1, défaite OT gardien = 1, victoire gardien = 2. Jeu blanc : supporté dans la config, défaut 0 (désactivé).
2. **Top X par position paramétrable** (X pour F, D, G séparés, définis par l'admin; défaut : tous comptent). Groupes : F = C/L/R, D, G.
3. **Le score d'équipe est invariant aux transactions** : score = somme des points des X meilleurs joueurs du roster courant (points **saison complète** du joueur) **± ajustements** créés à chaque transaction pour compenser. Après un échange, le total de l'équipe ne change pas; seule la production future du nouveau joueur profite.
4. **Effet des transactions** : le lendemain de l'acceptation, après la mise à jour des scores. (v1 : add/drop immédiats — équivalent, car les totaux ne bougent que la nuit.)
5. **`countsInScore`** (« compteDansLesPoints ») : le moteur marque, par équipe, quels joueurs sont retenus dans le top X — persisté pour transparence dans l'UI. *(Hypothèse : calculé par le moteur, pas choisi manuellement — à confirmer à la review.)*
6. **Assignments** : historique `(playerId, team, from, to)` conservé pour audit/affichage des « points acquis avec l'équipe » — mais le score se calcule via totaux saison + ajustements.

## Modèle de données (Firestore)

- `leagues/{id}` — ajout `ruleConfig` : `{ pointValues: {goal:1, assist:1, goalieWin:2, goalieOtLoss:1, shutout:0}, topCount: {F:null, D:null, G:null} }` (null = tous comptent). Modifiable par le commissioner via l'API.
- `leagues/{id}/teams/{username}` — ajouts calculés par le job : `score`, `rawTopXScore`, `adjustmentsTotal`, `countedPlayerIds[]`, `playerPoints` (map playerId → pts saison, pour l'UI).
- `leagues/{id}/teams/{username}/adjustments/{auto}` — ledger : `{dateUtc, delta, reason ("add"/"drop"/"trade"), playerIdsIn[], playerIdsOut[]}`.
- `leagues/{id}/assignments/{auto}` — `{playerId, teamUsername, from (YYYY-MM-DD), to (null si actif), source, sourceRefId}`. Ouvert à l'add, fermé au drop.
  - **`source` (demandé par Nick 2026-07-22)** : provenance de l'acquisition — `"draft"` (avec `sourceRefId` = id du pick), `"trade"` (avec `sourceRefId` = id du trade, quand le mécanisme d'échange existera en Phase 6), `"free_agency"` (add manuel), `"initial"` (saisie du roster par le commissioner). `sourceRefId` nullable tant que l'entité référencée n'existe pas encore.
- **Pas de collection de totaux joueurs** : les totaux saison par joueur sont obtenus par **requêtes d'agrégation Firestore** (`sum(goals)`, `sum(assists)`… sur `playerGameStats` où `playerId == X && season == S && gameType == 2`) — sans état, idempotent, ~1 read/1000 entrées. ⚠️ Peut requérir un index composite (le message d'erreur Firestore fournit le lien de création en un clic; le documenter dans project_status).

## Moteur (Core, fonctions pures + tests)

`backend/FantasyWarrior.Core/Scoring/` :
- `RuleConfig` (+ défauts), `PositionGroup.From(position)` (C/L/R→F).
- `ScoringEngine.PlayerPoints(rawTotals, pointValues)` → points fantasy d'un joueur.
- `ScoringEngine.TeamScore(rosterTotals, ruleConfig)` → `{rawTopX, countedPlayerIds}` : classe par groupe de position, retient top X (égalité : points desc, puis playerId asc pour déterminisme), somme.
- Invariant transaction : `adjustment = rawTopXBefore − rawTopXAfter` (calculé au moment du changement de roster).

Tests unitaires : sélection top-X par groupe (dont X=null → tous), égalités, valeurs de points custom, invariance add/drop/trade (score identique avant/après), joueur sans stats.

## Job `score-calc` (Jobs)

`score-calc [--league <id>]` — pour chaque ligue (ou une seule) :
1. Charger `ruleConfig`, équipes et rosters.
2. Totaux saison des joueurs rostered via requêtes d'agrégation (batch parallèle).
3. `TeamScore(...)` par équipe; lire `adjustmentsTotal` (somme du ledger); écrire les champs calculés sur chaque team doc.
4. Log de contrôle (score par équipe).

Intégration cron : `daily-jobs.yml` → stats-sync → **score-calc** → player-sync. (Les transactions différées « lendemain matin » viendront s'insérer ici en Phase 6.)

## API (FantasyWarrior.Api/Program.cs)

- `PATCH /api/leagues/{id}/rules` (commissioner only — vérifie `commissionerUsername` == username fourni) : met à jour `ruleConfig`.
- **Roster add/remove** (endpoints existants) : envelopper dans une transaction Firestore —
  - calculer `rawTopXBefore` (agrégats des joueurs concernés + roster courant), appliquer le changement, calculer `rawTopXAfter`, créer l'entrée d'ajustement si delta ≠ 0;
  - ouvrir/fermer l'assignment (from/to = date du jour ET);
  - v1 pré-saison : deltas = 0 naturellement (aucune stats).
- `GET /api/leagues/{id}` : exposer `score`, `countedPlayerIds`, `adjustmentsTotal`, `playerPoints`, `ruleConfig`. Les standings trient par `score` desc.
- Commande de migration `league-init-assignments` (Jobs) : crée les assignments manquants pour les rosters existants (from = date de création de la ligue).

## UI (frontend)

- **Standings** : trier par `score` réel; afficher les points en `pts` (fini le placeholder); cap total reste en sous-titre; afficher `±adjustments` discret si ≠ 0.
- **Roster** : points saison par joueur (selon la config de la ligue); badge/highlight cyan sur les joueurs `countsInScore` (top X); ligne « Adjustments » dans le résumé d'équipe.
- **Config ligue (commissioner)** : petite section dans la vue ligue (visible commissioner seulement) pour éditer valeurs de points + top X — formulaire simple conforme au design system Night Arena.

## Vérification E2E (avec les données 2025-26 déjà synchronisées)

1. `dotnet test` — moteur (top-X, invariance, égalités).
2. Créer une ligue de test saison `20252026` (API), 2 équipes, rosters avec des joueurs ayant joué du 1-4 avril 2026 (ex. J. Hughes, Marner, Oettinger).
3. `score-calc` → vérifier manuellement les scores vs `stats-check` (ex. Marner 3B+2P le 2 avril = 5 pts avec défauts).
4. Simuler un échange : drop/add croisés entre les 2 équipes → vérifier que les deux scores sont **inchangés** après `score-calc` (ledger d'ajustements créé), puis synchroniser un jour de plus (`stats-sync --date 2026-04-05` + `score-calc`) → seuls les nouveaux points bougent.
5. UI locale : standings triés avec vrais points, joueurs comptés surlignés, config commissioner fonctionnelle.
6. Mettre à jour `project_status.md` + commit + push (CI verte).

## Fichiers touchés

- Nouveau : `Core/Scoring/RuleConfig.cs`, `Core/Scoring/ScoringEngine.cs`, `Core/Leagues/Assignment.cs`, `Jobs/Scoring/ScoreCalcJob.cs`, `Core.Tests/Scoring/ScoringEngineTests.cs`
- Modifiés : `Core/Leagues/League.cs` (ruleConfig), `Core/Leagues/Team.cs` (champs calculés), `Api/Program.cs` (rules PATCH, roster tx, league detail), `Jobs/Program.cs` (score-calc, league-init-assignments), `.github/workflows/daily-jobs.yml`, `frontend/src/screens/Standings.tsx`, `Roster.tsx`, `api.ts` (+ section config commissioner)
