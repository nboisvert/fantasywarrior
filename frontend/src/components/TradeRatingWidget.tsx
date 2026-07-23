// TradeRatingWidget — 5-level "who won this trade" vote: 1 = proposer's
// team clearly won, 3 = fair, 5 = counterparty's team clearly won. One vote
// per league member per trade (re-voting overwrites); shows the average +
// vote count to everyone. Only meaningful for `processed` trades (the
// screen only renders this on the past-trades list).

import { useState } from "react";
import { api } from "../api";
import type { Trade } from "../api";
import "./TradeRatingWidget.css";

const SEGMENT_CLASS = ["trw-seg-left", "trw-seg-left", "trw-seg-mid", "trw-seg-right", "trw-seg-right"];

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

  const vote = async (level: number) => {
    if (voting) return;
    setVoting(true);
    try {
      await api.voteTrade(leagueId, trade.id, username, level);
      onVoted();
    } finally {
      setVoting(false);
    }
  };

  const markerPct = trade.avgRating != null ? ((trade.avgRating - 1) / 4) * 100 : null;

  return (
    <div className="trw">
      <div className="trw-anchor">
        <span>{trade.proposerTeamName}</span>
        <span>{trade.counterpartyTeamName}</span>
      </div>
      <div className="trw-scale">
        {[1, 2, 3, 4, 5].map((level, i) => (
          <button
            key={level}
            type="button"
            className={`trw-seg ${SEGMENT_CLASS[i]}${trade.myVote === level ? " selected" : ""}`}
            onClick={() => vote(level)}
            disabled={voting}
            aria-pressed={trade.myVote === level}
            aria-label={level === 3 ? "Fair trade" : `Level ${level} of 5`}
          >
            {level === 3 ? "Fair" : level}
          </button>
        ))}
      </div>
      {markerPct != null && (
        <div className="trw-track">
          <span className="trw-marker" style={{ left: `${markerPct}%` }} />
        </div>
      )}
      <small className="muted trw-summary">
        {trade.voteCount > 0
          ? `Avg ${trade.avgRating?.toFixed(1)} · ${trade.voteCount} vote${trade.voteCount === 1 ? "" : "s"}`
          : "No votes yet"}
      </small>
    </div>
  );
}
