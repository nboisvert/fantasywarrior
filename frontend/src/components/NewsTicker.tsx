// Global "news" ticker — a horizontal marquee mounted once at the app-shell
// level, visible on every tab, sitting in its own fixed band just above the
// bottom nav. Two kinds of items:
//  - plain player movements (add/drop) that are NOT part of a trade — trade
//    moves get their own richer representation below instead of showing up
//    twice.
//  - recently processed trades: both sides' "star" player (highest NHL
//    points among the players on that side, per the current roster data)
//    plus a "+N" for any others involved.
//
// A processed trade counts as "hot" for 30 minutes after it processed. While
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
import type { ActivityEntry, LeagueDetail, Trade, TradePlayer } from "../api";
import { ArrowLeftRightIcon, MinusIcon, PlusIcon } from "./Icons";

const HOT_WINDOW_MS = 30 * 60 * 1000;
/** How long after the user's last touch/wheel input before auto-advance resumes. */
const RESUME_AFTER_MS = 1500;
/** Matches the old CSS animation's pace: one full loop of the (undoubled) content in 60s. */
const LOOP_SECONDS = 60;

/** Small local helper, not shared — same "duplicated per file" convention as
 * formatMoneyCompact in Stats.tsx/Roster.tsx. */
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
  | { kind: "movement"; key: string; ts: number; type: "add" | "drop"; text: string }
  | { kind: "trade"; key: string; ts: number; label: string };

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
    // Fetch a generous raw batch (50 is the endpoint's max) and filter out
    // trade-sourced events BEFORE capping the display count — a burst of
    // trade activity produces 4 raw events each (2 opens + 2 closes), which
    // can fill a small fetch limit entirely and starve out freeagent
    // movements before this filter even runs. Capping the *filtered* list
    // instead keeps a real mix of both news types on screen.
    Promise.all([api.activity(leagueId, 50), api.trades(leagueId, username)])
      .then(([activity, trades]: [ActivityEntry[], Trade[]]) => {
        if (ignore) return;
        const movements: NewsItem[] = activity
          .filter((e) => e.source !== "trade")
          .slice(0, 10)
          .map((e) => ({
            kind: "movement",
            key: `mv-${e.playerId}-${e.dateUtc}`,
            ts: new Date(e.dateUtc).getTime(),
            type: e.type,
            text: `${e.teamName} ${e.type === "add" ? "added" : "dropped"} ${e.playerName} · ${timeAgo(e.dateUtc)}`,
          }));
        const processed: NewsItem[] = trades
          .filter((t): t is Trade & { processedUtc: string } => t.status === "processed" && t.processedUtc != null)
          .map((t) => ({
            kind: "trade",
            key: `tr-${t.id}`,
            ts: new Date(t.processedUtc).getTime(),
            label: tradeLabel(t, pointsById),
          }));
        setItems([...movements, ...processed].sort((a, b) => b.ts - a.ts).slice(0, 16));
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
      aria-label="Recent league activity"
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
              ) : item.type === "add" ? (
                <PlusIcon size={13} className="news-ticker-icon news-ticker-icon-add" />
              ) : (
                <MinusIcon size={13} className="news-ticker-icon news-ticker-icon-drop" />
              )}
              {item.kind === "trade" ? item.label : item.text}
            </span>
          );
        })}
      </div>
    </div>
  );
}
