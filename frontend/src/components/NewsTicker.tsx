// Global "news" ticker — a horizontal marquee mounted once at the app-shell
// level, visible on every tab, sitting in its own fixed band just above the
// bottom nav. Two kinds of items:
//  - real NHL news articles from external feeds (Rotowire, FantasySP —
//    see backend NewsSyncJob), unfiltered by league/roster — a generic NHL
//    news feed, not scoped to who's actually rostered.
//  - trades that are either accepted (both sides agreed, nightly
//    process-trades hasn't swapped the rosters yet — labeled "Agreed —")
//    or processed (already executed): both sides' "star" player (highest
//    NHL points among the players on that side, per the current roster
//    data) plus a "+N" for any others involved.
//
// Any trade item — accepted or processed — counts as "hot" for 30 minutes
// after its own stage timestamp (accepted -> respondedUtc, processed ->
// processedUtc). While
// a hot trade's ticker item is actually scrolled into view (tracked via
// IntersectionObserver against the ticker's own visible band, not just
// "present in the data"), the whole ticker switches to an alert treatment to
// draw the eye — and reverts the moment it scrolls back out. Since the
// marquee loops, the same item re-triggers the alert every time it comes
// back around, for as long as it's still within its 30-minute window.
//
// The band is a genuine scrollable element (not a CSS transform animation):
// a requestAnimationFrame loop nudges `scrollLeft` forward at a steady pace,
// but a real touch/wheel/trackpad scroll on it moves the SAME scrollLeft, so
// a user can swipe through faster than the auto-advance — auto-play just
// pauses for a couple seconds after manual input, then resumes from wherever
// the user left it.

import { useEffect, useMemo, useRef, useState } from "react";
import { api, topPlayersByNhlPoints } from "../api";
import type { LeagueDetail, NewsArticle, Trade, TradePlayer } from "../api";
import { ArrowLeftRightIcon, NewspaperIcon } from "./Icons";

const HOT_WINDOW_MS = 30 * 60 * 1000;
/** How long after the user's last touch/wheel input before auto-advance resumes. */
const RESUME_AFTER_MS = 1500;
/** Matches the old CSS animation's pace: one full loop of the (undoubled) content in 60s. */
const LOOP_SECONDS = 60;

/** Small local helper, not shared — same "duplicated per file" convention as
 * formatMoneyCompact in Stats.tsx. */
