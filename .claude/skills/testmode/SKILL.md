---
name: testmode
description: >
  Rejoue la saison NHL 2025-26 jour par jour dans Fantasy Warrior pour tester le
  pool en conditions réelles. Utilise ce skill dès que Nick dit : avance au
  <date>, avance d'une semaine, mode test, simulation, testmode, où on en est
  dans la simulation, recommence la saison, remets la simulation à zéro, ou
  toute demande de faire progresser le temps simulé. Déclenche aussi sur
  "/testmode".
---

# /testmode — simulation de saison

Rejoue la saison 2025-26 jour par jour. Le système traite chaque soirée comme le
job nocturne l'aurait fait, **sauf** la récupération des statistiques auprès de
l'API NHL : les 51 264 lignes de la saison sont déjà en base.

## Usage

```
/testmode                        # où on en est
/testmode status                 # idem
/testmode advance 2025-11-23     # avance jusqu'à cette date
/testmode advance +7d            # avance d'une semaine
/testmode init                   # remise à zéro, retour à la veille de la saison
/testmode off                    # arrête la simulation, retour au temps réel
```

**Toujours en dry-run d'abord** pour `init` — c'est destructif.

---

## Environnement

Toutes les commandes se lancent depuis `C:\Nick\fw`. Il faut une chaîne de
connexion, prise de `backend/FantasyWarrior.Api/appsettings.Local.json` :

```powershell
$conn = (Get-Content backend\FantasyWarrior.Api\appsettings.Local.json -Raw |
         ConvertFrom-Json).ConnectionStrings.FantasyWarrior
$env:AZURE_SQL_CONNECTION = $conn
```

La date simulée vit dans la table **`SimulationState`**, une seule ligne.
**C'est la seule source de vérité** — jobs, API locale et API déployée la lisent
toutes. Ce fichier ne la contient pas et ne doit jamais prétendre la connaître.

> Ce skill décrivait Firestore jusqu'au 2026-08-04. La migration vers Azure SQL
> (2026-08-02) a supprimé les variables `GOOGLE_APPLICATION_CREDENTIALS` et
> `FIRESTORE_PROJECT_ID`, le document `simulation/clock`, **et les quotas de
> lecture** qui justifiaient de compter les semaines à l'avance.

---

## Étape 1 — Toujours commencer par lire l'état

```bash
dotnet run --project backend/FantasyWarrior.Jobs -- sim-clock
```

Rapporte la date simulée, ou « No simulation running ». **Ne jamais supposer où
en est la simulation** : Nick a pu avancer depuis une autre session.

---

## Étape 2 — `status`

Après `sim-clock`, enrichis avec l'API si elle tourne (sinon dis-le simplement) :

```bash
curl -s http://localhost:5099/api/clock
curl -s "http://localhost:5099/api/leagues/<code>?username=nick"   # <code> = le join code, ex. TKW6UR
```

Rapporte à Nick, en français et brièvement :
- la journée simulée et la semaine courante,
- si la semaine est verrouillée (donc si les alignements de cette semaine sont encore modifiables),
- le classement (3-4 premiers, avec banqué + semaine courante),
- s'il reste des échanges acceptés en attente d'exécution.

---

## Étape 3 — `advance <date>`

```bash
dotnet run --project backend/FantasyWarrior.Jobs -- sim-advance --to 2025-11-23
```

Pour `+7d`, calcule la date cible depuis la date courante lue à l'étape 1.

Depuis un téléphone (ou toute session sans accès à `C:\Nick\fw`), le même job
est aussi exposé en `POST /api/testmode/advance?username=nick&to=2025-11-23`
sur l'API — voir la section « Depuis le mobile » de
[`testmode.md`](../../doc/testmode.md). Réservé à `nick`.

Le job s'arrête **à chaque fin de semaine** traversée : c'est ce qui fait que les
échanges d'une semaine s'exécutent à sa frontière et pas à la fin du saut.

**Avant d'avancer de plus d'une semaine**, préviens Nick :
- s'il y a des **échanges acceptés**, ils partiront à la **première** frontière
  traversée, pas à la fin du saut. Vérifie-les d'abord et dis lesquels — un
  échange accepté avant que le plafond soit appliqué peut mettre une équipe
  hors limites, et l'exécution n'est pas un point de contrôle ;
- **la simulation n'avance que.** Revenir en arrière exige un `init` complet.

