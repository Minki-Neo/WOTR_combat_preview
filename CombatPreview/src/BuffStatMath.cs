using System;

namespace CombatPreview
{
    internal static class BuffStatMath
    {
        internal static ulong Descriptor(ulong parent, ulong own) => parent | own;
        internal static int? RankValue(int original, int perRank, int fromRank, int toRank)
        {
            if (fromRank < 1 || toRank < 1) return null;
            long value = original + (long)perRank * ((long)toRank - fromRank);
            return value >= int.MinValue && value <= int.MaxValue ? (int?)value : null;
        }
        // AddContextStatBonus multiplies the context value, adds Powerful Change
        // only to nonnegative bonuses, then applies its optional minimum.
        internal static int? ContextBonus(int? value, int multiplier, int? powerfulChange, int? minimum)
        {
            if (!value.HasValue) return null;
            long result = (long)value.Value * multiplier;
            if (result >= 0)
            {
                if (!powerfulChange.HasValue) return null;
                result += powerfulChange.Value;
            }
            if (minimum.HasValue) result = Math.Max(result, minimum.Value);
            return result >= int.MinValue && result <= int.MaxValue ? (int?)result : null;
        }
    }
}
