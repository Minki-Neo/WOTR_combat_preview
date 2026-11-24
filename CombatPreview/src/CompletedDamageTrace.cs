using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.RuleSystem.Rules.Damage;

namespace CombatPreview
{
    // Copy the REAL, completed damage event. No game calculation, RNG, reduction
    // application, Decline getter (which updates a cache), or pool access is invoked.
    internal static class CompletedDamageTrace
    {
        static object Field(object value, string name) { return CombatFields.Read(value, name); }
        static object Auto(object value, string name) { return Field(value, "<" + name + ">k__BackingField"); }
        static Dictionary<string, object> ValueSource(object value)
        {
            return new Dictionary<string, object> {
                { "value", Field(value, "m_Value") }, { "source", Fact(Auto(value, "Source")) },
                { "reason", Auto(value, "Reason") }, { "metamagic", Auto(value, "Metamagic")?.ToString() }
            };
        }
        static object Fact(object fact)
        {
            var blueprint = CombatFields.Blueprint(fact) as Kingmaker.Blueprints.SimpleBlueprint;
            return fact == null ? null : new Dictionary<string, object> {
                { "id", blueprint?.AssetGuid.ToString() }, { "instanceId", BetaIdentity.Id(fact) }, { "label", CombatFields.Label(fact) }
            };
        }
        static object Modifiers(ModifiableBonus bonus)
        {
            if (bonus == null) return null;
            return new Dictionary<string, object> {
                { "total", bonus.TotalValue }, { "truncated", bonus.Modifiers.Take(65).Count() > 64 },
                { "entries", bonus.Modifiers.Take(64).Select(m => new Dictionary<string, object> {
                    { "value", m.Value }, { "descriptor", m.Descriptor.ToString() }, { "source", Fact(m.Fact) }
                }).ToArray() }
            };
        }
        static DamageKind Kind(BaseDamage source)
        {
            DamageKind kind;
            var energy = source as EnergyDamage;
            return source is PhysicalDamage ? DamageKind.Physical :
                energy != null && Enum.TryParse(energy.EnergyType.ToString(), out kind) ? kind : DamageKind.Unknown;
        }
        internal static Dictionary<string, object> Capture(RuleDealDamage evt, BetaComparison comparison)
        {
            if (!Compatibility.CanPreview) return new Dictionary<string, object> {
                { "traceVersion", 1 }, { "fullReplayVerified", false },
                { "missing", new[] { "Completed damage layout is not enabled for this game/mod configuration" } }
            };
            var missing = new List<string> {
                "Full pre-attack conditions and arbitrary mod handlers are not certified",
                "No independent replay of pre-reduction multipliers, shadow damage or expected-value distribution",
                "Final event Result is not HP loss; temporary HP and redirection can differ"
            };
            var list = evt.ResultList;
            var components = new List<Dictionary<string, object>>();
            var unmodeled = new HashSet<string>();
            if (evt.Half && !evt.AlreadyHalved) unmodeled.Add("eventHalf");
            if (evt.HalfBecauseSavingThrow) unmodeled.Add("savingThrowMultiplier");
            if (evt.DifficultyModifier != 1) unmodeled.Add("difficultyModifier");
            if (evt.ModifierBonus.GetValueOrDefault() != 0) unmodeled.Add("eventModifierBonus");
            if (evt.RedirectedDamage != 0) unmodeled.Add("damageRedirection");
            bool truncated = list != null && list.Count > 32;
            if (list == null || list.Count == 0) missing.Add("No completed ResultList");
            if (truncated) missing.Add("Damage components truncated at 32");
            var profile = comparison?.Preview?.Input.Defense;
            bool eligible = comparison != null && comparison.Status == BetaComparisonStatus.ComparedPartialModel
                && comparison.ActualComponents != null && !comparison.Differences.Any(d => d.StartsWith("Damage", StringComparison.Ordinal));
            var kinds = list == null ? new DamageKind[0] : list.Take(32).Select(v => v.Source == null ? DamageKind.Unknown : Kind(v.Source)).ToArray();
            long sumFinal = 0, sumBefore = 0, sumRaw = 0;
            for (int i = 0; list != null && i < Math.Min(list.Count, 32); i++)
            {
                var value = list[i]; var source = value.Source;
                if (source == null) { missing.Add("Missing source for component " + i); continue; }
                // Audited pure arithmetic getters on the supplied game build.
                int reduction = value.Reduction, final = value.FinalValue;
                var kind = kinds[i];
                if (Field(Auto(source, "Half"), "m_Value") as bool? == true && !source.AlreadyHalved) unmodeled.Add("componentHalf");
                var cachedDecline = Field(Field(source, "m_Decline"), "m_Value");
                if (cachedDecline != null && Convert.ToInt32(cachedDecline) != 0) unmodeled.Add("damageDecline");
                if (source.MaximumValue.HasValue) unmodeled.Add("maximumDamageCap");
                if (source.Precision || source.Sneak) unmodeled.Add("precisionOrSneak");
                sumFinal += final; sumBefore += value.ValueWithoutReduction; sumRaw += value.RollAndBonusValue;
                var declines = Field(source, "m_Declines") as System.Collections.IEnumerable;
                var declineRows = new List<object>();
                if (declines != null) foreach (var decline in declines)
                {
                    if (declineRows.Count == 16) { missing.Add("Decline sources truncated"); break; }
                    declineRows.Add(new Dictionary<string, object> {
                        { "type", Auto(decline, "Type")?.ToString() }, { "source", Fact(Auto(decline, "SourceFact")) },
                        { "incorporealDamageDivisor", Auto(decline, "IncorporealDamageDivisor") },
                        { "ascendantElement", Auto(decline, "AscendantElement") }
                    });
                }
                string replayReason = null; int? replay = null;
                if (!eligible) replayReason = "No comparable displayed weapon damage model";
                else if (list.Count != comparison.Preview.Input.Components.Count || i >= comparison.Preview.Input.Components.Count
                    || comparison.Preview.Input.Components[i].Kind != kind)
                    replayReason = "Completed damage components differ from displayed model";
                else if (profile == null || !profile.Complete || profile.HasConsumablePool || profile.PhysicalPool.HasValue)
                    replayReason = "Preview defense unavailable or consumable";
                else if (kind == DamageKind.Unknown || kinds.Count(k => k == kind) != 1) replayReason = "Unknown type or repeated type grouping unverified";
                else if (source.IgnoreReduction || evt.IgnoreDamageReduction || source.MaximumValue.HasValue || value.TacticalCombatDRModifier != 1)
                    replayReason = "Ignore-reduction, damage cap or tactical scaling outside static replay";
                else replay = DamageTrace.ReplayStaticDefense(value.ValueWithoutReduction, kind, profile);
                var physical = source as PhysicalDamage;
                components.Add(new Dictionary<string, object> {
                    { "index", i }, { "kind", kind.ToString() }, { "sourceType", source.GetType().FullName },
                    { "sourceFact", Fact(Auto(source, "SourceFact")) },
                    { "diceBase", Auto(source.Dice, "BaseFormula")?.ToString() },
                    { "diceModified", source.Dice?.ModifiedValue.ToString() },
                    { "diceModifierFields", CompletedAttackEvidence.Fields(source.Dice) },
                    { "diceModifierSources", CompletedAttackEvidence.DiceModifiers(source.Dice) },
                    { "bonus", source.Bonus }, { "bonusTargetRelated", source.BonusTargetRelated },
                    { "damageModifiers", Modifiers(Field(source, "m_Modifiers") as ModifiableBonus) },
                    { "ignoreModifiers", source.IgnoreModifiers }, { "preRolledValue", source.PreRolledValue },
                    { "empowerBonus", ValueSource(Field(source, "EmpowerBonus")) },
                    { "rollResult", value.RollResult }, { "rollAndBonusValue", value.RollAndBonusValue },
                    { "valueWithoutReduction", value.ValueWithoutReduction }, { "reduction", reduction }, { "finalValue", final },
                    { "reconstructedFinal", DamageTrace.ReconstructFinal(value.ValueWithoutReduction, reduction, source.MaximumValue) },
                    { "reconstructionMatches", final == DamageTrace.ReconstructFinal(value.ValueWithoutReduction, reduction, source.MaximumValue) },
                    { "half", ValueSource(Auto(source, "Half")) }, { "alreadyHalved", source.AlreadyHalved },
                    { "declineCached", ValueSource(Field(source, "m_Decline")) }, { "declines", declineRows },
                    { "immunityDeclineType", Field(source, "m_ImmunityDeclineType")?.ToString() },
                    { "bonusPercentStored", Field(source, "m_BonusPercent") }, { "vulnerabilityStored", Field(source, "m_Vulnerability") },
                    { "durabilityStored", Field(source, "m_Durability") }, { "criticalModifier", source.CriticalModifier },
                    { "criticalApplied", source.CriticalApplied.ToString() }, { "precision", source.Precision }, { "sneak", source.Sneak },
                    { "ignoreReduction", source.IgnoreReduction }, { "ignoreImmunities", source.IgnoreImmunities },
                    { "resistance", Modifiers(source.ReductionBecauseResistance) }, { "reductionPenalty", Modifiers(source.ReductionPenalty) },
                    { "hardness", ValueSource(Auto(source, "ReductionBecauseHardness")) }, { "maximumValue", source.MaximumValue },
                    { "tacticalDRModifier", value.TacticalCombatDRModifier },
                    { "physicalForm", physical == null ? null : (object)physical.Form.ToString() },
                    { "enchantment", physical == null ? null : (object)physical.Enchantment },
                    { "enchantmentTotal", physical == null ? null : (object)physical.EnchantmentTotal },
                    { "materials", physical == null ? null : Field(physical, "m_Materials")?.ToString() },
                    { "previewStaticDefenseReplay", replay }, { "previewStaticDefenseMatches", replay.HasValue ? (object)(replay.Value == final) : null },
                    { "replayUnavailableReason", replayReason }
                });
            }
            bool healthKnown = BetaReader.ReadHealth(evt.Target, out long finalHp, out int maximumHp, out int finalTemporary);
            return new Dictionary<string, object> {
                { "healthAtCompletion", healthKnown ? new Dictionary<string, object> {
                    { "currentHp", finalHp }, { "maximumHp", maximumHp }, { "temporaryHp", finalTemporary },
                    { "absorptionOrder", "target-defenses-then-temporary-hp-then-main-hp" },
                    { "scope", "final state only; does not infer consumed temporary HP from event Result" }
                } : null },
                { "traceVersion", 3 }, { "captureStage", "completed-RuleDealDamage" }, { "fullReplayVerified", false },
                { "previewId", comparison?.Preview?.Id }, { "previewComparisonStatus", comparison?.Status.ToString() },
                { "previewRowIndex", comparison?.Row == null ? null : (object)comparison.Row.Index },
                { "half", evt.Half }, { "halfBecauseSavingThrow", evt.HalfBecauseSavingThrow }, { "alreadyHalved", evt.AlreadyHalved },
                { "difficultyModifier", evt.DifficultyModifier }, { "modifierBonus", evt.ModifierBonus },
                { "ignoreDamageReduction", evt.IgnoreDamageReduction }, { "ignoreGrowthByDifficulty", evt.IgnoreGrowthByDifficulty },
                { "minimumHPAfterDamage", evt.MinHPAfterDamage }, { "redirectedDamage", evt.RedirectedDamage },
                { "redirectedPercent", evt.RedirectedPercent }, { "damageWithoutReduction", evt.DamageWithoutReduction },
                { "sumComponentFinal", sumFinal }, { "sumComponentBeforeReduction", sumBefore }, { "sumComponentRollAndBonus", sumRaw },
                { "componentCount", list?.Count }, { "componentsTruncated", truncated }, { "components", components },
                { "missing", missing },
                { "previewUnmodeledAdjustments", unmodeled.OrderBy(s => s).ToArray() },
                { "scope", "Component arithmetic and conditional static-defense replay using actual rolled damage; not expected-value validation" }
            };
        }
    }
}
