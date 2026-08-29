/** The draft room.
 *
 * One room, two drafts. The Drafting phase runs the steal rounds and then the
 * rookie / free-agent rounds back to back; the server decides which pool is
 * showing, and this screen never asks for a segment — it renders whatever it is
 * handed.
 *
 * **There is no clock.** Nobody is counted down and nothing auto-picks, so this
 * screen has no timer of its own. What replaces urgency is presence: the pick
 * feed and the gold "you're on the clock" state.
 *
 * **Pushes are signals, not data.** A "draft" push carries the whole board and
 * is applied wholesale, but the available list is always refetched — it depends
 * on quotas only the server knows, and a room applying a local delta would
 * eventually disagree with the database about who is takeable.
 */
import { useCallback, useEffect, useMemo, useState } from "react";
import {
  api,
  formatCapCompact,
  posGroupClass,
  type AutofillResult,
  type DraftCandidate,
  type DraftState,
  type DraftTurnRow,
  type LeagueDetail,
  type ProtectionBoard,
} from "../api";
import { useLive } from "../live/LiveProvider";
import { ListOrderedIcon } from "../components/Icons";
import "./DraftRoom.css";

type Pane = "available" | "board" | "teams" | "protections";

/** "S1.4", "R2.11" — the segment, its round, and the slot inside that round.
 * Steal and rookie rounds are numbered independently, so the letter is not
 * decoration: S2.1 and R2.1 are two different turns. */
function turnLabel(t: DraftTurnRow): string {
  return `${t.segment === "steal" ? "S" : "R"}${t.round}.${t.pickInRound}`;
}

const POSITIONS = ["ALL", "F", "D", "G"] as const;

