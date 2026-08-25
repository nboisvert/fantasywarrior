# Design system — « Night Arena »

> Approuvé par Nick le 2026-07-22. Les **règles** vivent dans `CLAUDE.md` ; ce
> document porte le détail qu'on va chercher au besoin (valeurs exactes,
> typographie, procédure de régénération des assets).

Tokens : `frontend/src/index.css` (variables CSS) · composants :
`frontend/src/App.css` · écrans : `frontend/src/screens/` · icônes SVG :
`frontend/src/components/Icons.tsx`.

## Couleurs

| Rôle | Valeur |
|---|---|
| Fond | `#0a0e1a`, avec des halos radiaux cyan/indigo fixes |
| Surface élevée | `#10162a` |
| Carte « verre » | `rgba(255,255,255,.045)` + bordure 1px `rgba(255,255,255,.09)` + backdrop-blur |
| Accent (dégradé) | ice cyan `#38bdf8` → `#22d3ee` |
| Lueur néon | `rgba(56,189,248,.35)` |
| Danger / dépassement de plafond | rose `#f43f5e` |
| Succès | `#4ade80` (`--success`) |
| Podium du classement | or `#fbbf24`, argent `#c7d2e0`, bronze `#d0885a` |
| Texte | `#f1f5f9` ; atténué `#8b96ab` |

**Positions** : attaquant = ice cyan · défenseur = violet `#a78bfa` (`--defense`)
· gardien = or. Le violet a remplacé l'argent le 2026-07-23 : l'argent était trop
peu contrasté pour la défense.

**Slot Équipe** (`T`, la franchise NHL que possède un DG) = le neutre de la
palette, `#c7d2e0` (`--franchise`), depuis le 2026-08-05. Volontairement **pas**
une quatrième teinte saturée : F/D/G sont des positions et partagent un code de
couleur, une franchise n'en est pas une. Et toutes les teintes restantes veulent
déjà dire autre chose — le vert est « en ligne », le rose est « danger »,
l'orange se confond avec l'or — donc une quatrième se lirait comme un statut
plutôt que comme une nature. Les deux patrons obligatoires existent :
`.roster-pos-pill-t` et `.pos-compact-t`.

Contraste AA au minimum, partout.

## Typographie

**Russo One** pour l'affichage (titres, noms d'équipe, chiffres, majuscules) et
**Chakra Petch** pour le corps de texte. Google Fonts, chargées dans `index.html`.

## Forme et mouvement

Rayons de 12 à 16 px. Transitions de 150 à 300 ms, et **uniquement** sur
`color`/`opacity`/`filter` — jamais rien qui déplace la mise en page au survol.
`fade-in` de 250 ms au montage d'un écran. `prefers-reduced-motion` respecté.

## Mise en page

Mobile d'abord, contenu à 680 px de large maximum. Barre de navigation basse
fixe à 64 px + `env(safe-area-inset-bottom)`, **4 onglets** : GM Office (défaut,
clé interne `dashboard`), Standings, Team, Trades — les réglages vivent dans un bouton-icône de la barre
du haut, pas dans la navigation. Le rembourrage bas du contenu doit dégager la
navigation.

Barre du haut collante et floutée, dans cet ordre : logo, sélecteur de ligue,
puis **collés à droite** l'icône de messagerie et le menu profil. Le sélecteur
porte le nom de la ligue seul, sans la saison — elle ne change pas d'une semaine
à l'autre et ne payait pas sa largeur (2026-08-03). L'icône de messagerie est
**nue**, sans pastille de fond : la pilule du profil est juste à côté, et deux
pilules côte à côte se lisent comme un contrôle segmenté plutôt que comme une
identité et une action.

Le compteur vert du profil ne compte **que les autres** — s'y inclure faisait un
badge qui ne descendait jamais sous 1, donc « 1 en ligne » et « personne » se
ressemblaient. Dans le menu, la liste des GMs passe avant Settings/Log out, qui
deviennent un pied de menu compact (36 px, sous la cible habituelle de 44 px,
assumé).

