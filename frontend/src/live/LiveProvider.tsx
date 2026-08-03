/* LiveProvider — the app's single SignalR connection.
 *
 * It exists for exactly two things: telling the league who is online, and
 * delivering private messages between its GMs. Nothing else belongs here.
 *
 * ---------------------------------------------------------------------------
 * CONNECTION LIFETIME
 * ---------------------------------------------------------------------------
 * A WebSocket is a request that never ends, so Azure Container Apps cannot
 * scale the API to zero while one is open, and the free grant is only about a
 * hundred awake hours a month. The lever that matters is the hidden tab: a tab
 * forgotten on a second monitor would otherwise hold the container up all
 * night for nobody.
 *
 *   league loaded, tab visible -> connect
 *   tab hidden for 60 s        -> disconnect
 *   tab visible again          -> connect
 *   pagehide                   -> disconnect now
 *
 * That is the whole policy. There is deliberately **no idle timeout and no
 * activity tracking**: SignalR keeps its own connection alive with pings, and
 * an earlier version that dropped the socket after three minutes without a
 * pointerdown/keydown/scroll bought little and cost a lot — it needed listeners
 * on scroll, it made "connected" a poor proxy for "in the app", and its retry
 * path could hammer /hubs/live/negotiate once per scroll event when the API was
 * cold. Drops mid-session are `withAutomaticReconnect()`'s job, not ours.
 *
 * The 60 s delay on hidden is not an idle timer — it only stops an alt-tab from
 * blinking every dot in the league.
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

const HIDDEN_GRACE_MS = 60_000;

export type LiveStatus = "idle" | "connecting" | "connected";

/** The whole presence payload: who is online in this league, right now. */
interface OnlinePayload {
  leagueId: string;
  online: string[];
}

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
  /** Authoritative for green dots and the counter. Replaced wholesale on every
   * push, so applying one twice changes nothing and a missed one is repaired by
   * the next. */
  online: ReadonlySet<string>;
  /** Authoritative for the "last seen 45min ago" wording. Comes from REST,
   * because it changes slowly and nobody needs it to the second. */
  roster: MemberPresence[];
  setRoster: (members: MemberPresence[]) => void;
  /** The two sources merged: live for the dot, fetched for the words. */
  presenceOf: (username: string) => MemberPresence;
  onMessage: (handler: (m: LiveMessage) => void) => () => void;
  onNotice: (handler: (n: LiveNotice) => void) => () => void;
}

const LiveContext = createContext<LiveContextValue | null>(null);

const UNKNOWN = (username: string): MemberPresence => ({
  username,
  online: false,
  lastSeenUtc: null,
  label: "—",
});

/** Null-object fallback so a component can call `useLive()` without caring
 * whether a league is open — outside the provider there is simply never a
 * connection and never an event. */
const INERT: LiveContextValue = {
  status: "idle",
  online: new Set(),
  roster: [],
  setRoster: () => {},
  presenceOf: UNKNOWN,
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
  const [online, setOnline] = useState<ReadonlySet<string>>(() => new Set());
  const [roster, setRoster] = useState<MemberPresence[]>([]);

  const connectionRef = useRef<HubConnection | null>(null);
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

  const byUsername = useMemo(() => new Map(roster.map((m) => [m.username, m])), [roster]);

  const presenceOf = useCallback(
    (name: string): MemberPresence => {
      const known = byUsername.get(name);
      const isOnline = online.has(name);
      // The live set wins over the fetched row: the row may have been fetched
      // minutes ago, the set is what the server said a moment ago.
      return {
        username: name,
        online: isOnline,
        lastSeenUtc: known?.lastSeenUtc ?? null,
        label: isOnline ? "Online" : known?.label ?? "—",
      };
    },
    [byUsername, online],
  );

  const stop = useCallback(async () => {
    const connection = connectionRef.current;
    connectionRef.current = null;
    setStatus("idle");
    setOnline(new Set());
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
      // Reconnecting is the normal case here, not an error path — and it is
      // SignalR's job, which is why there is no retry logic of our own.
      .withAutomaticReconnect()
      .build();

    connection.on("message", (m: LiveMessage) => {
      for (const handler of messageHandlers.current) handler(m);
    });
    connection.on("presence", (p: OnlinePayload) => setOnline(new Set(p.online)));
    connection.on("notice", (n: LiveNotice) => {
      for (const handler of noticeHandlers.current) handler(n);
    });

    connection.onreconnecting(() => setStatus("connecting"));
    connection.onreconnected(() => setStatus("connected"));
    connection.onclose(() => {
      if (connectionRef.current === connection) {
        connectionRef.current = null;
        setStatus("idle");
        // Nothing is arriving any more, so claiming anyone is online would be
        // a guess. The next connection re-establishes the truth in one push.
        setOnline(new Set());
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
      // A cold start can outlast the first attempt. Silence is right: the next
      // visibility change retries, and a banner here would fire on a situation
      // that fixes itself.
      if (connectionRef.current === connection) {
        connectionRef.current = null;
        setStatus("idle");
      }
    }
  }, [username, leagueId]);

  useEffect(() => {
    if (!username || !leagueId) {
      setRoster([]);
      setOnline(new Set());
      return;
    }

    wanted.current = true;
    if (document.visibilityState === "visible") void start();

    const onVisibility = () => {
      if (document.visibilityState === "hidden") {
        // The tab-on-a-second-monitor case is the expensive one: no value to
        // anybody, full price. Give it a minute in case they flick back.
        hiddenTimer.current = window.setTimeout(() => void stop(), HIDDEN_GRACE_MS);
      } else {
        window.clearTimeout(hiddenTimer.current);
        void start();
      }
    };
    document.addEventListener("visibilitychange", onVisibility);

    // pagehide rather than beforeunload: it is the one that actually fires on
    // mobile Safari, where a backgrounded tab is otherwise killed with the
    // socket still nominally open. A clean stop here is what lets the other
    // GMs see you leave immediately instead of waiting out the server's
    // 30-second timeout.
    const onPageHide = () => {
      wanted.current = false;
      void stop();
    };
    window.addEventListener("pagehide", onPageHide);

    return () => {
      wanted.current = false;
      window.clearTimeout(hiddenTimer.current);
      document.removeEventListener("visibilitychange", onVisibility);
      window.removeEventListener("pagehide", onPageHide);
      void stop();
    };
  }, [username, leagueId, start, stop]);

  const value = useMemo<LiveContextValue>(
    () => ({ status, online, roster, setRoster, presenceOf, onMessage, onNotice }),
    [status, online, roster, presenceOf, onMessage, onNotice],
  );

  return <LiveContext.Provider value={value}>{children}</LiveContext.Provider>;
}
