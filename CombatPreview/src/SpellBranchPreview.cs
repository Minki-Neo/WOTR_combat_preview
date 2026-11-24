using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.Designers.EventConditionActionSystem.Actions;

namespace CombatPreview
{
    internal static class SpellBranchPreview
    {
        sealed class DamageEffect { internal ContextActionDealDamage Action; internal bool Saved, StateUnresolved; internal SavingThrowType Type; }
        sealed class Branch
        {
            internal double Probability;
            internal bool Saved, Killed, SecondaryEffectsUnresolved;
            internal SavingThrowType SaveType;
            internal int? SaveDc;
            internal string Path = "";
            internal readonly List<string> Effects = new List<string>();
            internal readonly List<DamageEffect> Damage = new List<DamageEffect>();
            internal readonly Dictionary<string, bool> Unknown = new Dictionary<string, bool>();
            internal ProjectedEffectState State = new ProjectedEffectState(), CasterState = new ProjectedEffectState();
            internal Branch Copy()
            { var copy = new Branch { Probability = Probability, Saved = Saved, Killed = Killed, SecondaryEffectsUnresolved = SecondaryEffectsUnresolved, SaveType = SaveType, SaveDc = SaveDc, Path = Path,
                    State = State.Copy(), CasterState = CasterState.Copy() };
                if (ReferenceEquals(State, CasterState)) copy.CasterState = copy.State;
                copy.Effects.AddRange(Effects); copy.Damage.AddRange(Damage);
                foreach (var entry in Unknown) copy.Unknown[entry.Key] = entry.Value; return copy; }
        }
        internal static void Append(AbilityData ability, UnitEntityData caster, UnitEntityData target,
            BlueprintComponent[] components, List<SpellCheckRecord> checks, Dictionary<string, object> metadata)
        {
            var runs = components.OfType<AbilityEffectRunAction>().Take(2).ToArray();
            if (runs.Length != 1 || !metadata.TryGetValue("spellDamage", out var raw)
                || !(raw is Dictionary<string, object> model) || !Equals(model["deliveryCount"], 1)
                || !(model["terms"] is List<Dictionary<string, object>> terms)) return;
            // A plain single-save damage spell already has a shorter presentation.
            if (!(runs[0].Actions?.Actions?.Any(action => action is Conditional || action is ContextActionConditionalSaved
                || action is ContextActionSavingThrow) == true)) return;
            var unsupportedNodes = new HashSet<string>();
            var secondaryNodes = new HashSet<string>();
            var analysis = new Dictionary<string, object> { { "status", "ReadingActionTree" }, { "unsupportedNodes", new string[0] } };
            metadata["branchAnalysis"] = analysis;
            var sr = checks.FirstOrDefault(check => check.Kind == "sr");
            double? pass = sr?.Status == "NotRequired" || sr?.Status == "NoRegisteredSR" ? 1 : sr?.Probability;
            var attack = checks.Where(check => check.Kind == "attack" && check.Status != "NotIdentified").Take(2).ToArray();
            if (!pass.HasValue || attack.Length > 1 || attack.Length == 1 && !attack[0].Probability.HasValue)
            { analysis["status"] = "DeliveryProbabilityUnavailable"; return; }
            double gate = pass.Value * (attack.Length == 0 ? 1 : attack[0].Probability.Value);
            var initial = new Branch { Probability = gate };
            if (ReferenceEquals(caster, target)) initial.CasterState = initial.State;
            var branches = new List<Branch> { initial };
            bool supported = true;
            int budget = 512, unknownStages = 0;
            var projectedStages = new List<Dictionary<string, object>>();
            metadata["projectedSaveStages"] = projectedStages;
            if (runs[0].SavingThrowType != SavingThrowType.Unknown)
            {
                var root = checks.FirstOrDefault(check => check.Kind == "save" && check.Type == runs[0].SavingThrowType.ToString());
                branches = Split(branches, root?.Probability, runs[0].SavingThrowType);
                foreach (var branch in branches) branch.SaveDc = metadata["dc"] as int?;
            }
            branches = Walk(runs[0].Actions, branches, 0);
            if (!supported || branches.Count > 32)
            { analysis["status"] = "UnsupportedActionConditionOrBudget"; analysis["unsupportedNodes"] = unsupportedNodes.ToArray(); return; }
            var summaries = new List<string>();
            var temporaryContributions = new List<BranchContribution>();
            if (metadata.TryGetValue("directDamageNoAttackRoll", out var noAttackRoll) && Equals(noAttackRoll, true))
                summaries.Add("피해 전달 · 명중 굴림 없음");
            if (gate < 1) summaries.Add("명중·SR 미통과 " + (1 - gate).ToString("P0") + " · 효과 없음");
            bool exactDamage = BetaReader.ReadHealth(target, out long hp, out _, out int temp);
            var outcomes = new List<Dictionary<string, object>>();
            var contributions = new List<BranchContribution>();
            var damageContributions = new List<BranchContribution>();
            foreach (var branch in branches)
            {
                var parts = new List<SpellRollPart>();
                var formulas = new List<string>();
                bool branchSupported = Equals(metadata["damageMetamagicKnown"], true);
                bool poolOnlyIncomplete = false;
                double? branchMean = 0;
                foreach (var effect in branch.Damage)
                {
                    var action = effect.Action;
                    if (effect.StateUnresolved) branchSupported = false;
                    if (action.AlreadyHalved || action.UseMinHPAfterDamage) branchSupported = false;
                    var term = terms.FirstOrDefault(entry => Equals(entry["actionId"], BetaIdentity.Id(action)));
                    if (term == null || !Equals(term["hpDamage"], true) || !(term["dice"] is int dice)
                        || !(term["sides"] is int sides) || !(term["bonus"] is int bonus)
                        || Equals(term["usesSharedDefinition"], true)) { branchSupported = false; continue; }
                    formulas.Add((string)term["formula"]);
                    var kind = DefenseReader.Classify(action.DamageType);
                    var traits = new WeaponTraits { Form = CombatFields.Int(action.DamageType?.Physical, "Form"), Known = true };
                    var defense = AutomaticDefenseReader.Read(target, null, traits, out _, energyOnly: kind != DamageKind.Physical,
                        readEnergyPools: true, singleApplication: branch.Damage.Count == 1, incomingKind: kind);
                    if (!defense.Complete && !defense.PoolApproximationAllowed)
                    { branchSupported = false; continue; }
                    poolOnlyIncomplete |= !defense.Complete;
                    defense.Reduction.TryGetValue(kind, out int reduction);
                    double factor = effect.Saved && action.HalfIfSaved ? caster.State.Features.AzataFavorableMagic ? .75 : .5 : 1;
                    if (action.HalfIfSaved)
                        foreach (var fact in target.Facts.List.Where(fact => fact.Active))
                            foreach (var runtime in fact.Components)
                            {
                                var component = CombatFields.Component(runtime);
                                string name = component?.GetType().Name;
                                if (name == "EvasionAgainstDescriptor" || name == "EvasionWithTowerShield") branchSupported = false;
                                if (!Equals(CombatFields.Read(component, "SavingThrow"), effect.Type)) continue;
                                if (effect.Saved && name == "Evasion") factor = 0;
                                if (!effect.Saved && name == "ImprovedEvasion") factor = caster.State.Features.AzataFavorableMagic ? .75 : .5;
                            }
                    bool immune = defense.Immunities.Contains(kind);
                    if (defense.IncorporealEnergyDivisor && kind != DamageKind.PositiveEnergy && kind != DamageKind.NegativeEnergy
                        && kind != DamageKind.Holy && kind != DamageKind.Unholy && kind != DamageKind.Divine) factor *= .5;
                    parts.Add(new SpellRollPart { Kind = kind, Dice = dice, Sides = Math.Max(1, sides), Bonus = bonus, Reduction = reduction,
                        SharedRoll = (int)term["sharedRollGroup"], Half = Equals(term["effectiveHalf"], true),
                        Empower = Equals(term["empower"], true), Maximize = Equals(term["maximize"], true),
                        Immune = immune, FailedFactor = factor, PassedFactor = factor,
                        Vulnerability = SpellTargetScaling.Factor(term),
                        BonusPercent = SpellTargetScaling.Percent(term),
                        Protections = SpellProtectionReader.Read(defense, kind) });
                    branchMean += immune || factor == 0 ? 0 : SpellDamageMath.MeanHitPoints(dice, Math.Max(1, sides), bonus,
                        Equals(term["maximize"], true), Equals(term["empower"], true), Equals(term["effectiveHalf"], true), reduction, factor,
                        SpellTargetScaling.Factor(term), SpellTargetScaling.Percent(term));
                }
                var result = parts.Count == 0 || poolOnlyIncomplete ? null : SpellSequenceMath.Compute(parts.ToArray(), 1, 1, 1, 1, hp, Math.Max(0, temp));
                if (result == null && branchSupported && parts.Count > 0)
                    result = MeanSpellShieldApproximation.Compute(parts.ToArray(), 1, 1, 1, 1, hp, Math.Max(0, temp));
                if (parts.Any(part => part.Protections?.Length > 0)) branchMean = result?.ExpectedDamage;
                double? loss = branch.Damage.Count == 0 ? 0 : branchSupported ? result?.HpLoss : null;
                if (branch.Killed) loss = target.IsPlayersEnemy ? hp : (double?)null;
                if (!branchSupported) branchMean = null;
                if (branch.Killed) branchMean = loss;
                if (branch.SecondaryEffectsUnresolved) { loss = null; branchMean = null; }
                double? temporaryLoss = branch.Damage.Count == 0 ? 0 : branchSupported ? result?.TemporaryHpLoss : null;
                if (branch.SecondaryEffectsUnresolved) temporaryLoss = null;
                if (branch.Probability == 0) { loss = 0; temporaryLoss = 0; }
                temporaryContributions.Add(new BranchContribution { Weight = branch.Probability, Value = temporaryLoss, UnknownChoices = branch.Unknown });
                contributions.Add(new BranchContribution { Weight = branch.Probability, Value = loss, UnknownChoices = branch.Unknown });
                damageContributions.Add(new BranchContribution { Weight = branch.Probability, Value = branchMean, UnknownChoices = branch.Unknown });
                string effects = string.Join(" · ", formulas.Concat(branch.Effects));
                if (effects.Length == 0) effects = "피해·상태 효과 없음";
                double? chancePerCast = branch.Unknown.Count == 0 ? (double?)branch.Probability : branch.Probability == 0 ? 0 : (double?)null;
                summaries.Add(branch.Path.Trim(' ', '→') + " " + SpellLethalPreview.Percent(chancePerCast) + " · " + effects);
                outcomes.Add(new Dictionary<string, object> { { "path", branch.Path }, { "chancePerCast", chancePerCast },
                    { "damageFormula", formulas.ToArray() }, { "effectAttempts", branch.Effects.ToArray() }, { "hpLossIfReached", loss }, { "temporaryHpLossIfReached", temporaryLoss },
                    { "approximate", result?.Approximate == true && !branch.Killed },
                    { "damageActionIds", branch.Damage.Select(effect => BetaIdentity.Id(effect.Action)).ToArray() }, { "killed", branch.Killed },
                    { "targetProjectionReasons", branch.State.Reasons.ToArray() }, { "casterProjectionReasons", branch.CasterState.Reasons.ToArray() } });
            }
            double? expected = exactDamage ? SpellBranchMath.IndependentExpectation(contributions) : null;
            double? expectedDamage = SpellBranchMath.IndependentExpectation(damageContributions);
            analysis["status"] = secondaryNodes.Count > 0 ? "PartialSecondaryTargetEffects"
                : expected.HasValue ? "ComputedTrackedHpProjection" : "PartialDamageOrDefense";
            if (secondaryNodes.Count > 0) metadata["secondaryTargetEffects"] = new Dictionary<string, object> {
                { "status", "Unmodeled" }, { "nodes", secondaryNodes.ToArray() },
                { "scope", "Displayed odds describe the tracked direct-target branches before secondary random-target effects; additional effects and HP impact are unresolved." }
            };
            analysis["unknownSaveStages"] = unknownStages;
            if (expectedDamage.HasValue) summaries.Insert(0, "기대 피해 ~" + expectedDamage.Value.ToString("0.##"));
            metadata["branchPreview"] = new Dictionary<string, object> { { "summary", summaries.ToArray() },
                { "approximate", outcomes.Any(outcome => Equals(outcome["approximate"], true)) },
                { "outcomes", outcomes.ToArray() }, { "expectedHpLoss", expected }, { "expectedTemporaryHpLoss", exactDamage ? SpellBranchMath.IndependentExpectation(temporaryContributions) : null }, { "expectedDamage", expectedDamage },
                { "scope", "recognized same-target action tree; state-effect application still subject to buff immunity" } };

            List<Branch> Split(List<Branch> inputs, double? failure, SavingThrowType type)
            {
                var output = new List<Branch>();
                string unknownKey = failure.HasValue ? null : "save-" + (++unknownStages);
                foreach (var input in inputs)
                    foreach (bool saved in new[] { false, true })
                    {
                        var copy = input.Copy(); copy.Saved = saved; copy.SaveType = type;
                        if (failure.HasValue) copy.Probability *= saved ? 1 - failure.Value : failure.Value;
                        else copy.Unknown[unknownKey] = saved;
                        copy.Path += " → " + SpellLethalPreview.SaveLabel(type.ToString()) + (saved ? " 성공" : " 실패");
                        output.Add(copy);
                    }
                return output;
            }
            List<Branch> Walk(ActionList list, List<Branch> inputs, int depth)
            {
                if (list?.Actions == null) return inputs;
                if (depth > 12 || inputs.Count > 32) { supported = false; return inputs; }
                foreach (var action in list.Actions)
                {
                    if (action == null) continue;
                    if (--budget < 0) { supported = false; return inputs; }
                    if (action is ContextActionConditionalSaved conditionalSaved)
                        inputs = inputs.SelectMany(input => input.SaveType == SavingThrowType.Unknown
                            ? new List<Branch> { input } : Walk(input.Saved ? conditionalSaved.Succeed : conditionalSaved.Failed,
                                new List<Branch> { input }, depth + 1)).ToList();
                    else if (action is Conditional conditional)
                    {
                        if (inputs.Count > 1)
                        {
                            inputs = inputs.SelectMany(input => {
                                var selected = SpellPreviewRuntime.ReadSaveCondition(conditional.ConditionsChecker, target, caster, input.State, input.CasterState);
                                if (!selected.HasValue) { supported = false; return new List<Branch> { input }; }
                                return Walk(selected.Value ? conditional.IfTrue : conditional.IfFalse, new List<Branch> { input }, depth + 1);
                            }).ToList();
                            continue;
                        }
                        var active = SpellPreviewRuntime.ReadSaveCondition(conditional.ConditionsChecker, target, caster, inputs.FirstOrDefault()?.State, inputs.FirstOrDefault()?.CasterState);
                        if (!active.HasValue)
                        { foreach (var condition in conditional.ConditionsChecker?.Conditions ?? new Kingmaker.ElementsSystem.Condition[0])
                                if (condition != null) unsupportedNodes.Add(condition.GetType().Name);
                            supported = false; return inputs; }
                        inputs = Walk(active.Value ? conditional.IfTrue : conditional.IfFalse, inputs, depth + 1);
                    }
                    else if (action is ContextActionSavingThrow save)
                    {
                        var data = metadata.TryGetValue("nestedSaves", out var nested) ? nested as List<Dictionary<string, object>> : null;
                        var matching = data?.FirstOrDefault(entry => Equals(entry["actionId"], BetaIdentity.Id(save)));
                        double? failure = matching != null && matching.TryGetValue("failureChance", out var chance)
                            && chance is double probability ? probability : (double?)null;
                        inputs = inputs.SelectMany(input => {
                            if (matching != null && Equals(matching["source"], "existing-shared-save"))
                                return Walk(save.Actions, new List<Branch> { input }, depth + 1);
                            double? projectedFailure = failure;
                            var projectedSave = input.State.Save(target, save.Type);
                            if (!projectedSave.HasValue) projectedFailure = null;
                            int? projectedDc = matching?["unadjustedDc"] as int?;
                            bool inherit = save.UseDCFromContextSavingThrow && input.SaveType != SavingThrowType.Unknown;
                            if (inherit) projectedDc = input.SaveDc;
                            else if (matching != null && matching["conditionResults"] is List<Dictionary<string, object>> adjustments)
                            {
                                var increases = CombatFields.Read(save, "m_ConditionalDCIncrease") as Array;
                                if ((increases?.Length ?? 0) != adjustments.Count) projectedDc = null;
                                else for (int index = 0; index < adjustments.Count; index++)
                                {
                                    bool? active = SpellPreviewRuntime.ReadSaveCondition(CombatFields.Read(increases.GetValue(index), "Condition")
                                        as ConditionsChecker, target, caster, input.State, input.CasterState);
                                    if (active == false) continue;
                                    if (active != true || !(adjustments[index]["definedAdjustment"] is int amount)) { projectedDc = null; break; }
                                    long? sum = (long?)projectedDc + amount;
                                    projectedDc = sum >= int.MinValue && sum <= int.MaxValue ? (int?)sum : null;
                                }
                            }
                            if (projectedSave.HasValue && projectedDc.HasValue && metadata["persistentSave"] is bool persistent)
                            {
                                projectedFailure = ActiveD20Reader.SaveFailure(target, caster, ability, save.Type,
                                    projectedSave.Value, projectedDc.Value, persistent, out var rolls);
                                if (rolls.ConsumesEffect && input.SaveType != SavingThrowType.Unknown) projectedFailure = null;
                                if (input.State.HasStatProjectionChanges && rolls.Modifiers.Any(modifier => modifier.SaveBonusDescriptor.HasValue))
                                {
                                    projectedFailure = null;
                                    input.State.Reasons.Add("TypedConditionalSaveBonusAfterProjectedStateChange");
                                }
                            }
                            else projectedFailure = null;
                            projectedStages.Add(new Dictionary<string, object> {
                                { "actionId", BetaIdentity.Id(save) }, { "precedingPath", input.Path }, { "type", save.Type.ToString() },
                                { "bonus", projectedSave }, { "dc", projectedDc }, { "conditionalFailureChance", projectedFailure },
                                { "reachChancePerCast", input.Unknown.Count == 0 ? (double?)input.Probability : null },
                                { "targetProjectionReasons", input.State.Reasons.ToArray() }, { "casterProjectionReasons", input.CasterState.Reasons.ToArray() },
                                { "targetProjectedBuffState", input.State.Diagnostics() }, { "casterProjectedBuffState", input.CasterState.Diagnostics() }
                            });
                            var split = Split(new List<Branch> { input }, projectedFailure, save.Type);
                            foreach (var nestedBranch in split) nestedBranch.SaveDc = projectedDc;
                            var nestedBranches = Walk(save.Actions, split, depth + 1);
                            foreach (var nestedBranch in nestedBranches) { nestedBranch.Saved = input.Saved; nestedBranch.SaveType = input.SaveType; nestedBranch.SaveDc = input.SaveDc; }
                            return nestedBranches;
                        }).ToList();
                    }
                    else if (action.GetType() == typeof(ContextActionOnRandomTargetsAround))
                    {
                        var random = (ContextActionOnRandomTargetsAround)action;
                        if (random.NumberOfTargets <= 0 || random.Actions?.Actions == null || random.Actions.Actions.Length == 0) continue;
                        foreach (var input in inputs)
                        {
                            if (input.Probability == 0 && input.Unknown.Count == 0) continue;
                            secondaryNodes.Add(action.GetType().Name);
                            // Keep known earlier direct branches. Never execute
                            // the random picker or assign collateral odds/HP.
                            input.SecondaryEffectsUnresolved = true;
                            input.State.Invalidate("RandomSecondaryTargetEffects");
                            input.CasterState.Invalidate("RandomSecondaryTargetEffects");
                            input.Effects.Add("주변 무작위 효과 · 추가 판정 미확인");
                        }
                    }
                    else if (action is ContextActionDealDamage damage) foreach (var input in inputs)
                        input.Damage.Add(new DamageEffect { Action = damage, Saved = input.Saved, Type = input.SaveType,
                            StateUnresolved = input.State.Unresolved || input.CasterState.Unresolved });
                    else if (action is ContextActionKill) foreach (var input in inputs)
                        { input.Killed = true; input.Effects.Add("즉사"); }
                    else if (action is ContextActionApplyBuff buff)
                    {
                        var blueprint = LoadedBlueprintReader.Read(CombatFields.Read(buff, "m_Buff")) as Kingmaker.UnitLogic.Buffs.Blueprints.BlueprintBuff;
                        var recipient = buff.ToCaster ? caster : target;
                        foreach (var input in inputs)
                        {
                            var state = buff.ToCaster ? input.CasterState : input.State;
                            input.Effects.AddRange(BuffApplicationReader.Apply(ability, caster, recipient, blueprint, state, input.CasterState,
                                ProjectedBuffContext.Read(ability, caster, metadata))
                                .Select(description => (buff.ToCaster ? "시전자 " : "") + description));
                        }
                    }
                    else if (action.GetType() == typeof(ContextActionRemoveBuff))
                    {
                        var remove = (ContextActionRemoveBuff)action;
                        var blueprint = LoadedBlueprintReader.Read(CombatFields.Read(remove, "m_Buff")) as Kingmaker.UnitLogic.Buffs.Blueprints.BlueprintBuff;
                        foreach (var input in inputs)
                        {
                            var state = remove.ToCaster ? input.CasterState : input.State;
                            state.Remove(blueprint, remove.ToCaster ? caster : target, caster, remove.OnlyFromCaster, remove.RemoveRank);
                            input.Effects.Add((remove.ToCaster ? "시전자 " : "") + (blueprint?.Name ?? "상태 효과")
                                + (remove.RemoveRank ? " 랭크 제거" : " 제거") + (remove.OnlyFromCaster ? " (이 시전자)" : ""));
                        }
                    }
                    else if (action.GetType() == typeof(ContextActionRemoveBuffSingleStack))
                    {
                        var blueprint = LoadedBlueprintReader.Read(CombatFields.Read(action, "m_TargetBuff")) as Kingmaker.UnitLogic.Buffs.Blueprints.BlueprintBuff;
                        foreach (var input in inputs)
                        {
                            input.State.Remove(blueprint, target, caster, false, false);
                            input.Effects.Add((blueprint?.Name ?? "상태 효과") + " 한 항목 제거");
                        }
                    }
                    else if (action.GetType() == typeof(ContextActionRemoveBuffsByDescriptor))
                    {
                        var remove = (ContextActionRemoveBuffsByDescriptor)action;
                        foreach (var input in inputs)
                        {
                            input.State.RemoveDescriptor((Kingmaker.Blueprints.Classes.Spells.SpellDescriptor)remove.SpellDescriptor, target, caster);
                            input.Effects.Add(remove.SpellDescriptor + " 효과 제거");
                        }
                    }
                    else if (action.GetType() == typeof(ContextActionRemoveSelf))
                    {
                        // Direct spell actions have no executing Buff.Data or area
                        // activation scope. The game's RemoveSelf is a no-op here.
                    }
                    else if (action.GetType().Name == "ContextActionKnockdownTarget")
                        foreach (var input in inputs) input.Effects.Add("넘어짐 시도");
                    else if (!action.GetType().Name.StartsWith("ContextActionSpawnFx", StringComparison.Ordinal)
                        && action.GetType().Name != "ContextActionPlaySound")
                    { unsupportedNodes.Add(action.GetType().Name); supported = false; return inputs; }
                }
                return inputs;
            }
        }
    }
}
