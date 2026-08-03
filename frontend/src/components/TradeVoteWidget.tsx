// TradeVoteWidget — "who won this trade": proposer / fair / counterparty,
// no in-between. Storing the actual favored username (not a proposer/
// counterparty-relative number) is what makes votes aggregable across a GM's
// whole trade history later — see vPoolerTradeRecord.
//
// Blind ballot: the server withholds `trade.votes` (and the tally inside
// `trade.myVote`) until the viewer has voted themselves, so nobody's pick
// leans on the crowd's before they've formed their own. Once voted, each
// option reveals its vote count and a proportional fill, and the plurality
// leader gets extra emphasis — independent of which one is "mine".
//
// One vote per league member per trade, and it's permanent (2026-08-03, per
// Nick) — a confirm step sits between tapping an option and it actually
// being sent, and the server rejects a second vote regardless. Only
// meaningful for `processed` trades (the screen only renders this on the
// past-trades list).

import { useState } from "react";
import { api } from "../api";
import type { Trade } from "../api";
import "./TradeVoteWidget.css";

interface VoteOption {
  key: "proposer" | "fair" | "counterparty";
  favoredUsername: string | null;
  label: string;
}

export function TradeVoteWidget({
  leagueId,
  trade,
  username,
  onVoted,
}: {
  leagueId: string;
  trade: Trade;
  username: string;
  onVoted: () => void;
}) {
  const [voting, setVoting] = useState(false);
  // Optimistic selection: reflect the just-confirmed option immediately,
  // before the parent's full refetch round-trips `trade.myVote` back to us.
  const [pendingVote, setPendingVote] = useState<{ favoredUsername: string | null } | null>(null);
  // The option awaiting a yes/no in the confirm popup — not yet sent.
  const [confirming, setConfirming] = useState<VoteOption | null>(null);

  const options: VoteOption[] = [
    { key: "proposer", favoredUsername: trade.proposerUsername, label: trade.proposerTeamName },
    { key: "fair", favoredUsername: null, label: "Fair Trade" },
    { key: "counterparty", favoredUsername: trade.counterpartyUsername, label: trade.counterpartyTeamName },
  ];

  // Optimistic pick wins until the refetched trade catches up to it — and
  // its mere presence is also what locks the options against a second vote,
  // without waiting on that refetch.
  const selected = pendingVote ?? trade.myVote;
  const hasVoted = selected != null;
  const isMine = (opt: VoteOption) => selected != null && selected.favoredUsername === opt.favoredUsername;

  const confirmVote = async () => {
    const opt = confirming;
    if (opt == null || voting) return;
    setConfirming(null);
    setPendingVote({ favoredUsername: opt.favoredUsername });
    setVoting(true);
    try {
      await api.voteTrade(leagueId, trade.id, username, opt.favoredUsername);
      onVoted();
    } catch {
      setPendingVote(null); // failed — drop the optimistic highlight
    } finally {
      setVoting(false);
    }
  };

  // Only populated once the viewer has voted — the blind-ballot rule is
  // enforced server-side (trade.votes is null until then), this just renders
  // whatever the server is willing to show.
  const votes = trade.votes;
  const countFor = (opt: VoteOption) =>
    votes == null ? 0 : opt.key === "proposer" ? votes.proposer : opt.key === "fair" ? votes.fair : votes.counterparty;
  const pctFor = (opt: VoteOption) => (votes == null || votes.total === 0 ? 0 : (countFor(opt) / votes.total) * 100);
  const leaderKey =
    votes == null || votes.total === 0
      ? null
      : options.reduce((best, o) => (countFor(o) > countFor(best) ? o : best)).key;

  return (
    <div className="tvw">
      <span className="tvw-label">Who won the trade?</span>
      <div className={`tvw-options${voting ? " saving" : ""}`}>
        {options.map((opt) => {
          const mine = isMine(opt);
          const isLeader = leaderKey === opt.key;
          const count = countFor(opt);
          const pct = pctFor(opt);
          return (
            <button
              key={opt.key}
              type="button"
              className={`tvw-option tvw-option-${opt.key}${mine ? " mine" : ""}${isLeader ? " leading" : ""}`}
              onClick={() => setConfirming(opt)}
              disabled={voting || hasVoted}
              aria-pressed={mine}
              aria-label={votes != null ? `${opt.label}: ${count} of ${votes.total} votes` : opt.label}
            >
              {votes != null && (
                <span className="tvw-option-fill" style={{ width: `${pct}%` }} aria-hidden="true" />
              )}
              <span className="tvw-option-label">{opt.label}</span>
              {votes != null && (
                <span className="tvw-option-count">
                  {count} vote{count === 1 ? "" : "s"} · {Math.round(pct)}%
                </span>
              )}
            </button>
          );
        })}
      </div>
      <small className="muted tvw-hint">
        {voting
          ? "Saving your vote…"
          : votes != null
            ? `${votes.total} vote${votes.total === 1 ? "" : "s"} total`
            : "Vote to see how everyone else voted."}
      </small>

      {confirming && (
        <div className="tvw-confirm-overlay" role="dialog" aria-modal="true" onClick={() => setConfirming(null)}>
          <div className="tvw-confirm" onClick={(e) => e.stopPropagation()}>
            <p className="tvw-confirm-text">
              Vote for <strong>{confirming.label}</strong>? This can't be changed once cast.
            </p>
            <div className="tvw-confirm-actions">
              <button type="button" className="btn-ghost" onClick={() => setConfirming(null)}>
                Cancel
              </button>
              <button type="button" className="btn" onClick={() => void confirmVote()}>
                Confirm vote
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
