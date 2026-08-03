/* LiveProvider — the app's single SignalR connection, and the policy that
 * decides when it is allowed to exist.
 *
 * ---------------------------------------------------------------------------
 * WHY THIS FILE IS SO OPINIONATED ABOUT DISCONNECTING
 * ---------------------------------------------------------------------------
 * A WebSocket is a request that never ends. While one is open, Azure Container
 * Apps sees work in flight and **cannot scale the API to zero**. The free
 * grant (180 000 vCPU-s + 360 000 GiB-s a month) buys roughly 100 hours of an
 * awake replica; past that it is billed, and a single tab left open on a desk
 * overnight would burn the month on nobody.
 *
 * So the connection is a consumable, not a fixture:
 *
 *   league loaded        -> connect
 *   3 min no interaction -> disconnect (reconnects on the next touch)
 *   tab hidden 60 s      -> disconnect
 *   pagehide             -> disconnect now, don't wait for a server timeout
 *
 * The idle timeout is deliberately shorter than Container Apps' own ~5 min
 * scale-down cooldown: come back inside that window and the replica is still
 * warm, so reconnecting costs a handshake rather than a cold start. Go quiet
 * for longer and the app sleeps, which is the entire point.
 *
 * Presence agrees with this by construction — `Presence.GraceWindow` on the
 * server is 90 s, so a client that idles out reads as away rather than as a
 * green dot that is lying.
 * ---------------------------------------------------------------------------
 */

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { HubConnectionBuilder, type HubConnection } from "@microsoft/signalr";
import { API_BASE, type LiveMessage, type MemberPresence } from "../api";

const IDLE_MS = 3 * 60_000;
const HIDDEN_GRACE_MS = 60_000;

/** Activity that counts as "still here". Deliberately coarse: this only ever
 * resets a three-minute timer, so listening for mousemove would be a lot of
 * work to learn nothing extra. */
const ACTIVITY_EVENTS = ["pointerdown", "keydown", "scroll"] as const;

export type LiveStatus = "idle" | "connecting" | "connected";

/** A pushed event with no screen of its own — the channel the trade/scoring
 * pop-ups will use. Nothing produces one yet; the plumbing ships first so
 * adding a producer is one line in the endpoint that already handles the
 * action. */
export interface LiveNotice {
  id: string;
  kind: string;
  text: string;
}

interface LiveContextValue {
  status: LiveStatus;
  /** By username. Seeded by whatever fetched a list; kept current by pushes. */
  presence: Record<string, MemberPresence>;
  seedPresence: (members: MemberPresence[]) => void;
  onMessage: (handler: (m: LiveMessage) => void) => () => void;
  onNotice: (handler: (n: LiveNotice) => void) => () => void;
}

const LiveContext = createContext<LiveContextValue | null>(null);

/** Null-object fallback so a component can call `useLive()` without caring
 * whether a league is open — outside the provider there is simply never a
 * connection and never an event. */
const INERT: LiveContextValue = {
  status: "idle",
  presence: {},
  seedPresence: () => {},
  onMessage: () => () => {},
  onNotice: () => () => {},
};

export function useLive(): LiveContextValue {
  return useContext(LiveContext) ?? INERT;
}

