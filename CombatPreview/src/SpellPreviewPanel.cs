using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using UnityEngine;

namespace CombatPreview
{
    partial class HoverChance
    {
        AbilityData panelSpellAbility;
        string spellPanelWaitingReason;
        long spellPanelRevision;
        UnitEntityData spellLastHover;
        string spellWorkContext;
        readonly PreviewWorkQueue<UnitEntityData> spellWork = new PreviewWorkQueue<UnitEntityData>();
        readonly Dictionary<UnitEntityData, SpellPanelEntry> spellEntries = new Dictionary<UnitEntityData, SpellPanelEntry>();
        void ClearSpellWork() { spellWork.Clear(); spellEntries.Clear(); spellWorkContext = null; }
        readonly Dictionary<UnitEntityData, PreviewReadiness> spellReadiness = new Dictionary<UnitEntityData, PreviewReadiness>();
        void ShowSpellWaiting(AbilityData ability, string reason)
        {
            SpellPreviewRuntime.PanelState(ability, null, reason);
            ClearSpellWork();
            if (panelSpellAbility == ability && spellPanelWaitingReason == reason) return;
            panelSpellAbility = ability; spellPanelWaitingReason = reason;
            m_Target = null; panelSnapshot = null; panelAttacks.Clear(); spellRows.Clear();
            spellRows.Add(new SpellPanelEntry { Name = "주문 미리보기", Summary = new List<string> { reason }, Details = new List<string> { reason } });
        }
        sealed class SpellPanelEntry
        {
            internal string Name;
            internal List<string> Summary, Details;
            internal long CurrentHp;
            internal int MaximumHp;
            internal double? ExpectedHpLoss, ExpectedTemporaryHpLoss;
            internal int TemporaryHp;
            internal string HealthCaption;
            internal SpellOutcomeTableView Table;
            internal bool ShieldApproximate;
            internal double? ApproximateDamage;
        }
        readonly List<SpellPanelEntry> spellRows = new List<SpellPanelEntry>();
        bool spellArea;
        void UpdateSpellPanel(AbilityData ability, UnitEntityData selected)
        {
            var caster = SpellPreviewRuntime.Caster(ability);
            if (caster == null) { ShowSpellWaiting(ability, "시전자 정보 확인 중"); return; }
            if (caster != selected && caster != BetaReader.Mount(selected) && caster != BetaReader.Rider(selected))
            { ShowSpellWaiting(ability, "현재 턴 시전자와 선택된 시전자가 다름"); return; }
            string context = BetaIdentity.Id(ability) + ":" + BetaIdentity.Id(caster) + ":" + BetaIdentity.Id(Game.Instance?.TurnBasedCombatController?.CurrentTurn);
            bool changed = panelSpellAbility != ability || spellWorkContext != context;
            if (changed) { ClearSpellWork(); spellWorkContext = context; nextRefresh = 0; panelScroll = Vector2.zero; spellReadiness.Clear(); }
            if (!changed && OverPanel() && spellRows.Count > 0) { panelHeld = true; return; }
            if (panelHeld) nextRefresh = 0;
            panelHeld = false;
            var hoverNow = HoveredTarget();
            bool hoverChanged = !ReferenceEquals(hoverNow, spellLastHover);
            long revision = SpellPreviewRuntime.Revision(ability);
            bool parametersChanged = spellPanelRevision != revision;
            if (!changed && spellWork.PendingCount == 0 && Time.unscaledTime < nextRefresh && spellPanelRevision == revision && ReferenceEquals(hoverNow, spellLastHover)) return;
            spellLastHover = hoverNow;
            var cursorTarget = ability.MagicHackData != null ? HoveredTarget() : null;
            var targetFilter = new SpellTargetFilter(ability, caster);
            var candidates = SpellPreviewRuntime.Targets(ability);
            var observedTargets = candidates.Where(targetFilter.Allows).ToArray();
            if (cursorTarget != null && !targetFilter.Allows(cursorTarget)) cursorTarget = null;
            var targets = observedTargets;
            spellArea = observedTargets.Length > 0;
            if (cursorTarget != null && cursorTarget.IsVisibleForPlayer && !cursorTarget.Descriptor.State.IsDead
                && !targets.Take(32).Any(target => ReferenceEquals(target, cursorTarget)))
                targets = targets.Take(31).Concat(new[] { cursorTarget }).ToArray();
            if (targets.Length == 0)
            {
                var hover = HoveredTarget();
                if (hover != null && hover.IsVisibleForPlayer && !hover.Descriptor.State.IsDead && targetFilter.Allows(hover)) targets = new[] { hover };
            }
            if (targets.Length == 0) { ShowSpellWaiting(ability, "대상을 가리키세요 · 현재 대상 판독 대기"); return; }
            targets = targets.Distinct().OrderBy(target => ReferenceEquals(target, hoverNow) ? 0 : 1).Take(32).ToArray();
            foreach (var removed in spellEntries.Keys.Where(target => !targets.Contains(target)).ToArray()) spellEntries.Remove(removed);
            if (spellWork.PendingCount == 0) spellWork.Begin(targets, hoverNow);
            else spellWork.Reconcile(targets, hoverNow);
            if (hoverChanged || parametersChanged) spellWork.RefreshPriority(hoverNow);
            foreach (var queued in targets)
            {
                if (!spellEntries.ContainsKey(queued)) spellEntries[queued] = new SpellPanelEntry {
                    Name = queued.CharacterName, Summary = new List<string> { "계산 대기 중" }, Details = new List<string> { "광역 대상 순차 계산 대기" }
                };
                if (!spellReadiness.ContainsKey(queued))
                { if (spellReadiness.Count >= 64) spellReadiness.Clear(); spellReadiness[queued] = new PreviewReadiness(PreviewPerformance.NowSeconds, Time.frameCount); }
            }
            spellPanelWaitingReason = null;
            SpellPreviewRuntime.PanelState(ability, targets[0], "Shown");
            bool firstTarget = changed || !ReferenceEquals(m_Target, targets[0]);
            BetaRuntime.InvalidateDisplayed();
            panelSpellAbility = ability; panelSnapshot = null; panelReason = null; panelAttacks.Clear();
            m_Target = targets[0]; attacker = caster; panelHeld = false; retention.Clear();
            int processed = 0; double batchStart = PreviewPerformance.NowSeconds;
            using (ScopedReadMemo<UnitEntityData, string>.Begin())
            using (ScopedReadMemo<AbilityData, string>.Begin())
            using (ScopedReadMemo<UnitEntityData, ActiveD20Reader.PartyRollSources>.Begin())
            using (ScopedReadMemo<UnitEntityData, SpellParameterReader.SchoolInputs>.Begin())
            using (ScopedReadMemo<(AbilityData, UnitEntityData, Metamagic), SpellParameterReader.ReadResult>.Begin())
            using (ScopedReadMemo<(AbilityData, UnitEntityData, Metamagic), AutoMetamagicReader.Result>.Begin())
            using (ScopedReadMemo<(UnitEntityData, UnitEntityData, string, Kingmaker.EntitySystem.Stats.SavingThrowType, AbilityData), D20RollModel>.Begin())
            using (PreviewPerformance.Measure("spell-preview"))
            using (PreviewPerformance.Measure("spell-batch"))
            using (PreviewPerformance.Measure(firstTarget ? "spell-first-target" : "spell-refresh"))
            while (PreviewWorkQueue<UnitEntityData>.CanContinue(processed, PreviewPerformance.NowSeconds - batchStart) && spellWork.Take(out var target))
            {
                using (PreviewPerformance.Measure("spell-target"))
                {
                    double targetStart = PreviewPerformance.NowSeconds;
                    if (!spellReadiness.TryGetValue(target, out var readiness))
                    { if (spellReadiness.Count >= 64) spellReadiness.Clear(); spellReadiness[target] = readiness = new PreviewReadiness(PreviewPerformance.NowSeconds, Time.frameCount); }
                    List<string> values;
                    List<SpellCheckRecord> checkDetails = null; Dictionary<string, object> metadata = null;
                    try { values = SpellPreviewRuntime.Describe(ability, target, out checkDetails, out metadata, ReferenceEquals(target, cursorTarget)); }
                    catch (NotSupportedException e) { values = new List<string> { e.Message }; }
                    catch (Exception e) { Main.Error("Spell preview unavailable", e); values = new List<string> { "주문 정보 판독 불가 · " + e.GetType().Name }; }
                    if (metadata == null) metadata = new Dictionary<string, object>();
                    metadata["areaTargetFilter"] = new Dictionary<string, object> {
                        { "observedCandidates", candidates.Length }, { "eligibleObserved", observedTargets.Length },
                        { "excludedObserved", candidates.Length - observedTargets.Length },
                        { "excludedObservationSamples", SpellPreviewRuntime.TargetExclusions(ability) },
                        { "scope", "audited delivery relationship/known condition exclusions before queue and 32-target cap; unknown filters retained" }
                    };
                    if (checkDetails == null) checkDetails = new List<SpellCheckRecord>();
                    if (!metadata.ContainsKey("hcoReference") && Main.Settings.CompareBeta)
                        metadata["hcoReference"] = OverlayReference.Spell(ability, caster, target, checkDetails);
                    var staged = SpellLethalPreview.Describe(ability, target, checkDetails, metadata, values)
                        ?? SpellOutcome.DescribeSingleSave(checkDetails, metadata);
                    if (staged == null && metadata.TryGetValue("branchPreview", out var branch) && branch is Dictionary<string, object> branchData)
                        staged = ((string[])branchData["summary"]).ToList();
                    if (metadata.TryGetValue("pitPreview", out var pit) && pit is Dictionary<string, object> pitData)
                        staged = ((string[])pitData["summary"]).ToList();
                    var summary = staged ?? SummarizeSpell(checkDetails, metadata);
                    var entry = new SpellPanelEntry {
                        Name = target.CharacterName + (target.IsPlayersEnemy ? "" : " · 아군/중립"),
                        Summary = summary, Details = values, ShieldApproximate = UsesShieldApproximation(metadata)
                    };
                    if (entry.ShieldApproximate && metadata.TryGetValue("spellDamage", out var approximateDamage)
                        && approximateDamage is Dictionary<string, object> damageModel
                        && damageModel.TryGetValue("expectedApproximateTotal", out var approximateMean)) entry.ApproximateDamage = approximateMean as double?;
                    if (BetaReader.ReadHealth(target, out entry.CurrentHp, out entry.MaximumHp, out int temporaryHp))
                        DescribeSpellHealth(entry, Math.Max(0, temporaryHp), metadata);
                    var issue = PreviewIssuePresentation.Build(metadata, checkDetails);
                    if (issue != null)
                    {
                        string sourceType = issue["sourceType"] as string;
                        var sources = sourceType == null ? new string[0] : caster.Facts.List.Where(fact => fact.Active
                            && fact.Components.Any(runtime => CombatFields.Component(runtime)?.GetType().Name == sourceType))
                            .Select(fact => CombatFields.Label(fact)).Distinct().Take(4).ToArray();
                        issue["sourceLabels"] = sources;
                        metadata["unavailableDiagnostics"] = issue;
                        string reason = (string)issue["summary"] + (sources.Length > 0 ? " · " + sources[0] : "");
                        if (!entry.ExpectedHpLoss.HasValue) entry.HealthCaption = "미확인: " + reason;
                        else entry.Details.Add("미확인 판정: " + reason);
                    }
                    metadata["hoverReadiness"] = readiness.Observe(PreviewPerformance.NowSeconds,
                        metadata.TryGetValue("dc", out var knownDc) && knownDc is int, entry.ExpectedHpLoss.HasValue);
                    metadata["areaScheduling"] = new Dictionary<string, object> {
                        { "targetCount", targets.Length }, { "frame", Time.frameCount }, { "processedThisFrame", processed + 1 },
                        { "hoverChanged", hoverChanged }, { "parameterRevisionChanged", parametersChanged },
                        { "pendingAfterTarget", spellWork.PendingCount }, { "maximumPerFrame", PreviewWorkQueue<UnitEntityData>.MaximumPerFrame },
                        { "softBudgetMs", PreviewWorkQueue<UnitEntityData>.SoftBudgetSeconds * 1000 }
                    };
                    var outcomeTable = SpellOutcomePresentation.Build(metadata, checkDetails);
                    if (outcomeTable.Rows.Count > 0) entry.Table = PresentSpellTable(outcomeTable);
                    metadata["outcomeTable"] = new Dictionary<string, object> {
                        { "probabilityHeading", outcomeTable.ProbabilityHeading }, { "checksCaption", outcomeTable.ChecksCaption },
                        { "note", outcomeTable.Note }, { "rows", outcomeTable.Rows.Select(row => new Dictionary<string, object> {
                            { "path", row.Path }, { "chance", row.Chance }, { "displayedChance", row.ChanceText },
                            { "effect", row.Effect }, { "expectedHpLossContribution", row.ExpectedHpLoss }, { "lethal", row.Lethal }
                        }).ToArray() }
                    };
                    var loggedLines = staged == null ? values : staged.Concat(values).ToList();
                    ((Dictionary<string, object>)metadata["areaScheduling"])["computeMsBeforeLogging"] = (PreviewPerformance.NowSeconds - targetStart) * 1000;
                    Main.Diagnostics?.SpellShown(new DiagnosticContext(BetaIdentity.Id(caster), BetaIdentity.Id(target), BetaIdentity.Id(ability),
                        BetaIdentity.Id(Game.Instance.TurnBasedCombatController.CurrentTurn), "unknown", 0, "", false), ability.Blueprint.Name, loggedLines, observedTargets.Contains(target), checkDetails, metadata);
                    spellEntries[target] = entry;
                    processed++;
                }
            }
            spellRows.Clear();
            foreach (var target in targets)
                spellRows.Add(spellEntries[target]);
            if (spellWork.PendingCount == 0) nextRefresh = Time.unscaledTime + .15f;
            spellPanelRevision = SpellPreviewRuntime.Revision(ability);
        }
        void DrawSpellPanel()
        {
            var settings = Main.Settings;
            bool details = settings.PanelDetails;
            GUILayout.Label(panelSpellAbility.Blueprint.Name, titleStyle);
            if (spellWork.PendingCount > 0) GUILayout.Label("광역 계산 중 · 남은 대상 " + spellWork.PendingCount, mutedStyle);
            if (details) GUILayout.Label(spellArea ? "범위 판정 대상 및 커서 후보" : "커서 대상 · 사거리 미확인", mutedStyle);
            panelScroll = GUILayout.BeginScrollView(panelScroll);
            foreach (var entry in spellRows)
            {
                GUILayout.BeginVertical(cardStyle);
                GUILayout.Label(entry.Name, numberStyle);
                if (entry.ShieldApproximate) GUILayout.Label("≈ 보호막 근사" + (entry.ApproximateDamage.HasValue
                    ? " · 기대 피해 " + entry.ApproximateDamage.Value.ToString("0.##") : " · 평균 피해/잔량 기준"), mutedStyle);
                if (entry.MaximumHp > 0)
                    DrawHealthBar(entry.CurrentHp, entry.MaximumHp,
                        PanelPresentation.HealthLoss(entry.CurrentHp, entry.MaximumHp, 0, entry.ExpectedHpLoss), 22, entry.TemporaryHp, entry.ExpectedTemporaryHpLoss);
                if (entry.HealthCaption != null) GUILayout.Label(entry.HealthCaption, mutedStyle);
                if (entry.Table != null) DrawSpellTable(entry.Table);
                else foreach (var value in entry.Summary) GUILayout.Label(value, textStyle);
                if (details)
                    foreach (var value in entry.Details) GUILayout.Label(value, textStyle);
                GUILayout.EndVertical();
            }
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            GUILayout.Label(panelHeld ? "보관 중  ·  ~ 추정" : "~ 추정 · 면역 별도", mutedStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(settings.DetailsKey + (details ? " 접기" : " 상세"), GUILayout.Width(82)))
            {
                settings.PanelDetails = !details;
                panelScroll = Vector2.zero;
            }
            GUILayout.EndHorizontal();
            GUI.DragWindow(new Rect(0, 0, 10000, 28));
        }

