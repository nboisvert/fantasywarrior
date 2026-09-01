import { useCallback, useEffect, useState } from "react";
import { api } from "./api";
import type { DraftState, LeagueDetail } from "./api";
import {
  ActivityIcon,
  ArrowLeftRightIcon,
  BriefcaseIcon,
  ChevronDownIcon,
  ListOrderedIcon,
  MessageSquareIcon,
  TrophyIcon,
} from "./components/Icons";
import { LoadingLogo } from "./components/LoadingLogo";
import { ProfileMenu } from "./components/ProfileMenu";
import logo from "./assets/logo.webp";
import { Login } from "./screens/Login";
import { LeagueGate } from "./screens/LeagueGate";
import { Dashboard } from "./screens/Dashboard";
import { Standings } from "./screens/Standings";
import { Palmares } from "./screens/Palmares";
import { Stats } from "./screens/Stats";
import { Trades } from "./screens/Trades";
import { Settings } from "./screens/Settings";
import { NewsTicker } from "./components/NewsTicker";
import { ChatSheet } from "./components/ChatSheet";
import DraftRoom from "./screens/DraftRoom";
import { ToastHost } from "./components/Toast";
import { CockmanCampaignGate } from "./components/CockmanCampaignGate";
import { DoneDealRewardGate } from "./components/DoneDealRewardGate";
import { LiveProvider, useLive } from "./live/LiveProvider";
import { useLanguage } from "./i18n/LanguageContext";
import "./App.css";

type Tab = "dashboard" | "standings" | "stats" | "trades" | "draft" | "settings";

/** Keeps the topbar's unread badge live while the chat sheet is closed.
 *
 * The sheet subscribes to incoming messages itself, but it is only mounted
 * while it is open — so a message arriving with it closed had nothing
 * listening, and the badge sat still until the next tab change. This has to
 * live inside LiveProvider, which is why it is a component rather than a hook
 * call in App. Renders nothing. */
function UnreadBridge({ username, onIncoming }: { username: string; onIncoming: () => void }) {
  const { onMessage } = useLive();
  useEffect(
    () => onMessage((m) => { if (m.to === username) onIncoming(); }),
    [onMessage, username, onIncoming],
  );
  return null;
}

/** Keeps the Draft tab honest while you are somewhere else in the app.
 *
 * The room itself subscribes to the same push, but it is only mounted while
 * the Draft tab is open - so a pick landing while you read the standings had
 * nothing listening, and the nav would keep saying it was your turn after you
 * had already been passed. Has to live inside LiveProvider, hence a component
 * rather than a hook call in App. Renders nothing. */
function DraftBridge({ onDraft: onPush }: { onDraft: (s: DraftState) => void }) {
  const { onDraft } = useLive();
  useEffect(() => onDraft(onPush), [onDraft, onPush]);
  return null;
}

