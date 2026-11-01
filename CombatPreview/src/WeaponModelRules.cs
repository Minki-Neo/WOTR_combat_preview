using System;
using System.Collections.Generic;

namespace CombatPreview
{
    public static class WeaponModelRules
    {
        public static int StrengthDamage(int strengthBonus, bool twoHands)
        { return twoHands && strengthBonus > 0 ? checked(strengthBonus * 3) / 2 : strengthBonus; }
        public static int[] BasicPattern(int bab, bool full, bool natural, int extra, int haste)
        {
            if (!full || natural) return new[] { 0 };
            return CountPattern(1 + extra + haste, Math.Min(3, Math.Max(0, bab - 1) / 5), false);
        }
        public static int[] CountPattern(int mainAttacks, int penalized, bool flawless)
        {
            if (mainAttacks < 1 || mainAttacks > 16 || penalized < 0 || penalized > 8 || mainAttacks + penalized > 24)
                throw new ArgumentOutOfRangeException("attack count");
            var result = new List<int>();
            for (int i = 0; i < mainAttacks; i++) result.Add(0);
            for (int i = 1; i <= penalized; i++) result.Add(flawless ? 0 : i * 5);
            return result.ToArray();
        }
    }
}
