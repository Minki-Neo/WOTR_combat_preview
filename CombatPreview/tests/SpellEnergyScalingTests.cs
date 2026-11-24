using System;
using System.Collections.Generic;
using CombatPreview;

static class SpellEnergyScalingTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static void Near(double actual, double expected, string message)
    { Check(!double.IsNaN(actual) && Math.Abs(actual - expected) < 1e-8, message + ": " + actual + " != " + expected); }
    public static void Run()
    {
        Check(SpellEnergyScaling.Percent(1.05f) == 4, "each single-precision source truncates before aggregation");
        Check(SpellEnergyScaling.Percent(1.5f) == 50 && SpellEnergyScaling.Percent(.5f) == -50, "signed percent contribution");
        Check(SpellEnergyScaling.SumPercent(new[] { 50, 50 }) == 100, "two bonuses add percentages rather than multiplying scales");
        Check(SpellEnergyScaling.SumPercent(new[] { int.MaxValue, 1 }) == null, "aggregate overflow remains unknown");
        Check(SpellEnergyScaling.Percent(float.NaN) == null && SpellEnergyScaling.Percent(float.PositiveInfinity) == null,
            "non-finite serialized multipliers rejected");
        Near(SpellEnergyScaling.Combined(1, Math.Max(1.5, 1.5), .5).Value, .75, "duplicate energy vulnerability takes max, then durability multiplies");
        Check(SpellEnergyScaling.Combined(1.5, 1.5, 1) == null, "order-dependent mixed vulnerability is not guessed");
        Near(SpellEnergyScaling.Combined(1.5, 1, .5).Value, .75, "unambiguous swarm and durability remain supported");
        var unknown = new Dictionary<string, object> { { "targetDamageMultiplier", null }, { "targetVulnerability", 1.5 } };
        double unavailable = SpellTargetScaling.Factor(unknown);
        Check(double.IsNaN(unavailable), "unknown new scaling cannot fall back to old vulnerability");
        Check(SpellDamageMath.MeanHitPoints(1, 4, 0, false, false, false, vulnerability: unavailable) == null,
            "unknown scale propagates through mean");
        Check(SpellDamageMath.MeanCappedHpLoss(1, 4, 0, false, false, false, 8, 1, vulnerability: unavailable) == null,
            "unknown scale propagates through HP projection without NaN output");
        Check(SpellDamageMath.MeanIndependentHitsHpLoss(1, 4, 0, false, false, false, 0, 1, 2, .5, 8, 1, unavailable) == null,
            "unknown scale propagates through repeated hits");
        foreach (int bonus in new[] { -3, 0, 2 })
        foreach (int percent in new[] { -100, -25, 4, 30, 100 })
        foreach (double vulnerability in new[] { .25, .75, 1.5 })
        foreach (double saved in new[] { .5, .75, 1.0 })
        foreach (bool empower in new[] { false, true })
        foreach (bool maximize in new[] { false, true })
        foreach (int reduction in new[] { 0, 2 })
        {
            // Literal engine-stage oracle over all die pairs; no production
            // transform or distribution helper is called to derive the answers.
            int Oracle(int a, int b)
            {
                float value = (maximize ? 8 : a + b) + bonus;
                if (empower) value *= 1.5f;
                value *= (float)saved;
                value = Math.Max(1f, value);
                value *= 1f + percent / 100f;
                value *= (float)vulnerability;
                return Math.Max(1, (int)Math.Floor(value));
            }
            double mean = 0, hp = 0, pooledMean = 0, pooledHp = 0;
            for (int a = 1; a <= 4; a++) for (int b = 1; b <= 4; b++)
            {
                int damage = Math.Max(0, Oracle(a, b) - reduction);
                mean += damage / 16.0; hp += Math.Min(8, Math.Max(0, damage - 1)) / 16.0;
                for (int c = 1; c <= 4; c++) for (int d = 1; d <= 4; d++)
                {
                    int left = 4, total = 0;
                    foreach (int raw in new[] { Oracle(a, b), Oracle(c, d) })
                    {
                        int available = Math.Min(3, left);
                        if (reduction >= available) total += Math.Max(0, raw - reduction);
                        else { int absorbed = Math.Min(raw, available); left -= absorbed; total += raw - absorbed; }
                    }
                    pooledMean += total / 256.0; pooledHp += Math.Min(8, Math.Max(0, total - 1)) / 256.0;
                }
            }
            Near(SpellDamageMath.MeanHitPoints(2, 4, bonus, maximize, empower, false, reduction, saved, vulnerability, percent).Value,
                mean, "percent / minimum / save / resistance order");
            Near(SpellDamageMath.MeanCappedHpLoss(2, 4, bonus, maximize, empower, false, 8, 1, reduction, saved, vulnerability, percent).Value,
                hp, "percent in capped HP loss");
            var first = new SpellRollPart { Dice = 2, Sides = 4, Bonus = bonus, Maximize = maximize,
                Empower = empower, FailedFactor = saved, Vulnerability = vulnerability, BonusPercent = percent, Reduction = reduction };
            Near(SpellSequenceMath.Compute(new[] { first }, 1, 1, 1, 1, 8, 1).HpLoss, hp, "same percent in final sequence");
            var protections = new[] { new SpellProtection { Order = 0, Remaining = -1, PerHit = reduction },
                new SpellProtection { Order = 1, Remaining = 4, PerHit = 3 } };
            first.Protections = protections;
            var second = new SpellRollPart { Dice = 2, Sides = 4, Bonus = bonus, Maximize = maximize, Empower = empower,
                FailedFactor = saved, Vulnerability = vulnerability, BonusPercent = percent, SharedRoll = 1, Protections = protections };
            var result = SpellSequenceMath.Compute(new[] { first, second }, 1, 1, 1, 1, 8, 1);
            Near(result.ExpectedDamage, pooledMean, "percent changes pool consumption and cache identity");
            Near(result.HpLoss, pooledHp, "percent in protected HP distribution");
        }
        Console.WriteLine("PASS: " + checks + " energy percent / vulnerability assertions");
    }
}
