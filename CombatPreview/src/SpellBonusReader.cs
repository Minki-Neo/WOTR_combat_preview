using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Designers.Mechanics.Buffs;
using Kingmaker.UnitLogic.Parts;

namespace CombatPreview
{
    internal static class SpellBonusReader
    {
        internal static int? Bonus(AbilityData ability, UnitEntityData caster, int dice, BlueprintComponent[] components,
            int? cl, Metamagic? meta, int? rankBonus, List<string> sources, int[] observedRanks = null)
        {
            if (ability == null || caster == null) return 0;
            int bonus = 0;
            foreach (var fact in caster.Facts.List)
            {
                if (!fact.Active) continue;
                foreach (var runtime in fact.Components)
                {
                    if (!CombatFields.Bool(runtime, "<IsSubscribedOnEventBus>k__BackingField")) continue;
                    var component = CombatFields.Component(runtime);
                    int value;
                    if (component?.GetType() == typeof(SpecificSpellDamageBonus))
                    {
                        var spells = CombatFields.Read(component, "m_Spell") as IEnumerable;
                        if (spells == null || !spells.Cast<object>().Any(spell => CombatFields.Reference(spell, ability.Blueprint))) continue;
                        value = ((SpecificSpellDamageBonus)component).Bonus;
                    }
                    else if (component?.GetType() == typeof(DiceDamageBonusOnSpell)
                        || component?.GetType() == typeof(DraconicBloodlineArcana))
                    {
                        bool useContext;
                        Kingmaker.UnitLogic.Mechanics.ContextValue contextValue;
                        if (component is DiceDamageBonusOnSpell perDie)
                        {
                            var spells = CombatFields.Read(component, "m_Spells") as IEnumerable;
                            if (spells == null || !spells.Cast<object>().Any(spell => CombatFields.Reference(spell, ability.Blueprint))) continue;
                            useContext = perDie.UseContextBonus; contextValue = perDie.Value;
                        }
                        else
                        {
                            var bloodline = (DraconicBloodlineArcana)component;
                            if (!ability.SpellDescriptor.HasAnyFlag((SpellDescriptor)bloodline.SpellDescriptor)
                                || bloodline.SpellsOnly && !ability.Blueprint.IsSpell || (int)ability.Blueprint.Type == 6) continue;
                            useContext = bloodline.UseContextBonus; contextValue = bloodline.Value;
                        }
                        // Both game handlers use RuleCalculateDamage.Reason.Context:
                        // the damage spell's context, rather than the source fact.
                        int? amount = useContext
                            ? SpellDamagePreview.Value(contextValue, components, cl, meta, caster, rankBonus, observedRanks) : 1;
                        if (!amount.HasValue) { sources.Add(CombatFields.Label(fact) + ": 주사위당 동적 보정 판독 대기"); return null; }
                        value = checked(amount.Value * dice);
                    }
                    else continue;
                    bonus = checked(bonus + value);
                    sources.Add(CombatFields.Label(fact) + ": 피해 " + value.ToString("+0;-0;0"));
                }
            }
            return bonus;
        }
        internal static int? TargetBonus(UnitEntityData caster, UnitEntityData target, List<string> sources)
        {
            if (caster == null || target == null || caster.Get<UnitPartDisableBonusForDamage>()?.DisableAdditionalDamage == true) return 0;
            int result = 0;
            foreach (var fact in target.Facts.List.Where(fact => fact.Active))
            foreach (var runtime in fact.Components)
            {
                if (!CombatFields.Bool(runtime, "<IsSubscribedOnEventBus>k__BackingField")
                    || CombatFields.Component(runtime)?.GetType() != typeof(DamageBonusAgainstTarget)) continue;
                var rule = (DamageBonusAgainstTarget)CombatFields.Component(runtime);
                if (!rule.ApplyToSpellDamage) continue;
                var context = CombatFields.Read(fact, "m_Context");
                var owner = CombatFields.Read(CombatFields.Read(CombatFields.Read(context, "m_CasterRef"), "m_Proxy"), "<Entity>k__BackingField") as UnitEntityData;
                bool applies = rule.CheckCaster && caster == owner
                    || rule.CheckCasterFriend && owner != null && caster != owner && caster.GroupId == owner.GroupId;
                if (!applies) continue;
                if (!CombatFields.Scalar(rule.Value, fact, out int value))
                { sources.Add(CombatFields.Label(fact) + ": 대상별 피해 보정의 동적 값 미확인"); return null; }
                result = checked(result + value);
                sources.Add(CombatFields.Label(fact) + ": 대상별 피해 " + value.ToString("+0;-0;0"));
            }
            return result;
        }
    }
}