export function LiveProvider({
  username,
  leagueId,
  children,
}: {
  username: string | null;
  leagueId: string | null;
  children: ReactNode;
}) {
  const [status, setStatus] = useState<LiveStatus>("idle");
  const [presence, setPresence] = useState<Record<string, MemberPresence>>({});

  const connectionRef = useRef<HubConnection | null>(null);
  const idleTimer = useRef<number | undefined>(undefined);
  const hiddenTimer = useRef<number | undefined>(undefined);
  /** False once the provider unmounts or the identity changes, so an in-flight
   * start() can't resurrect a connection nobody wants any more — StrictMode
   * mounts every effect twice in dev and would otherwise leave one behind. */
  const wanted = useRef(false);

  const messageHandlers = useRef(new Set<(m: LiveMessage) => void>());
  const noticeHandlers = useRef(new Set<(n: LiveNotice) => void>());

  const onMessage = useCallback((handler: (m: LiveMessage) => void) => {
    messageHandlers.current.add(handler);
    return () => {
      messageHandlers.current.delete(handler);
    };
  }, []);

  const onNotice = useCallback((handler: (n: LiveNotice) => void) => {
    noticeHandlers.current.add(handler);
    return () => {
      noticeHandlers.current.delete(handler);
    };
  }, []);

  const seedPresence = useCallback((members: MemberPresence[]) => {
    setPresence((prev) => {
      const next = { ...prev };
      for (const m of members) next[m.username] = m;
      return next;
    });
  }, []);

  const stop = useCallback(async () => {
    const connection = connectionRef.current;
    connectionRef.current = null;
    setStatus("idle");
    if (connection) await connection.stop().catch(() => {});
  }, []);

  const start = useCallback(async () => {
    if (!username || !leagueId) return;
    if (connectionRef.current) return;

    const url =
      `${API_BASE}/hubs/live?username=${encodeURIComponent(username)}` +
      `&league=${encodeURIComponent(leagueId)}`;

    const connection = new HubConnectionBuilder()
      // withCredentials false on purpose: there is no auth to carry, and the
      // default (true) would demand an explicit-origin CORS policy, which the
      // API's AllowAnyOrigin cannot legally be combined with.
      .withUrl(url, { withCredentials: false })
      // Every deploy publishes a new revision and drops all sockets at once.
      // Reconnecting is the normal case here, not an error path.
      .withAutomaticReconnect()
      .build();

    connection.on("message", (m: LiveMessage) => {
      for (const handler of messageHandlers.current) handler(m);
    });
    connection.on("presence", (p: MemberPresence) => {
      setPresence((prev) => ({ ...prev, [p.username]: p }));
    });
    connection.on("notice", (n: LiveNotice) => {
      for (const handler of noticeHandlers.current) handler(n);
    });

    connection.onreconnecting(() => setStatus("connecting"));
    connection.onreconnected(() => setStatus("connected"));
    connection.onclose(() => {
      if (connectionRef.current === connection) {
        connectionRef.current = null;
        setStatus("idle");
      }
    });

    connectionRef.current = connection;
    setStatus("connecting");

    try {
      await connection.start();
      // Torn down while the handshake was in flight (StrictMode, a fast league
      // switch, a tab hidden immediately) — don't leave the socket open.
      if (!wanted.current || connectionRef.current !== connection) {
        await connection.stop().catch(() => {});
        return;
      }
      setStatus("connected");
    } catch {
      // A cold start can outlast the first attempt. Staying silent is right:
      // the next interaction retries, and a banner for this would fire on a
      // situation that fixes itself.
      if (connectionRef.current === connection) {
        connectionRef.current = null;
        setStatus("idle");
      }
    }
  }, [username, leagueId]);

  /** Called on any sign of life: restarts the idle countdown, and reconnects
   * if a previous countdown already expired. */
  const wake = useCallback(() => {
    if (!wanted.current) return;

    window.clearTimeout(idleTimer.current);
    idleTimer.current = window.setTimeout(() => {
      void stop();
    }, IDLE_MS);

    if (!connectionRef.current) void start();
  }, [start, stop]);

  useEffect(() => {
    if (!username || !leagueId) {
      setPresence({});
      return;
    }

    wanted.current = true;
    wake();

    const onActivity = () => {
      // Cheap guard: while connected and already counting down, this is a
      // clearTimeout/setTimeout pair per event, which is what we want on a
      // scroll handler.
      wake();
    };
    for (const event of ACTIVITY_EVENTS) {
      window.addEventListener(event, onActivity, { passive: true });
    }

    const onVisibility = () => {
      if (document.visibilityState === "hidden") {
        // The tab-on-a-second-monitor case is the expensive one: no value to
        // anybody, full price. Give it a minute in case they flick back.
        hiddenTimer.current = window.setTimeout(() => {
          void stop();
        }, HIDDEN_GRACE_MS);
      } else {
        window.clearTimeout(hiddenTimer.current);
        wake();
      }
    };
    document.addEventListener("visibilitychange", onVisibility);

    // pagehide rather than beforeunload: it is the one that actually fires on
    // mobile Safari, where a backgrounded tab is otherwise killed with the
    // socket still nominally open.
    const onPageHide = () => {
      wanted.current = false;
      void stop();
    };
    window.addEventListener("pagehide", onPageHide);

    return () => {
      wanted.current = false;
      window.clearTimeout(idleTimer.current);
      window.clearTimeout(hiddenTimer.current);
      for (const event of ACTIVITY_EVENTS) window.removeEventListener(event, onActivity);
      document.removeEventListener("visibilitychange", onVisibility);
      window.removeEventListener("pagehide", onPageHide);
      void stop();
    };
  }, [username, leagueId, wake, stop]);

  const value = useMemo<LiveContextValue>(
    () => ({ status, presence, seedPresence, onMessage, onNotice }),
    [status, presence, seedPresence, onMessage, onNotice],
  );

  return <LiveContext.Provider value={value}>{children}</LiveContext.Provider>;
}
