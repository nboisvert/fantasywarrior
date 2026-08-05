# Mode test — rejouer la saison 2025-26

> Documentation et journal. **La date simulée n'est pas ici** — elle vit dans le
> table `SimulationState` (une seule ligne), seule source de vérité. Ce fichier n'est
> jamais lu par le code.
>
> Le skill : [`.claude/skills/testmode/SKILL.md`](../skills/testmode/SKILL.md).
> Les règles de pointage : [`scoring-model.md`](scoring-model.md).

## Pourquoi

Le modèle de pointage hebdomadaire n'avait jamais tourné sur une vraie saison.
Rien n'avait validé le cycle complet : verrouillage du lundi, report des
alignements oubliés, exécution des échanges au rollover, accumulation sur 28
semaines.

On a la saison 2025-26 entière en base — 1 312 matchs, 51 264 lignes. Le mode
test la rejoue jour par jour, en traitant chaque soirée comme le job nocturne
l'aurait fait.

## Ce qui est simulé

**Une seule étape est sautée : `stats-sync`.** Les gamelogs sont déjà en base, aller les rechercher auprès de l'API NHL serait du gaspillage. Tout le reste
s'exécute réellement : agrégats de saison des joueurs, pointage hebdomadaire,
banquage, exécution des échanges, matérialisation de la semaine suivante.

| Sur l'horloge simulée | Volontairement sur l'horloge réelle |
|---|---|
| La journée courante de toute l'app (API incluse) | — |
| Le pointage et le banquage | La synchro des nouvelles et sa purge 30 jours — vrai pipeline de données |
| Les verrous d'alignement | `player-sync` — les alignements NHL récupérés doivent rester les vrais |
| L'exécution des échanges | |
| Les agrégats de saison | |

## Le curseur

`SimulationState.AsOfDate` est **le dernier jour de match dont les résultats
sont connus**. La journée simulée est le lendemain, ce qui reproduit exactement
la relation du monde réel (`lastStatDate = today − 1`) — c'est pourquoi aucun
code de pointage n'a de cas particulier pour la simulation.

Conséquence : pour que la journée simulée soit la veille de la saison, le
curseur est **deux jours** avant le début de la semaine 1. Sinon l'app se croit
au lundi, après le verrou de minuit, et l'alignement d'ouverture est gelé avant
que quiconque ait pu le saisir.

## Commandes

| Besoin | Commande |
|---|---|
| Où on en est | `sim-clock` |
| Avancer | `sim-advance --to 2025-11-23` |
| Repartir du début | `wipe-pools` puis `seed-mordus` et `sim-clock --set 2025-10-04` |
| Revenir au temps réel | `sim-clock --off` |

`sim-advance` **s'arrête à chaque fin de semaine** traversée. C'est ce qui fait
qu'un échange proposé en semaine 5 s'exécute à la frontière de la semaine 5 et
non à la fin d'un saut jusqu'à la semaine 8.

**Coût** : plus de quota de lectures depuis le passage à Azure SQL. Rejouer une
saison complète d'un coup est redevenu une opération ordinaire ; avancer semaine
par semaine reste le rythme de test naturel.

## Deux choses qui surprennent

**Le jour de grâce.** Une semaine n'est banquée qu'un jour après sa fin, pour
laisser passer les corrections tardives de feuilles de match NHL. Avancer
jusqu'au dimanche score la semaine sans la banquer; il faut aller au lundi.

**Pas de retour arrière.** La simulation n'avance que. Pour rejouer un scénario,
il faut `sim-reset` et repartir du début.

---

## Journal

| Date simulée | Semaine | Ce qui s'est passé |
|---|---|---|
| 2025-10-05 | — (veille) | `sim-reset` initial. 23 équipes remises à zéro sur les deux ligues, 447 roster spots conservés, 92 alignements et 9 échanges supprimés, 28 semaines dé-banquées, 1 048 agrégats joueurs effacés. Alignement de la semaine 1 saisi pour `nick` via l'API (9F/4D/1G). |
| 2025-10-12 | 1 | Semaine 1 scorée (nick 24 actifs / 4 au banc), semaine 2 matérialisée par report. Pas encore banquée — jour de grâce. 1 968 lectures. |
| 2025-10-13 | 2 | Semaine 1 **banquée**. A révélé un bug : le classement n'affichait que la semaine courante, le score étant calculé avant le banquage dans le même passage. Corrigé — la finalisation réécrit `score`. |
| … | 3-11 | **Trou dans le journal** : la simulation a été avancée jusqu'au 2025-12-15 sans que ces passages soient consignés. Rien à en dire de fiable. |
| 2026-01-19 | 16 | Saut de 5 semaines depuis le 2025-12-15. **5 semaines banquées** (11 à 15). L'échange 6 (Montréal ↔ Toronto, 6 joueurs) s'est exécuté à la première frontière, effectif au 2025-12-29 — il datait d'avant l'application du plafond, et l'exécution n'est délibérément pas un point de contrôle, donc **Montréal est passé à 128,1 M$ contre un plafond de 115 M$**. Detroit était déjà à 116,1 M$. Ce sont les deux seules équipes hors limites ; le roster min de la ligue est 18, pas 23, donc Colorado à 19 joueurs est conforme. Classement : Los Angeles 560, Floride 549, Montréal 538, Colorado 529. |
| 2026-02-16 | 20 | Saut de 4 semaines depuis le 2026-01-19. **4 semaines banquées** (16 à 19). Les deux échanges acceptés se sont exécutés à la première frontière, effectifs au 2026-02-02 : l'échange 9 (Colorado → Los Angeles : Kris Letang + un choix, contre Shayne Gostisbehere) et l'échange 10 (Colorado → Edmonton : Joel Hofer + Shakir Mukhamadullin, contre Connor Hellebuyck). Colorado est descendu à **18 joueurs, soit exactement le roster min** — la prochaine transaction sortante sera bloquée. Aucune des trois équipes impliquées ne dépasse le plafond après coup (LA 113,4 M$, Colorado 111,3 M$, Edmonton 97,7 M$) ; Montréal (128,1 M$) et Detroit (116,1 M$) restent hors limites depuis l'échange 6. **W19 et W20 sont des semaines de pause** (bris olympique, aucun match) : W19 a été banquée à zéro pour tout le monde et le curseur atterrit au milieu de W20, tout aussi vide. Classement : Los Angeles 674, Colorado 636, Montréal 627, Floride 626. |
