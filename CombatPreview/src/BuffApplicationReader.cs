using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Enums;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.Designers.Mechanics.Facts;

namespace CombatPreview
{
    internal static class BuffApplicationReader
    {
        internal static string[] Apply(AbilityData ability, UnitEntityData caster, UnitEntityData target, BlueprintBuff buff,
            ProjectedEffectState state, ProjectedEffectState casterState, ProjectedBuffContext values = null)
            => Apply(ability, caster, target, buff, state, casterState, out _, values);
        internal static string[] Apply(AbilityData ability, UnitEntityData caster, UnitEntityData target, BlueprintBuff buff,
            ProjectedEffectState state, ProjectedEffectState casterState, out string appliedToken, ProjectedBuffContext values = null)
        {
            var descriptions = new List<string>();
            string rootToken = null;
            var path = new HashSet<string>();
            int budget = 32;
            Visit(buff, caster, ability?.SpellDescriptor ?? SpellDescriptor.None, casterState, ability, 0);
            appliedToken = rootToken;
            return descriptions.ToArray();

            void Visit(BlueprintBuff incoming, UnitEntityData source, SpellDescriptor descriptor,
                ProjectedEffectState sourceState, AbilityData sourceAbility, int depth)
            {
                string name = incoming?.Name ?? "상태 효과";
                if (incoming == null || depth > 8 || --budget < 0 || !path.Add(incoming.AssetGuid.ToString()))
                { state.Invalidate(incoming == null ? "SubstituteBuffNotLoaded" : "SubstitutionCycleOrBudget"); descriptions.Add(name + " 치환 경로 판독 불가"); return; }
                try
                {
                    var ownDescriptor = incoming.ComponentsArray.OfType<SpellDescriptorComponent>().FirstOrDefault();
                    descriptor = (SpellDescriptor)BuffStatMath.Descriptor(unchecked((ulong)descriptor),
                        ownDescriptor == null ? 0 : unchecked((ulong)(SpellDescriptor)ownDescriptor.Descriptor));
                    var components = state.EventComponents(target).Where(component => component != null).ToArray();
                    bool? allowed = CanApply(sourceAbility, source, target, incoming, descriptor, components, sourceState);
                    if (allowed != true)
                    {
                        descriptions.Add(name + (allowed == false ? " 면역" : " 부여 시도"));
                        if (allowed != false) state.Invalidate("BuffApplicationHandlerOrProtectionUnresolved:" + incoming.AssetGuid);
                        return;
                    }
                    bool replaced = false;
                    // OnDidTrigger handlers do not test CanApply. Several matching
                    // substitutions can all create replacements; preserve that order.
                    foreach (var substitution in components.OfType<BuffSubstitutionOnApply>())
                    {
                        if (substitution.GetType() != typeof(BuffSubstitutionOnApply)) continue;
                        var immunity = substitution.CheckConditionImmunity
                            ? incoming.ComponentsArray.OfType<AddCondition>().Select(condition => state.HasConditionImmunity(target, condition.Condition)).ToArray()
                            : Array.Empty<bool?>();
                        if (immunity.Any(value => !value.HasValue))
                        { state.Invalidate("ProjectedConditionImmunityUnavailable"); continue; }
                        bool matches = BuffTransitionPolicy.Matches(substitution.CheckDescriptor, unchecked((ulong)descriptor),
                            unchecked((ulong)(SpellDescriptor)substitution.SpellDescriptor),
                            CombatFields.Reference(CombatFields.Read(substitution, "m_GainedFact"), incoming), substitution.CheckConditionImmunity,
                            immunity.Any(value => value == true));
                        if (!matches) continue;
                        replaced = true;
                        var replacement = LoadedBlueprintReader.Read(CombatFields.Read(substitution, "m_SubstituteBuff")) as BlueprintBuff;
                        descriptions.Add(name + " → " + (replacement?.Name ?? "치환 버프 판독 불가"));
                        SpellDescriptor replacementDescriptor = SpellDescriptor.None;
                        if (replacement != null)
                        {
                            var definedDescriptor = replacement.ComponentsArray.OfType<SpellDescriptorComponent>().FirstOrDefault();
                            if (definedDescriptor != null) replacementDescriptor = (SpellDescriptor)definedDescriptor.Descriptor;
                        }
                        // UnitHelper.AddBuff is given evt.Initiator (the recipient)
                        // as caster, with a new buff-rooted mechanics context.
                        Visit(replacement, target, replacementDescriptor, state, null, depth + 1);
                    }
                    if (!replaced)
                    {
                        // This core-rule veto sets CanApply, not Immunity. A
                        // substitution still runs before considering this veto.
                        if (incoming.RemoveOnResurrect && target.State.Features.Immortality && !target.State.ForceKill)
                            descriptions.Add(name + " 적용 불가 (불사)");
                        else
                        {
                            var applied = state.Apply(incoming, target, source, descriptor, sourceAbility == ability ? values : null);
                            if (depth == 0) rootToken = applied?.Token;
                            descriptions.Add(name + " 적용");
                        }
                    }
                }
                finally { path.Remove(incoming.AssetGuid.ToString()); }
            }
        }