**Grille de la page Team — un bloc de stats par swipe (2026-08-25).** Les
vingt colonnes chiffrées défilent horizontalement sous la colonne collante du
nom, et elles s'arrêtent maintenant **bloc par bloc**, jamais à cheval sur deux
catégories : Fantasy point, Record, NHL, Extra, Cap hit. C'est du `scroll-snap`
CSS, pas du JavaScript — `scroll-snap-type: x mandatory` sur
`.stats-grid-scroll` garantit le point d'arrêt, `scroll-snap-stop: always` sur
`.stats-group-start` fait qu'un geste avance **exactement un** bloc plutôt que
le nombre que son élan porterait. Aucun balisage nouveau : la classe
`.stats-group-start` marquait déjà la première colonne de chaque groupe dans
l'en-tête, chaque rangée et le pied.

Le `scroll-padding-left` doit valoir la largeur de la colonne collante du nom,
sinon le bloc atterrit **dessous** et sa première colonne sort de l'écran —
c'est exactement ce qui s'est produit au premier jet, avec un 9,5 rem codé en
dur. Cette largeur est dictée par le contenu (nom long, marqueur de blessure,
logo de franchise), donc 9,5 rem n'en est que le plancher.

**Contrepartie à connaître** : sous `mandatory`, on ne peut se poser qu'au début
d'un bloc. Un groupe plus large que l'espace visible (l'écran moins la colonne
du nom) verrait donc sa dernière colonne inatteignable au repos. `Extra`
(+/-, PIM, SOG, GAA, SV%) est le plus large et c'est lui qu'il faut surveiller
si une colonne s'ajoute. Le repli tient en un mot : `mandatory` → `proximity`,
qui laisse se poser entre deux blocs.

**Une seule largeur pour les trois grilles de l'écran (2026-08-25).** Roster,
Departed et Incoming sont trois `<table>` distincts, et `table-layout: auto`
donne à chacun sa propre largeur de colonne — même balisage, mais le nom le
plus long de *sa* liste la fixe. Vérifié en direct (Chrome headless, DOM
mesuré) : sur une même équipe, la colonne du nom faisait 211 px sur Roster et
188 px sur Departed, un vrai saut visible en descendant d'une section à
l'autre — c'est le décalage que Nick a signalé entre le nom et le toggle.

Deux causes empilées, deux correctifs :
- **Departed et Incoming n'affichent jamais le toggle actif/banc** (rien à
  activer pour un joueur parti ou pas encore arrivé) — la ligne avait donc une
  icône de moins que Roster. `.stats-toggle-spacer` (même boîte que
  `.lineup-toggle`, vide) comble ce trou dans les deux grilles, pour que la
  structure soit identique partout.
- **Même structure ne garantit pas même largeur** : chaque table calcule
  toujours sa colonne à partir de ses propres noms. Le hook
  `useSharedPlayerColumnWidth` de `Stats.tsx`, posé sur la `<section>` racine
  de l'écran (pas sur une grille), mesure la cellule la plus large **à
  travers les trois grilles montées** et écrit une seule valeur dans
  `--stats-player-w`. `.stats-col-player` s'y range avec
  `width: var(--stats-player-w)` — un vrai `width`, pas juste un `min-width`,
  sinon rien ne forcerait les trois tables à s'aligner. Tant que la variable
  est absente (`var()` invalide sans repli → valeur initiale `auto`), le
  `min-width: 9.5rem` gouverne seul — c'est justement l'état non contraint
  dont le hook a besoin pour mesurer.
- **`--stats-snap-pad` a disparu**, remplacé par `--stats-player-w` réutilisé
  directement pour `scroll-padding-left` : une fois que toutes les colonnes
  partagent la même largeur forcée, il n'y a plus qu'une valeur à connaître,
  pas deux à garder synchronisées. L'ancien hook par-grille
  (`useSnapPadding`) est parti avec elle — il aurait de toute façon mesuré
  *avant* que le hook partagé (posé sur le parent) n'ait forcé la largeur,
  React exécutant les effets des enfants avant ceux du parent.

