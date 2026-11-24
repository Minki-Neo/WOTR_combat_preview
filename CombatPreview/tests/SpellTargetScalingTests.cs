using System;
using System.Collections.Generic;
using CombatPreview;

static class SpellTargetScalingTests
{
    static int checks;
    static void Near(double actual, double expected, string message)
    { checks++; if (double.IsNaN(actual) || Math.Abs(actual - expected) > 1e-9) throw new Exception(message + ": " + actual + " != " + expected); }
    public static void Run()
    {
        for (int flags = 0; flags < 8; flags++)
            Near(SpellTargetScaling.IsArea((flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0) ? 1 : 0,
                flags == 0 ? 0 : 1, "explicit action, area context or TargetsAround enables AoE");
        Near(SpellTargetScaling.Durability(false, 2), 1, "single target damage ignores area-only defenses");
        var metadata = new Dictionary<string, object> { { "targetVulnerability", 1.5 }, { "targetDamageMultiplier", .375 } };
        Near(SpellTargetScaling.Factor(metadata), .375, "consumers use combined target scale once");
        metadata.Remove("targetDamageMultiplier");
        Near(SpellTargetScaling.Factor(metadata), 1.5, "older metadata retains vulnerability");
        for (int halves = 0; halves <= 2; halves++)
        foreach (double vulnerability in new[] { 1.0, 1.5 })
        foreach (bool maximize in new[] { false, true })
        foreach (bool empower in new[] { false, true })
        foreach (double save in new[] { .5, .75 })
        for (int reduction = 0; reduction <= 3; reduction++)
        {
            double scale = vulnerability * SpellTargetScaling.Durability(true, halves);
            double mean = 0, hpMean = 0, poolMean = 0, poolHpMean = 0;
            // Enumerate two independent 2d3 damage rolls. Apply the target scale
            // before DR/pool consumption, preserving the engine minimum of one.
            int Roll(int a, int b) => Math.Max(1, (int)Math.Floor(Math.Max(1,
                (maximize ? 6 : a + b) * (empower ? 1.5 : 1) * save) * scale));
            for (int a = 1; a <= 3; a++) for (int b = 1; b <= 3; b++)
            {
                int raw = Roll(a, b), defended = Math.Max(0, raw - reduction);
                mean += defended / 9.0;
                hpMean += Math.Min(7, Math.Max(0, defended - 2)) / 9.0;
                for (int c = 1; c <= 3; c++) for (int d = 1; d <= 3; d++)
                {
                    int firstAbsorbed = Math.Min(raw, 3), left = 4 - firstAbsorbed;
                    int second = Roll(c, d);
                    int total = raw - firstAbsorbed + second - Math.Min(second, Math.Min(3, left));
                    poolMean += total / 81.0;
                    poolHpMean += Math.Min(7, Math.Max(0, total - 2)) / 81.0;
                }
            }
            Near(SpellDamageMath.MeanHitPoints(2, 3, 0, maximize, empower, false, reduction, save, scale).Value,
                mean, "target scaling before resistance");
            Near(SpellDamageMath.MeanCappedHpLoss(2, 3, 0, maximize, empower, false, 7, 2, reduction, save, scale).Value,
                hpMean, "target scaling through temporary HP and HP cap");
            var part = new SpellRollPart { Dice = 2, Sides = 3, Reduction = reduction, Maximize = maximize,
                Empower = empower, Vulnerability = scale, FailedFactor = save };
            Near(SpellSequenceMath.Compute(new[] { part }, 1, 1, 1, 1, 7, 2).HpLoss,
                hpMean, "sequence agrees with standalone scaled HP distribution");
            var pool = new SpellProtection { Order = 0, Remaining = 4, PerHit = 3 };
            part.Protections = new[] { pool };
            var secondPart = new SpellRollPart { Dice = 2, Sides = 3, SharedRoll = 1, Maximize = maximize,
                Empower = empower, Vulnerability = scale, FailedFactor = save, Protections = new[] { pool } };
            var result = SpellSequenceMath.Compute(new[] { part, secondPart }, 1, 1, 1, 1, 7, 2);
            Near(result.ExpectedDamage, poolMean, "area defenses change shared pool depletion");
            Near(result.HpLoss, poolHpMean, "protected HP projection uses the same scale and cache key");
        }
        Console.WriteLine("PASS: " + checks + " spell target scaling assertions");
    }
}
