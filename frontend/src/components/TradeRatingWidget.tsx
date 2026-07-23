// TradeRatingWidget — "who won this trade" vote as a literal star scale:
// ★★ / ★ / Fair / ★ / ★★, from proposer-clear-win to counterparty-clear-win.
// Storing the actual favored username (not a proposer/counterparty-relative
// number) is what makes votes aggregable across a GM's whole trade history
// later. One vote per league member per trade (re-voting overwrites); tally
// + a plain-language summary is shown to everyone. Only meaningful for
// `processed` trades (the screen only renders this on the past-trades list).

import { useState } from "react";
import { api } from "../api";
import type { Trade } from "../api";
import { StarIcon } from "./Icons";
import "./TradeRatingWidget.css";

interface RatingOption {
  key: string;
  favoredUsername: string | null;
  magnitude: number;
  stars: number;
  side: "proposer" | "fair" | "counterparty";
  label: string;
}

export function TradeRatingWidget({
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

  const options: RatingOption[] = [
    { key: "proposer-2", favoredUsername: trade.proposerUsername, magnitude: 2, stars: 2, side: "proposer", label: `${trade.proposerTeamName} clearly won` },
    { key: "proposer-1", favoredUsername: trade.proposerUsername, magnitude: 1, stars: 1, side: "proposer", label: `${trade.proposerTeamName} leans favored` },
    { key: "fair", favoredUsername: null, magnitude: 0, stars: 0, side: "fair", label: "Fair trade" },
    { key: "counterparty-1", favoredUsername: trade.counterpartyUsername, magnitude: 1, stars: 1, side: "counterparty", label: `${trade.counterpartyTeamName} leans favored` },
    { key: "counterparty-2", favoredUsername: trade.counterpartyUsername, magnitude: 2, stars: 2, side: "counterparty", label: `${trade.counterpartyTeamName} clearly won` },
  ];

  const vote = async (opt: RatingOption) => {
    if (voting) return;
    setVoting(true);
    try {
      await api.voteTrade(leagueId, trade.id, username, opt.favoredUsername, opt.magnitude);
      onVoted();
    } finally {
      setVoting(false);
    }
  };

  const isSelected = (opt: RatingOption) =>
    trade.myVote != null &&
    trade.myVote.favoredUsername === opt.favoredUsername &&
    trade.myVote.magnitude === opt.magnitude;

  const { proposerClear, proposerLean, fair, counterpartyLean, counterpartyClear, total } = trade.votes;
  const proposerSum = proposerClear + proposerLean;
  const counterpartySum = counterpartyLean + counterpartyClear;

  let summary: string;
  if (total === 0) {
    summary = "No votes yet";
  } else if (fair >= proposerSum && fair >= counterpartySum) {
    summary = `Fair trade (${fair} of ${total} voter${total === 1 ? "" : "s"})`;
  } else if (proposerSum > counterpartySum) {
    summary = `${trade.proposerTeamName} favored, ${proposerSum} of ${total} voter${total === 1 ? "" : "s"}`;
  } else {
    summary = `${trade.counterpartyTeamName} favored, ${counterpartySum} of ${total} voter${total === 1 ? "" : "s"}`;
  }

  const markerPct =
    total > 0
      ? (((-2 * proposerClear - proposerLean + counterpartyLean + 2 * counterpartyClear) / total + 2) / 4) * 100
      : null;

  return (
    <div className="trw">
      <div className="trw-anchor">
        <span>{trade.proposerTeamName}</span>
        <span>{trade.counterpartyTeamName}</span>
      </div>
      <div className="trw-scale">
        {options.map((opt) => (
          <button
            key={opt.key}
            type="button"
            className={`trw-seg trw-seg-${opt.side}${isSelected(opt) ? " selected" : ""}`}
            onClick={() => vote(opt)}
            disabled={voting}
            aria-pressed={isSelected(opt)}
            aria-label={opt.label}
          >
            {opt.side === "fair" ? (
              "Fair"
            ) : (
              <span className="trw-stars">
                {Array.from({ length: opt.stars }).map((_, i) => (
                  <StarIcon key={i} size={16} />
                ))}
              </span>
            )}
          </button>
        ))}
      </div>
      {markerPct != null && (
        <div className="trw-track">
          <span className="trw-marker" style={{ left: `${markerPct}%` }} />
        </div>
      )}
      <small className="muted trw-summary">{summary}</small>
    </div>
  );
}