**Piège rencontré, à ne pas répéter** : la première tentative posait
`position: sticky` sur un `<span>` niché dans une cellule à `colSpan`. Ça ne
collait pas — measuré en direct, l'élément restait à sa position de flux
normal (x≈1000px) au lieu de x≈17px. La colonne `.stats-col-player` elle-même
colle très bien (elle porte les noms de joueurs depuis le début) ; c'est
spécifiquement la stickiness d'un descendant *imbriqué* dans une cellule
`colSpan` qui échoue silencieusement. Le correctif : ne jamais réinventer un
mécanisme sticky quand un autre marche déjà dans le même tableau — voir
« Prospects » ci-dessous, qui réutilise `.stats-col-player` au lieu d'un
`position: sticky` maison.

**`.stats-col-player` ne doit jamais porter `display: flex` (2026-08-25).**
Nick l'a repéré zoomé sur son téléphone : un fin liseré vertical entre les
bordures de rangée de la colonne collante et celles des colonnes chiffrées.
Mesuré en direct (un décodeur PNG écrit à la main, `System.Drawing` ayant
refusé de répondre ce jour-là) : les bordures de la colonne du nom
atterrissaient 1 à 3 px plus haut que celles du reste de la rangée — sous 1 px
CSS une fois ramené à la densité de l'écran, donc invisible à l'œil nu, mais
bien réel au zoom. Un `<td>` en `display: flex` **sort de l'algorithme de
suivi des colonnes du tableau** (déjà noté plus haut pour l'en-tête, mais la
portée était sous-estimée) : chaque cellule de la colonne se met alors à
arrondir sa propre hauteur indépendamment sous `border-collapse`, au lieu de
partager la hauteur véritable de sa rangée avec ses voisines.

Le correctif retire `display: flex` de `.stats-col-player` lui-même — qui
redevient une cellule de tableau ordinaire — et déplace la mise en page flex
(icône-ou-espace réservé, nom, bouton calendrier, pastille de position) sur
un `<div className="stats-col-player-inner">` à l'intérieur. C'est exactement
le principe que l'en-tête `rowSpan={2}` appliquait déjà (« le bouton fournit
sa propre mise en page interne, la cellule n'a pas besoin de l'être ») —
étendu maintenant à chaque cellule collante : la rangée de joueur, la rangée
« Prospects » et le pied « Total ». La surcharge `display: table-cell` du
thead est devenue inutile et a été retirée ; il ne reste que le
`vertical-align: middle` qui centre « Player » sur les deux rangées d'en-tête
fusionnées. Vérifié après coup sur les 34 rangées de la page : écart de
**0 px exactement** entre chaque cellule collante et sa voisine, contre
0,5-1 px avant.

**Zone prospect, en bas de la grille Team (2026-08-25).** Les joueurs sans aucun
match de carrière LNH (règle et provenance dans
[data-model.md](data-model.md)) sont épinglés **sous** le roster, dans un ordre
fixe **F → D → G puis nom**, et ils sont **hors tri** : cliquer sur une colonne
ne les réordonne pas. C'est délibéré — un DG qui classe son roster par points ne
classe pas vingt joueurs qui n'ont jamais joué, et une zone qui garde toujours
la même tête est la seule qu'une bordure peut honnêtement délimiter; trier
dedans ferait bouger cette bordure sous le pouce.

La zone s'ouvre par une **rangée dédiée**, pas seulement une bordure — le mot
« PROSPECTS » à la même proportion que le bandeau « Trade Alert » du ticker
(16 px, police minuscule en gras, lettres très espacées) pour que les deux se
lisent comme le même *type* de marque, sans en partager la couleur : le or
veut déjà dire « trade » ailleurs sur l'écran, l'ice veut déjà dire « chiffre
vedette » sur la colonne PT deux centimètres plus loin, et emprunter l'un ou
l'autre ici attribuerait une signification qui n'existe pas. Le texte muté dit
« section », rien de plus.

