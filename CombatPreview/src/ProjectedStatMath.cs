using System;
using System.Collections.Generic;
using System.Linq;
namespace CombatPreview
{
    internal sealed class ProjectedModifier
    {
        internal int Descriptor, Value;
        internal bool Stacks;
        internal string Source;
        internal string Component;
        internal string Identity;
    }
    internal static class ProjectedStatMath
    {
        // Mirrors ModifiableValue.ApplyModifiersFiltered, including the shared
        // negative armor/encumbrance selection. Inputs are value copies only.
        internal static int Sum(IEnumerable<ProjectedModifier> modifiers)
        {
            int sum = 0, armor = 0, encumbrance = 0;
            foreach (var group in modifiers.GroupBy(modifier => modifier.Descriptor))
            {
                int positive = 0, negative = 0;
                foreach (var modifier in group)
                {
                    if (modifier.Value < 0 && modifier.Descriptor == 4) { armor = Math.Min(armor, modifier.Value); continue; }
                    if (modifier.Value < 0 && modifier.Descriptor == 41) { encumbrance = Math.Min(encumbrance, modifier.Value); continue; }
                    if (modifier.Stacks) sum = checked(sum + modifier.Value);
                    else { positive = Math.Max(positive, modifier.Value); negative = Math.Min(negative, modifier.Value); }
                }
                sum = checked(sum + positive + negative);
            }
            return checked(sum + Math.Min(armor, encumbrance));
        }
        internal static int Project(int current, IEnumerable<ProjectedModifier> existing, IEnumerable<ProjectedModifier> additions,
            System.Func<string, bool> removed = null)
        {
            var before = existing.ToArray();
            return checked(current + Sum(before.Concat(additions).Where(modifier => removed == null || !removed(modifier.Source))) - Sum(before));
        }
        internal static int ProjectFiltered(int currentUnfiltered, IEnumerable<ProjectedModifier> existing,
            IEnumerable<ProjectedModifier> additions, System.Func<ProjectedModifier, bool> allowed, System.Func<string, bool> removed,
            System.Func<ProjectedModifier, ProjectedModifier> replacement = null)
        {
            var before = existing.ToArray();
            var after = before.Select(modifier => replacement == null ? modifier : replacement(modifier)).Concat(additions)
                .Where(modifier => removed == null || !removed(modifier.Source)).ToArray();
            int baseline = checked(currentUnfiltered - Sum(before));
            // RecalculateMoraleInsight caps each filtered cache at ModifiedValue.
            // In particular, suppressing a negative Insight bonus cannot raise it.
            return checked(baseline + System.Math.Min(Sum(after), Sum(after.Where(allowed))));
        }
    }
}
