import type React from "react";
import { useMemo, useState } from "react";
import { InfoIcon } from "../components/Icons";
import { api } from "../api";
import type { LeagueDetail, RuleGap, RuleSet } from "../api";
import { useLanguage } from "../i18n/LanguageContext";

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
  const { t } = useLanguage();
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

  const groupLabel = (key: (typeof GROUPS)[number]["key"]) => t(`rulesPanel.${GROUP_LABEL_KEY[key]}`);
  const statLabel = (key: string) => t(`rulesPanel.${STAT_LABEL_KEY[key] ?? key}`);

  return (
    <form onSubmit={save} className="card fade-in rules-panel">
      <span className="section-title">{t("rulesPanel.panelTitle")}</span>
      {error && <p className="error-banner">{error}</p>}

      <Section title={t("rulesPanel.sectionPool")}>
        <Gap gap={gapAt("poolType")} />
        <div className="rules-grid">
          <label>
            {t("rulesPanel.poolTypeLabel")}
            <select
              className="field"
              value={rules.poolType}
              onChange={(e) => edit((d) => (d.poolType = e.target.value as RuleSet["poolType"]))}
            >
              <option value="keeper">{t("rulesPanel.poolTypeKeeper")}</option>
              <option value="singleSeason">{t("rulesPanel.poolTypeSingleSeason")}</option>
            </select>
          </label>
        </div>
      </Section>

      <Section title={t("rulesPanel.sectionCap")} note={t("rulesPanel.capNote")}>
        <Gap gap={gapAt("cap.min")} />
        <div className="rules-grid">
          <Money
            label={t("rulesPanel.capCeiling")}
            value={rules.cap.max}
            placeholder={t("rulesPanel.noCap")}
            onChange={(v) => edit((d) => (d.cap.max = v))}
          />
          <Money
            label={t("rulesPanel.capFloor")}
            value={rules.cap.min}
            placeholder={t("rulesPanel.noFloor")}
            onChange={(v) => edit((d) => (d.cap.min = v))}
          />
        </div>
        <small className="muted">{t("rulesPanel.capDefaultNote")}</small>
        <div className="rules-grid">
          <Money
            label={t("rulesPanel.unsignedCountsAs")}
            value={rules.cap.defaultCapHit}
            placeholder="0"
            onChange={(v) => edit((d) => (d.cap.defaultCapHit = v ?? 0))}
          />
        </div>
      </Section>

      <Section title={t("rulesPanel.sectionRoster")} note={t("rulesPanel.rosterNote")}>
        <Gap gap={gapAt("roster.byPosition")} />
        <div className="rules-grid">
          <Count
            label={t("rulesPanel.minimum")}
            value={rules.roster.min}
            placeholder={t("rulesPanel.noLimit")}
            onChange={(v) => edit((d) => (d.roster.min = v))}
          />
          <Count
            label={t("rulesPanel.maximum")}
            value={rules.roster.max}
            placeholder={t("rulesPanel.noLimit")}
            onChange={(v) => edit((d) => (d.roster.max = v))}
          />
        </div>
        <small className="muted">{t("rulesPanel.rosterByPositionNote")}</small>
        <div className="rules-grid">
          {GROUPS.map(({ key }) => (
            <Count
              key={`rmin-${key}`}
              label={t("rulesPanel.groupMin", { label: groupLabel(key) })}
              value={rules.roster.byPosition[key].min}
              placeholder="—"
              onChange={(v) => edit((d) => (d.roster.byPosition[key].min = v))}
            />
          ))}
          {GROUPS.map(({ key }) => (
            <Count
              key={`rmax-${key}`}
              label={t("rulesPanel.groupMax", { label: groupLabel(key) })}
              value={rules.roster.byPosition[key].max}
              placeholder="—"
              onChange={(v) => edit((d) => (d.roster.byPosition[key].max = v))}
            />
          ))}
        </div>
        <small className="muted">
          {t("rulesPanel.franchiseSlotPrefix")}{" "}
          <strong>{rules.roster.franchiseSlot ? t("rulesPanel.yes") : t("rulesPanel.no")}</strong>.{" "}
          {t("rulesPanel.franchiseSlotSuffix")}
        </small>
      </Section>

      <Section title={t("rulesPanel.sectionLineup")} note={t("rulesPanel.lineupNote")}>
        <Gap gap={gapAt("lineup.mode")} />
        <Gap gap={gapAt("lineup.onMissing")} />
        <div className="rules-grid">
          <label>
            {t("rulesPanel.whoScores")}
            <select
              className="field"
              value={rules.lineup.mode}
              onChange={(e) => edit((d) => (d.lineup.mode = e.target.value as RuleSet["lineup"]["mode"]))}
            >
              <option value="activeSelection">{t("rulesPanel.modeActiveSelection")}</option>
              <option value="topN">{t("rulesPanel.modeTopN")}</option>
            </select>
          </label>
          <label>
            {t("rulesPanel.forgottenLineup")}
            <select
              className="field"
              value={rules.lineup.onMissing}
              onChange={(e) =>
                edit((d) => (d.lineup.onMissing = e.target.value as RuleSet["lineup"]["onMissing"]))
              }
            >
              <option value="carryForward">{t("rulesPanel.onMissingCarryForward")}</option>
              <option value="scoreZero">{t("rulesPanel.onMissingScoreZero")}</option>
            </select>
          </label>
        </div>
        <div className="rules-grid">
          {GROUPS.map(({ key }) => (
            <Count
              key={`slot-${key}`}
              label={groupLabel(key)}
              value={rules.lineup.slots[key]}
              placeholder="0"
              onChange={(v) => edit((d) => (d.lineup.slots[key] = v ?? 0))}
            />
          ))}
        </div>
      </Section>

      <Section title={t("rulesPanel.sectionScoring")} note={t("rulesPanel.scoringNote")}>
        <Gap gap={gapAt("scoring.byPosition")} />
        <Gap gap={gapAt("scoring.includePlayoffs")} />
        <div className="rules-grid">
          {SCORED_STATS.filter(
            ({ key }) => !TEAM_STATS.includes(key) || rules.roster.franchiseSlot,
          ).map(({ key }) => (
            <label key={key}>
              {statLabel(key)}
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
          {t("rulesPanel.playoffGamesScore")}
        </label>
      </Section>

      <Section title={t("rulesPanel.sectionTrades")}>
        <Gap gap={gapAt("trades.approval")} />
        <Gap gap={gapAt("trades.pickYearsAhead")} />
        <label className="rules-check">
          <input
            type="checkbox"
            checked={rules.trades.enabled}
            onChange={(e) => edit((d) => (d.trades.enabled = e.target.checked))}
          />
          {t("rulesPanel.tradesAllowed")}
        </label>
        <label className="rules-check">
          <input
            type="checkbox"
            checked={rules.trades.picksTradable}
            onChange={(e) => edit((d) => (d.trades.picksTradable = e.target.checked))}
          />
          {t("rulesPanel.picksTradable")}
        </label>
        <div className="rules-grid">
          <Count
            label={t("rulesPanel.picksYearsAhead")}
            value={rules.trades.pickYearsAhead}
            placeholder="1"
            onChange={(v) => edit((d) => (d.trades.pickYearsAhead = v ?? 1))}
          />
          <label>
            {t("rulesPanel.approval")}
            <select
              className="field"
              value={rules.trades.approval}
              onChange={(e) =>
                edit((d) => (d.trades.approval = e.target.value as RuleSet["trades"]["approval"]))
              }
            >
              <option value="none">{t("rulesPanel.approvalNone")}</option>
              <option value="commissioner">{t("rulesPanel.approvalCommissioner")}</option>
              <option value="leagueVote">{t("rulesPanel.approvalLeagueVote")}</option>
            </select>
          </label>
        </div>
      </Section>

      <Section title={t("rulesPanel.sectionProtections")} note={t("rulesPanel.protectionsNote")}>
        <Gap gap={gapAt("protection.slotsByPosition")} />
        <Gap gap={gapAt("protection.afterDraft")} />
        <div className="rules-grid">
          <Count
            label={t("rulesPanel.protectionSlots")}
            value={rules.protection.slots}
            placeholder={t("rulesPanel.notConfigured")}
            onChange={(v) => edit((d) => (d.protection.slots = v))}
          />
          <label>
            {t("rulesPanel.unclaimedExposed")}
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
              <option value="stayWithTeam">{t("rulesPanel.afterDraftStay")}</option>
              <option value="releasedToFreeAgents">{t("rulesPanel.afterDraftReleased")}</option>
            </select>
          </label>
        </div>
        <label className="rules-check">
          <input
            type="checkbox"
            checked={rules.protection.auto.enabled}
            onChange={(e) => edit((d) => (d.protection.auto.enabled = e.target.checked))}
          />
          {t("rulesPanel.protectFree")}
        </label>
        <small className="muted">{t("rulesPanel.goalieNote")}</small>
        <div className="rules-grid">
          <Count
            label={t("rulesPanel.skaterCareerGames")}
            value={rules.protection.auto.skaterMaxCareerGames}
            placeholder="100"
            onChange={(v) => edit((d) => (d.protection.auto.skaterMaxCareerGames = v ?? 0))}
          />
          <Count
            label={t("rulesPanel.goalieCareerGames")}
            value={rules.protection.auto.goalieMaxCareerGames}
            placeholder="50"
            onChange={(v) => edit((d) => (d.protection.auto.goalieMaxCareerGames = v ?? 0))}
          />
        </div>
      </Section>

      <Section title={t("rulesPanel.sectionDraft")}>
        <Gap gap={gapAt("draft.unprotectedDisposition")} />
        <Gap gap={gapAt("draft.steal.turnsTradable")} />
        <Gap gap={gapAt("draft.snake")} />
        <div className="rules-grid">
          <label>
            {t("rulesPanel.unprotectedPlayers")}
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
              <option value="stealRounds">{t("rulesPanel.unprotectedStealRounds")}</option>
              <option value="openPool">{t("rulesPanel.unprotectedOpenPool")}</option>
            </select>
          </label>
          <Count
            label={t("rulesPanel.stealRounds")}
            value={rules.draft.steal.rounds}
            placeholder="0"
            onChange={(v) => edit((d) => (d.draft.steal.rounds = v ?? 0))}
          />
          <Count
            label={t("rulesPanel.mostTeamMayLose")}
            value={rules.draft.steal.maxLossesPerTeam}
            placeholder={t("rulesPanel.uncapped")}
            onChange={(v) => edit((d) => (d.draft.steal.maxLossesPerTeam = v))}
          />
          <Count
            label={t("rulesPanel.rookieRounds")}
            value={rules.draft.rookieRounds}
            placeholder={t("rulesPanel.noDraft")}
            onChange={(v) => edit((d) => (d.draft.rookieRounds = v))}
          />
        </div>
        <label className="rules-check">
          <input
            type="checkbox"
            checked={rules.draft.steal.turnsTradable}
            onChange={(e) => edit((d) => (d.draft.steal.turnsTradable = e.target.checked))}
          />
          {t("rulesPanel.stealTurnsTradable")}
        </label>
        <label className="rules-check">
          <input
            type="checkbox"
            checked={rules.draft.snake}
            onChange={(e) => edit((d) => (d.draft.snake = e.target.checked))}
          />
          {t("rulesPanel.snakeOrder")}
        </label>
      </Section>

      <Section title={t("rulesPanel.sectionFreeAgency")}>
        <Gap gap={gapAt("freeAgency.mode")} />
        <div className="rules-grid">
          <label>
            {t("rulesPanel.when")}
            <select
              className="field"
              value={rules.freeAgency.mode}
              onChange={(e) =>
                edit((d) => (d.freeAgency.mode = e.target.value as RuleSet["freeAgency"]["mode"]))
              }
            >
              <option value="none">{t("rulesPanel.freeAgencyNever")}</option>
              <option value="anytime">{t("rulesPanel.freeAgencyAnytime")}</option>
              <option value="windows">{t("rulesPanel.freeAgencyWindows")}</option>
            </select>
          </label>
          <label>
            {t("rulesPanel.what")}
            <select
              className="field"
              value={rules.freeAgency.allow}
              onChange={(e) =>
                edit((d) => (d.freeAgency.allow = e.target.value as RuleSet["freeAgency"]["allow"]))
              }
            >
              <option value="both">{t("rulesPanel.allowBoth")}</option>
              <option value="add">{t("rulesPanel.allowAdd")}</option>
              <option value="drop">{t("rulesPanel.allowDrop")}</option>
            </select>
          </label>
          <Count
            label={t("rulesPanel.movesPerWeek")}
            value={rules.freeAgency.movesPerPeriod}
            placeholder={t("rulesPanel.unlimited")}
            onChange={(v) => edit((d) => (d.freeAgency.movesPerPeriod = v))}
          />
        </div>
      </Section>

      <p className="muted" style={{ margin: 0, fontSize: "0.8rem" }}>
        {t("rulesPanel.footerNote")}
      </p>
      <div style={{ display: "flex", gap: "0.6rem" }}>
        <button type="submit" className="btn" disabled={busy} style={{ flex: 1 }}>
          {busy ? t("rulesPanel.saving") : t("rulesPanel.saveRules")}
        </button>
        <button type="button" className="btn-outline" onClick={onClose}>
          {t("common.cancel")}
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
  const { t } = useLanguage();
  if (!gap) return null;
  return (
    <p className="rules-gap">
      <InfoIcon size={14} />
      <span>
        <strong>{t("rulesPanel.notEnforcedYet")}</strong> {gap.message}
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

/** Dictionary leaf name for each group's translated label — see `groupLabel` above. */
const GROUP_LABEL_KEY: Record<(typeof GROUPS)[number]["key"], string> = {
  forwards: "groupForwards",
  defense: "groupDefense",
  goalies: "groupGoalies",
};

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

/** Dictionary leaf name for each stat's translated label — see `statLabel` above. */
const STAT_LABEL_KEY: Record<string, string> = {
  goals: "statGoal",
  assists: "statAssist",
  wins: "statGoalieWin",
  otLosses: "statGoalieOtLoss",
  shutouts: "statShutout",
  plusMinus: "statPlusMinus",
  pim: "statPim",
  shots: "statShot",
  hits: "statHit",
  blockedShots: "statBlockedShot",
  teamWins: "statFranchiseWin",
  teamOtLosses: "statFranchiseOtLoss",
  teamLosses: "statFranchiseLoss",
};
