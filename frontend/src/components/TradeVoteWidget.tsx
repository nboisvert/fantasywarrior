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
// One vote per league member per trade (re-voting overwrites). Only
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
  // Optimistic selection: reflect the just-clicked option immediately, before
  // the parent's full refetch round-trips `trade.myVote` back to us.
  const [pendingVote, setPendingVote] = useState<{ favoredUsername: string | null } | null>(null);

  const options: VoteOption[] = [
    { key: "proposer", favoredUsername: trade.proposerUsername, label: trade.proposerTeamName },
    { key: "fair", favoredUsername: null, label: "Fair Trade" },
    { key: "counterparty", favoredUsername: trade.counterpartyUsername, label: trade.counterpartyTeamName },
  ];

  const vote = async (opt: VoteOption) => {
    if (voting) return;
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

  // Optimistic pick wins until the refetched trade catches up to it.
  const selected = pendingVote ?? trade.myVote;
  const isMine = (opt: VoteOption) => selected != null && selected.favoredUsername === opt.favoredUsername;

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
              onClick={() => vote(opt)}
              disabled={voting}
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
    </div>
  );
}
