using System;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Designers.EventConditionActionSystem.Actions;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Conditions;

namespace CombatPreview
{
    internal static class EarPiercingScreamDamage
    {
        const string SpellId = "8e7cfa5f213a90549aadd18f8f6f4664";
        const string EnhancedDamageFactId = "e46f819691478e345b657a5df8f4bb0c";
        const string DazeBuffId = "9934fedff1b14994ea90205d189c8759";

        internal static ContextActionDealDamage Resolve(AbilityData ability, UnitEntityData caster,
            BlueprintComponent[] components, out bool enhanced, out int dazeBaseRounds)
        {
            enhanced = false;
            dazeBaseRounds = 0;
            if (!string.Equals(ability.Blueprint.AssetGuid.ToString(), SpellId, StringComparison.OrdinalIgnoreCase))
                return null;
            var runs = components.OfType<AbilityEffectRunAction>().Take(2).ToArray();
            if (runs.Length != 1 || runs[0].SavingThrowType != SavingThrowType.Fortitude
                || runs[0].Actions?.Actions?.Length != 1 || !(runs[0].Actions.Actions[0] is Conditional branch))
                return null;
            var tests = branch.ConditionsChecker?.Conditions;
            if (tests?.Length != 1 || !(tests[0] is ContextConditionCasterHasFact factCheck)) return null;

            var enhancedDamage = ReadDamage(branch.IfTrue, out int enhancedRounds);
            var normalDamage = ReadDamage(branch.IfFalse, out int normalRounds);
            if (enhancedDamage == null || normalDamage == null) return null;
            if (factCheck.Not || !Guid.TryParse(ReferenceId(CombatFields.Read(factCheck, "m_Fact")), out Guid factId)
                || factId.ToString("N") != EnhancedDamageFactId) return null;
            var blueprintId = new BlueprintGuid(factId);
            enhanced = caster.Facts.List.Any(fact => CombatFields.Blueprint(fact) is SimpleBlueprint blueprint
                && blueprint.AssetGuid.Equals(blueprintId));
            dazeBaseRounds = enhanced ? enhancedRounds : normalRounds;
            return enhanced ? enhancedDamage : normalDamage;
        }

        static ContextActionDealDamage ReadDamage(ActionList list, out int dazeBaseRounds)
        {
            dazeBaseRounds = 0;
            var actions = list?.Actions;
            // DazeBuff's actions are visual only. Its unloaded reference does not
            // affect the preceding Sonic damage or introduce another saving throw.
            if (actions?.Length != 2 || !(actions[0] is ContextActionDealDamage damage)
                || !(actions[1] is ContextActionConditionalSaved followUp) || !damage.HalfIfSaved
                || damage.DamageType == null || DefenseReader.Classify(damage.DamageType) != DamageKind.Sonic
                || (followUp.Succeed?.Actions?.Length ?? 0) != 0 || followUp.Failed?.Actions?.Length != 1
                || !(followUp.Failed.Actions[0] is ContextActionApplyBuff buff)
                || !Guid.TryParse(ReferenceId(CombatFields.Read(buff, "m_Buff")), out Guid buffId)
                || buffId.ToString("N") != DazeBuffId)
                return null;
            var duration = buff.DurationValue;
            if (!buff.Permanent && !buff.UseDurationSeconds && duration?.Rate.ToString() == "Rounds"
                && duration.DiceCountValue?.ValueType == ContextValueType.Simple && duration.DiceCountValue.Value == 0
                && duration.BonusValue?.ValueType == ContextValueType.Simple)
                dazeBaseRounds = duration.BonusValue.Value;
            return damage;
        }

        static string ReferenceId(object reference)
            => (CombatFields.Read(reference, "<Cached>k__BackingField") as SimpleBlueprint)?.AssetGuid.ToString()
                ?? CombatFields.Read(reference, "guid") as string
                ?? CombatFields.Read(reference, "deserializedGuid")?.ToString();
    }
}
