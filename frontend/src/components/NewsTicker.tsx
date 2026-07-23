// Global "news" ticker — a horizontal marquee of recent league activity
// (adds/drops/trades), mounted once at the app-shell level so it's visible
// on every tab, sitting in its own fixed band just above the bottom nav.
// This revives a Dashboard-only "Recent Activity" ticker that was built then
// fully removed during an earlier redesign (deferred, per project_status.md)
// — same idea, now app-wide and a simpler horizontal marquee instead of the
// old 3-row vertical carousel.

import { useEffect, useState } from "react";
import { api } from "../api";
import type { ActivityEntry } from "../api";
import { ActivityIcon } from "./Icons";

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

function entryText(e: ActivityEntry): string {
  const verb = e.type === "add" ? "added" : "dropped";
  const suffix = e.source === "trade" ? " (trade)" : "";
  return `${e.teamName} ${verb} ${e.playerName}${suffix} · ${timeAgo(e.dateUtc)}`;
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

export function NewsTicker({ leagueId }: { leagueId: string | null }) {
  const [entries, setEntries] = useState<ActivityEntry[]>([]);
  const reducedMotion = usePrefersReducedMotion();

  useEffect(() => {
    if (!leagueId) {
      setEntries([]);
      return;
    }
    let ignore = false;
    api
      .activity(leagueId, 12)
      .then((list) => {
        if (!ignore) setEntries(list);
      })
      .catch(() => {
        if (!ignore) setEntries([]);
      });
    return () => {
      ignore = true;
    };
  }, [leagueId]);

  if (entries.length === 0) return null;

  const items = reducedMotion ? entries : [...entries, ...entries];

  return (
    <div className="news-ticker" aria-label="Recent league activity">
      <div className="news-ticker-track" style={reducedMotion ? { animation: "none" } : undefined}>
        {items.map((e, i) => (
          <span className="news-ticker-item" key={`${e.playerId}-${e.dateUtc}-${i}`}>
            <ActivityIcon size={13} className="news-ticker-icon" />
            {entryText(e)}
          </span>
        ))}
      </div>
    </div>
  );
}
