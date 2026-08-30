import type React from "react";
import { useMemo, useState } from "react";
import { InfoIcon } from "../components/Icons";
import { api } from "../api";
import type { LeagueDetail, RuleGap, RuleSet } from "../api";

/**
 * Commissioner-only league rules — the whole configuration surface, in the
 * sections a GM would name them.
 *
 * **Saved is not the same as enforced.** The API answers with the rules nothing
 * acts on yet, and every one of them is badged here rather than left to look
 * live. A commissioner may record the pool's real rules before the code catches
 * up; what must never happen is a value stored, ignored, and silently replaced
 * by a default.
 *
 * The panel edits the season being *prepared*. During the off-season that is
 * not the season the standings are being scored under, which is why a change
 * here never restates a finished season.
 */
export function RulesPanel({
  league,
  username,
  onSaved,
  onClose,
}: {
  league: LeagueDetail;
  username: string;
  onSaved: () => void;
  onClose: () => void;
}): React.JSX.Element {
  const [rules, setRules] = useState<RuleSet>(structuredClone(league.ruleSet));
  const [gaps, setGaps] = useState<RuleGap[]>(league.unsupported);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  const gapAt = useMemo(() => {
    const byPath = new Map(gaps.map((g) => [g.path, g]));
    return (path: string): RuleGap | undefined => byPath.get(path);
  }, [gaps]);

  /** Applies a change to a copy — the panel never mutates the league's own document. */
  const edit = (change: (draft: RuleSet) => void) => {
    const draft = structuredClone(rules);
    change(draft);
    setRules(draft);
  };

  const save = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError("");
    try {
      const res = await api.updateRules(league.id, username, rules);
      // What the server says is inert, not what this screen guessed — the two
      // could otherwise disagree the moment a rule ships.
      setGaps(res.unsupported);
      onSaved();
      onClose();
    } catch (err) {
      setError((err as Error).message);
      setBusy(false);
    }
  };

  return (
    <form onSubmit={save} className="card fade-in rules-panel">
      <span className="section-title">League rules (commissioner)</span>
      {error && <p className="error-banner">{error}</p>}

      <Section title="Pool">
        <Gap gap={gapAt("poolType")} />
        <div className="rules-grid">
          <label>
            Type
            <select
              className="field"
              value={rules.poolType}
              onChange={(e) => edit((d) => (d.poolType = e.target.value as RuleSet["poolType"]))}
            >
              <option value="keeper">Keeper — rosters carry over</option>
              <option value="singleSeason">Single season — redraft every year</option>
            </select>
          </label>
        </div>
      </Section>

      <Section
        title="Salary cap"
        note="Enforced on trades and on draft selections, against the engaged figures — a trade already accepted counts before it lands."
      >
        <Gap gap={gapAt("cap.min")} />
        <div className="rules-grid">
          <Money
            label="Cap ceiling"
            value={rules.cap.max}
            placeholder="no cap"
            onChange={(v) => edit((d) => (d.cap.max = v))}
          />
          <Money
            label="Cap floor"
            value={rules.cap.min}
            placeholder="no floor"
            onChange={(v) => edit((d) => (d.cap.min = v))}
          />
        </div>
        <small className="muted">
          What a player with no contract on file counts against the cap. Unsigned free
          agents and undrafted prospects never get a salary — set 0 to carry them for free.
        </small>
        <div className="rules-grid">
          <Money
            label="Unsigned player counts as"
            value={rules.cap.defaultCapHit}
            placeholder="0"
            onChange={(v) => edit((d) => (d.cap.defaultCapHit = v ?? 0))}
          />
        </div>
      </Section>

      <Section title="Roster size" note="Enforced on trades, when proposed and when accepted.">
        <Gap gap={gapAt("roster.byPosition")} />
        <div className="rules-grid">
          <Count
            label="Minimum"
            value={rules.roster.min}
            placeholder="no limit"
            onChange={(v) => edit((d) => (d.roster.min = v))}
          />
          <Count
            label="Maximum"
            value={rules.roster.max}
            placeholder="no limit"
            onChange={(v) => edit((d) => (d.roster.max = v))}
          />
        </div>
        <small className="muted">
          Per position, on top of the overall bounds. Empty means that group is not
          constrained on its own.
        </small>
        <div className="rules-grid">
          {GROUPS.map(({ key, label }) => (
            <Count
              key={`rmin-${key}`}
              label={`${label} min`}
              value={rules.roster.byPosition[key].min}
              placeholder="—"
              onChange={(v) => edit((d) => (d.roster.byPosition[key].min = v))}
            />
          ))}
          {GROUPS.map(({ key, label }) => (
            <Count
              key={`rmax-${key}`}
              label={`${label} max`}
              value={rules.roster.byPosition[key].max}
              placeholder="—"
              onChange={(v) => edit((d) => (d.roster.byPosition[key].max = v))}
            />
          ))}
        </div>
        <small className="muted">
          Équipe slot (each GM owns one NHL franchise that scores):{" "}
          <strong>{rules.roster.franchiseSlot ? "yes" : "no"}</strong>. Set when the league
          is built — turning it on here would create no slots.
        </small>
      </Section>

      <Section
        title="Weekly lineup"
        note="How many players each GM may activate per position. Only active players score, and fielding fewer is allowed."
      >
        <Gap gap={gapAt("lineup.mode")} />
        <Gap gap={gapAt("lineup.onMissing")} />
        <div className="rules-grid">
          <label>
            Who scores
            <select
              className="field"
              value={rules.lineup.mode}
              onChange={(e) => edit((d) => (d.lineup.mode = e.target.value as RuleSet["lineup"]["mode"]))}
            >
              <option value="activeSelection">The GM picks each week</option>
              <option value="topN">The best N per position, automatically</option>
            </select>
          </label>
          <label>
            A forgotten lineup
            <select
              className="field"
              value={rules.lineup.onMissing}
              onChange={(e) =>
                edit((d) => (d.lineup.onMissing = e.target.value as RuleSet["lineup"]["onMissing"]))
              }
            >
              <option value="carryForward">Carries forward from last week</option>
              <option value="scoreZero">Scores nothing</option>
            </select>
          </label>
        </div>
        <div className="rules-grid">
          {GROUPS.map(({ key, label }) => (
            <Count
              key={`slot-${key}`}
              label={label}
              value={rules.lineup.slots[key]}
              placeholder="0"
              onChange={(v) => edit((d) => (d.lineup.slots[key] = v ?? 0))}
            />
          ))}
        </div>
      </Section>

      <Section
        title="Point values"
        note="Any stat the app tracks can be scored — adding one is a setting, not a release."
      >
        <Gap gap={gapAt("scoring.byPosition")} />
        <Gap gap={gapAt("scoring.includePlayoffs")} />
        <div className="rules-grid">
          {SCORED_STATS.filter(
            ({ key }) => !TEAM_STATS.includes(key) || rules.roster.franchiseSlot,
          ).map(({ key, label }) => (
            <label key={key}>
              {label}
              <input
                className="field"
                inputMode="decimal"
                value={rules.scoring.values[key] ?? 0}
                onChange={(e) =>
                  edit((d) => (d.scoring.values[key] = Number(e.target.value) || 0))
                }
              />
            </label>
          ))}
        </div>
        <label className="rules-check">
          <input
            type="checkbox"
            checked={rules.scoring.includePlayoffs}
            onChange={(e) => edit((d) => (d.scoring.includePlayoffs = e.target.checked))}
          />
          Playoff games score
        </label>
      </Section>

      <Section title="Trades">
        <Gap gap={gapAt("trades.approval")} />
        <Gap gap={gapAt("trades.pickYearsAhead")} />
        <label className="rules-check">
          <input
            type="checkbox"
            checked={rules.trades.enabled}
            onChange={(e) => edit((d) => (d.trades.enabled = e.target.checked))}
          />
          Trades are allowed
        </label>
        <label className="rules-check">
          <input
            type="checkbox"
            checked={rules.trades.picksTradable}
            onChange={(e) => edit((d) => (d.trades.picksTradable = e.target.checked))}
          />
          Draft picks can be traded
        </label>
        <div className="rules-grid">
          <Count
            label="Picks exist this many years ahead"
            value={rules.trades.pickYearsAhead}
            placeholder="1"
            onChange={(v) => edit((d) => (d.trades.pickYearsAhead = v ?? 1))}
          />
          <label>
            Approval
            <select
              className="field"
              value={rules.trades.approval}
              onChange={(e) =>
                edit((d) => (d.trades.approval = e.target.value as RuleSet["trades"]["approval"]))
              }
            >
              <option value="none">Both GMs agreeing is enough</option>
              <option value="commissioner">The commissioner may veto</option>
              <option value="leagueVote">The league votes</option>
            </select>
          </label>
        </div>
      </Section>

      <Section
        title="Off-season protections"
        note="How many roster spots each GM shelters before the steal draft. A player with too little NHL experience is safe for free and costs no slot."
      >
        <Gap gap={gapAt("protection.slotsByPosition")} />
        <Gap gap={gapAt("protection.afterDraft")} />
        <div className="rules-grid">
          <Count
            label="Protection slots"
            value={rules.protection.slots}
            placeholder="not configured"
            onChange={(v) => edit((d) => (d.protection.slots = v))}
          />
          <label>
            Unclaimed exposed players
            <select
              className="field"
              value={rules.protection.afterDraft}
              onChange={(e) =>
                edit(
                  (d) =>
                    (d.protection.afterDraft = e.target
                      .value as RuleSet["protection"]["afterDraft"]),
                )
              }
            >
              <option value="stayWithTeam">Stay on their team</option>
              <option value="releasedToFreeAgents">Are released to free agency</option>
            </select>
          </label>
        </div>
        <label className="rules-check">
          <input
            type="checkbox"
            checked={rules.protection.auto.enabled}
            onChange={(e) => edit((d) => (d.protection.auto.enabled = e.target.checked))}
          />
          Protect inexperienced players for free
        </label>
        <small className="muted">
          Goalies count separately and lower: a goalie plays about half his club's games,
          so a skater's bar would keep him untouchable for twice as many seasons.
        </small>
        <div className="rules-grid">
          <Count
            label="Skater career NHL games"
            value={rules.protection.auto.skaterMaxCareerGames}
            placeholder="100"
            onChange={(v) => edit((d) => (d.protection.auto.skaterMaxCareerGames = v ?? 0))}
          />
          <Count
            label="Goalie career NHL games"
            value={rules.protection.auto.goalieMaxCareerGames}
            placeholder="50"
            onChange={(v) => edit((d) => (d.protection.auto.goalieMaxCareerGames = v ?? 0))}
          />
        </div>
      </Section>

      <Section title="Off-season draft">
        <Gap gap={gapAt("draft.unprotectedDisposition")} />
        <Gap gap={gapAt("draft.steal.turnsTradable")} />
        <Gap gap={gapAt("draft.snake")} />
        <div className="rules-grid">
          <label>
            Unprotected players
            <select
              className="field"
              value={rules.draft.unprotectedDisposition}
              onChange={(e) =>
                edit(
                  (d) =>
                    (d.draft.unprotectedDisposition = e.target
                      .value as RuleSet["draft"]["unprotectedDisposition"]),
                )
              }
            >
              <option value="stealRounds">Are taken in dedicated steal rounds</option>
              <option value="openPool">Join the ordinary draft pool</option>
            </select>
          </label>
          <Count
            label="Steal rounds"
            value={rules.draft.steal.rounds}
            placeholder="0"
            onChange={(v) => edit((d) => (d.draft.steal.rounds = v ?? 0))}
          />
          <Count
            label="Most a team may lose"
            value={rules.draft.steal.maxLossesPerTeam}
            placeholder="uncapped"
            onChange={(v) => edit((d) => (d.draft.steal.maxLossesPerTeam = v))}
          />
          <Count
            label="Rookie rounds"
            value={rules.draft.rookieRounds}
            placeholder="no draft"
            onChange={(v) => edit((d) => (d.draft.rookieRounds = v))}
          />
        </div>
        <label className="rules-check">
          <input
            type="checkbox"
            checked={rules.draft.steal.turnsTradable}
            onChange={(e) => edit((d) => (d.draft.steal.turnsTradable = e.target.checked))}
          />
          Steal turns can be traded
        </label>
        <label className="rules-check">
          <input
            type="checkbox"
            checked={rules.draft.snake}
            onChange={(e) => edit((d) => (d.draft.snake = e.target.checked))}
          />
          Snake order (reverses every round)
        </label>
      </Section>

      <Section title="Free agency">
        <Gap gap={gapAt("freeAgency.mode")} />
        <div className="rules-grid">
          <label>
            When
            <select
              className="field"
              value={rules.freeAgency.mode}
              onChange={(e) =>
                edit((d) => (d.freeAgency.mode = e.target.value as RuleSet["freeAgency"]["mode"]))
              }
            >
              <option value="none">Never — trades and the draft only</option>
              <option value="anytime">Any time</option>
              <option value="windows">Only in set windows</option>
            </select>
          </label>
          <label>
            What
            <select
              className="field"
              value={rules.freeAgency.allow}
              onChange={(e) =>
                edit((d) => (d.freeAgency.allow = e.target.value as RuleSet["freeAgency"]["allow"]))
              }
            >
              <option value="both">Add and drop</option>
              <option value="add">Add only</option>
              <option value="drop">Drop only</option>
            </select>
          </label>
          <Count
            label="Moves per week"
            value={rules.freeAgency.movesPerPeriod}
            placeholder="unlimited"
            onChange={(v) => edit((d) => (d.freeAgency.movesPerPeriod = v))}
          />
        </div>
      </Section>

      <p className="muted" style={{ margin: 0, fontSize: "0.8rem" }}>
        Applies from the next nightly scoring run. Weeks already scored keep the scale
        they were played under — changing the rules mid-season does not restate history.
      </p>
      <div style={{ display: "flex", gap: "0.6rem" }}>
        <button type="submit" className="btn" disabled={busy} style={{ flex: 1 }}>
          {busy ? "Saving…" : "Save rules"}
        </button>
        <button type="button" className="btn-outline" onClick={onClose}>
          Cancel
        </button>
      </div>
    </form>
  );
}

