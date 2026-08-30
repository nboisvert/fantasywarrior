---
name: doc-cleaner
description: >
  Audite CLAUDE.md et .claude/doc/*.md pour repérer des contradictions
  (entre deux docs, ou entre un doc et le code réel), de la duplication entre
  domaines, et des résidus d'historique. Corrige directement en traitant le
  code comme la vérité. Déclenche sur "/doc-clean", "nettoie la doc",
  "clean the docs", "vérifie la cohérence de la doc", "audit la doc", ou
  toute demande de vérifier/nettoyer la documentation du projet.
---

# /doc-clean — nettoyeur de documentation

Deux règles au-dessus de tout :

1. **Le code fait foi.** Une doc qui le contredit est fausse et se corrige —
   jamais l'inverse. Ce skill ne touche jamais au code pour le faire concorder
   avec un doc.
2. **Le comportement actuel est la seule vérité qu'on garde.** La doc décrit le
   système tel qu'il est, au présent. L'historique vit dans `git log`, dont les
   messages de commit sont détaillés exprès.

## Usage

```
/doc-clean                # audit complet + corrections
/doc-clean check          # audit seul, aucune correction (dry-run)
/doc-clean mordus.md      # portée réduite à un fichier
```

---

## Étape 1 — Inventaire

`CLAUDE.md` + tous les `.claude/doc/*.md`, **sauf** :

- `ideas/` — espace de brainstorm de l'Engagement Queen, pas une doc factuelle ;
- `cockman-concept.md` — doc de concept assumée, append-only par décision.

## Étape 2 — Vérifier chaque fait contre le code

Pour chaque doc, repère les affirmations **vérifiables** :

- routes d'API (`GET /...`, `POST /...`) → `backend/FantasyWarrior.Api/*Endpoints.cs`
- noms de jobs / valeurs de `case` → le commentaire en tête de
  `backend/FantasyWarrior.Jobs/Program.cs`
- tables, colonnes, vues, contraintes → `backend/FantasyWarrior.Data/Configurations/`
  et les migrations sous `backend/FantasyWarrior.Data/Migrations`
- classes CSS, variables, hooks → `frontend/src/index.css`, `App.css`, `screens/`
- libellés d'UI, noms d'onglets → `frontend/src/App.tsx` et `frontend/src/screens/`
- déclencheurs de déploiement, ordre du cron → `.github/workflows/`
- montants, comptes, codes d'accès de ligue → **pas dans le code** : ils vivent
  en base, donc jamais vérifiables par grep. Compare-les entre docs (étape 3) et
  signale-les comme à confirmer avec Nick s'ils ne sont cités nulle part ailleurs.

Ne devine jamais un comptage. Pour les tests : `dotnet test FantasyWarrior.slnx`,
ou à défaut compter `[Fact]`/`[Theory]`/`[InlineData]` **en notant que c'est une
approximation**. Un chiffre qui pourrit est pire que pas de chiffre — si tu ne
peux pas le mesurer, retire-le.

**Piège connu** : des jobs cités dans la doc ou dans des messages d'erreur
n'existent pas (`set-league-rules`, `sim-reset`, `recompute`). Vérifie toujours
un nom de job contre `Program.cs` avant de le croire, d'où qu'il vienne.

## Étape 3 — Les six frontières

C'est le cœur de l'audit. Chaque fait vit à **un** endroit ; partout ailleurs
c'est un lien. Un fait énoncé dans deux fichiers est un défaut, même si les deux
énoncés concordent aujourd'hui — c'est précisément ainsi qu'ils divergent demain.

| # | Domaine | Propriétaire unique |
|---|---|---|
| 1 | **Statut** — construit / pas construit / risque ouvert | `project_status.md` |
| 2 | **Schéma** — tables, colonnes, index, vues, contraintes | `data-model.md` |
| 3 | **Entre-saison** — phases, protections, vols, repêchage | `offseason.md` |
| 4 | **Chiffres des Mordus** — plafond, slots, taille de roster, barème | `mordus.md` |
| 5 | **Jobs, commandes, runbooks, déploiement** | `deployment.md` |
| 6 | **Couleurs, CSS, mise en page, conventions d'écran** | `design-system.md` |

Conséquence de la frontière 1, à vérifier partout : **aucun doc autre que
`project_status.md` ne dit « fait le \<date\> » ni « pas encore construit ».**
Les autres décrivent le système tel qu'il est. Une limitation réelle s'énonce au
présent (« l'écran de protection n'existe pas, donc un DG ne peut pas contredire
le défaut ») — ce n'est pas la même chose qu'une case à cocher de suivi de projet.

Vérifie aussi :
- la table « Reference docs » de `CLAUDE.md` — chaque doc listé existe, et la
  description résume encore son contenu réel ;
- les renvois « voir X.md §N » — la section existe toujours ;
- aucun lien mort vers un fichier supprimé.

## Étape 4 — Corriger

**Toute correction est silencieuse.** Tu réécris au présent et tu passes à la
suite. Tu n'ajoutes **jamais** un bloc de citation du genre « ce fichier disait X
jusqu'au \<date\> », ni une note de renversement, ni une entrée datée. Cette
convention a existé dans ce projet et c'est exactement ce qui a fait gonfler la
doc à 3 600 lignes dont l'essentiel racontait ce qui n'était plus vrai.

Ce qui survit d'une décision, c'est **sa raison, au présent** :

- ✅ « Un `RosterSpot` tient un joueur ou une franchise. Le coût est une colonne
  nullable et une contrainte CHECK — moins cher qu'un deuxième moteur de
  pointage. »
- ❌ « Le 2026-08-05, Nick a renversé la décision du matin qui gardait la
  franchise hors des `RosterSpots`… »

Le test : **la phrase survit-elle si on en retire toute date ?** Si oui, elle
reste. Sinon elle part — sauf si elle explique pourquoi une alternative évidente
a été écartée, auquel cas réécris-la au présent.

Si un revirement mérite vraiment d'être retenu, sa place est le message de
commit, pas la doc.

## Étape 5 — Purger les résidus d'historique

Cherche et retire :

```
~~texte barré~~   ✅ ⬜ 🟨   « superseded »   « renversé »
« avant le 20.. »   « jusqu'au 20.. »   « la première version de ce document »
« fait le <date> »   « livré le <date> »
```

Retire aussi un contenu qui documente un design **jamais construit** et non
signalé comme volontairement conservé. Si le statut est ambigu — le doc ne dit
pas si c'est un vestige ou une référence assumée — **ne tranche pas** : signale-le
dans le résumé final et laisse le fichier tel quel.

## Étape 6 — Garder les pièges

Un piège n'est pas de l'historique, même s'il a une histoire. Ce sont des mines
vivantes, et elles restent, sous forme d'encadrés courts au présent :

- SQL Server considère deux NULL comme égaux dans un index unique ;
- un NULL dans un `NOT IN` vide tout le résultat en silence ;
- deux appels `HasIndex` sur la même propriété **redéfinissent** un index au lieu
  d'en créer deux ;
- `EnableRetryOnFailure` oblige toute transaction manuelle à passer par
  `db.Database.CreateExecutionStrategy()` ;
- `display: flex` sur un `<td>` le sort de l'algorithme de colonnes du tableau ;
- `position: sticky` sur un descendant imbriqué dans une cellule `colSpan` échoue
  silencieusement.

Si tu hésites entre « piège » et « histoire », demande-toi si un agent qui
l'ignore casserait quelque chose. Si oui, c'est un piège.

## Étape 7 — Résumer et committer

Liste au format court les changements fichier par fichier (une ligne par fichier
touché : ce qui a changé et pourquoi), puis le total de lignes avant → après.
Committe avec un message descriptif ; pas de push automatique vers une branche
que Nick n'a pas désignée pour la session en cours.
