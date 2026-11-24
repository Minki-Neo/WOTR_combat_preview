using System;
using System.Collections.Generic;

namespace CombatPreview
{
    internal static class ConditionalSaveBonusMath
    {
        // Only activated modifiers compete. Baselines describe the existing
        // nonstacking group already included in the save's ordinary bonus.
        internal static int Sum(IList<D20Modifier> modifiers, int applied)
        {
            int sum = 0;
            for (int i = 0; i < modifiers.Count; i++)
            {
                if ((applied & (1 << i)) == 0) continue;
                var current = modifiers[i];
                if (!current.SaveBonusDescriptor.HasValue) { sum = checked(sum + current.SaveBonus); continue; }
                bool earlier = false;
                for (int j = 0; j < i; j++)
                    if ((applied & (1 << j)) != 0 && modifiers[j].SaveBonusDescriptor == current.SaveBonusDescriptor) earlier = true;
                if (earlier) continue;
                int positive = current.SaveBonusBaselinePositive, negative = current.SaveBonusBaselineNegative;
                for (int j = i; j < modifiers.Count; j++)
                {
                    var other = modifiers[j];
                    if ((applied & (1 << j)) == 0 || other.SaveBonusDescriptor != current.SaveBonusDescriptor) continue;
                    if (other.SaveBonusBaselinePositive != current.SaveBonusBaselinePositive || other.SaveBonusBaselineNegative != current.SaveBonusBaselineNegative)
                        throw new NotSupportedException("Inconsistent typed saving throw baselines");
                    positive = Math.Max(positive, other.SaveBonus); negative = Math.Min(negative, other.SaveBonus);
                }
                sum = checked(sum + positive + negative - current.SaveBonusBaselinePositive - current.SaveBonusBaselineNegative);
            }
            return sum;
        }
    }
}