/* ---------- pieces ---------- */

function Section({
  title,
  note,
  children,
}: {
  title: string;
  note?: string;
  children: React.ReactNode;
}): React.JSX.Element {
  return (
    <div className="rules-section">
      <span className="section-title" style={{ letterSpacing: "0.05em" }}>
        {title}
      </span>
      {note && <small className="muted">{note}</small>}
      {children}
    </div>
  );
}

/**
 * A rule that is recorded but that nothing acts on. Rendered only when the
 * server says so, so a feature shipping removes the badge with no change here.
 */
function Gap({ gap }: { gap: RuleGap | undefined }): React.JSX.Element | null {
  if (!gap) return null;
  return (
    <p className="rules-gap">
      <InfoIcon size={14} />
      <span>
        <strong>Not enforced yet.</strong> {gap.message}
      </span>
    </p>
  );
}

/** A whole-number field where empty means "no value", never zero. */
function Count({
  label,
  value,
  placeholder,
  onChange,
}: {
  label: string;
  value: number | null;
  placeholder: string;
  onChange: (value: number | null) => void;
}): React.JSX.Element {
  return (
    <label>
      {label}
      <input
        className="field"
        inputMode="numeric"
        placeholder={placeholder}
        value={value ?? ""}
        onChange={(e) =>
          onChange(e.target.value === "" ? null : Math.max(0, Number(e.target.value) || 0))
        }
      />
    </label>
  );
}