        // Copies of registered initiator handlers; none are invoked.
        static bool? CanApply(AbilityData ability, UnitEntityData caster, UnitEntityData target, BlueprintBuff buff,
            SpellDescriptor descriptor, BlueprintComponent[] components, ProjectedEffectState casterState)
        {
            if (buff == null || caster == null) return null;
            if (ability?.Blueprint.ComponentsArray.Any(component => component?.GetType().Name.Contains("Shadow") == true) == true) return null;
            bool unknown = false;
            bool? Has(object reference) => reference == null ? false : casterState != null ? casterState.HasFact(caster, reference) : CombatFields.HasFact(caster, reference);
            foreach (var component in components)
            {
                if (component.GetType() == typeof(BuffDescriptorImmunity))
                {
                    var immunity = (BuffDescriptorImmunity)component;
                    if (caster.State.Features.MythicReduceResistances || !immunity.Descriptor.HasAnyFlag(descriptor)) continue;
                    bool? ignore = Has(CombatFields.Read(immunity, "m_IgnoreFeature"));
                    bool? check = !immunity.CheckFact ? true : Has(CombatFields.Read(immunity, "m_FactToCheck"));
                    bool? blocks = BuffTransitionPolicy.Blocks(ignore, check);
                    if (blocks == true) return false;
                    if (!blocks.HasValue) unknown = true;
                    continue;
                }
                if (component.GetType() == typeof(SpecificBuffImmunity))
                {
                    if (!CombatFields.Reference(CombatFields.Read(component, "m_Buff"), buff)) continue;
                    var alignment = (AlignmentComponent)CombatFields.Read(component, "Alignment");
                    if (!caster.Alignment.ValueRaw.HasComponent(alignment)) continue;
                    bool? check = !CombatFields.Bool(component, "m_CheckCasterFact") ? true : Has(CombatFields.Read(component, "m_FactToCheck"));
                    if (check == true) return false;
                    if (!check.HasValue) unknown = true;
                    continue;
                }
                if (component.GetType() == typeof(BuffSubstitutionOnApply)) continue;
                if (component.GetType().GetInterfaces().Any(type => type.IsGenericType
                    && type.GetGenericArguments().Contains(typeof(RuleCanApplyBuff)))) unknown = true;
            }
            bool positive = buff.ComponentsArray.Any(component => component?.GetType().Name == "IsPositiveEffect");
            if (!positive && SpellDescriptorOperations.IsNegativeEffect(descriptor)
                && (target.IsPlayerFaction || !target.IsPlayersEnemy && target != caster)) unknown = true;
            return unknown ? (bool?)null : true;
        }
    }
}
