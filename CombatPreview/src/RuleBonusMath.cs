using System.Collections.Generic;
using System.Linq;

namespace CombatPreview
{
    internal static class RuleBonusMath
    {
        // RuleSystem.ModifiableBonus keeps the highest value per nonstacking
        // descriptor, including negative values; this differs from stat stacking.
        internal static int Sum(IEnumerable<ProjectedModifier> modifiers) => modifiers
            .GroupBy(modifier => modifier.Descriptor)
            .Sum(group => group.Where(modifier => modifier.Stacks).Sum(modifier => modifier.Value)
                + (group.Any(modifier => !modifier.Stacks) ? group.Where(modifier => !modifier.Stacks).Max(modifier => modifier.Value) : 0));
    }
}
