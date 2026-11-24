using System;
using System.Collections.Generic;

namespace CombatPreview
{
    internal static class SpellEnergyScaling
    {
        // EnergyDamageBonus truncates each single-precision contribution before
        // adding it to BonusPercent; it does not multiply the source multipliers.
        internal static int? Percent(float multiplier)
        {
            float value = (multiplier - 1f) * 100f;
            return float.IsNaN(value) || float.IsInfinity(value) || value < int.MinValue || value >= 2147483648f
                ? (int?)null : (int)value;
        }
        internal static int? SumPercent(IEnumerable<int> contributions)
        {
            long sum = 0;
            foreach (int value in contributions) sum += value;
            return sum < int.MinValue || sum > int.MaxValue ? (int?)null : (int)sum;
        }
        internal static double? Combined(double swarm, double energyVulnerability, double durability)
        {
            if (!(swarm >= 1) || !(energyVulnerability >= 1)
                || !(durability > 0) || double.IsInfinity(durability) || double.IsInfinity(swarm)) return null;
            // set_Vulnerability takes Max(existing, incoming), whereas a swarm
            // handler reads and multiplies existing. Their unknown registration
            // order can therefore produce different results.
            if (swarm > 1 && energyVulnerability > 1) return null;
            double result = Math.Max(swarm, energyVulnerability) * durability;
            return double.IsInfinity(result) ? (double?)null : result;
        }
    }
}
