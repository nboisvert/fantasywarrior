---
name: doc-cleaner
description: >
  Audite CLAUDE.md et .claude/doc/*.md pour repérer des contradictions
  (entre deux docs, ou entre un doc et le code réel), corrige directement en
  traitant le code comme la vérité, et garde project_status.md court en
  archivant les vieilles entrées de son journal de décisions. Déclenche sur
  "/doc-clean", "nettoie la doc", "clean the docs", "vérifie la cohérence de
  la doc", "audit la doc", ou toute demande de vérifier/nettoyer la
  documentation du projet.
---

# /doc-clean — nettoyeur de documentation

Le code fait foi. Une doc qui le contredit est fausse et se corrige — jamais
l'inverse. Ce skill ne touche jamais au code pour le faire concorder avec un
doc.

## Usage

```
/doc-clean                # audit complet + corrections
/doc-clean check          # audit seul, aucune correction (dry-run)
/doc-clean mordus-pool.md # portée réduite à un fichier
```

---

## Étape 1 — Inventaire

Liste `CLAUDE.md` + tous les `.claude/doc/*.md` **sauf** `ideas/` (espace de
brainstorm de l'Engagement Queen, pas une doc factuelle) et
`decisions-archive.md` lui-même (c'est une sortie de ce skill, pas une
entrée — voir étape 6).

## Étape 2 — Vérifier chaque fait contre le code

Pour chaque doc, repère les affirmations **vérifiables** :

- routes d'API (`GET /...`, `POST /...`) → `backend/FantasyWarrior.Api/*Endpoints.cs`
- noms de jobs / valeurs de `case` → le commentaire en tête de
  `backend/FantasyWarrior.Jobs/Program.cs`
- tables, colonnes, vues, contraintes → migrations EF Core sous
  `backend/FantasyWarrior.Data/Migrations`
- libellés d'UI, noms d'onglets, textes visibles → `frontend/src/App.tsx` et
  `frontend/src/screens/`
- ordre du cron nocturne → `.github/workflows/daily-jobs.yml`
- montants, comptes, codes d'accès de ligue, identifiants → pas dans le
  code : vivent en base, donc **jamais vérifiables par grep**. Compare-les
  plutôt entre docs (étape 3) et signale-les comme à confirmer avec Nick s'ils
  ne sont cités nulle part ailleurs.

Utilise Grep/Read pour chaque affirmation choisie ; ne devine jamais un
comptage (ex. nombre de tests) — `dotnet test` (ou à défaut compter
`[Fact]`/`[Theory]`+`[InlineData]`, en notant que c'est une approximation si
`dotnet` n'est pas disponible dans l'environnement).

## Étape 3 — Cohérence inter-docs

Le même fait cité dans deux fichiers doit concorder — y compris **dans un
même fichier** entre deux sections écrites à des moments différents (ex. le
code de ligue de `project_status.md` a un jour désigné son propre
prédécesseur sans que la phrase du dessus soit mise à jour — la
contradiction la plus facile à manquer est celle qu'on ne pense pas à
chercher parce qu'elle est dans le même document que sa correction).

Vérifie aussi :
- la table « Reference docs » de `CLAUDE.md` (chaque doc listé existe, la
  description résume encore son contenu réel) ;
- les renvois « voir X.md §N » (la section N existe toujours) ;
- les liens vers `decisions-archive.md` une fois qu'il existe.

## Étape 4 — Corriger

**Un fait périmé ordinaire** (code de ligue, nombre de tests, libellé d'un
onglet, chemin de fichier déplacé) → correction silencieuse, sans
commentaire ni bloc de citation.

**Un revirement de conception qui vaut la peine d'être retenu** (le genre de
décision qui, si elle se reproduit, ferait perdre du temps à la retrouver) →
utilise le bloc de citation déjà en usage dans ce projet :

> Ce fichier disait X jusqu'au DATE. [raison du changement].

Exemples déjà en place à imiter, pas à dupliquer : `testmode.md:1-9` (migration
Firestore → Azure SQL), `testmode.md` et le skill `/testmode` sur le faux job
`sim-reset`, `mordus-pool.md §3` sur le renversement du slot Équipe,
`scoring-model.md` sur le faux job `set-league-rules`,
`news-integration-guide.md` sur les sélecteurs CSS corrigés v2/v3.

Ne jamais ajouter un bloc de citation pour un simple chiffre qui a changé —
ça noierait les vrais revirements de conception sous du bruit.

## Étape 5 — Purger le mort, avec prudence

Un contenu qui documente un design **jamais construit** et non signalé comme
volontairement conservé (contrairement à `news-integration-guide.md`, dont
l'en-tête explique déjà pourquoi le guide Python original est gardé tel
quel comme référence) est un candidat à condenser ou retirer. Si le statut
est ambigu — le doc ne dit pas explicitement si c'est un vestige ou une
référence assumée — ne tranche pas : signale-le dans le résumé final et
laisse le fichier tel quel.

## Étape 6 — `project_status.md` compact

Le journal de décisions ne garde que les entrées récentes — repère la
coupure au jugement (typiquement les 5 à 10 derniers jours, ou assez pour
que le fichier reste sous ~250-300 lignes), **par catégorie** (Architecture,
Scoring, UI, Trades, Product) plutôt qu'un seuil global : une catégorie peu
active ne doit pas se retrouver vide simplement parce que sa dernière entrée
est un peu plus vieille que celles d'une catégorie très active.

Le reste part **verbatim**, dans l'ordre (le plus récent en premier), vers
[`.claude/doc/decisions-archive.md`](../../doc/decisions-archive.md) — créé
s'il n'existe pas encore, sinon les nouvelles entrées archivées s'ajoutent
au bon endroit de chaque section. **Jamais de suppression** : une décision
qui sort de `project_status.md` doit être retrouvable dans l'archive, mot
pour mot. Laisse un pointeur d'une ligne en tête du journal de décisions de
`project_status.md` vers l'archive.

Si une entrée archivée a depuis été explicitement renversée par une entrée
plus récente restée dans `project_status.md`, ajoute une note d'une ligne
dans l'archive plutôt que de réécrire l'entrée d'origine.

## Étape 7 — Résumer et committer

Liste au format court les changements fichier par fichier (une ligne par
fichier touché : ce qui a changé et pourquoi). Committe avec un message
descriptif ; pas de push automatique vers une branche que Nick n'a pas
désignée pour la session en cours.
