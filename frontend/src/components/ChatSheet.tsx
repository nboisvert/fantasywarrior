// ChatSheet — direct messages between the GMs of one league.
//
// Full-screen sheet rather than a dropdown, and unlike CockmanChat it is
// deliberately *inside* the Night Arena theme: Cockman clashes on purpose
// because it plays a bolted-on third-party widget, whereas this is a native
// screen and should read as one.
//
// Borrows the modal mechanics that PlayerCard and CockmanChat already share —
// overlay, Escape/backdrop close, focus trap, scroll lock, focus restore,
// auto-scroll to the newest message — behaviour only, none of the gc- styles.
//
// Two views in one sheet, list <-> thread, the same shape as ProfileMenu's two
// panels. Threads are per league (see Message.cs): the contact list is this
// league's membership, never a union across pools.

import { useCallback, useEffect, useRef, useState } from "react";
import type { KeyboardEvent as ReactKeyboardEvent, MouseEvent as ReactMouseEvent } from "react";
import { api, initials } from "../api";
import type { ChatMessage, Conversation } from "../api";
import { useLive } from "../live/LiveProvider";
import { useLanguage } from "../i18n/LanguageContext";
import { ArrowLeftIcon, MessageSquareIcon, SendIcon, XIcon } from "./Icons";
import "./ChatSheet.css";

/** Right-hand stamp on a conversation row: a time today, a weekday this week,
 * a date beyond that. Local to this file, same "duplicated per screen" habit
 * as NewsTicker's timeAgo — the shape a stamp should take is a property of the
 * screen, not of the app. */
function stampLabel(iso: string | null): string {
  if (!iso) return "";
  const then = new Date(iso);
  const now = new Date();
  const sameDay = then.toDateString() === now.toDateString();
  if (sameDay) return then.toLocaleTimeString(undefined, { hour: "numeric", minute: "2-digit" });

  const days = (now.getTime() - then.getTime()) / 86_400_000;
  if (days < 7) return then.toLocaleDateString(undefined, { weekday: "short" });
  return then.toLocaleDateString(undefined, { month: "numeric", day: "numeric" });
}

function messageTime(iso: string): string {
  return new Date(iso).toLocaleTimeString(undefined, { hour: "numeric", minute: "2-digit" });
}

