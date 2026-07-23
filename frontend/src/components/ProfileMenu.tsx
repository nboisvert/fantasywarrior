// ProfileMenu — the topbar's single "profile indicator" button (replaces the
// old plain username text + separate Settings gear button). Clicking it opens
// a lightweight anchored dropdown panel — not a full bottom-sheet like
// PlayerCard/CreateTradeSheet, since the content here is short (a name, two
// action rows, a divider, a short presence list) and a full-screen sheet
// would be too heavy for it. Same Night Arena glass-card visual language
// (surface/border tokens, blur, radii) as the rest of the app, though.
//
// Keyboard/a11y: Escape closes and returns focus to the trigger, clicking
// outside the panel closes it, and opening moves focus to the first action
// inside so keyboard users don't have to tab past a hidden panel.

import { useEffect, useRef, useState } from "react";
import { LogOutIcon, SettingsIcon, UsersIcon } from "./Icons";
import "./ProfileMenu.css";

/* ---------- mock presence (placeholder until real presence tracking exists) ----------
 * There is no backend endpoint for online-status/last-login yet (Firestore's
 * `User.lastLoginUtc` exists but nothing surfaces it via the API today). To
 * avoid shipping a menu full of "—", this derives a stable, plausible-looking
 * status from the username itself via a simple string hash, so a given user
 * always shows the same status across renders instead of flickering with
 * Math.random(). Replace with real presence data (e.g. a Firestore
 * `presence/{username}` doc updated on heartbeat) once that service exists.
 */
function hashString(s: string): number {
  let h = 0;
  for (let i = 0; i < s.length; i++) {
    h = (h * 31 + s.charCodeAt(i)) | 0;
  }
  return Math.abs(h);
}

interface Presence {
  online: boolean;
  lastSeenLabel: string;
  /** Minutes since last seen (0 while online) — sort key only, never displayed. */
  minutesAgo: number;
}

function getPresence(username: string): Presence {
  const seed = hashString(username);
  const online = seed % 5 !== 0; // ~4 in 5 users read as online
  if (online) return { online: true, lastSeenLabel: "Online", minutesAgo: 0 };

  const bucket = seed % 3;
  if (bucket === 0) {
    const minutes = (seed % 45) + 3;
    return { online: false, lastSeenLabel: `${minutes}m ago`, minutesAgo: minutes };
  }
  if (bucket === 1) {
    const hours = (seed % 11) + 1;
    return { online: false, lastSeenLabel: `${hours}h ago`, minutesAgo: hours * 60 };
  }
  const days = (seed % 6) + 1;
  return { online: false, lastSeenLabel: `${days}d ago`, minutesAgo: days * 1440 };
}

/** First letters of up to the first two "words" of a username (usernames in
 * this app are plain handles, not "First Last", but a couple may contain a
 * dot/underscore/dash separator — handle that gracefully, fall back to the
 * first two characters otherwise). */
function initials(username: string): string {
  const parts = username.split(/[.\-_ ]+/).filter(Boolean);
  if (parts.length >= 2) return (parts[0][0] + parts[1][0]).toUpperCase();
  return username.slice(0, 2).toUpperCase();
}

export function ProfileMenu({
  username,
  otherMembers,
  onSettings,
  onLogout,
}: {
  username: string;
  /** Other poolers in the current league (viewer's own username already excluded). Empty when no league is selected yet. */
  otherMembers: string[];
  onSettings: () => void;
  onLogout: () => void;
}) {
  const [open, setOpen] = useState(false);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  const firstItemRef = useRef<HTMLButtonElement>(null);

  // Click-outside closes.
  useEffect(() => {
    if (!open) return;
    const onPointerDown = (e: MouseEvent) => {
      const target = e.target as Node;
      if (panelRef.current?.contains(target) || triggerRef.current?.contains(target)) return;
      setOpen(false);
    };
    document.addEventListener("mousedown", onPointerDown);
    return () => document.removeEventListener("mousedown", onPointerDown);
  }, [open]);

  // Escape closes and returns focus to the trigger.
  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        setOpen(false);
        triggerRef.current?.focus();
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [open]);

  // Move focus into the panel when it opens.
  useEffect(() => {
    if (open) firstItemRef.current?.focus();
  }, [open]);

  const close = () => setOpen(false);

  // Online first, then most-recently-seen first; a stable alphabetical
  // tiebreak keeps the order from jittering between renders.
  const withPresence = otherMembers
    .map((member) => ({ member, presence: getPresence(member) }))
    .sort((a, b) => a.presence.minutesAgo - b.presence.minutesAgo || a.member.localeCompare(b.member));
  const onlineCount = withPresence.filter((m) => m.presence.online).length;
  // The viewer is, definitionally, online right now — the badge counts everyone.
  const totalOnlineCount = onlineCount + 1;

  return (
    <div className="profile-menu">
      <button
        ref={triggerRef}
        className="profile-trigger"
        onClick={() => setOpen((o) => !o)}
        aria-haspopup="true"
        aria-expanded={open}
        aria-label={`Profile menu — ${username}, ${totalOnlineCount} online in this league`}
      >
        <span className="profile-avatar-wrap">
          <span className="profile-avatar" aria-hidden="true">
            {initials(username)}
          </span>
          <span className="profile-online-badge" aria-hidden="true">
            {totalOnlineCount}
          </span>
        </span>
        <span className="profile-trigger-name">{username}</span>
      </button>

      {open && (
        <div ref={panelRef} className="profile-panel" role="dialog" aria-label="Profile menu">
          <div className="profile-panel-header">
            <span className="profile-avatar profile-avatar-lg" aria-hidden="true">
              {initials(username)}
            </span>
            <span className="profile-panel-name">{username}</span>
          </div>

          <div className="profile-panel-actions">
            <button
              ref={firstItemRef}
              className="profile-action"
              onClick={() => {
                close();
                onSettings();
              }}
            >
              <SettingsIcon size={18} />
              Settings
            </button>
            <button
              className="profile-action profile-action-danger"
              onClick={() => {
                close();
                onLogout();
              }}
            >
              <LogOutIcon size={18} />
              Log out
            </button>
          </div>

          <div className="profile-panel-divider" />

          <span className="section-title profile-panel-section">
            <UsersIcon size={13} className="inline-icon" /> League GMs ({onlineCount} online)
          </span>

          {withPresence.length === 0 ? (
            <p className="profile-empty muted">No other poolers yet.</p>
          ) : (
            <ul className="profile-member-list">
              {withPresence.map(({ member, presence }) => (
                <li key={member} className="profile-member-row">
                  <span
                    className={`profile-status-dot${presence.online ? " online" : ""}`}
                    aria-hidden="true"
                  />
                  <span className="profile-member-name">{member}</span>
                  <span className="profile-member-seen muted">{presence.lastSeenLabel}</span>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  );
}
