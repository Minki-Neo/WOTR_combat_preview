using System;
namespace CombatPreview
{
    internal struct WeaponSizeDice
    { internal int Size, Rolls, Sides, Type; }
    internal static class WeaponFallbackMath
    {
        internal static int AttributeDamage(int bonus, float multiplier)
        {
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier)) throw new ArgumentException("Damage multiplier");
            return checked((int)(bonus * (bonus < 0 ? 1f : multiplier)));
        }
        internal static int EffectiveSize(int size, int shift) => (int)Math.Max(2L, Math.Min(8L, (long)size + shift));
        // Melee/RangedWeaponSizeChange use the sign only. WeaponSizeChange
        // passes its signed value to Decrease (which subtracts it) when negative.
        internal static int SizeChange(int configured, bool signOnly)
            => signOnly ? Math.Sign(configured) : configured < 0 ? checked(-configured) : configured;
        internal static WeaponSizeDice? Scale(int rolls, int sides, int fromSize, int toSize, WeaponSizeDice[][] rows)
        {
            if (rows == null) return null;
            foreach (var row in rows)
            {
                bool match = false;
                foreach (var item in row) if (item.Size == fromSize && item.Rolls == rolls && item.Sides == sides) { match = true; break; }
                if (!match) continue;
                foreach (var item in row) if (item.Size == toSize) return item;
                return null;
            }
            return null;
        }
    }
}
