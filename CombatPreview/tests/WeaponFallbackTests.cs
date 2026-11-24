using System;
using CombatPreview;
static class WeaponFallbackTests
{
    static int count;
    static void Check(bool value, string message) { count++; if (!value) throw new Exception(message); }
    public static void Run()
    {
        Check(WeaponFallbackMath.AttributeDamage(8, 1) == 8, "ranged attribute enchantment contributes the missing +8");
        Check(WeaponFallbackMath.AttributeDamage(-3, 1.5f) == -3, "negative ability damage is never multiplied");
        Check(WeaponFallbackMath.AttributeDamage(5, .5f) == 2, "positive fractional multiplier truncates");
        Check(WeaponFallbackMath.AttributeDamage(5, 1.5f) == 7, "positive two-handed scale truncates");
        Check(WeaponFallbackMath.SizeChange(3, true) == 1 && WeaponFallbackMath.SizeChange(-3, true) == -1,
            "ranged and melee size-change handlers use sign only");
        Check(WeaponFallbackMath.SizeChange(-2, false) == 2, "generic negative size handler reproduces Decrease(negative)");
        Check(WeaponFallbackMath.EffectiveSize(5, 1) == 6, "large actor plus ranged size step becomes huge");
        Check(WeaponFallbackMath.EffectiveSize(4, int.MaxValue) == 8 && WeaponFallbackMath.EffectiveSize(4, int.MinValue) == 2,
            "effective weapon size clamps without addition overflow");
        var table = new[] { new[] {
            new WeaponSizeDice { Size = 3, Rolls = 1, Sides = 6 },
            new WeaponSizeDice { Size = 4, Rolls = 1, Sides = 8 },
            new WeaponSizeDice { Size = 5, Rolls = 2, Sides = 6 },
            new WeaponSizeDice { Size = 6, Rolls = 3, Sides = 6 } } };
        var scaled = WeaponFallbackMath.Scale(1, 8, 4, 6, table);
        Check(scaled.HasValue && scaled.Value.Rolls == 3 && scaled.Value.Sides == 6, "1d8 weapon scales to 3d6 using supplied row");
        Check(WeaponFallbackMath.Scale(1, 12, 4, 6, table) == null, "unmatched formula is not assigned another row");
        Check(WeaponFallbackMath.Scale(1, 8, 4, 7, table) == null, "absent destination stays unknown");
        // Sanitized 0.5.48 beta-378 input discrepancy, not an accuracy
        // certification: actual comparison reported 3d6+22 vs preview 1d8+14.
        Check(14 + WeaponFallbackMath.AttributeDamage(8, 1) == 22, "observed flat bonus gap explained without substituting the actual result");
        var terms = new[] { new ProjectedModifier { Descriptor = 12, Value = 1 },
            new ProjectedModifier { Descriptor = 12, Value = 1 } };
        Check(RuleBonusMath.Sum(terms) == 1, "duplicate nonstacking competence attack modifiers do not add");
        int active = RuleBonusMath.Sum(new[] { terms[0] });
        Check(RuleBonusMath.Sum(terms) - active == 0, "pending weaker/equal typed effect does not get projected twice");
        terms[1].Value = 3;
        Check(RuleBonusMath.Sum(terms) - active == 2, "pending stronger typed bonus projects only its net increase");
        Console.WriteLine("PASS: " + count + " weapon fallback / observed-gap assertions");
    }
}