Le coût, lui, n'est plus un argument : depuis Azure SQL il n'y a plus de quota
de lectures à ménager.

**Après l'avance**, rapporte : la nouvelle date, les semaines banquées, les
échanges exécutés, et le classement mis à jour. Puis ajoute une ligne au journal
(étape 5).

---

## Étape 4 — `init` (destructif)

> **Il n'y a pas de job `sim-reset`.** Ce fichier en décrivait un jusqu'au
> 2026-08-05; il n'a jamais existé sous Azure SQL. La remise à zéro est une
> séquence de trois jobs, et elle est **plus destructive** que ce que ce
> paragraphe promettait.

```bash
dotnet run --project backend/FantasyWarrior.Jobs -- wipe-pools --dry-run
# montrer le constat, puis :
dotnet run --project backend/FantasyWarrior.Jobs -- wipe-pools
dotnet run --project backend/FantasyWarrior.Jobs -- seed-mordus
dotnet run --project backend/FantasyWarrior.Jobs -- sim-clock --set 2025-10-04 --season 20252026
```

**Montre le dry-run à Nick et attends sa confirmation** avant de lancer
`wipe-pools` sans `--dry-run`.

Ce que `wipe-pools` efface : alignements, échanges, scores, **roster spots,
équipes, ligues et utilisateurs**, plus le banquage de toutes les semaines et
le curseur de simulation. Contrairement à ce que ce fichier affirmait, **les
roster spots et les utilisateurs ne survivent pas** — `seed-mordus` les
recrée depuis `data/mordus-rosters.json`, ce qui est justement pourquoi ce
fichier doit être à jour avant de commencer.

Ce qui est conservé : toutes les données NHL (joueurs, contrats, matchs,
statistiques) et le calendrier des semaines lui-même.

`seed-mordus` refuse d'écraser une ligue existante, donc `wipe-pools` doit
passer avant. **Le code d'accès de la ligue change à chaque re-seed** — il est
tiré au hasard, préviens Nick du nouveau.

Le curseur atterrit **deux jours** avant le début de la saison, pas un : la
journée simulée doit être la veille pour que la semaine 1 reste modifiable.
Pour 2025-26 la semaine 1 commence le 2025-10-06, donc `--set 2025-10-04`.

Après l'init, dis à Nick de saisir les alignements de la semaine 1 dans l'app
avant d'avancer.

---

## Étape 5 — Tenir le journal

Après chaque `advance` ou `init`, ajoute une ligne au tableau de
[`.claude/doc/testmode.md`](../../doc/testmode.md) : date atteinte, semaine, et
ce qui s'est passé (banquage, échanges exécutés, alignements reportés).

Ce journal est purement documentaire — **jamais lu par le code**. Il sert à
retrouver le fil dans l'historique git.

Commit avec le reste du travail, sans le pousser séparément.

---

## Ce qui est simulé et ce qui ne l'est pas

| Simulé | Réel |
|---|---|
| La journée courante de toute l'app | — |
| Le pointage hebdomadaire et le banquage | La synchro des nouvelles et sa purge 30 jours |
| Les verrous d'alignement | `player-sync` (les alignements NHL récupérés) |
| L'exécution des échanges | |
| Les agrégats de saison des joueurs | |

L'étape volontairement sautée : `stats-sync`. Les gamelogs de 2025-26 sont déjà
en base, donc les rejouer serait du gaspillage.

---

## Pièges

- **La simulation n'avance que.** Pour revenir en arrière, il faut `init` et tout
  rejouer. Ne propose jamais un « rewind » : il n'existe pas.
- **Le jour de grâce.** Une semaine n'est banquée qu'un jour après sa fin, pour
  laisser passer les corrections tardives de feuilles de match NHL. Avancer
  jusqu'au dimanche score la semaine sans la banquer; il faut aller au lundi.
- **L'API déployée** (Azure Container Apps) doit être à jour pour que l'app
  mobile respecte la date simulée. Sinon elle croit être au temps réel.
- **La base serverless se met en pause** après une heure d'inactivité : la
  première commande échoue à la connexion, et la reprise prend de dix secondes
  à deux minutes. Réessayer suffit — les jobs ont `EnableRetryOnFailure`.
- **La simulation est globale.** Elle s'applique aux deux ligues et à tous les
  utilisateurs, pas seulement à celle qu'on teste.