export function ChatSheet({
  leagueId,
  username,
  initialPeer,
  onClose,
  onUnreadChanged,
}: {
  leagueId: string;
  username: string;
  /** Opens straight onto this GM's thread — the ProfileMenu entry point. Null
   * opens the list, which is what the topbar bubble does. */
  initialPeer: string | null;
  onClose: () => void;
  /** Lets the topbar badge settle without waiting for the next tab change. */
  onUnreadChanged: () => void;
}) {
  const { t } = useLanguage();
  const { presenceOf, setRoster, onMessage, status } = useLive();
  // Named apart from the row's own fetched fields: the live set decides the
  // dot, the fetched row only supplied the wording.

  const [peer, setPeer] = useState<string | null>(initialPeer);
  const [conversations, setConversations] = useState<Conversation[] | null>(null);
  const [thread, setThread] = useState<ChatMessage[] | null>(null);
  const [draft, setDraft] = useState("");
  const [sending, setSending] = useState(false);
  const [error, setError] = useState("");

  const sheetRef = useRef<HTMLDivElement>(null);
  const firstFocusRef = useRef<HTMLButtonElement>(null);
  const threadRef = useRef<HTMLUListElement>(null);

  const loadConversations = useCallback(() => {
    api
      .conversations(leagueId, username)
      .then((rows) => {
        setConversations(rows);
        // The list carries the same presence the hub pushes, built by the same
        // roster code — so it doubles as a roster refresh for the ProfileMenu
        // when the connection happens to be idle-disconnected. The viewer is
        // absent from it, which is fine: nothing renders the viewer's own dot.
        setRoster(rows);
      })
      .catch((e) => setError((e as Error).message));
  }, [leagueId, username, setRoster]);

  useEffect(loadConversations, [loadConversations]);

  // Opening a thread reads it. Marking read is fire-and-forget: the badge
  // correcting itself a moment later is not worth blocking the messages on.
  useEffect(() => {
    if (!peer) {
      setThread(null);
      return;
    }
    let ignore = false;
    setThread(null);
    api
      .thread(leagueId, username, peer)
      .then((messages) => {
        if (ignore) return;
        setThread(messages);
        return api.markRead(leagueId, username, peer).then(() => {
          if (ignore) return;
          onUnreadChanged();
          setConversations((prev) =>
            prev?.map((c) => (c.username === peer ? { ...c, unreadCount: 0 } : c)) ?? prev,
          );
        });
      })
      .catch((e) => {
        if (!ignore) setError((e as Error).message);
      });
    return () => {
      ignore = true;
    };
  }, [leagueId, username, peer, onUnreadChanged]);

  // Live arrivals. A message for the thread on screen is appended; anything
  // else re-reads the list, which is cheap and keeps previews and unread
  // counts honest without a second set of rules for patching them.
  useEffect(
    () =>
      onMessage((m) => {
        if (m.leagueId !== leagueId) return;

        const other = m.from === username ? m.to : m.from;
        if (other === peer) {
          setThread((prev) => {
            if (!prev) return prev;
            // The POST response and the pushed copy are the same message; the
            // id is what keeps it from appearing twice.
            if (prev.some((x) => x.id === m.id)) return prev;
            return [...prev, { id: m.id, fromMe: m.from === username, body: m.body, sentUtc: m.sentUtc }];
          });
          if (m.from !== username) {
            api.markRead(leagueId, username, other).then(onUnreadChanged).catch(() => {});
          }
        } else {
          loadConversations();
          onUnreadChanged();
        }
      }),
    [onMessage, leagueId, username, peer, loadConversations, onUnreadChanged],
  );

  useEffect(() => {
    const el = threadRef.current;
    if (el) el.scrollTop = el.scrollHeight;
  }, [thread]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== "Escape") return;
      // Escape backs out one level, then closes — the sheet is two screens
      // deep and jumping straight out would lose the list.
      if (peer && !initialPeer) setPeer(null);
      else onClose();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose, peer, initialPeer]);

  useEffect(() => {
    const prevBody = document.body.style.overflow;
    // The root too, not just the body: whichever of the two is the scrolling
    // element varies, and locking only one left the page scrollable behind the
    // overlay with a stray scrollbar down the side.
    const prevRoot = document.documentElement.style.overflow;
    const prevFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    document.body.style.overflow = "hidden";
    document.documentElement.style.overflow = "hidden";
    return () => {
      document.body.style.overflow = prevBody;
      document.documentElement.style.overflow = prevRoot;
      prevFocus?.focus();
    };
  }, []);

  // Re-focus when the view swaps, since firstFocusRef re-attaches to whichever
  // of the two is mounted.
  useEffect(() => {
    firstFocusRef.current?.focus();
  }, [peer]);

  const trapFocus = (e: ReactKeyboardEvent<HTMLDivElement>) => {
    if (e.key !== "Tab" || !sheetRef.current) return;
    const focusables = sheetRef.current.querySelectorAll<HTMLElement>(
      'button:not([disabled]), [href], input, select, textarea, [tabindex]:not([tabindex="-1"])',
    );
    if (focusables.length === 0) return;
    const first = focusables[0];
    const last = focusables[focusables.length - 1];
    if (e.shiftKey && document.activeElement === first) {
      e.preventDefault();
      last.focus();
    } else if (!e.shiftKey && document.activeElement === last) {
      e.preventDefault();
      first.focus();
    }
  };

  const onBackdrop = (e: ReactMouseEvent<HTMLDivElement>) => {
    if (e.target === e.currentTarget) onClose();
  };

  const send = async () => {
    const body = draft.trim();
    if (!body || !peer || sending) return;
    setSending(true);
    setError("");
    try {
      const sent = await api.sendMessage(leagueId, username, peer, body);
      setDraft("");
      setThread((prev) => {
        if (!prev) return prev;
        if (prev.some((x) => x.id === sent.id)) return prev;
        return [...prev, { id: sent.id, fromMe: true, body: sent.body, sentUtc: sent.sentUtc }];
      });
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setSending(false);
    }
  };

  const livePresence = (row: Conversation) => presenceOf(row.username);
  const peerPresence = peer ? presenceOf(peer) : undefined;

  return (
    <div className="chat-overlay" onClick={onBackdrop}>
      <div
        ref={sheetRef}
        className="chat-sheet"
        role="dialog"
        aria-modal="true"
        aria-label={peer ? t("chatSheet.conversationWith", { peer }) : t("chatSheet.messages")}
        onKeyDown={trapFocus}
      >
        <header className="chat-header">
          {peer && !initialPeer ? (
            <button
              ref={firstFocusRef}
              className="chat-icon-btn"
              onClick={() => setPeer(null)}
              aria-label={t("chatSheet.backToConversations")}
            >
              <ArrowLeftIcon size={20} />
            </button>
          ) : (
            <button ref={firstFocusRef} className="chat-icon-btn" onClick={onClose} aria-label={t("chatSheet.closeMessages")}>
              <XIcon size={20} />
            </button>
          )}

          {peer ? (
            <span className="chat-header-peer">
              <span className="chat-avatar" aria-hidden="true">
                {initials(peer)}
              </span>
              <span className="chat-header-text">
                <span className="chat-header-name">{peer}</span>
                <span className="chat-header-sub muted">{peerPresence?.label ?? ""}</span>
              </span>
            </span>
          ) : (
            <span className="chat-header-title">{t("chatSheet.messages")}</span>
          )}

          {peer && !initialPeer && (
            <button className="chat-icon-btn chat-header-close" onClick={onClose} aria-label={t("chatSheet.closeMessages")}>
              <XIcon size={20} />
            </button>
          )}
        </header>

        {error && <p className="error-banner chat-error">{error}</p>}
        {status === "connecting" && (
          <p className="chat-status muted">{t("chatSheet.connecting")}</p>
        )}

        {!peer && (
          <ul className="chat-list">
            {conversations === null ? (
              <li className="chat-empty muted">{t("chatSheet.loading")}</li>
            ) : conversations.length === 0 ? (
              <li className="chat-empty muted">{t("chatSheet.noOtherGms")}</li>
            ) : (
              conversations.map((row) => {
                const p = livePresence(row);
                return (
                  <li key={row.username}>
                    <button className="chat-row" onClick={() => setPeer(row.username)}>
                      <span className="chat-avatar-wrap">
                        <span className="chat-avatar" aria-hidden="true">
                          {initials(row.username)}
                        </span>
                        <span
                          className={`chat-dot${p.online ? " online" : ""}`}
                          aria-label={p.online ? t("chatSheet.online") : p.label}
                        />
                      </span>

                      <span className="chat-row-body">
                        <span className="chat-row-top">
                          <span className="chat-row-name">{row.username}</span>
                          <span className="chat-row-stamp muted">
                            {row.lastSentUtc ? stampLabel(row.lastSentUtc) : p.label}
                          </span>
                        </span>
                        <span className="chat-row-bottom">
                          <span className={`chat-row-preview muted${row.unreadCount > 0 ? " unread" : ""}`}>
                            {row.preview
                              ? `${row.lastFromMe ? t("chatSheet.youPrefix") : ""}${row.preview}`
                              : t("chatSheet.noMessagesYet")}
                          </span>
                          {row.unreadCount > 0 && (
                            <span className="chat-row-badge" aria-label={t("chatSheet.unreadAria", { count: row.unreadCount })}>
                              {row.unreadCount > 9 ? "9+" : row.unreadCount}
                            </span>
                          )}
                        </span>
                      </span>
                    </button>
                  </li>
                );
              })
            )}
          </ul>
        )}

        {peer && (
          <>
            <ul ref={threadRef} className="chat-thread">
              {thread === null ? (
                <li className="chat-empty muted">{t("chatSheet.loading")}</li>
              ) : thread.length === 0 ? (
                <li className="chat-empty muted">
                  <MessageSquareIcon size={18} className="inline-icon" /> {t("chatSheet.sayToPeer", { peer })}
                </li>
              ) : (
                thread.map((m) => (
                  <li key={m.id} className={`chat-bubble${m.fromMe ? " mine" : ""}`}>
                    <span className="chat-bubble-text">{m.body}</span>
                    <span className="chat-bubble-time muted">{messageTime(m.sentUtc)}</span>
                  </li>
                ))
              )}
            </ul>

            <div className="chat-composer">
              <input
                className="field chat-input"
                placeholder={t("chatSheet.messagePlaceholder", { peer })}
                value={draft}
                maxLength={1000}
                onChange={(e) => setDraft(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === "Enter") void send();
                }}
              />
              <button
                className="chat-send"
                onClick={() => void send()}
                disabled={!draft.trim() || sending}
                aria-label={t("chatSheet.send")}
              >
                <SendIcon size={18} />
              </button>
            </div>
          </>
        )}
      </div>
    </div>
  );
}
