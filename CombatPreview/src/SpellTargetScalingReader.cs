using System.Collections.Generic;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;

namespace CombatPreview
{
    internal static class SpellTargetScalingReader
    {
        // Snapshot registered target handlers once per preview, not once per die.
        internal static Dictionary<string, object>[] AreaHalfSources(UnitEntityData target)
        {
            var sources = new List<Dictionary<string, object>>();
            if (target == null) return sources.ToArray();
            foreach (var fact in target.Facts.List.Where(fact => fact.Active))
            foreach (var runtime in fact.Components)
            {
                if (!CombatFields.Bool(runtime, "<IsSubscribedOnEventBus>k__BackingField")) continue;
                var type = CombatFields.Component(runtime)?.GetType();
                if (type != typeof(HalveIncomingAreaDamage) && type != typeof(BlinkAoEDamageResistance)) continue;
                sources.Add(new Dictionary<string, object> {
                    { "source", BetaIdentity.Id(fact) }, { "name", CombatFields.Label(fact) },
                    { "component", type.Name }, { "factor", .5 }
                });
            }
            return sources.ToArray();
        }
        internal static bool IsArea(ContextActionDealDamage damage, AbilityData ability)
        {
            // Reuse an already-computed primitive without invoking CalculateIsAOE
            // or writing to its blueprint cache. This caller handles direct actions.
            if (CombatFields.Read(damage, "m_IsAOE") is bool cached) return cached;
            return SpellTargetScaling.IsArea(damage.IsAoE, false,
                ability?.Blueprint.ComponentsArray.Any(component => component is AbilityTargetsAround) == true);
        }
    }
}
