using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatPreview
{
    internal sealed class BranchContribution
    {
        internal double Weight;
        internal double? Value;
        internal Dictionary<string, bool> UnknownChoices = new Dictionary<string, bool>();
    }
    internal static class SpellBranchMath
    {
        // Unknown downstream checks must not hide an upstream guaranteed damage
        // result. A multilinear expectation is constant iff all binary corners
        // agree. This never assigns a guessed probability to an unknown save.
        internal static double? IndependentExpectation(IReadOnlyList<BranchContribution> leaves)
        {
            var keys = leaves.SelectMany(leaf => leaf.UnknownChoices.Keys).Distinct().ToArray();
            if (keys.Length > 8 || leaves.Count > 32) return null;
            double? constant = null;
            for (int mask = 0; mask < 1 << keys.Length; mask++)
            {
                double mean = 0;
                foreach (var leaf in leaves)
                {
                    if (leaf.Weight == 0) continue;
                    bool active = true;
                    for (int bit = 0; bit < keys.Length; bit++)
                        if (leaf.UnknownChoices.TryGetValue(keys[bit], out bool choice)
                            && choice != ((mask & (1 << bit)) != 0)) { active = false; break; }
                    if (!active) continue;
                    if (!leaf.Value.HasValue || double.IsNaN(leaf.Value.Value)) return null;
                    mean += leaf.Weight * leaf.Value.Value;
                }
                if (constant.HasValue && Math.Abs(mean - constant.Value) > 1e-9) return null;
                constant = mean;
            }
            return constant;
        }
    }
}
