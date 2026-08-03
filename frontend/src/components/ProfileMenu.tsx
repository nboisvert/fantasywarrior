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
//
// Presence here is real as of 2026-08-03. It used to be a hash of the username
// and the thread below it was five scripted lines; both are gone. The dots now
// come from LiveProvider — pushed the moment someone connects or drops — and
// tapping a GM hands off to ChatSheet rather than opening a chat in a 320px
// dropdown.

import { useEffect, useMemo, useRef, useState } from "react";
import { api, initials } from "../api";
import { useLive } from "../live/LiveProvider";
import { CockcoinIcon, LogOutIcon, MessageSquareIcon, SettingsIcon, UsersIcon } from "./Icons";
import "./ProfileMenu.css";

export function ProfileMenu({
  username,
  leagueId,
  otherMembers,
  onSettings,
  onLogout,
  onOpenChat,
}: {
  username: string;
  /** Null before a league is picked — presence is league-scoped, so there is
   * nothing to fetch until then. */
  leagueId: string | null;
  /** Other poolers in the current league (viewer's own username already excluded). Empty when no league is selected yet. */
  otherMembers: string[];
  onSettings: () => void;
  onLogout: () => void;
  onOpenChat: (peer: string) => void;
}) {
  const [open, setOpen] = useState(false);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);

  const { presenceOf, setRoster } = useLive();

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

  const close = () => setOpen(false);

  // Everyone starts at 0 (cockman-concept.md) — null just means "not fetched
  // yet", shown as 0 rather than a flash of nothing while it loads.
  const [cockcoin, setCockcoin] = useState<number | null>(null);

  // Fetched on open, not eagerly — same lazy-per-open pattern as the presence
  // fetch below.
  useEffect(() => {
    if (!open) return;
    let ignore = false;
    api
      .cockcoinBalance(username)
      .then((r) => {
        if (!ignore) setCockcoin(r.balance);
      })
      .catch(() => {
        if (!ignore) setCockcoin(0);
      });
    return () => {
      ignore = true;
    };
  }, [open, username]);

  // A fallback, not the source. The hub pushes the whole roster the moment the
  // connection opens, so the badge is already right before this menu is ever
  // opened; this only covers the case where the connection is idle-disconnected
  // (see LiveProvider) and there is nothing live to listen to.
  useEffect(() => {
    if (!open || !leagueId) return;
    let ignore = false;
    api
      .conversations(leagueId, username)
      .then((rows) => {
        if (!ignore) setRoster(rows);
      })
      // Silent: the list still renders, just without dots.
      .catch(() => {});
    return () => {
      ignore = true;
    };
  }, [open, leagueId, username, setRoster]);

  // Focus the panel itself rather than its first control. Now that Settings
  // and Log out sit at the bottom, focusing a button would scroll a
  // thirteen-GM list straight to its end on open; from the container, Tab
  // still walks the list in order.
  useEffect(() => {
    if (open) panelRef.current?.focus();
  }, [open]);

  // Built from the league's own membership, not from whatever presence has
  // arrived: everyone is listed from the first render, and `presenceOf` fills
  // in the dot live and the wording once a fetch has happened.
  const members = useMemo(
    () =>
      otherMembers
        .map(presenceOf)
        .sort(
          (a, b) =>
            Number(b.online) - Number(a.online) ||
            (b.lastSeenUtc ?? "").localeCompare(a.lastSeenUtc ?? "") ||
            a.username.localeCompare(b.username),
        ),
    [otherMembers, presenceOf],
  );

  // Other GMs only. Counting the viewer (2026-08-03, per Nick) meant the badge
  // never read below 1, so "1 online" was indistinguishable from "nobody is
  // here" — it announced your own presence back at you. A badge should say
  // something you don't already know.
  const onlineCount = members.filter((m) => m.online).length;

  return (
    <div className="profile-menu">
      <button
        ref={triggerRef}
        className="profile-trigger"
        onClick={() => setOpen((o) => !o)}
        aria-haspopup="true"
        aria-expanded={open}
        aria-label={
          onlineCount > 0
            ? `Profile menu — ${username}, ${onlineCount} other GM${onlineCount > 1 ? "s" : ""} online`
            : `Profile menu — ${username}, nobody else online`
        }
      >
        <span className="profile-avatar-wrap">
          <span className="profile-avatar" aria-hidden="true">
            {initials(username)}
          </span>
          {onlineCount > 0 && (
            <span className="profile-online-badge" aria-hidden="true">
              {onlineCount}
            </span>
          )}
        </span>
        <span className="profile-trigger-name">{username}</span>
      </button>

      {open && (
        <div ref={panelRef} className="profile-panel" role="dialog" aria-label="Profile menu" tabIndex={-1}>
          <div className="profile-panel-header">
            <span className="profile-avatar profile-avatar-lg" aria-hidden="true">
              {initials(username)}
            </span>
            <span className="profile-panel-name">{username}</span>
            {/* Back inline with the profile row (2026-08-03, per Nick) — the
             * icon alone carries the "look here", everything next to it
             * (amount, ticker, label) stays quiet on purpose: no frame, no
             * gold background, this isn't the screen's focal point. */}
            <span className="profile-cockcoin" aria-label={`${cockcoin ?? 0} CK cockcoin`}>
              <CockcoinIcon size={32} />
              <span className="profile-cockcoin-text">
                <span className="profile-cockcoin-amount">{cockcoin ?? 0} CK</span>
                <span className="profile-cockcoin-label">cockcoin</span>
              </span>
            </span>
          </div>

          <span className="section-title profile-panel-section">
            <UsersIcon size={13} className="inline-icon" /> League GMs ({onlineCount} online)
          </span>

          {members.length === 0 ? (
            <p className="profile-empty muted">No other poolers yet.</p>
          ) : (
            <ul className="profile-member-list">
              {members.map((p) => {
                const member = p.username;
                return (
                  <li key={member} className="profile-member-row">
                    <span
                      className={`profile-status-dot${p.online ? " online" : ""}`}
                      aria-hidden="true"
                    />
                    <span className="profile-member-name">{member}</span>
                    {/* Shown for everyone, not only whoever is online: with
                     * real presence, messaging someone who stepped away is
                     * the normal case, not an error. */}
                    <button
                      className="profile-msg-btn"
                      onClick={() => {
                        close();
                        onOpenChat(member);
                      }}
                      aria-label={`Message ${member}`}
                    >
                      <MessageSquareIcon size={15} />
                    </button>
                    {/* "last seen" spelled out here (2026-08-03, per Nick) —
                     * this row has the width for it, unlike the chat list's
                     * right-aligned stamp, which keeps the bare form. The
                     * wording of the interval itself comes from Core either
                     * way, so there is still one implementation of it. */}
                    <span className="profile-member-seen muted">
                      {p.online ? "Online" : p.label === "—" ? "—" : `last seen ${p.label}`}
                    </span>
                  </li>
                );
              })}
            </ul>
          )}

          {/* Below the GM list, not above it (2026-08-03, per Nick): the list
           * is what the menu is for, and Settings/Log out are the things you
           * reach for least. Compact for the same reason. */}
          <div className="profile-panel-divider" />

          <div className="profile-panel-actions">
            <button
              className="profile-action"
              onClick={() => {
                close();
                onSettings();
              }}
            >
              <SettingsIcon size={15} />
              Settings
            </button>
            <button
              className="profile-action profile-action-danger"
              onClick={() => {
                close();
                onLogout();
              }}
            >
              <LogOutIcon size={15} />
              Log out
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
