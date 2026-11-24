using System;
using System.Collections.Generic;
using CombatPreview;
static class SpellSequenceTests
{
    static int count;
    static void Near(double value, double expected, string message)
    { count++; if (Math.Abs(value - expected) > 1e-9) throw new Exception(message + ": " + value + " != " + expected); }
    public static void Run()
    {
        // Exhaustively enumerate two independent d4 components, HP and temp HP.
        for (int hp = 1; hp <= 10; hp++) for (int temp = 0; temp <= 4; temp++)
        {
            double expected = 0, kills = 0, shared = 0, expectedTemporary = 0;
            for (int a = 1; a <= 4; a++) for (int b = 1; b <= 4; b++)
            { expectedTemporary += Math.Min(temp, a + b) / 16.0; expected += Math.Min(hp, Math.Max(0, a + b - temp)) / 16.0; if (a + b >= hp + temp) kills += 1 / 16.0; }
            for (int a = 1; a <= 4; a++) shared += Math.Min(hp, Math.Max(0, a * 2 - temp)) / 4.0;
            var parts = new[] { new SpellRollPart { Dice = 1, Sides = 4, SharedRoll = 0 },
                new SpellRollPart { Dice = 1, Sides = 4, SharedRoll = 1 } };
            var result = SpellSequenceMath.Compute(parts, 1, 1, 1, 1, hp, temp);
            Near(result.TemporaryHpLoss, expectedTemporary, "joint temp consumption matches enumerated rolls");
            Near(result.HpLoss, expected, "independent components use one temp HP pool");
            Near(result.KillChance, kills, "damage kill probability enumerated");
            parts[1].SharedRoll = 0;
            Near(SpellSequenceMath.Compute(parts, 1, 1, 1, 1, hp, temp).HpLoss, shared, "shared roll correlation retained");
        }
        // DR is applied per landed hit before one shared temporary-HP budget.
        for (int reduction = 0; reduction <= 5; reduction++)
        for (int temp = 0; temp <= 8; temp++)
        foreach (double chance in new[] { 0.0, .25, 1.0 })
        {
            double body = 0, temporary = 0;
            for (int a = 0; a <= 4; a++) for (int b = 0; b <= 4; b++)
            {
                double pa = a == 0 ? 1 - chance : chance / 4;
                double pb = b == 0 ? 1 - chance : chance / 4;
                int damage = Math.Max(0, a - reduction) + Math.Max(0, b - reduction);
                body += pa * pb * Math.Min(6, Math.Max(0, damage - temp));
                temporary += pa * pb * Math.Min(temp, damage);
            }
            var result = SpellSequenceMath.Compute(new[] { new SpellRollPart { Dice = 1, Sides = 4, Reduction = reduction } },
                2, chance, 1, 1, 6, temp);
            Near(result.HpLoss, body, "target DR precedes temp absorption on every hit");
            Near(result.TemporaryHpLoss, temporary, "misses never spend temp HP; shared across hits");
        }
        var fixedHit = new[] { new SpellRollPart { Dice = 0, Sides = 1, Bonus = 10, PassedFactor = .5 } };
        Near(SpellSequenceMath.Compute(fixedHit, 1, .8, .5, .25, 100, 0).HpLoss, 2.5,
            "hit SR and failed/passed save weighted once for a whole damage application");
        // One image, two certain attacks: first hits image 1/2; second then hits
        // body 1, otherwise body 1/2. Expected real hits = .5 + .75.
        Near(SpellSequenceMath.Compute(fixedHit, 2, 1, 1, 1, 100, 0, 1).HpLoss, 12.5,
            "mirror depletion correlated across deliveries");
        Near(SpellSequenceMath.Compute(fixedHit, 2, 1, 1, 1, 10, 0, 1).HpLoss, 10,
            "two attacks guarantee at least one body hit after one image");
        Near(SpellSequenceMath.Compute(fixedHit, 2, 0, 1, 1, 100, 0).HpLoss, 0, "all misses zero HP loss");
        Near(SpellSequenceMath.Compute(fixedHit, 1, 1, 0, 1, 100, 0).HpLoss, 0, "immune/blocked spell zero HP loss");
        var highHp = SpellSequenceMath.Compute(fixedHit, 1, 1, 1, 1, 100000, 0);
        Near(highHp.HpLoss, 10, "large target HP uses the reachable damage range, not a huge HP array");
        Near(highHp.KillChance, 0, "damage range saturation is not a kill on a high HP target");
        foreach (int sides in new[] { 6, 8, 10, 12 })
        {
            double damage = SpellDamageMath.MeanHitPoints(16, sides, 0, false, false, false).Value;
            var branches = new[] {
                new BranchContribution { Weight = 1, Value = damage, UnknownChoices = new Dictionary<string, bool> { { "status-save", false } } },
                new BranchContribution { Weight = 1, Value = damage, UnknownChoices = new Dictionary<string, bool> { { "status-save", true } } }
            };
            Near(SpellBranchMath.IndependentExpectation(branches).Value, 16 * (sides + 1) / 2.0,
                "unconditional CL-scaled damage survives an unknown downstream status save");
            branches[1].Value /= 2;
            count++; if (SpellBranchMath.IndependentExpectation(branches).HasValue)
                throw new Exception("unknown damage-halving save must not invent an expectation");
            branches[1].Value = null;
            count++; if (SpellBranchMath.IndependentExpectation(branches).HasValue)
                throw new Exception("unknown reachable damage cannot certify HP loss");
        }
        Console.WriteLine("PASS: " + count + " joint spell damage distribution assertions");
    }
}
