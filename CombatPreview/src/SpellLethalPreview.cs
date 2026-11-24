using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Mechanics.Actions;

namespace CombatPreview
{
    internal static class SpellLethalPreview
    {
        const string PhantasmalKiller = "6717dbaef00c0eb4897a1c908a75dfe5";
        const string SecondSavePath = "cast/ContextActionConditionalSaved.Failed/ContextActionSavingThrow.Actions/";

        internal static List<string> Describe(AbilityData ability, UnitEntityData target, List<SpellCheckRecord> checks,
            Dictionary<string, object> metadata, List<string> details)
        {
            if (!metadata.TryGetValue("spellDamage", out var damage) || !(damage is Dictionary<string, object> model)
                || !Equals(model["deliveryCount"], 1) || !Equals(model["scanTruncated"], false)
                || !Equals(model["unloadedLinks"], 0)
                || !(model["terms"] is List<Dictionary<string, object>> terms)
                || checks.Any(check => check.Kind == "attack" && check.Status != "NotIdentified"))
                return null;

            // The vanilla blueprint can include extra visual actions alongside its rolls.
            bool knownSpell = string.Equals(ability.Blueprint.AssetGuid.ToString(), PhantasmalKiller, StringComparison.OrdinalIgnoreCase);
            var runs = ability.Blueprint.ComponentsArray.OfType<AbilityEffectRunAction>().Take(2).ToArray();
            if (runs.Length != 1 || runs[0].SavingThrowType == SavingThrowType.Unknown) return null;
            var first = FindAction<ContextActionConditionalSaved>(runs[0].Actions, knownSpell);
            var second = FindAction<ContextActionSavingThrow>(first?.Failed, knownSpell);
            if (second == null || second.Type == SavingThrowType.Unknown || second.Type == runs[0].SavingThrowType)
                return null;
            knownSpell &= runs[0].SavingThrowType == SavingThrowType.Will && second.Type == SavingThrowType.Fortitude;

            var branch = FindAction<ContextActionConditionalSaved>(second.Actions, knownSpell);
            bool confirmedTree = Empty(first.Succeed) && branch != null
                && OnlyAction(branch.Failed) is ContextActionKill
                && (Empty(branch.Succeed) || OnlyAction(branch.Succeed) is ContextActionDealDamage);
            if (!confirmedTree && !knownSpell) return null;
            if (terms.Count > 1 || terms.Any(term => !Equals(term["path"], SecondSavePath + "ContextActionConditionalSaved.Succeed")
                || !Equals(term["hpDamage"], true) || !Equals(term["meanRepeats"], 1.0))) return null;
            if (!confirmedTree && terms.Count != 1) return null;

            var sr = checks.FirstOrDefault(check => check.Kind == "sr");
            var firstSave = checks.FirstOrDefault(check => check.Kind == "save" && check.Type == runs[0].SavingThrowType.ToString());
            var secondSave = checks.FirstOrDefault(check => check.Kind == "save" && check.Type == second.Type.ToString());
            double? penetration = sr?.Status == "NotRequired" || sr?.Status == "NoRegisteredSR" ? 1 : sr?.Probability;
            double? firstFailure = firstSave?.Probability, secondFailure = secondSave?.Probability;
            string secondSource = secondSave?.Source;
            if (metadata.TryGetValue("nestedSaves", out var nested) && nested is List<Dictionary<string, object>> nestedSaves)
            {
                var matching = nestedSaves.Where(entry => Equals(entry["actionId"], BetaIdentity.Id(second))).Take(2).ToArray();
                if (matching.Length == 1)
                {
                    // A consumed reroll or action-specific DC can make this
                    // stage unknown even when a generic save-type estimate exists.
                    secondFailure = matching[0].TryGetValue("failureChance", out var value) ? value as double? : null;
                    secondSource = "readonly-nested-save-action";
                }
            }
            if (metadata.TryGetValue("projectedSaveStages", out var stages) && stages is List<Dictionary<string, object>> projected)
            {
                var matching = projected.Where(entry => Equals(entry["actionId"], BetaIdentity.Id(second))).Take(2).ToArray();
                if (matching.Length == 1)
                { secondFailure = matching[0]["conditionalFailureChance"] as double?; secondSource = "readonly-projected-save-stage"; }
            }

            bool lifecycleSequence = false;
            if (confirmedTree && !second.HasCustomDC && !second.FromBuff && !second.UseDCFromContextSavingThrow
                && !((CombatFields.Read(second, "m_ConditionalDCIncrease") as Array)?.Length > 0)
                && firstSave?.Bonus is int firstBonus && firstSave.Threshold is int firstDc
                && secondSave?.Bonus is int secondBonus && secondSave.Threshold is int secondDc)
            {
                var firstRolls = ActiveD20Reader.Read(target, ability.Caster, "save", runs[0].SavingThrowType, ability);
                var secondRolls = ActiveD20Reader.Read(target, ability.Caster, "save", second.Type, ability);
                if (firstRolls.ConsumesEffect || secondRolls.ConsumesEffect)
                {
                    var sequence = D20SaveSequence.Compute(firstRolls, firstBonus, firstDc,
                        Equals(metadata["persistentSave"], true), secondRolls, secondBonus, secondDc);
                    metadata["saveSequence"] = new Dictionary<string, object> {
                        { "status", sequence == null ? "UnavailableLifecycleOrBudget" : "Computed" },
                        { "firstFailure", sequence?.FirstFailure }, { "bothFailure", sequence?.BothFailure },
                        { "secondFailureIfReached", sequence?.SecondFailureIfReached },
                        { "scope", "two saves; numeric rerolls and passive cooldown markers; tracked fact consumption" }
                    };
                    if (sequence != null)
                    {
                        firstFailure = sequence.FirstFailure; secondFailure = sequence.SecondFailureIfReached;
                        secondSource = "readonly-consumed-d20-sequence"; lifecycleSequence = true;
                        firstSave.Probability = firstFailure; secondSave.Probability = secondFailure;
                        firstSave.Status = "Estimated"; secondSave.Status = secondFailure.HasValue ? "Estimated" : "Unavailable";
                        firstSave.Source = secondSource; secondSave.Source = secondSource;
                        firstSave.Reason = "TrackedSaveConsumption"; secondSave.Reason = "ConditionalTrackedSaveConsumption";
                    }
                }
            }
            var source = new Dictionary<string, object> {
                { "sr", sr?.Source }, { "firstSave", firstSave?.Source }, { "secondSave", secondSource }
            };
            // A known blocked stage makes all downstream outcomes impossible.
            double? reachesSecond = SpellDamageMath.Through(penetration, firstFailure);
            double? instantDeath = SpellDamageMath.Through(reachesSecond, secondFailure);
            double? damageChance = SpellDamageMath.Through(reachesSecond, 1 - secondFailure);
            var termOnPass = terms.FirstOrDefault();
            double? damageLoss = termOnPass == null ? 0 : (double?)null;
            double? expectedLoss = reachesSecond == 0 || instantDeath == 0 && damageChance == 0 ? 0 : (double?)null;
            long? hp = null;
            int? tempHp = null;
            if (BetaReader.ReadHealth(target, out long currentHp, out _, out int temporaryHp))
            {
                hp = currentHp;
                tempHp = Math.Max(temporaryHp, 0);
                if (termOnPass != null && Equals(metadata["damageMetamagicKnown"], true)
                    && termOnPass["dice"] is int dice && termOnPass["sides"] is int sides && termOnPass["bonus"] is int bonus
                    && Equals(termOnPass["usesSharedDefinition"], false) && Equals(termOnPass["halfIfSaved"], false))
                    damageLoss = SpellDamageMath.MeanCappedHpLoss(dice, Math.Max(1, sides), bonus,
                        Equals(termOnPass["maximize"], true), Equals(termOnPass["empower"], true),
                        Equals(termOnPass["effectiveHalf"], true), currentHp, tempHp.Value,
                        vulnerability: SpellTargetScaling.Factor(termOnPass), bonusPercent: SpellTargetScaling.Percent(termOnPass));
                if (penetration.HasValue && firstFailure.HasValue && secondFailure.HasValue && (damageLoss.HasValue || damageChance == 0))
                    expectedLoss = SpellDamageMath.ExpectedLethalSaveHpLoss(penetration.Value, firstFailure.Value,
                        secondFailure.Value, currentHp, damageLoss.GetValueOrDefault());
            }
            double? killLoss = hp;
            string hpSource = "basic-two-save-damage-model";
            bool approximateHp = false;
            double? temporaryLoss = tempHp == 0 || termOnPass == null ? 0 : (double?)null;
            if (metadata.TryGetValue("branchPreview", out var rawBranch) && rawBranch is Dictionary<string, object> branchData
                && branchData["outcomes"] is IEnumerable<Dictionary<string, object>> branchOutcomes)
            {
                string damageActionId = termOnPass != null && termOnPass.TryGetValue("actionId", out var termAction) ? termAction as string : null;
                var matchingDamage = branchOutcomes.Where(outcome => outcome.TryGetValue("damageActionIds", out var ids)
                    && ids is string[] actions && damageActionId != null && actions.Contains(damageActionId)).Take(2).ToArray();
                var matchingKill = branchOutcomes.Where(outcome => outcome.TryGetValue("killed", out var killed) && Equals(killed, true)).Take(2).ToArray();
                if (matchingDamage.Length == 1 && matchingDamage[0].TryGetValue("temporaryHpLossIfReached", out var rawTemporary) && rawTemporary is double temporaryBranch)
                    temporaryLoss = damageChance == 0 ? 0 : damageChance * temporaryBranch;
                if (termOnPass != null) damageLoss = matchingDamage.Length == 1 ? matchingDamage[0]["hpLossIfReached"] as double? : null;
                if (matchingKill.Length == 1) killLoss = matchingKill[0]["hpLossIfReached"] as double?;
                // The common action-tree model includes supported target defenses,
                // temporary HP and unknown projected states. Do not replace its
                // unavailable result with an unmitigated special-case estimate.
                expectedLoss = lifecycleSequence && hp.HasValue && firstFailure.HasValue && secondFailure.HasValue && penetration.HasValue
                    && damageLoss.HasValue && killLoss.HasValue
                    ? penetration.Value * firstFailure.Value * (secondFailure.Value * killLoss.Value + (1 - secondFailure.Value) * damageLoss.Value)
                    : branchData["expectedHpLoss"] as double?;
                hpSource = "readonly-action-tree-defense-distribution";
                approximateHp = branchData.TryGetValue("approximate", out var approximation) && Equals(approximation, true);
            }
            double? killContribution = instantDeath == 0 ? 0 : instantDeath * killLoss;
            double? damageContribution = damageChance == 0 || damageLoss == 0 ? 0 : damageChance * damageLoss;
            metadata["stagedOutcome"] = new Dictionary<string, object> {
                { "model", "two-saves-kill-on-second-failure" }, { "sources", source },
                { "firstSaveType", runs[0].SavingThrowType.ToString() }, { "secondSaveType", second.Type.ToString() },
                { "srPass", penetration }, { "firstSaveFailure", firstFailure }, { "secondSaveFailureIfReached", secondFailure },
                { "secondSaveReached", reachesSecond }, { "instantDeath", instantDeath }, { "damageBranchChance", damageChance },
                { "currentHp", hp }, { "temporaryHp", tempHp }, { "damageBranchHpLoss", damageLoss },
                { "expectedHpLossFromInstantDeath", killContribution }, { "expectedHpLossFromDamage", damageContribution },
                { "expectedHpLoss", expectedLoss }, { "expectedTemporaryHpLoss", temporaryLoss },
                { "approximate", approximateHp },
                { "hpProjectionSource", hpSource },
                { "basis", "conditional on spell applying; effect immunities and special damage modifiers separate" }
            };
            var stage = (ability.Blueprint.SpellResistance ? "SR 관통 " + Percent(penetration) + " → " : "")
                + SaveLabel(runs[0].SavingThrowType.ToString()) + " 실패 " + Percent(firstFailure) + " → "
                + SaveLabel(second.Type.ToString()) + " 실패 " + Percent(secondFailure) + " (조건부)";
            details.Insert(0, "후속 내성까지 도달 " + Percent(reachesSecond)
                + " · 기대 HP 감소 중 즉사 " + Hp(killContribution) + " / 주사위 피해 " + Hp(damageContribution));
            string firstLabel = SaveLabel(runs[0].SavingThrowType.ToString());
            string secondLabel = SaveLabel(second.Type.ToString());
            var outcomes = new List<SpellOutcome>();
            if (ability.Blueprint.SpellResistance) outcomes.Add(new SpellOutcome("SR 차단", 1 - penetration, "피해 없음", hpLossIfReached: 0));
            outcomes.Add(new SpellOutcome(firstLabel + " 성공", SpellDamageMath.Through(penetration, 1 - firstFailure), "피해 없음", hpLossIfReached: 0));
            outcomes.Add(new SpellOutcome(firstLabel + " 실패 → " + secondLabel + " 실패",
                instantDeath, "즉사" + (hp.HasValue ? " (" + hp.Value + " HP)" : ""), hpLossIfReached: killLoss));
            outcomes.Add(new SpellOutcome(firstLabel + " 실패 → " + secondLabel + " 성공",
                damageChance, termOnPass == null ? "피해 없음" : (string)termOnPass["formula"], hpLossIfReached: damageLoss));
            return SpellOutcome.Describe(stage, metadata, outcomes);
        }

        static GameAction OnlyAction(ActionList list)
            => list?.Actions?.Length == 1 ? list.Actions[0] : null;

        static bool Empty(ActionList list) => (list?.Actions?.Length ?? 0) == 0;

        static T FindAction<T>(ActionList list, bool allowOtherActions) where T : GameAction
        {
            if (!allowOtherActions) return OnlyAction(list) as T;
            var matches = list?.Actions?.OfType<T>().Take(2).ToArray();
            return matches?.Length == 1 ? matches[0] : null;
        }

        internal static string Percent(double? value) => value.HasValue ? "~" + value.Value.ToString("P0") : "—";
        static string Hp(double? value) => value.HasValue ? value.Value.ToString("0.##") : "—";
        internal static string SaveLabel(string type)
            => type == "Fortitude" ? "인내" : type == "Reflex" ? "반사" : type == "Will" ? "의지" : type;
    }
}