**Sans bordure (2026-08-25).** La teinte de fond seule marque la rangée —
`border-top: none`, à outrepasser explicitement le séparateur ordinaire de
1 px que chaque rangée hérite sinon (`.stats-grid tbody tr + tr td`, plus
spécifique qu'une simple absence de déclaration). Et le mot ne colle plus à
la bordure collante : `padding-left: 0.5rem` sur `.stats-prospect-label-cell`,
la même valeur que le rembourrage ordinaire des cellules — mais elle aussi
doit être écrite à une spécificité qui bat la règle de la rangée
(`.stats-grid tbody tr.stats-prospect-label-row td` remet tout à zéro à
`(0,2,3)`), sans quoi le padding s'écrit et ne s'applique jamais.

## Messagerie (2026-08-03)

Les DMs vivent dans une **feuille plein écran**, préfixe `chat-`
(`components/ChatSheet.css`), et **dans** le thème — contrairement à
`CockmanChat.css`, qui est autonome et détonne exprès parce qu'il joue un widget
tiers greffé sur l'app. Une feuille pour deux vues qui s'échangent, liste ↔ fil,
même forme que les deux panneaux du `ProfileMenu`.

Deux portes, deux destinations distinctes — la règle « pas de destination
dupliquée » tient : la bulle de la barre du haut ouvre la **liste** (et porte la
pastille de non-lus, en `--ice` et non en or, l'or restant réservé à « une offre
t'attend »), tandis que le bouton message d'une ligne GM du `ProfileMenu` ouvre
**un fil précis**.

Ligne de conversation : avatar avec pastille de présence, nom, heure à droite ;
2e ligne l'aperçu du dernier message (préfixé « You: » si c'est le nôtre) et la
pastille de non-lus. Les pop-ups d'événements live sont préfixées `toast-` et se
placent **au-dessus** du bandeau d'actualités, jamais par-dessus.

## Logo

Écusson circulaire : guerrier barbu, casque rouge, bâtons croisés. Master
`fw_logo.png` à la racine du dépôt (1024 px, transparent). Asset applicatif
`frontend/src/assets/logo.webp` (512 px, nettoyé et recadré). Utilisé dans le
héros de connexion (180 px, ombre portée cyan) et dans les barres du haut (30 px).

## Icônes d'écran d'accueil et PWA (2026-07-25)

`frontend/public/manifest.json` (lié dans `index.html`, `name`/`short_name`
« Fantasy Warrior », `display: standalone`, `background_color` et `theme_color`
à `#0a0e1a`) fait qu'« Ajouter à l'écran d'accueil » installe une vraie icône
d'application plutôt qu'un raccourci de navigateur dans une boîte blanche.

Tous les assets sont régénérés depuis `logo.webp` via Pillow — recadrage sur la
boîte englobante du contenu, centrage, redimensionnement. **Si le logo change,
les régénérer de la même façon.**

| Fichier | Taille | Fond | Rôle |
|---|---|---|---|
| `favicon.png` | 64 | transparent | onglet du navigateur seulement |
| `favicon-192.png` | 192 | transparent | manifest, `purpose: "any"` |
| `favicon-512.png` | 512 | transparent | manifest, `purpose: "any"` |
| `maskable-icon.png` | 512 | **`#0a0e1a` opaque** | manifest, `purpose: "maskable"` |
| `apple-touch-icon.png` | 180 | **`#0a0e1a` opaque** | iOS |

Les deux dernières **ne doivent jamais être transparentes ni blanches**. Sur
`maskable-icon.png`, le logo est réduit à ~72 % pour survivre au recadrage de la
zone de sécurité des icônes adaptatives d'Android ; un fond transparent est
exactement ce qui produisait l'ancien rendu « cercle blanc avec badge Chrome ».
iOS ne compose pas l'alpha de façon fiable sur les icônes tactiles, d'où le fond
opaque sur `apple-touch-icon.png` aussi.

`index.html` porte également les balises `apple-mobile-web-app-capable`,
`-status-bar-style` et `-title` pour un lancement propre en mode autonome sur iOS.
