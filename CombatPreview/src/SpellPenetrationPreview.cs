using System;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Enums;

namespace CombatPreview
{
    internal static class SpellPenetrationPreview
    {
        // The vanilla component contributes its context scalar to the check.
        // Evaluate only cached or constant values, without running handlers.
        internal static int Bonus(UnitEntityData caster, out bool unsupported)
        {
            unsupported = false;
            var modifiers = new System.Collections.Generic.List<ProjectedModifier>();
            foreach (var fact in caster.Facts.List)
            {
                if (!fact.Active) continue;
                foreach (var runtime in fact.Components)
                {
                    var component = CombatFields.Component(runtime);
                    if (component?.GetType() != typeof(SpellPenetrationBonus)
                        || !CombatFields.Bool(runtime, "<IsSubscribedOnEventBus>k__BackingField")) continue;
                    var bonus = (SpellPenetrationBonus)component;
                    if (bonus.CheckFact)
                    {
                        var required = CombatFields.Read(bonus, "m_RequiredFact");
                        if (!CombatFields.HasFact(caster, required)) continue;
                    }
                    if (!CombatFields.Scalar(bonus.Value, fact, out int value))
                    { unsupported = true; continue; }
                    modifiers.Add(new ProjectedModifier { Value = value, Descriptor = (int)bonus.Descriptor, Stacks = bonus.Descriptor.IsStackable() });
                }
            }
            return RuleBonusMath.Sum(modifiers);
        }
    }
}