        float SpellPanelHeight(bool details)
        {
            if (details) return 420;
            float width = Mathf.Max(80, panelWidth - 55);
            float height = 64;
            foreach (var entry in spellRows)
            {
                height += EntryHeight(entry.Name, numberStyle);
                if (entry.MaximumHp > 0) height += 22;
                if (entry.HealthCaption != null) height += EntryHeight(entry.HealthCaption, mutedStyle);
                height += (entry.Table != null ? SpellTableHeight(entry.Table, Mathf.Max(160, panelWidth - 56))
                    : entry.Summary.Sum(line => EntryHeight(line, textStyle))) + 12;
                if (height >= 360) return 360;
            }
            return height;

            float EntryHeight(string line, GUIStyle style) => style.CalcHeight(new GUIContent(line), width);
        }

        static void DescribeSpellHealth(SpellPanelEntry entry, int temporaryHp, Dictionary<string, object> metadata)
        {
            entry.ExpectedHpLoss = HealthProjectionSelection.Select(metadata, entry.CurrentHp, entry.MaximumHp, temporaryHp,
                out bool authoritative, out bool approximateHealth, out string basis);
            entry.TemporaryHp = temporaryHp;
            entry.ExpectedTemporaryHpLoss = HealthProjectionSelection.TemporaryLoss(metadata, temporaryHp, basis);
            bool damageSpell = authoritative;
            if (!authoritative && metadata.TryGetValue("spellDamage", out var damage) && damage is Dictionary<string, object> model
                && model["terms"] is List<Dictionary<string, object>> terms)
            {
                damageSpell = terms.Any(term => Equals(term["hpDamage"], true));
                var hpTerms = terms.Where(term => Equals(term["hpDamage"], true)).Take(2).ToArray();
                if (model.TryGetValue("sequenceProjection", out var sequence) && sequence is Dictionary<string, object> projection
                    && projection["expectedHpLoss"] is double sequenceLoss)
                {
                    entry.ExpectedHpLoss = sequenceLoss;
                    entry.ExpectedTemporaryHpLoss = projection.TryGetValue("expectedTemporaryHpLoss", out var tempLoss) ? tempLoss as double? : null;
                    approximateHealth = Equals(projection.TryGetValue("approximate", out var approximateSequence) ? approximateSequence : null, true);
                    basis = Equals(projection.TryGetValue("approximate", out var approximate) ? approximate : null, true)
                        ? "approximate-shield-mean-state" : "joint-damage-roll-distribution";
                }
                else if (hpTerms.Length == 1 && hpTerms[0].TryGetValue("expectedSupportedModel", out var expected) && expected is double mean)
                {
                    var term = hpTerms[0];
                    bool single = term.TryGetValue("singleDamageApplication", out var application) && Equals(application, true);
                    if (mean == 0) entry.ExpectedHpLoss = 0;
                    else if (single || term.ContainsKey("independentDamageApplications"))
                        entry.ExpectedHpLoss = SpellHpLoss(term, entry.CurrentHp, temporaryHp);
                    if (entry.ExpectedHpLoss.HasValue)
                    {
                        basis = "damage-roll-distribution";
                        var totalLoss = SpellHpLoss(term, Math.Min(int.MaxValue, entry.CurrentHp + (long)temporaryHp), 0);
                        entry.ExpectedTemporaryHpLoss = mean == 0 ? 0 : totalLoss - entry.ExpectedHpLoss;
                    }
                    else
                    {
                        entry.ExpectedHpLoss = HealthProjectionSelection.MeanLoss(entry.CurrentHp, entry.MaximumHp, temporaryHp, mean);
                        entry.ExpectedTemporaryHpLoss = HealthProjectionSelection.MeanTemporaryLoss(temporaryHp, mean);
                        approximateHealth = true;
                        basis = "mean-state-hp-display";
                    }
                }
                else if (model.TryGetValue("expectedSupportedTotal", out var total) && total is double totalMean)
                {
                    entry.ExpectedHpLoss = HealthProjectionSelection.MeanLoss(entry.CurrentHp, entry.MaximumHp, temporaryHp, totalMean);
                    entry.ExpectedTemporaryHpLoss = HealthProjectionSelection.MeanTemporaryLoss(temporaryHp, totalMean);
                    approximateHealth = true;
                    basis = "mean-state-hp-display";
                }
            }

            approximateHealth |= UsesShieldApproximation(metadata);
            string temp = temporaryHp > 0 ? "파랑: 임시 " + temporaryHp + " · 기대 소모 "
                + (entry.ExpectedTemporaryHpLoss.HasValue ? "~" + entry.ExpectedTemporaryHpLoss.Value.ToString("0.##") : "—") : null;
            if (entry.ExpectedHpLoss is double projected)
                entry.HealthCaption = (approximateHealth ? "≈ 평균 피해 기준 HP 감소" : basis == "sequence-mean-capped-for-display" ? "기대 피해 기준"
                    : basis == "initial-pit-fall" ? "최초 낙하 기대 HP 감소" : "기대 HP 감소")
                    + " ~" + projected.ToString("0.##") + " · 잔여 ~" + Math.Max(0, entry.CurrentHp - projected).ToString("0.##");
            else if (damageSpell) entry.HealthCaption = "기대 HP 감소 —";
            if (temp != null) entry.HealthCaption = entry.HealthCaption == null ? temp : entry.HealthCaption + " · " + temp;
            metadata["healthProjection"] = new Dictionary<string, object> {
                { "currentHp", entry.CurrentHp }, { "maximumHp", entry.MaximumHp }, { "temporaryHp", temporaryHp },
                { "expectedHpLoss", entry.ExpectedHpLoss }, { "expectedTemporaryHpLoss", entry.ExpectedTemporaryHpLoss },
                { "absorptionOrder", "target-defenses-then-temporary-hp-then-main-hp" }, { "basis", basis }, { "hasDamage", damageSpell },
                { "approximate", approximateHealth || UsesShieldApproximation(metadata) }
            };
        }

