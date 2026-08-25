# Cycle de vie d'une saison — conception

> **Statut : conception, rien n'est construit.** Aucune colonne `Phase`
> n'existe, aucune transition n'est codée. Ce document fixe la forme avant
> qu'on écrive la première ligne, parce que la première version envisagée
> effaçait des données irrécupérables (§4).
>
> Idée de Nick, 2026-08-25. À faire vivre : quand une phase est construite,
> elle passe de « proposé » à « fait » ici même.

## Le problème

L'app n'a **jamais franchi une frontière de saison**. Les Mordus est pourtant
un pool keeper : les rosters se reportent, les points repartent à zéro, et
entre les deux se tient un repêchage avec protections et vols
([scoring-model.md](scoring-model.md) §11).

Deux choses manquent, et ce sont deux choses **différentes** :

1. **« Les points de quelle saison comptent ? »** — aujourd'hui personne ne
   pose la question. `vStandings` et `vRosterSpotTotals` somment *toutes* les
   `RosterAssignments` d'une équipe depuis toujours. Au premier jour de
   2026-27, le classement afficherait encore les points de 2025-26.
2. **« Qu'est-ce que l'app permet en ce moment ? »** — les échanges sont-ils
   ouverts, la fenêtre de protection est-elle fermée, le repêchage roule-t-il.
   Rien de tout ça ne se lit dans un calendrier ; c'est une décision que
   quelqu'un prend.

---

## 1. Deux colonnes, deux questions

| Colonne | Répond à | L'avancer fait quoi |
|---|---|---|
| **`Leagues.Season`** (existe déjà) | Les points de quelle saison comptent | **C'est la remise à zéro.** Une écriture, réversible. |
| **`Leagues.Phase`** (à créer) | Ce que l'app permet en ce moment | Ouvre et ferme des mécanismes |

Les séparer est le cœur de la conception. Fondues en une seule, on ne pourrait
plus afficher le classement final de la saison écoulée pendant l'entre-saison —
c'est-à-dire précisément au moment où les DG le regardent le plus.

### Pourquoi une colonne `Phase` alors que `Period` s'en passe

`Period` dit explicitement qu'il n'a **aucune colonne de statut** : à venir /
en cours / terminée se dérivent des dates et ne feraient que diverger si on les
stockait. Le même raisonnement s'applique ici — mais seulement à moitié.

- **« La saison se joue-t-elle ? »** *est* dérivable : aujourd'hui tombe-t-il
  dans une `Period` de `League.Season`.
- **« Où en est-on dans le programme d'entre-saison ? »** ne l'est pas. La
  fenêtre de protection ferme quand le commissaire le décide, pas à une date
  que le calendrier LNH connaît. Le repêchage commence quand 14 personnes sont
  disponibles un mardi soir.

`Phase` porte donc la deuxième moitié, celle qu'aucune date ne peut répondre.
La première moitié reste dérivée, et les deux doivent s'accorder — voir §5.

---

## 2. Les phases

```
InSeason ──(dernière semaine banquée)──> OffSeason
                                             │
                                             ▼
                                        Protecting ──(verrouillage)──> Drafting
                                                                          │
                                                                          ▼
                                    InSeason <──(Season++)── PreSeason <──┘
```

| Phase | Ce qui est ouvert | Ce qui est fermé |
|---|---|---|
| **InSeason** | Alignements hebdo, échanges, pointage | Protections, repêchage |
| **OffSeason** | Échanges | Alignements (plus de semaines), protections pas encore ouvertes |
| **Protecting** | Le DG choisit ses protégés | **Échanges gelés** — voir §3 |
| **Drafting** | Les vols, à tour de rôle | Échanges, protections |
| **PreSeason** | Échanges, réparation de roster sous le minimum | Alignements (semaine 1 pas commencée) |

`PreSeason` existe parce qu'une équipe peut sortir du repêchage sous
`RosterMin` — elle a perdu deux joueurs et n'en a repêché qu'un — et qu'il faut
une fenêtre pour se remettre en règle avant que le pointage reprenne.

---

## 3. Ce que chaque phase change, et où

Toutes ces vérifications ont déjà un endroit où atterrir.

| Mécanisme | Où le contrôle se branche | Règle |
|---|---|---|
| Échanges | `TradeValidation.Validate` — **prend déjà `League` en paramètre** | Refusés en `Protecting` et `Drafting` |
| Alignements | `LineupEndpoints`, verrou de période | Inchangé : le verrou de semaine suffit, il n'y a pas de semaine hors saison |
| Protections | à écrire | Modifiables en `Protecting` seulement |
| Vols | à écrire | En `Drafting` seulement |
| `protection-reset` | existe | Joué en entrant dans `InSeason` |

### Le gel des échanges n'est pas cosmétique

Un échange ferme un spot et en ouvre un neuf. Le spot neuf **n'hérite d'aucune
protection** — le joueur deviendrait exposé sans que personne ne l'ait décidé.
Ce n'est pas un détail d'ergonomie, c'est une faille : le gel entre le
verrouillage des protections et la fin des vols est ce qui la ferme.

---