function timeAgo(dateUtc: string): string {
  const ms = Date.now() - new Date(dateUtc).getTime();
  const minutes = Math.floor(ms / 60_000);
  if (minutes < 1) return "now";
  if (minutes < 60) return `${minutes}m`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h`;
  return `${Math.floor(hours / 24)}d`;
}

/** Mirrors the reduced-motion hook the old (removed) Dashboard ticker used —
 * no shared hooks module in this codebase yet, so this is a small local
 * copy rather than a new shared file for one caller. */
function usePrefersReducedMotion(): boolean {
  const [reduced, setReduced] = useState(
    () => window.matchMedia("(prefers-reduced-motion: reduce)").matches,
  );
  useEffect(() => {
    const mq = window.matchMedia("(prefers-reduced-motion: reduce)");
    const onChange = () => setReduced(mq.matches);
    mq.addEventListener("change", onChange);
    return () => mq.removeEventListener("change", onChange);
  }, []);
  return reduced;
}

type NewsItem =
  | { kind: "article"; key: string; ts: number; source: NewsArticle["source"]; text: string }
  | { kind: "trade"; key: string; ts: number; label: string; status: "accepted" | "processed" };

/** "Star" player per side = highest NHL points among the current roster data
 * (a proxy for "the name worth putting in the ticker" — the trade endpoint
 * doesn't carry stats itself, so this looks the players up in the league's
 * already-loaded roster data instead of adding a new backend field). */
function tradeSideLabel(players: TradePlayer[], pointsById: Map<number, number>): string {
  if (players.length === 0) return "nothing";
  const [top] = topPlayersByNhlPoints(players, pointsById, 1);
  return players.length > 1 ? `${top.name} (+${players.length - 1})` : top.name;
}

function tradeLabel(trade: Trade, pointsById: Map<number, number>): string {
  return `${trade.proposerTeamName}: ${tradeSideLabel(trade.playersFromProposer, pointsById)} ⇄ ${trade.counterpartyTeamName}: ${tradeSideLabel(trade.playersFromCounterparty, pointsById)}`;
}

export function NewsTicker({
  leagueId,
  league,
  username,
}: {
  leagueId: string | null;
  league: LeagueDetail | null;
  username: string;
}) {
  const [items, setItems] = useState<NewsItem[]>([]);
  const reducedMotion = usePrefersReducedMotion();
  const tickerRef = useRef<HTMLDivElement>(null);
  const visibleHotNodes = useRef<Set<Element>>(new Set());
  const [alertActive, setAlertActive] = useState(false);
  const lastInputRef = useRef(0);
  const isHoveringRef = useRef(false);

  // Ticks every 30s purely to re-evaluate "is this trade still within its
  // 30-minute hot window" — without this, a trade that ages out while the
  // tab stays open would never lose its highlight until the next data fetch.
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), 30_000);
    return () => clearInterval(id);
  }, []);

  const pointsById = useMemo(() => {
    const map = new Map<number, number>();
    for (const team of league?.teams ?? [])
      for (const [id, pts] of Object.entries(team.playerNhlPoints)) map.set(Number(id), pts);
    return map;
  }, [league]);

  useEffect(() => {
    if (!leagueId) {
      setItems([]);
      return;
    }
    let ignore = false;
    // News is a global (unfiltered) NHL feed, not scoped to this league's
    // rosters — trades stay league-scoped via the leagueId/username-bound
    // api.trades() call.
    Promise.all([api.news(30), api.trades(leagueId, username)])
      .then(([news, trades]: [NewsArticle[], Trade[]]) => {
        if (ignore) return;
        // Cap articles BEFORE merging with trades (same guard the old
        // movement/trade merge had) — two of the three news sources have no
        // real per-item date, so a fresh sync can stamp dozens of items with
        // essentially the same "just synced" timestamp. Without this cap,
        // that burst wins every slot in the ts-sorted top-16 and trades
        // vanish from the ticker entirely.
        const articles: NewsItem[] = news
          .slice(0, 10)
          .map((a) => ({
            kind: "article",
            key: `nw-${a.id}`,
            ts: new Date(a.publishedUtc).getTime(),
            source: a.source,
            text: `${a.headline} · ${timeAgo(a.publishedUtc)}`,
          }));
        const processed: NewsItem[] = trades
          .filter((t): t is Trade & { processedUtc: string } => t.status === "processed" && t.processedUtc != null)
          .map((t) => ({
            kind: "trade",
            key: `tr-${t.id}-processed`,
            ts: new Date(t.processedUtc).getTime(),
            label: tradeLabel(t, pointsById),
            status: "processed",
          }));
        // Accepted-but-not-yet-processed trades (the nightly process-trades
        // job hasn't swapped the rosters yet) — still newsworthy the moment
        // both sides agree, not just once it actually executes.
        const accepted: NewsItem[] = trades
          .filter((t): t is Trade & { respondedUtc: string } => t.status === "accepted" && t.respondedUtc != null)
          .map((t) => ({
            kind: "trade",
            key: `tr-${t.id}-accepted`,
            ts: new Date(t.respondedUtc).getTime(),
            label: `Agreed — ${tradeLabel(t, pointsById)}`,
            status: "accepted",
          }));
        setItems([...articles, ...processed, ...accepted].sort((a, b) => b.ts - a.ts).slice(0, 16));
      })
      .catch(() => {
        if (!ignore) setItems([]);
      });
    return () => {
      ignore = true;
    };
  }, [leagueId, pointsById, username]);

  // Which trades currently qualify as "hot" — a plain string so the effect
  // below only re-runs when the actual set changes, not on every 30s tick.
  const hotKey = items
    .filter((it) => it.kind === "trade" && now - it.ts < HOT_WINDOW_MS)
    .map((it) => it.key)
    .join(",");

  // Watch every hot trade item's DOM node; the ticker gets the alert
  // treatment for as long as at least one is actually scrolled into view.
  useEffect(() => {
    const root = tickerRef.current;
    if (!root) return;
    const nodes = root.querySelectorAll<HTMLElement>("[data-hot-trade]");
    if (nodes.length === 0) {
      setAlertActive(false);
      return;
    }
    const observer = new IntersectionObserver(
      (entries) => {
        for (const entry of entries) {
          if (entry.isIntersecting) visibleHotNodes.current.add(entry.target);
          else visibleHotNodes.current.delete(entry.target);
        }
        setAlertActive(visibleHotNodes.current.size > 0);
      },
      { root, threshold: 0.4 },
    );
    nodes.forEach((n) => observer.observe(n));
    return () => {
      observer.disconnect();
      visibleHotNodes.current.clear();
      setAlertActive(false);
    };
  }, [hotKey]);

  // Mark recent manual input (touch/wheel/pointer) so the auto-advance loop
  // below knows to back off for a bit instead of fighting the user's scroll.
  // Hovering pauses for as long as the pointer stays over it (WCAG 2.2.2 —
  // not just a fixed timeout like the other input types).
  useEffect(() => {
    const el = tickerRef.current;
    if (!el) return;
    const markActive = () => {
      lastInputRef.current = performance.now();
    };
    const onEnter = () => {
      isHoveringRef.current = true;
    };
    const onLeave = () => {
      isHoveringRef.current = false;
      lastInputRef.current = performance.now();
    };
    el.addEventListener("wheel", markActive, { passive: true });
    el.addEventListener("touchstart", markActive, { passive: true });
    el.addEventListener("pointerdown", markActive);
    el.addEventListener("mouseenter", onEnter);
    el.addEventListener("mouseleave", onLeave);
    el.addEventListener("focusin", onEnter);
    el.addEventListener("focusout", onLeave);
    return () => {
      el.removeEventListener("wheel", markActive);
      el.removeEventListener("touchstart", markActive);
      el.removeEventListener("pointerdown", markActive);
      el.removeEventListener("mouseenter", onEnter);
      el.removeEventListener("mouseleave", onLeave);
      el.removeEventListener("focusin", onEnter);
      el.removeEventListener("focusout", onLeave);
    };
  }, []);

  // Auto-advance: a real scrollLeft nudge every frame, not a CSS transform,
  // so a manual scroll/swipe on the same element can move it faster — this
  // just backs off for RESUME_AFTER_MS after any detected user input.
  useEffect(() => {
    if (reducedMotion) return;
    const el = tickerRef.current;
    if (!el) return;
    let raf = 0;
    let last = performance.now();

    const step = (t: number) => {
      const dt = (t - last) / 1000;
      last = t;
      if (!isHoveringRef.current && performance.now() - lastInputRef.current > RESUME_AFTER_MS) {
        const half = el.scrollWidth / 2;
        if (half > 0) {
          const pxPerSecond = half / LOOP_SECONDS;
          el.scrollLeft += pxPerSecond * dt;
          if (el.scrollLeft >= half) el.scrollLeft -= half;
        }
      }
      raf = requestAnimationFrame(step);
    };
    raf = requestAnimationFrame(step);
    return () => cancelAnimationFrame(raf);
  }, [reducedMotion, items]);

  // A manual scroll can also carry scrollLeft past the halfway point (the
  // doubled list's seam) — wrap it the same way the auto-advance loop does,
  // so scrolling fast doesn't run off the end of the (real) content.
  useEffect(() => {
    const el = tickerRef.current;
    if (!el || reducedMotion) return;
    const onScroll = () => {
      const half = el.scrollWidth / 2;
      if (half > 0 && el.scrollLeft >= half) el.scrollLeft -= half;
    };
    el.addEventListener("scroll", onScroll, { passive: true });
    return () => el.removeEventListener("scroll", onScroll);
  }, [reducedMotion, items]);

  if (items.length === 0) return null;

  const displayItems = reducedMotion ? items : [...items, ...items];

  return (
    <div
      ref={tickerRef}
      className={`news-ticker${alertActive ? " alert" : ""}`}
      aria-label="NHL news and league trades"
    >
      {alertActive && (
        <span className="news-ticker-alert-label">
          <span className="news-ticker-alert-label-text">Trade Alert</span>
        </span>
      )}
      <div className="news-ticker-track">
        {displayItems.map((item, i) => {
          const isHot = item.kind === "trade" && now - item.ts < HOT_WINDOW_MS;
          return (
            <span
              className={`news-ticker-item${isHot ? " hot" : ""}`}
              key={`${item.key}-${i}`}
              data-hot-trade={isHot ? "true" : undefined}
            >
              {item.kind === "trade" ? (
                <ArrowLeftRightIcon size={13} className="news-ticker-icon news-ticker-icon-trade" />
              ) : (
                <NewspaperIcon size={13} className="news-ticker-icon news-ticker-icon-article" />
              )}
              {item.kind === "trade" ? item.label : item.text}
            </span>
          );
        })}
      </div>
    </div>
  );
}