/** Whole dollars, entered as digits. Same empty-means-none rule as Count. */
function Money({
  label,
  value,
  placeholder,
  onChange,
}: {
  label: string;
  value: number | null;
  placeholder: string;
  onChange: (value: number | null) => void;
}): React.JSX.Element {
  return (
    <label>
      {label}
      <input
        className="field"
        inputMode="numeric"
        placeholder={placeholder}
        value={value ?? ""}
        onChange={(e) =>
          onChange(e.target.value === "" ? null : Math.max(0, Number(e.target.value) || 0))
        }
      />
    </label>
  );
}

/* ---------- the fields these sections iterate ---------- */

const GROUPS = [
  { key: "forwards", label: "Forwards" },
  { key: "defense", label: "Defense" },
  { key: "goalies", label: "Goalies" },
] as const;

const TEAM_STATS = ["teamWins", "teamOtLosses", "teamLosses"];

/**
 * The stats a commissioner can price from this screen. Not the whole of
 * `StatKeys` — the rest are scored the same way and can be added here without
 * touching the backend, which is the point of a map over a fixed list.
 */
const SCORED_STATS = [
  { key: "goals", label: "Goal" },
  { key: "assists", label: "Assist" },
  { key: "wins", label: "Goalie win" },
  { key: "otLosses", label: "Goalie OT loss" },
  { key: "shutouts", label: "Shutout" },
  { key: "plusMinus", label: "Plus / minus" },
  { key: "pim", label: "Penalty minute" },
  { key: "shots", label: "Shot" },
  { key: "hits", label: "Hit" },
  { key: "blockedShots", label: "Blocked shot" },
  { key: "teamWins", label: "Franchise win" },
  { key: "teamOtLosses", label: "Franchise OT loss" },
  { key: "teamLosses", label: "Franchise loss" },
];
