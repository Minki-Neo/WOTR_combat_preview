using System.Collections.Generic;
using System.Linq;

namespace CombatPreview
{
    internal static class SpellSchoolMath
    {
        internal static int Focus(int bonus, int sourceSchool, IEnumerable<int> mythicSchools, ISet<int> expanded)
            => checked(bonus * (mythicSchools.Any(school => school == sourceSchool || expanded.Contains(sourceSchool)) ? 2 : 1));
        // ExpandedArsenalMagicSchools sums each donor school separately, then
        // transfers the largest school total, excluding the spell's own school.
        internal static int Transfer(int targetSchool, IEnumerable<(int School, int Bonus)> sources)
            => sources.Where(source => source.School != targetSchool).GroupBy(source => source.School)
                .Select(group => group.Sum(source => source.Bonus)).DefaultIfEmpty(0).Max();
    }
}
