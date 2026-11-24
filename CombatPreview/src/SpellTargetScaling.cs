using System;
using System.Collections.Generic;

namespace CombatPreview
{
    internal static class SpellTargetScaling
    {
        // ContextActionDealDamage.CalculateIsAOE uses these three inputs.
        // Projectile shape and a displayed radius are not inputs to this rule.
        internal static bool IsArea(bool explicitArea, bool associatedArea, bool targetsAround)
            => explicitArea || associatedArea || targetsAround;
        internal static double Durability(bool area, int halfSources)
            => halfSources < 0 ? double.NaN : area ? Math.Pow(.5, halfSources) : 1;
        internal static double Factor(Dictionary<string, object> term)
            => term.TryGetValue("targetDamageMultiplier", out var factor) ? factor is double scale ? scale : double.NaN
                : term.TryGetValue("targetVulnerability", out var old) && old is double vulnerability ? vulnerability : 1;
        internal static int Percent(Dictionary<string, object> term)
            => term.TryGetValue("energyBonusPercent", out var percent) && percent is int value ? value : 0;
    }
}