        static double? SpellHpLoss(Dictionary<string, object> term, long hp, int temporaryHp)
        {
            if (Equals(term["registeredEnergyImmunity"], true)) return 0;
            if (!(term["probabilityInputs"] is Dictionary<string, object> inputs)
                || !(term["dice"] is int dice) || !(term["sides"] is int sides) || !(term["bonus"] is int bonus)
                || !(term["registeredResistance"] is int reduction)) return null;
            double decline = (double)term["damageDeclineFactor"];
            double vulnerability = SpellTargetScaling.Factor(term);
            if (term.TryGetValue("independentDamageApplications", out var applications) && applications is int count)
                return SpellDamageMath.MeanIndependentHitsHpLoss(dice, Math.Max(1, sides), bonus,
                    Equals(term["maximize"], true), Equals(term["empower"], true), Equals(term["effectiveHalf"], true),
                    reduction, decline, count, (double)inputs["hitAfterVisibility"], hp, temporaryHp, vulnerability, SpellTargetScaling.Percent(term));
            double? Roll(double factor) => SpellDamageMath.MeanCappedHpLoss(dice, Math.Max(1, sides), bonus,
                Equals(term["maximize"], true), Equals(term["empower"], true), Equals(term["effectiveHalf"], true),
                hp, temporaryHp, reduction, factor * decline, vulnerability, SpellTargetScaling.Percent(term));
            bool hasSave = Equals(term["halfIfSaved"], true);
            double savedFactor = (double)inputs["savedMultiplier"];
            var failed = Roll(hasSave && Equals(inputs["improvedEvasion"], true) ? savedFactor : 1);
            double? passed = !hasSave || Equals(inputs["evasion"], true) ? 0 : Roll(savedFactor);
            return failed.HasValue && passed.HasValue
                ? SpellDamageMath.Expected(1, (double)inputs["hitAfterVisibility"], (double)inputs["srPenetration"],
                    (double)inputs["targetFailsSave"], failed.Value, passed.Value) : (double?)null;
        }
        static bool UsesShieldApproximation(Dictionary<string, object> metadata)
        {
            foreach (string key in new[] { "stagedOutcome", "branchPreview" })
                if (metadata.TryGetValue(key, out var value) && value is Dictionary<string, object> model
                    && model.TryGetValue("approximate", out var flag) && Equals(flag, true)) return true;
            return metadata.TryGetValue("spellDamage", out var damage) && damage is Dictionary<string, object> data
                && data.TryGetValue("sequenceProjection", out var raw) && raw is Dictionary<string, object> projection
                && projection.TryGetValue("approximate", out var approximate) && Equals(approximate, true);
        }

