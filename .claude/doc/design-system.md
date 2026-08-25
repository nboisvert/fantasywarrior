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
logo de franchise), donc 9,5 rem n'en est que le plancher : le hook
`useSnapPadding` de `Stats.tsx` mesure la cellule d'en-tête et écrit la vraie
valeur dans `--stats-snap-pad`, la feuille de style ne gardant que le repli.
Ne jamais réinjecter cette mesure dans le `min-width` de la colonne — elle ne
pourrait plus rétrécir, et aucun redimensionnement ne viendrait la corriger.

**Contrepartie à connaître** : sous `mandatory`, on ne peut se poser qu'au début
d'un bloc. Un groupe plus large que l'espace visible (l'écran moins la colonne
du nom) verrait donc sa dernière colonne inatteignable au repos. `Extra`
(+/-, PIM, SOG, GAA, SV%) est le plus large et c'est lui qu'il faut surveiller
si une colonne s'ajoute. Le repli tient en un mot : `mandatory` → `proximity`,
qui laisse se poser entre deux blocs.

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