## 4. Ce qu'on ne fait **pas** : effacer les `RosterAssignments`

La première version de l'idée effaçait les assignations de la saison écoulée au
moment du rollover. Ça remet bien le classement à zéro — et ça coûte quatre
choses.

**Ça ne touche pas que le classement.** `vRosterSpotTotals` lit les mêmes
lignes. En keeper, un `RosterSpot` survit à la saison : effacer 2025-26 met la
colonne PTS de l'écran Team à 0 **pour un joueur encore sur le roster**, et rend
« combien ce joueur m'a rapporté depuis que je l'ai » sans réponse pour
toujours. C'est la question d'un pool à vie.

**Ça contredit l'invariant central.** `RosterAssignment` est *le* grain honnête
du modèle, et toute la mécanique de banque existe pour qu'une semaine appartienne
définitivement à qui a aligné le joueur ([scoring-model.md](scoring-model.md)
§4). Effacer au rollover dit l'inverse.

**C'est irréversible là où ça compte.** `PlayerGameStats` survit (≈50 000
lignes), donc on pourrait rejouer — mais **sous le barème d'aujourd'hui**. Or
changer le barème ne recalcule jamais le passé ; un efface-et-rejoue
recalculerait tout. Le chiffre banqué est le procès-verbal de ce qui était vrai,
pas un cache reconstructible.

**Ça n'achète rien.** 5 434 lignes au 2026-08-25 (11 semaines banquées, 2
ligues), ≈14 000 pour une saison complète, contre 50 000 `PlayerGameStats`. Il
n'y a ni problème d'espace ni problème de vitesse à résoudre.

Et en creux : `CLAUDE.md` pose l'interaction entre DG comme l'attrait principal.
« Le classement final de la saison 3 », « les 5 meilleures semaines de
l'histoire du pool », « X n'a jamais fini devant Y en quatre ans » sont tous à un
`WHERE` près tant que les lignes restent, et impossibles sinon.

---

## 5. Le vrai correctif : scoper les vues

Les points repartent à zéro parce que **le filtre bouge**, pas parce que la
donnée meurt.

Dans `vStandings`, le CTE `Scoring` :

```sql
FROM [RosterAssignments] a
JOIN [RosterSpots] sp ON sp.[RosterSpotId] = a.[RosterSpotId]
JOIN [Periods]     p  ON p.[PeriodId]      = a.[PeriodId]
JOIN [Teams]       t  ON t.[TeamId]        = sp.[TeamId]
JOIN [Leagues]     l  ON l.[LeagueId]      = t.[LeagueId]
WHERE p.[Season] = l.[Season]
```

Le même filtre dans `vRosterSpotTotals`. `vTeamPeriodScores` est déjà par
période, donc déjà scopable par la saison de la période.

**C'est un bug latent aujourd'hui, indépendamment des phases** : les deux vues
sont fausses dès la première `Period` d'une deuxième saison, même sans
repêchage. À corriger avant tout le reste.

### La conséquence à assumer

`vStandings` joint `PlayerContracts` sur `l.Season`. Avancer `League.Season`
reprice donc tout le plafond au même instant — c'est correct (les contrats de la
nouvelle saison sont les bons), mais ça arrive d'un coup et il faut le voir une
fois avant que 14 personnes le découvrent.

C'est aussi pourquoi **`Season` s'avance en dernier**, en entrant dans
`InSeason`, et non à l'ouverture de l'entre-saison : jusque-là le classement
affiche encore la saison qui vient de finir, ce qui est exactement ce qu'on veut
regarder en juillet.

---

## 6. Ordre de construction

1. **Scoper `vStandings` et `vRosterSpotTotals`** — une migration, aucune
   dépendance sur le reste. Corrige un bug qui existe déjà.
2. `period-init --season 20262027` — le repêchage doit dater les spots qu'il
   ouvre sur une semaine 1 qui existe.
3. `Leagues.Phase` + les transitions + le contrôle dans
   `TradeValidation.Validate`.
4. La phase `Protecting` : l'écran de sélection, le verrouillage,
   l'auto-remplissage des silencieux.
5. La phase `Drafting` : l'ordre, les vols, les quotas.
6. `PreSeason` → `InSeason` : `Season++` et `protection-reset`.

## 7. Encore ouvert

- **`TradeSchedule.NextPeriodStart` retourne `null`** passé la dernière semaine
  d'une saison, donc aucun échange n'est possible en `OffSeason` ni en
  `PreSeason` — alors que le tableau du §2 les dit ouverts. Il faut qu'il sache
  atteindre la semaine 1 de la saison suivante.
- **Combien de joueurs protégeables par DG**, et **combien de pertes maximum
  par équipe** — les deux chiffres manquent encore
  ([mordus-pool.md](mordus-pool.md)).
- **Qui déclenche une transition ?** Le commissaire à la main, ou le job
  nocturne quand la dernière semaine banque. Les deux se défendent ; la main est
  plus simple et plus sûre pour une première saison.
- **Les listes de protection sont-elles publiques** avant le repêchage ? C'est
  une décision de produit, pas de modèle, et elle change beaucoup l'ambiance.
