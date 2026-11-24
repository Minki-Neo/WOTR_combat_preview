using System;
using System.Collections.Generic;

namespace CombatPreview
{
    internal enum StatusTransition { None, Activate, Deactivate }
    // Detached copies of UnitState's signed-byte source and immunity counts.
    internal sealed class ProjectedConditionCounts
    {
        readonly int[] counts, immunities;
        readonly HashSet<int> touched = new HashSet<int>();
        internal ProjectedConditionCounts(sbyte[] counts, sbyte[] immunities)
        {
            if (counts == null || immunities == null || counts.Length != immunities.Length) throw new ArgumentException("Condition count layout");
            this.counts = Array.ConvertAll(counts, value => (int)value);
            this.immunities = Array.ConvertAll(immunities, value => (int)value);
        }
        internal IEnumerable<int> Touched => touched;
        internal bool? Active(int condition) => Valid(condition) ? (bool?)(counts[condition] > 0 && immunities[condition] <= 0) : null;
        internal bool? Immune(int condition) => Valid(condition) ? (bool?)(immunities[condition] > 0) : null;
        internal int? Sources(int condition) => Valid(condition) ? (int?)counts[condition] : null;
        bool Valid(int condition) => condition >= 0 && condition < counts.Length && counts[condition] >= 0 && immunities[condition] >= 0;
        internal bool Change(int condition, int direction, bool immunity = false)
        {
            if (!Valid(condition) || direction != -1 && direction != 1) return false;
            var values = immunity ? immunities : counts;
            // Preserve ordinary game count semantics, but never certify a byte
            // overflow/corrupted counter as a legitimate inactive condition.
            if (values[condition] < 0 || values[condition] == sbyte.MaxValue && direction > 0) return false;
            values[condition] = Math.Max(0, values[condition] + direction); touched.Add(condition); return true;
        }
        internal static StatusTransition Transition(bool active, bool exists, bool isSourceBuff)
            => !active && exists ? StatusTransition.Deactivate
                : active && !exists && !isSourceBuff ? StatusTransition.Activate : StatusTransition.None;
        internal ProjectedConditionCounts Copy()
        {
            var copy = new ProjectedConditionCounts(Array.ConvertAll(counts, value => (sbyte)value), Array.ConvertAll(immunities, value => (sbyte)value));
            foreach (var value in touched) copy.touched.Add(value); return copy;
        }
    }
}
