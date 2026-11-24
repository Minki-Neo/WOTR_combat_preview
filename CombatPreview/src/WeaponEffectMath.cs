using System;
using System.Collections.Generic;
using System.Linq;
namespace CombatPreview
{
    internal static class WeaponEffectMath
    {
        internal static int Enhancement(int basis, IEnumerable<(int Value, bool Stacks)> normal, IEnumerable<(int Value, bool Stacks)> extra)
        {
            int Group(IEnumerable<(int Value, bool Stacks)> source, int initial)
            {
                int highest = initial, stacked = 0;
                foreach (var value in source)
                    if (value.Stacks) stacked = checked(stacked + value.Value); else highest = Math.Max(highest, value.Value);
                return highest >= 5 ? highest : Math.Min(5, checked(highest + stacked));
            }
            return checked(Group(normal, basis) + Group(extra, 0));
        }
        internal static int FavoredStep(int current, bool matches, int bonus) => matches ? Math.Max(current, bonus) : current;
    }
}