export default function DraftRoom({
  league,
  username,
}: {
  league: LeagueDetail;
  username: string;
}) {
  const { onDraft, status } = useLive();

  const [state, setState] = useState<DraftState | null>(null);
  const [pool, setPool] = useState<DraftCandidate[]>([]);
  const [pane, setPane] = useState<Pane>("available");
  const [search, setSearch] = useState("");
  const [pos, setPos] = useState<string>("ALL");
  const [confirming, setConfirming] = useState<DraftCandidate | null>(null);
  const [passing, setPassing] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  // What the last autofill did, so the commissioner sees the size of the pool
  // he is about to draft from before he opens the room.
  const [autofill, setAutofill] = useState<AutofillResult | null>(null);
  // Every team's slate, fetched once when the pane is first opened. It does not
  // move during a draft — a steal only ever takes an exposed player, and nobody
  // exposed is in this list.
  const [protections, setProtections] = useState<ProtectionBoard | null>(null);
  const [protectionsTeam, setProtectionsTeam] = useState<string | null>(null);

  const isCommissioner = league.commissionerUsername === username;

  const refreshState = useCallback(async () => {
    try {
      setState(await api.draft(league.id, username));
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setLoading(false);
    }
  }, [league.id, username]);

  const refreshPool = useCallback(async () => {
    try {
      setPool(await api.draftAvailable(league.id, username, { search, pos }));
    } catch {
      // The board above is still correct without it, and an empty list already
      // reads as "nothing to take".
      setPool([]);
    }
  }, [league.id, username, search, pos]);

  useEffect(() => {
    void refreshState();
  }, [refreshState]);

  useEffect(() => {
    if (!state?.running) return;
    // Debounced so typing in the search box does not fire a request per
    // keystroke against a container that may have to wake up.
    const t = setTimeout(() => void refreshPool(), 250);
    return () => clearTimeout(t);
  }, [state?.turnsMade, state?.running, refreshPool]);

  // A push means the board moved. Apply the state it carries, then refetch the
  // pool — see the file header for why the pool is never taken from a push.
  useEffect(
    () =>
      onDraft((pushed) => {
        setState(pushed);
        void refreshPool();
      }),
    [onDraft, refreshPool],
  );

  // LiveProvider drops the socket after 60s of a hidden tab, so anything that
  // happened while you were away arrived nowhere. Coming back re-reads.
  useEffect(() => {
    if (status === "connected") void refreshState();
  }, [status, refreshState]);

  // Fetched on first open rather than with the board: it is a whole league's
  // rosters, it never changes while the room is running, and eleven twelfths of
  // the visits to this screen never open the pane at all.
  useEffect(() => {
    if (pane !== "protections" || protections) return;
    let cancelled = false;
    void api
      .draftProtections(league.id)
      .then((board) => {
        if (cancelled) return;
        setProtections(board);
        setProtectionsTeam(
          (current) =>
            current ??
            // Yours first: it is the one you came to check.
            board.teams.find((t) => t.ownerUsername === username)?.teamName ??
            board.teams[0]?.teamName ??
            null,
        );
      })
      .catch((e) => !cancelled && setError((e as Error).message));
    return () => {
      cancelled = true;
    };
  }, [pane, protections, league.id, username]);

  const select = async (playerId: number | null) => {
    if (!state?.onTheClock) return;
    setBusy(true);
    setError(null);
    try {
      setState(await api.draftSelect(league.id, username, playerId, state.onTheClock.overallIndex));
      setConfirming(null);
      setPassing(false);
      await refreshPool();
    } catch (e) {
      setError((e as Error).message);
      // Whatever went wrong, the board is the authority — re-read it rather
      // than leaving the screen showing a turn that has moved on.
      await refreshState();
    } finally {
      setBusy(false);
    }
  };

  const command = async (run: () => Promise<unknown>) => {
    setBusy(true);
    setError(null);
    try {
      await run();
      await refreshState();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const progress = useMemo(() => {
    if (!state?.totalTurns) return null;
    return `${state.turnsMade ?? 0} of ${state.totalTurns}`;
  }, [state]);

  if (loading) return <p className="empty-state">Loading the draft room…</p>;

  if (!state?.running) {
    return (
      <div className="draft-room">
        <p className="empty-state">
          No draft is running. This league is <strong>{state?.phase ?? "not in a season"}</strong>.
        </p>
        {isCommissioner && state?.phase === "Protecting" && (
          <div className="draft-setup">
            <p className="draft-setup-note">
              {autofill
                ? `${autofill.protectedCount} protected, ${autofill.slots} per team. ` +
                  `${autofill.freeCount} are safe already on NHL experience, ` +
                  `${autofill.exposedCount} are exposed.`
                : "Nobody is protected yet, so every veteran in the league is exposed."}
            </p>
            <div className="draft-commissioner">
              {/* .btn-outline, not .btn-ghost: it is the paired secondary of
                  "Open the draft" and has to hold its own weight. */}
              <button
                className="btn-outline"
                disabled={busy}
                onClick={() =>
                  command(async () => {
                    setAutofill(await api.autofillProtections(league.id, username));
                  })
                }
              >
                Auto-protect each roster
              </button>
              <button
                className="btn"
                disabled={busy}
                onClick={() => command(() => api.openDraft(league.id, username))}
              >
                Open the draft
              </button>
            </div>
          </div>
        )}
        {error && <p className="error-banner">{error}</p>}
      </div>
    );
  }

  const turn = state.onTheClock;
  const mine = state.isMyTurn === true;

  return (
    <div className="draft-room">
      <header className="draft-head">
        <h2 className="draft-title">
          <ListOrderedIcon size={18} />
          {state.year} draft
        </h2>
        <p className="draft-sub">
          {state.segment === "steal" ? "Steal round" : "Rookie &amp; free agents, round"}{" "}
          {state.round} of {state.segment === "steal" ? state.stealRounds : state.draftRounds}
          {progress && <span className="draft-progress"> · pick {progress}</span>}
        </p>
      </header>

      {/* Not shown while a sheet is open (Nick, 2026-08-29): a rejected pick
          used to land here, behind the confirm overlay's dim backdrop — a GM
          who tapped Confirm on an over-max roster saw the sheet just sit
          there, because the reason was rendered somewhere he could not read
          it. It now shows inside the sheet itself instead. */}
      {error && !confirming && !passing && <p className="error-banner">{error}</p>}

      <section className={`draft-clock${mine ? " mine" : ""}`} aria-live="polite">
        {turn ? (
          <>
            <span className="draft-clock-label">{mine ? "You're on the clock" : "On the clock"}</span>
            <span className="draft-clock-team">{turn.teamName}</span>
            {!mine && state.turnsUntilMine != null && (
              <span className="draft-clock-wait">
                {state.turnsUntilMine === 0 ? "" : `You pick in ${state.turnsUntilMine}`}
              </span>
            )}
            {!mine && state.turnsUntilMine == null && (
              <span className="draft-clock-wait">You have no turns left</span>
            )}
          </>
        ) : (
          <span className="draft-clock-label">Every turn is used.</span>
        )}
      </section>

      {state.myTeam && (
        <p className="draft-quota">
          <span>
            <strong>{state.myTeam.takes}</strong> taken
          </span>
          <span>
            <strong>{state.myTeam.losses}</strong>
            {state.maxLossesPerTeam != null ? ` of ${state.maxLossesPerTeam}` : ""} lost
          </span>
        </p>
      )}

      <div className="draft-panes" role="tablist" aria-label="Draft room sections">
        {(["available", "board", "teams", "protections"] as Pane[]).map((p) => (
          <button
            key={p}
            role="tab"
            aria-selected={pane === p}
            className={`draft-pane-btn${pane === p ? " active" : ""}`}
            onClick={() => setPane(p)}
          >
            {p === "available"
              ? "Available"
              : p === "board"
                ? "Picks"
                : p === "teams"
                  ? "Teams"
                  : "Protections"}
          </button>
        ))}
      </div>

      {pane === "available" && (
        <>
          <div className="draft-filters">
            <input
              className="draft-search"
              type="search"
              value={search}
              placeholder="Search a name"
              aria-label="Search available players"
              onChange={(e) => setSearch(e.target.value)}
            />
            <div className="draft-pos-filter">
              {POSITIONS.map((p) => (
                <button
                  key={p}
                  className={`draft-pos-chip${pos === p ? " active" : ""}`}
                  aria-pressed={pos === p}
                  onClick={() => setPos(p)}
                >
                  {p}
                </button>
              ))}
            </div>
          </div>

          {pool.length === 0 ? (
            <div className="draft-empty">
              <p className="empty-state">
                Nobody is available to take.
                {state.segment === "steal" &&
                  " Every remaining player is protected, auto-protected, or on a team that has already lost its limit."}
              </p>
              {mine && (
                <button className="btn-outline" disabled={busy} onClick={() => setPassing(true)}>
                  Pass the turn
                </button>
              )}
            </div>
          ) : (
            <ul className="draft-list">
              {pool.map((c) => (
                <li key={c.playerId}>
                  <button
                    className="draft-row"
                    disabled={!mine || busy}
                    aria-disabled={!mine}
                    aria-label={
                      `Draft ${c.shortName}` +
                      (c.ownerTeamName ? ` from ${c.ownerTeamName}` : "") +
                      `, ${formatCapCompact(c.capHit)}`
                    }
                    onClick={() => mine && setConfirming(c)}
                  >
                    <span className={`draft-row-pos pos-compact-${posGroupClass(c.position)}`}>
                      {c.positionGroup}
                    </span>
                    <span className="draft-row-name">{c.shortName}</span>
                    {/* The GM who holds him is what matters in a steal round.
                        In the rookie rounds nobody does, so the NHL club takes
                        the column back. */}
                    <span className="draft-row-owner">{c.ownerTeamName ?? c.nhlTeam ?? "—"}</span>
                    <span className="draft-row-cap">{formatCapCompact(c.capHit)}</span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </>
      )}

      {/* The whole board, taken turns and coming ones in one continuous list
          (Nick, 2026-08-29). The label — S1.4, R2.11 — is on every row rather
          than in round headers, so a row read on its own still says where in
          the draft it sits. */}
      {pane === "board" && (
        <ul className="draft-board">
          {(state.board ?? []).length === 0 && <li className="empty-state">No turns yet.</li>}
          {(state.board ?? []).map((t) => (
            <li
              key={t.overallIndex}
              className={`draft-board-row${t.done ? "" : " upcoming"}${
                t.overallIndex === turn?.overallIndex ? " onclock" : ""
              }`}
              aria-current={t.overallIndex === turn?.overallIndex ? "step" : undefined}
            >
              <span className="draft-board-index">{t.overallIndex + 1}</span>
              <span className="draft-board-slot">{turnLabel(t)}</span>
              <span className="draft-board-team">{t.byTeamName}</span>
              <span className="draft-board-outcome">
                {t.overallIndex === turn?.overallIndex ? (
                  <span className="draft-board-clock">On the clock</span>
                ) : !t.done ? (
                  ""
                ) : t.passed ? (
                  <span className="draft-board-passed">Passed</span>
                ) : (
                  <>
                    <span className={`pos-compact-${posGroupClass(t.player?.position ?? "")}`}>
                      {t.player?.positionGroup}
                    </span>{" "}
                    {t.player?.shortName}
                    {t.fromTeamName && (
                      <span className="draft-board-from"> ← {t.fromTeamName}</span>
                    )}
                  </>
                )}
              </span>
            </li>
          ))}
        </ul>
      )}

      {pane === "teams" && (
        <ul className="draft-teams">
          {(state.teams ?? []).map((t) => (
            <li key={t.teamName} className="draft-team-row">
              <span className="draft-team-name">{t.teamName}</span>
              <span className="draft-team-takes">+{t.takes}</span>
              <span
                className={`draft-team-losses${
                  state.maxLossesPerTeam != null && t.losses >= state.maxLossesPerTeam ? " maxed" : ""
                }`}
              >
                −{t.losses}
                {state.maxLossesPerTeam != null ? ` / ${state.maxLossesPerTeam}` : ""}
              </span>
            </li>
          ))}
        </ul>
      )}

      {/* One team at a time, chosen from a dropdown (Nick, 2026-08-29). Fourteen
          slates at once would be 300 rows of nothing anyone asked for; the
          question a GM actually has is about one rival. */}
      {pane === "protections" && (
        <div className="draft-protections">
          {!protections ? (
            <p className="empty-state">Loading the protection lists…</p>
          ) : (
            <>
              <label className="draft-team-picker">
                <span className="draft-team-picker-label">Team</span>
                <select
                  value={protectionsTeam ?? ""}
                  onChange={(e) => setProtectionsTeam(e.target.value)}
                >
                  {protections.teams.map((t) => (
                    <option key={t.teamName} value={t.teamName}>
                      {t.teamName}
                      {t.ownerUsername ? ` · ${t.ownerUsername}` : ""}
                    </option>
                  ))}
                </select>
              </label>
              {(() => {
                const team = protections.teams.find((t) => t.teamName === protectionsTeam);
                if (!team) return <p className="empty-state">Pick a team.</p>;
                return (
                  <>
                    <p className="draft-protections-summary">
                      <span>
                        <strong>{team.protectedCount}</strong>
                        {protections.slots != null ? ` of ${protections.slots}` : ""} protected
                      </span>
                      <span>
                        <strong>{team.autoCount}</strong> safe for free
                      </span>
                      <span>
                        <strong>{team.exposedCount}</strong> exposed
                      </span>
                    </p>
                    {team.players.length === 0 ? (
                      <p className="empty-state">
                        Nobody is protected. Every veteran on this roster is takeable.
                      </p>
                    ) : (
                      <ul className="draft-protection-list">
                        {team.players.map((p) => (
                          <li key={p.playerId} className="draft-protection-row">
                            <span className={`pos-compact-${posGroupClass(p.position)}`}>
                              {p.positionGroup}
                            </span>
                            <span className="draft-protection-name">{p.shortName}</span>
                            <span className="draft-protection-cap">{formatCapCompact(p.capHit)}</span>
                            {/* "Auto" is free and nobody chose it; "protected"
                                cost a slot. Collapsing them would hide the one
                                thing worth arguing about — a slot spent on
                                someone who was already safe. */}
                            <span className={`draft-protection-tag ${p.status}`}>
                              {p.status === "protected"
                                ? "Protected"
                                : p.status === "auto"
                                  ? "Auto"
                                  : "No NHL data"}
                            </span>
                          </li>
                        ))}
                      </ul>
                    )}
                  </>
                );
              })()}
            </>
          )}
        </div>
      )}

      {isCommissioner && (
        <div className="draft-commissioner">
          <button className="btn-outline" disabled={busy} onClick={() => command(() => api.closeDraft(league.id, username))}>
            Close the draft
          </button>
        </div>
      )}

      {confirming && (
        <ConfirmSheet
          title={state.segment === "steal" ? "Take this player?" : "Draft this player?"}
          body={
            <>
              <p className="draft-confirm-name">
                <span className={`pos-compact-${posGroupClass(confirming.position)}`}>
                  {confirming.positionGroup}
                </span>{" "}
                {confirming.shortName}
              </p>
              <p className="draft-confirm-detail">
                {confirming.ownerTeamName
                  ? `Taken from ${confirming.ownerTeamName}.`
                  : `Unrostered${confirming.nhlTeam ? ` · ${confirming.nhlTeam}` : ""}.`}{" "}
                Cap hit {formatCapCompact(confirming.capHit)}.
              </p>
              <p className="draft-confirm-warn">This cannot be undone.</p>
            </>
          }
          confirmLabel={busy ? "Working…" : "Confirm"}
          busy={busy}
          error={error}
          onCancel={() => setConfirming(null)}
          onConfirm={() => void select(confirming.playerId)}
        />
      )}

      {passing && (
        <ConfirmSheet
          title="Pass your turn?"
          body={<p className="draft-confirm-detail">You will take nobody and the draft moves on.</p>}
          confirmLabel={busy ? "Working…" : "Pass"}
          busy={busy}
          error={error}
          onCancel={() => setPassing(false)}
          onConfirm={() => void select(null)}
        />
      )}
    </div>
  );
}

/** Reuses PlayerCard's overlay/sheet chrome rather than inventing modal CSS —
 * the same thing CreateTradeSheet does. Never window.confirm: it is not Night
 * Arena and it cannot be styled. */
function ConfirmSheet({
  title,
  body,
  confirmLabel,
  busy,
  error,
  onCancel,
  onConfirm,
}: {
  title: string;
  body: React.ReactNode;
  confirmLabel: string;
  busy: boolean;
  /** A rejected attempt from the same sheet — a cap or roster-size rule, most
   * often. Shown inside the sheet rather than as the room's own banner: that
   * banner sits behind this overlay's dimmed backdrop, which used to make a
   * declined pick look like the Confirm button had done nothing at all. */
  error?: string | null;
  onCancel: () => void;
  onConfirm: () => void;
}) {
  return (
    <div className="pc-overlay" role="dialog" aria-modal="true" aria-label={title}>
      <div className="pc-sheet draft-confirm">
        <h3 className="draft-confirm-title">{title}</h3>
        {body}
        {error && <p className="error-banner">{error}</p>}
        <div className="draft-confirm-actions">
          <button className="btn-outline" onClick={onCancel} disabled={busy}>
            Cancel
          </button>
          <button className="btn" onClick={onConfirm} disabled={busy}>
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
}
