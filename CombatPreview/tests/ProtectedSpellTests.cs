using System;
using CombatPreview;
static class ProtectedSpellTests
{
    static int assertions;
    static void Near(double actual, double expected, string message)
    { assertions++; if (Math.Abs(actual - expected) > 1e-9) throw new Exception(message + ": " + actual + " != " + expected); }
    public static void Run()
    {
        for (int pool = 0; pool <= 6; pool++) for (int hp = 1; hp <= 8; hp++) for (int temp = 0; temp <= 2; temp++)
        {
            var source = new SpellProtection { Order = 0, Remaining = pool, PerHit = 3 };
            var parts = new[] { new SpellRollPart { Dice = 1, Sides = 3, SharedRoll = 0, Protections = new[] { source } },
                new SpellRollPart { Dice = 1, Sides = 3, SharedRoll = 1, Protections = new[] { source } } };
            double expectedHp = 0, expectedDamage = 0, expectedTemporary = 0;
            for (int a = 1; a <= 3; a++) for (int b = 1; b <= 3; b++)
            {
                int firstAbsorbed = Math.Min(a, pool), remaining = pool - firstAbsorbed;
                int damage = a - firstAbsorbed + b - Math.Min(b, remaining);
                expectedTemporary += Math.Min(temp, damage) / 9.0;
                expectedDamage += damage / 9.0;
                expectedHp += Math.Min(hp, Math.Max(0, damage - temp)) / 9.0;
            }
            var result = SpellSequenceMath.Compute(parts, 1, 1, 1, 1, hp, temp);
            Near(result.TemporaryHpLoss, expectedTemporary, "protection consumed before temp HP");
            Near(SpellSequenceMath.Compute(parts, 1, 1, 1, 1, hp, temp).TemporaryHpLoss, expectedTemporary, "protected cache preserves temp loss");
            Near(result.HpLoss, expectedHp, "mixed damage components share a pool and temporary HP");
            Near(result.ExpectedDamage, expectedDamage, "defended expectation remains uncapped by target HP");
        }
        var constantFirst = new SpellProtection { Order = 0, Remaining = -1, PerHit = 3 };
        var poolAfter = new SpellProtection { Order = 1, Remaining = 3, PerHit = 3 };
        var fixedParts = new[] { new SpellRollPart { Dice = 0, Sides = 1, Bonus = 5, Protections = new[] { constantFirst, poolAfter } } };
        Near(SpellSequenceMath.Compute(fixedParts, 2, 1, 1, 1, 100, 0).ExpectedDamage, 4,
            "constant-first priority ties do not consume the pool");
        var firstPool = new SpellProtection { Order = 0, Remaining = 3, PerHit = 3 };
        var secondPool = new SpellProtection { Order = 1, Remaining = 4, PerHit = 4 };
        fixedParts[0].Protections = new[] { firstPool, secondPool };
        Near(SpellSequenceMath.Compute(fixedParts, 3, 1, 1, 1, 100, 0).ExpectedDamage, 8,
            "overlapping protections select one source then spend its remaining amount");
        Near(SpellSequenceMath.Compute(fixedParts, 1, 0, 1, 1, 100, 0).ExpectedDamage, 0, "misses never spend protection");
        var changingPool = new SpellProtection { Order = 1, Remaining = 5, PerHit = 5 };
        var smallThenLarge = new[] {
            new SpellRollPart { Sides = 1, Bonus = 1, Protections = new[] { constantFirst, changingPool } },
            new SpellRollPart { Sides = 1, Bonus = 5, Protections = new[] { constantFirst, changingPool } } };
        var firstResult = SpellSequenceMath.Compute(smallThenLarge, 1, 1, 1, 1, 100, 0);
        Near(firstResult.ExpectedDamage, 1, "source selection compares full reduction before capping to rolled damage");
        firstResult.ExpectedDamage = 999;
        Near(SpellSequenceMath.Compute(smallThenLarge, 1, 1, 1, 1, 100, 0).ExpectedDamage, 1, "cached results are independent copies");
        changingPool.Remaining = 0;
        Near(SpellSequenceMath.Compute(smallThenLarge, 1, 1, 1, 1, 100, 0).ExpectedDamage, 2, "cache invalidates for changed pool balances");
        smallThenLarge[1].Bonus = 8;
        Near(SpellSequenceMath.Compute(smallThenLarge, 1, 1, 1, 1, 100, 0).ExpectedDamage, 5, "cache invalidates for changed damage inputs");
        Console.WriteLine("PASS: " + assertions + " protected mixed-spell distribution assertions");
    }
}