        static List<string> SummarizeSpell(List<SpellCheckRecord> checks, Dictionary<string, object> metadata)
        {
            var summary = new List<string>();
            if (metadata.TryGetValue("directDamageNoAttackRoll", out var noRoll) && Equals(noRoll, true))
                summary.Add("피해 전달 · 명중 굴림 없음");
            var labels = checks.Where(check => check.Status != "NotIdentified" && check.Status != "NotRequired"
                && check.Status != "NoRegisteredSR").Select(check =>
                    (check.Kind == "attack" ? (check.AutomaticHit ? "자동 명중 " : "명중 ") : check.Kind == "sr" ? "SR 관통 " : SpellLethalPreview.SaveLabel(check.Type) + " 실패 ")
                    + SpellLethalPreview.Percent(check.Probability)
                    + (check.Kind == "attack" && check.VisibilityMiss > 0 ? " (빗나감 " + check.VisibilityMiss.Value.ToString("P0") + ")" : "")
                    + (check.Kind == "attack" && check.MirrorImages > 0 ? " · 거울상 " + check.MirrorImages + "개 · 첫 타격" : "")).ToArray();
            if (labels.Length > 0) summary.Add(string.Join("  ·  ", labels));
            if (metadata.TryGetValue("spellDamage", out var damage) && damage is Dictionary<string, object> model
                && model["terms"] is List<Dictionary<string, object>> terms && terms.Count > 0)
            {
                bool conditional = terms.Any(term => Equals(term["branchCandidate"], true));
                var formulas = string.Join(conditional ? " / " : " + ", terms.Take(3).Select(term => term["formula"]
                    + (Equals(term["effectiveHalf"], true) ? "÷2" : "")
                    + (terms.Count > 1 ? " " + term["damageType"] : "")));
                if (terms.Count > 3) formulas += " 외 " + (terms.Count - 3) + "개";
                string line = (conditional ? "피해 분기 " : "피해 ") + formulas;
                if (model["deliveryCount"] is int count && count > 1) line += " ×" + count;
                if (terms.Count == 1 && terms[0]["meanRepeats"] is double repeats && repeats > 1)
                    line += " · 반복 " + repeats.ToString("0.##") + "회";
                if (terms.Any(term => Equals(term["branchCandidate"], true))) line += " · 조건부";
                if (terms.Any(term => term.TryGetValue("targetVulnerability", out var factor) && factor is double scale && scale > 1))
                    line += " · 군체 범위 취약 반영";
                if (terms.Any(term => term.TryGetValue("targetDurability", out var durability) && durability is double scale && scale < 1))
                    line += " · 범위 피해 반감 반영";
                if (terms.Any(term => SpellTargetScaling.Percent(term) != 0 && !double.IsNaN(SpellTargetScaling.Factor(term)))) line += " · 에너지 피해 보정 반영";
                if (terms.Any(term => term.TryGetValue("energyVulnerability", out var energy) && energy is double scale && scale > 1
                    && !double.IsNaN(SpellTargetScaling.Factor(term))))
                    line += " · 에너지 취약 반영";
                if (terms.Any(term => term.TryGetValue("targetScalingUnavailableReason", out var reason) && reason != null))
                    line += " · 배율 미확인";
                summary.Add(line);
            }
            if (summary.Count == 0) summary.Add("표시할 판정 없음");
            return summary;
        }
    }
}