export default function App() {
  const { t } = useLanguage();
  const [username, setUsername] = useState<string | null>(localStorage.getItem("fw-username"));
  const [leagueId, setLeagueId] = useState<string | null>(localStorage.getItem("fw-league"));
  const [league, setLeague] = useState<LeagueDetail | null>(null);
  const [tab, setTab] = useState<Tab>("dashboard");
  // Which team the Stats screen shows: null = the signed-in user's own team.
  // Set when navigating from a Standings row; reset when the Stats tab is tapped.
  const [statsTeam, setStatsTeam] = useState<string | null>(null);
  // Palmarès sits inside the Standings tab rather than its own bottom-nav slot
  // (already full) or a second shortcut anywhere else — one reachable path.
  const [showPalmares, setShowPalmares] = useState(false);
  const [showPicker, setShowPicker] = useState(false);
  const [defaultChecked, setDefaultChecked] = useState(false);
  const [error, setError] = useState("");
  // Offers waiting on *this* user to accept or decline. Deliberately not every
  // pending trade: an offer you sent is waiting on someone else, and a badge
  // that lights up for your own outgoing offer is noise, not a notification.
  const [offersToAnswer, setOffersToAnswer] = useState(0);
  // Chat lives in a sheet, not a tab: the bottom nav is full, and a
  // conversation is something you dip into from wherever you are rather than a
  // place you navigate to. `chatPeer` non-null opens straight onto one thread.
  const [chatOpen, setChatOpen] = useState(false);
  const [chatPeer, setChatPeer] = useState<string | null>(null);
  const [unread, setUnread] = useState(0);

  const openLeague = (id: string) => {
    localStorage.setItem("fw-league", id);
    setLeague(null);
    setLeagueId(id);
    setTab("dashboard");
    setShowPicker(false);
  };

  const logout = () => {
    localStorage.removeItem("fw-username");
    localStorage.removeItem("fw-league");
    setUsername(null);
    setLeagueId(null);
    setLeague(null);
    setShowPicker(false);
    setDefaultChecked(false);
    setTab("dashboard");
  };

  const refreshLeague = useCallback(() => {
    if (!leagueId || !username) return;
    api
      .league(leagueId, username)
      .then((detail) => {
        setLeague(detail);
        setError("");
      })
      .catch((e) => setError((e as Error).message));
  }, [leagueId, username]);
  useEffect(refreshLeague, [refreshLeague]);

  // Re-read on every tab change as well as on league/user change: leaving the
  // Trades screen is exactly when the count is most likely to have just become
  // stale, because answering an offer is what you went there to do.
  useEffect(() => {
    if (!leagueId || !username) {
      setOffersToAnswer(0);
      return;
    }
    api
      .trades(leagueId, username)
      .then((trades) =>
        setOffersToAnswer(
          trades.filter((t) => t.status === "pending" && t.counterpartyUsername === username).length,
        ),
      )
      // A badge is not worth an error banner: worst case it shows nothing.
      .catch(() => setOffersToAnswer(0));
  }, [leagueId, username, tab]);

  // Whether the Draft tab exists at all, and whether it is your turn. Guarded
  // by the phase the league already reports, so this costs nothing the eleven
  // months of the year when nobody is drafting.
  const draftPhase = league?.activeSeason?.phase;
  const draftRunning = draftPhase === "Drafting";
  // The commissioner needs a door before the room opens: "Open the draft" is
  // the only thing that freezes the pick order, and it lives *inside*
  // DraftRoom's Protecting branch. Without this the tab appeared only once the
  // draft was already running, so nobody could ever reach the button.
  const draftTabVisible =
    draftRunning ||
    (draftPhase === "Protecting" && league?.commissionerUsername === username);
  const [draftTurn, setDraftTurn] = useState<DraftState | null>(null);

  useEffect(() => {
    if (!leagueId || !username || !draftRunning) {
      setDraftTurn(null);
      return;
    }
    api
      .draft(leagueId, username)
      .then(setDraftTurn)
      // Same posture as the offers badge: a nav pill is not worth an error
      // banner, and the room itself will report anything real.
      .catch(() => setDraftTurn(null));
  }, [leagueId, username, draftRunning, tab]);

  const isMyDraftTurn = draftTurn?.isMyTurn === true;

  // Same shape as the offers badge above, for the same reason. While a live
  // connection is up the sheet keeps this current by calling refreshUnread
  // itself; this covers the rest of the time, when the app is deliberately
  // disconnected (see LiveProvider) and no push can arrive.
  const refreshUnread = useCallback(() => {
    if (!leagueId || !username) {
      setUnread(0);
      return;
    }
    api
      .unreadCount(leagueId, username)
      .then((r) => setUnread(r.count))
      .catch(() => setUnread(0));
  }, [leagueId, username]);
  useEffect(refreshUnread, [refreshUnread, tab]);

  // Closing the league closes the conversation with it — the thread belongs to
  // the pool, so keeping it open across a switch would show the wrong one.
  useEffect(() => {
    setChatOpen(false);
    setChatPeer(null);
  }, [leagueId]);

  // First-time landing logic: with no remembered league, decide where returning
  // vs. brand-new users go — one league auto-selects it, several open the
  // picker, zero sends the user straight to Settings (create/join lives there).
  useEffect(() => {
    if (!username || leagueId || defaultChecked) return;
    api
      .myLeagues(username)
      .then((list) => {
        if (list.length === 0) {
          setTab("settings");
        } else if (list.length === 1) {
          openLeague(list[0].id);
        } else {
          setShowPicker(true);
        }
        setDefaultChecked(true);
      })
      .catch((e) => {
        setError((e as Error).message);
        setDefaultChecked(true);
      });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [username, leagueId, defaultChecked]);

  if (!username)
    return (
      <Login
        onLogin={(u) => {
          localStorage.setItem("fw-username", u);
          setUsername(u);
        }}
      />
    );

  if (showPicker)
    return (
      <LeagueGate
        username={username}
        onOpen={openLeague}
        onLogout={logout}
        onGoSettings={() => {
          setShowPicker(false);
          setTab("settings");
        }}
        onClose={leagueId ? () => setShowPicker(false) : undefined}
      />
    );

  const openChat = (peer: string | null) => {
    setChatPeer(peer);
    setChatOpen(true);
  };

  return (
    <LiveProvider username={username} leagueId={leagueId}>
    <div className="shell">
      <header className="topbar">
        <img className="topbar-logo" src={logo} alt="" />
        {/* No season next to the name (2026-08-03, per Nick) — it is the same
            every week, so it spent topbar width saying nothing. Settings still
            shows it where it is actually being read. */}
        <button className="league-switch" onClick={() => setShowPicker(true)} aria-label={t("app.switchLeague")}>
          <span className="league-switch-text">
            <span className="name">{league?.name ?? (leagueId ? "…" : t("app.selectLeague"))}</span>
          </span>
          <ChevronDownIcon size={16} />
        </button>
        {/* The week earns its topbar width where the season did not: it changes
            every Monday, and "which week am I looking at" is the question
            behind most of what the app shows. Not a button — it is a status,
            and nothing here would be a sensible destination. */}
        {league?.currentPeriod && (
          <span className="topbar-week">{t("app.week", { index: league.currentPeriod.index })}</span>
        )}
        {league && (
          <button
            className="topbar-chat"
            onClick={() => openChat(null)}
            aria-label={unread > 0 ? t("app.messagesUnread", { count: unread }) : t("app.messages")}
          >
            <MessageSquareIcon size={20} />
            {unread > 0 && (
              <span className="topbar-chat-badge" aria-hidden="true">
                {unread > 9 ? "9+" : unread}
              </span>
            )}
          </button>
        )}
        <ProfileMenu
          username={username}
          leagueId={leagueId}
          otherMembers={league?.members.filter((m) => m !== username) ?? []}
          onSettings={() => setTab("settings")}
          onLogout={logout}
          onOpenChat={openChat}
        />
      </header>

      <main className="shell-content" style={{ paddingTop: "1rem" }}>
        {error && <p className="error-banner">{error}</p>}

        {tab === "settings" && (
          <Settings
            username={username}
            league={league}
            onOpen={openLeague}
            onLogout={logout}
            onRulesSaved={refreshLeague}
          />
        )}

        {tab !== "settings" && !leagueId && (
          <p className="empty-state">
            {t("app.noLeagueSelected")}{" "}
            <button className="link-btn" onClick={() => setTab("settings")}>
              {t("settings.title")}
            </button>{" "}
            {t("app.noLeagueSelectedSuffix")}
          </p>
        )}
        {tab !== "settings" && leagueId && !league && !error && <LoadingLogo label={t("app.loadingLeague")} />}
        {league && tab === "dashboard" && <Dashboard league={league} username={username} />}
        {league && tab === "standings" && showPalmares && (
          <Palmares leagueId={league.id} onClose={() => setShowPalmares(false)} />
        )}
        {league && tab === "standings" && !showPalmares && (
          <Standings
            league={league}
            username={username}
            onOpenTeamStats={(owner) => {
              setStatsTeam(owner);
              setTab("stats");
            }}
            onOpenPalmares={() => setShowPalmares(true)}
          />
        )}
        {league && tab === "stats" && (
          <Stats
            league={league}
            username={username}
            targetUsername={statsTeam ?? username}
            onBackToStandings={() => setTab("standings")}
          />
        )}
        {league && tab === "trades" && <Trades league={league} username={username} />}
        {league && tab === "draft" && <DraftRoom league={league} username={username} />}
      </main>

      <NewsTicker leagueId={leagueId} league={league} username={username} />

      <nav className="bottom-nav" aria-label={t("app.mainNavigation")}>
        <button
          className={`nav-tab${tab === "dashboard" ? " active" : ""}`}
          onClick={() => setTab("dashboard")}
          aria-current={tab === "dashboard" ? "page" : undefined}
        >
          <BriefcaseIcon size={22} />
          {t("app.navGmOffice")}
        </button>
        <button
          className={`nav-tab${tab === "standings" ? " active" : ""}`}
          onClick={() => setTab("standings")}
          aria-current={tab === "standings" ? "page" : undefined}
        >
          <TrophyIcon size={22} />
          {t("app.navStandings")}
        </button>
        <button
          className={`nav-tab${tab === "stats" ? " active" : ""}`}
          onClick={() => {
            setStatsTeam(null);
            setTab("stats");
          }}
          aria-current={tab === "stats" ? "page" : undefined}
        >
          <ActivityIcon size={22} />
          {t("app.navTeam")}
        </button>
        <button
          className={`nav-tab${tab === "trades" ? " active" : ""}${
            offersToAnswer > 0 ? " nav-tab-awaiting" : ""
          }`}
          onClick={() => setTab("trades")}
          aria-current={tab === "trades" ? "page" : undefined}
          aria-label={offersToAnswer > 0 ? t("app.navTradesAwaiting", { count: offersToAnswer }) : undefined}
        >
          <span className="nav-tab-icon">
            <ArrowLeftRightIcon size={22} />
            {/* The count is the signal, not the colour — the gold only
                reinforces it, so this reads the same without colour vision. */}
            {offersToAnswer > 0 && (
              <span className="nav-tab-badge" aria-hidden="true">
                {offersToAnswer > 9 ? "9+" : offersToAnswer}
              </span>
            )}
          </span>
          {t("app.navTrades")}
        </button>
        {/* Only while a draft is running, plus the commissioner's one-phase
            preview of it in Protecting — he has to get in to open it. A
            permanent tab leading to "no draft is running" would be a dead
            destination in a bar that is already full. */}
        {draftTabVisible && (
          <button
            className={`nav-tab${tab === "draft" ? " active" : ""}${
              isMyDraftTurn ? " nav-tab-awaiting" : ""
            }`}
            onClick={() => setTab("draft")}
            aria-current={tab === "draft" ? "page" : undefined}
            aria-label={
              !draftRunning
                ? t("app.navDraftNotOpen")
                : isMyDraftTurn
                  ? t("app.navDraftYourTurn")
                  : t("app.navDraftLive")
            }
          >
            <span className="nav-tab-icon">
              <ListOrderedIcon size={22} />
              {/* The pill states a fact. Before the room opens there is nothing
                  live to announce. */}
              {draftRunning && (
                <span className="nav-tab-pill" aria-hidden="true">
                  {t("app.draftLive")}
                </span>
              )}
            </span>
            {t("app.navDraft")}
          </button>
        )}
      </nav>

      <UnreadBridge username={username} onIncoming={refreshUnread} />
      {draftRunning && <DraftBridge onDraft={setDraftTurn} />}
      {league && <CockmanCampaignGate username={username} league={league} />}
      <DoneDealRewardGate username={username} />

      <ToastHost />

      {chatOpen && league && username && (
        <ChatSheet
          leagueId={league.id}
          username={username}
          initialPeer={chatPeer}
          onClose={() => {
            setChatOpen(false);
            setChatPeer(null);
            refreshUnread();
          }}
          onUnreadChanged={refreshUnread}
        />
      )}
    </div>
    </LiveProvider>
  );
}
