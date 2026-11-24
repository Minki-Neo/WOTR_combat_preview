using System;
using System.Collections.Generic;
using CombatPreview;
static class CombatRegression67Tests
{
    public static void Run()
    {
        int count = 0;
        void Near(double actual, double expected, string reason)
        { count++; if (double.IsNaN(actual) || Math.Abs(actual - expected) > 1e-9) throw new Exception(reason + ": " + actual); }
        var empty = Array.Empty<(int, bool)>();
        Near(WeaponEffectMath.Enhancement(0, new[] { (2, false) }, empty), 2, "record 4826: equipment natural enhancement is +2");
        Near(8 + 2 + WeaponEffectMath.Enhancement(0, new[] { (2, false) }, empty), 12,
            "record 4826: strength + bone fist + natural enhancement produces +12, not +10");
        Near(WeaponEffectMath.Enhancement(3, new[] { (2, false) }, empty), 3, "nonstacking enhancement cannot lower or double the weapon's own +3");
        Near(WeaponEffectMath.Enhancement(3, new[] { (2, true), (2, true) }, empty), 5, "normal stacked enhancement cap is five");
        Near(WeaponEffectMath.Enhancement(7, new[] { (2, true) }, empty), 7, "native nonstacking value above cap is retained without extra normal stacking");
        Near(WeaponEffectMath.Enhancement(3, new[] { (2, false) }, new[] { (2, true) }), 5, "fake enhancement is a separately clamped extra channel");
        Near(WeaponEffectMath.Enhancement(5, empty, new[] { (2, true) }), 7, "extra channel can exceed ordinary enhancement cap");
        int favored = 0;
        foreach (var entry in new[] { (true, 2), (false, 8), (true, 6), (true, 4) })
            favored = WeaponEffectMath.FavoredStep(favored, entry.Item1, entry.Item2);
        Near(favored, 6, "overlapping favored targets take maximum matching registered bonus");
        Near(17 + WeaponEffectMath.FavoredStep(0, true, 2), 19, "record 4838: omitted target bonus raises +17 to +19");
        Near(RuleBonusMath.Sum(new[] { new ProjectedModifier { Value = -2, Descriptor = 25, Stacks = true },
            new ProjectedModifier { Value = -1, Descriptor = 25, Stacks = true } }), -3, "negative-level CL penalties stack across active sources");
        var persistent = new D20RollModel();
        Near(persistent.SaveFailure(0, 11, true), .75, "persistent save failure probability comes from independent dice math, not HCO");
        persistent.AfterRoll.Add(-1);
        double fail = persistent.SaveFailure(0, 11, true);
        Near(fail, .9375, "persistent plus favorable magic uses both independent reroll stages");
        Near(.8 * fail * fail, .703125, "two-save instant death branches apply SR and BOTH saving-throw probabilities");
        Near(SpellDamageMath.ExpectedLethalSaveHpLoss(.8, fail, fail, 100, 10.5),
            .8 * .9375 * (.9375 * 100 + .0625 * 10.5), "expected HP loss includes death and nonlethal damage branches separately");
        Near(RodMetamagicRules.Applies(128, 128, false, false, true, 4, 6, false) ? 1 : 0, 1, "persistent rod eligible for ordinary level-four spell");
        Near(RodMetamagicRules.Applies(128, 128, false, false, true, 7, 6, false) ? 1 : 0, 0, "rod level cap enforced");
        Near(RodMetamagicRules.Applies(128, 128, false, false, false, null, 0, true) ? 1 : 0, 1, "explicit whitelist bypasses ordinary book requirement");
        Near(RodMetamagicRules.Applies(128, 128, false, true, true, 4, 6, false) ? 1 : 0, 0, "sticky delivery does not apply rod twice");
        Near(RodMetamagicRules.Applies(4, 4, true, false, true, 4, 6, false) ? 1 : 0, 0, "swift cooldown disallows quicken-only rod");
        Near(RodMetamagicRules.Applies(6, 2, true, false, true, 4, 6, false) ? 1 : 0, 1, "engine combined-rod suitability tests non-quicken bits on swift cooldown");
        Near(RodMetamagicRules.Applies(128, 2, false, false, true, 4, 6, false) ? 1 : 0, 0, "unavailable metamagic rejected before whitelist/book match");
        using (ScopedReadMemo<string, string>.Begin())
        {
            ScopedReadMemo<string, string>.Put("state", "outer");
            using (ScopedReadMemo<string, string>.BeginShared())
                Near(ScopedReadMemo<string, string>.TryGet("state", out var value) && value == "outer" ? 1 : 0, 1,
                    "nested read-only capture shares its current batch, avoiding duplicate state copies");
            using (ScopedReadMemo<string, string>.Begin())
                Near(ScopedReadMemo<string, string>.TryGet("state", out _) ? 1 : 0, 0, "explicit fresh scope remains isolated");
        }
        using (ScopedReadMemo<string, string>.BeginShared())
            Near(ScopedReadMemo<string, string>.TryGet("state", out _) ? 1 : 0, 0, "batch memo cannot leak into next frame");
        Console.WriteLine("PASS: " + count + " logged combat / persistent-rod / batch-isolation regressions");
    }
}
