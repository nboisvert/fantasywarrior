# News Integration Guide — Rotowire + FantasySP

> Supplied by Nick 2026-07-29 after the first live `news-sync` run showed 0
> items from both sources. Written against a Python/BeautifulSoup stack as a
> reference — the actual implementation (`backend/FantasyWarrior.Jobs/News/`)
> is C# (RssNewsClient for Rotowire's RSS, FantasySpScraper + HtmlAgilityPack
> for FantasySP's HTML table), but follows this guide's URLs, page
> structures, and constraints. Kept verbatim below for reference; see
> `project_status.md` for what's actually built vs. still a placeholder
> (e.g. the "Autres endpoints RotoWire utiles" section — rumors, free
> agents, lineups — isn't wired up yet).

---

# Guide d'implémentation — Feed Fantasy Hockey (RotoWire + FantasySP)

## Objectif
Construire un pipeline qui agrège les nouvelles fantasy NHL (blessures, transactions, changements de trios) depuis deux sources et les normalise dans un format JSON unifié, consultable via une base de données ou un fichier.

## Contraintes générales
- Respecter les `robots.txt` et conditions d'utilisation des deux sites avant tout scraping en production.
- Ajouter un `User-Agent` identifiable et un délai (rate limiting) entre requêtes — 1 req/2-3 sec minimum.
- Ce pipeline est pour un usage personnel/non commercial. Toute redistribution ou usage commercial du contenu RotoWire/FantasySP nécessite une licence (voir leur page `/partners`).
- Ne jamais republier le texte "ANALYSE" de RotoWire (verrouillé par abonnement) sans y être autorisé.

---

## Système 1 : RotoWire (flux RSS + fallback HTML)

### Étape 1 — Flux RSS (source primaire, préférée)
- URL : `https://www.rotowire.com/rss/news.php?sport=NHL`
- Format : XML standard RSS 2.0
- Pas de clé API requise, gratuit pour usage blog/personnel

**Implémentation :**
```python
import feedparser

feed = feedparser.parse("https://www.rotowire.com/rss/news.php?sport=NHL")
for entry in feed.entries:
    item = {
        "title": entry.title,
        "link": entry.link,
        "published": entry.published,
        "summary": entry.summary,  # peut contenir du HTML, à nettoyer
        "source": "rotowire_rss"
    }
```
- Nettoyer le HTML dans `summary` avec `BeautifulSoup(entry.summary, "html.parser").get_text()`.
- Le flux RSS ne contient PAS la section "ANALYSE" (contenu premium) — seulement le fait brut.
- Poller ce flux toutes les 5-15 minutes suffit largement (fréquence de publication : plusieurs items/jour en saison, quasi nul hors-saison sauf transactions).

### Étape 2 — Scraping HTML de secours (si RSS insuffisant en détail)
- URL cible : `https://www.rotowire.com/hockey/news.php?view=injuries` (et variantes `?view=top`, `?team=XXX`, `?level=majors`)
- Chaque item de nouvelle suit cette structure DOM répétée :
  - Logo d'équipe (image)
  - Nom du joueur (lien vers page joueur, contient l'ID RotoWire dans l'URL, ex : `connor-bedard-6916`)
  - Titre court de la nouvelle (headline)
  - Position + nom d'équipe
  - Type de blessure (libellé court : "Shoulder", "Upper Body", etc.)
  - Date (format "Month Day, Year")
  - Paragraphe de nouvelle brute (texte factuel, avec lien vers la source/journaliste)
  - Bloc "ANALYSIS" → verrouillé par abonnement, ignorer ou marquer `analysis_locked: true`

**Sélecteurs à cibler (structure approximative, à valider en inspectant le DOM réel) :**
- Chercher les blocs contenant une image `teamlogo` suivie d'un lien `/hockey/player/`
- Extraire le texte suivant le nom du joueur jusqu'au prochain bloc `teamlogo`

**Implémentation suggérée :**
```python
import requests
from bs4 import BeautifulSoup

HEADERS = {"User-Agent": "Mozilla/5.0 (compatible; FantasyNewsBot/1.0)"}

def scrape_rotowire_injuries():
    url = "https://www.rotowire.com/hockey/news.php?view=injuries"
    resp = requests.get(url, headers=HEADERS, timeout=10)
    soup = BeautifulSoup(resp.text, "html.parser")
    # Identifier les blocs de nouvelles (à ajuster selon la classe CSS réelle,
    # inspecter via navigateur : chercher un conteneur répété par nouvelle)
    news_items = soup.select("div.news-item")  # PLACEHOLDER — vérifier la vraie classe
    results = []
    for item in news_items:
        player_link = item.select_one("a[href*='/hockey/player/']")
        if not player_link:
            continue
        results.append({
            "player": player_link.get_text(strip=True),
            "player_id": player_link["href"].split("-")[-1],
            "team": item.select_one(".team-name").get_text(strip=True) if item.select_one(".team-name") else None,
            "injury_type": item.select_one(".injury-type").get_text(strip=True) if item.select_one(".injury-type") else None,
            "date": item.select_one(".date").get_text(strip=True) if item.select_one(".date") else None,
            "headline": item.select_one(".headline").get_text(strip=True) if item.select_one(".headline") else None,
            "body": item.select_one(".news-body").get_text(strip=True) if item.select_one(".news-body") else None,
            "source": "rotowire_html"
        })
    return results
```
**Note pour l'agent :** les sélecteurs CSS ci-dessus sont des placeholders. Avant de coder en dur, faire un `requests.get()` + sauvegarder le HTML localement, puis inspecter la structure réelle (elle change périodiquement). Utiliser `soup.prettify()` pour explorer.

### Autres endpoints RotoWire utiles
- Rumeurs de transactions : `https://www.rotowire.com/hockey/rumors.php`
- Free agents : `https://www.rotowire.com/hockey/news.php?view=free-agents`
- Alignements/trios prévus : `https://www.rotowire.com/hockey/nhl-lineups.php`

---

## Système 2 : FantasySP (scraping HTML uniquement, pas de RSS public)

### Structure de la page
- URL : `https://www.fantasysp.com/injuries/nhl/`
- Contenu organisé en tableaux HTML groupés par équipe (`<h5>` ou similaire pour le nom d'équipe, suivi d'un `<table>`)
- Chaque ligne de tableau contient : `#`, Player (lien), Team, Pos, Injury, News (texte résumé en une phrase)

**Implémentation suggérée :**
```python
def scrape_fantasysp_injuries():
    url = "https://www.fantasysp.com/injuries/nhl/"
    resp = requests.get(url, headers=HEADERS, timeout=10)
    soup = BeautifulSoup(resp.text, "html.parser")
    results = []
    current_team = None
    for el in soup.select("h5, table"):  # PLACEHOLDER — vérifier la structure réelle
        if el.name == "h5":
            current_team = el.get_text(strip=True)
        elif el.name == "table":
            for row in el.select("tbody tr"):
                cols = row.select("td")
                if len(cols) < 5:
                    continue
                player_link = cols[1].select_one("a")
                results.append({
                    "player": player_link.get_text(strip=True) if player_link else cols[1].get_text(strip=True),
                    "player_url": player_link["href"] if player_link else None,
                    "team": current_team,
                    "position": cols[2].get_text(strip=True),
                    "injury_type": cols[3].get_text(strip=True),
                    "news": cols[4].get_text(strip=True),
                    "source": "fantasysp"
                })
    return results
```
**Note pour l'agent :** FantasySP charge une partie du contenu dynamiquement (widgets "Matchups", flux Twitter intégrés). La table d'injuries semble être rendue côté serveur (visible en HTML brut), mais vérifier avec `curl` ou `requests` seul avant d'investir dans Selenium/Playwright — ne complexifier que si nécessaire.

### Page complémentaire
- Fil "Player News" agrégé multi-sport sur la même page (colonne latérale) — contient des items NHL mélangés à NFL/NBA/MLB, filtrer par `sport=nhl` dans le contexte ou par mots-clés d'équipes NHL.

---

## Étape 3 — Normalisation et fusion

Schéma JSON cible commun :
```json
{
  "player": "string",
  "team": "string (code 3 lettres si possible)",
  "position": "string",
  "injury_type": "string | null",
  "headline": "string",
  "body": "string",
  "date": "ISO 8601",
  "source": "rotowire_rss | rotowire_html | fantasysp",
  "url": "string | null"
}
```

- Dédupliquer par `(player, date, source_type)` approximatif — un même événement peut apparaître sur les deux sites avec un texte différent ; garder les deux versions (utile pour comparer les angles) ou fusionner avec un champ `duplicate_of`.
- Stocker en base (SQLite suffit pour un usage perso) ou fichier JSON append-only avec horodatage de scraping.

## Étape 4 — Orchestration
- Script principal qui :
  1. Appelle `feedparser` sur le flux RSS RotoWire (rapide, fiable)
  2. Scrape FantasySP toutes les 15-30 minutes
  3. Scrape RotoWire HTML (pour les détails absents du RSS) à fréquence plus faible (30-60 min) pour limiter la charge
  4. Merge + dédoublonnage + écriture en base
- Ajouter un `try/except` par source : si une source échoue (changement de structure HTML), logguer l'erreur sans bloquer l'autre source.
- Alerter (log ou notification) si un scraper retourne 0 résultats de façon inattendue — signe que la structure HTML a changé et que les sélecteurs doivent être mis à jour.

## Points de vigilance pour l'agent
1. **Les sélecteurs CSS fournis sont indicatifs** — toujours vérifier le HTML réel avant de finaliser, car les deux sites changent leur structure périodiquement.
2. **Respecter un rate limit** raisonnable pour éviter un blocage IP.
3. **Ne pas scraper le contenu "ANALYSIS" verrouillé** de RotoWire (nécessite un compte payant — hors scope, potentiellement contraire aux CGU).
4. **Prévoir un fallback silencieux** si un site est temporairement indisponible.
