using System;
using System.Collections.Generic;
using System.Linq;
using CombatPreview;

static class ConditionalSaveBonusTests
{
    static int count;
    static void Near(double actual, double expected, string message)
    { count++; if (double.IsNaN(actual) || Math.Abs(actual - expected) > 1e-9) throw new Exception(message + ": " + actual + " != " + expected); }
    static D20Modifier Typed(int value, int positive = 0, int negative = 0, int descriptor = 15)
        => new D20Modifier { SaveBonus = value, SaveBonusDescriptor = descriptor,
            SaveBonusBaselinePositive = positive, SaveBonusBaselineNegative = negative };
    public static void Run()
    {
        var modifiers = new List<D20Modifier> { Typed(4, 2), Typed(3, 2), new D20Modifier { SaveBonus = 1 } };
        Near(ConditionalSaveBonusMath.Sum(modifiers, 7), 3, "max morale delta plus stacking bonus");
        Near(ConditionalSaveBonusMath.Sum(modifiers, 0), 0, "no activation is no bonus");
        Near(ConditionalSaveBonusMath.Sum(new[] { Typed(2, 4) }, 1), 0, "stronger existing bonus wins");
        Near(ConditionalSaveBonusMath.Sum(new[] { Typed(-4, 2, -2), Typed(4, 2, -2) }, 3), 0, "best positive and worst negative compete separately");
        Near(ConditionalSaveBonusMath.Sum(new[] { Typed(4, 2), Typed(3, 0, 0, 10) }, 3), 5, "different descriptors add");
        Near(modifiers[0].SaveBonusBaselinePositive, 2, "baseline snapshot remains unchanged");
        for (int baseline = 0; baseline <= 5; baseline++)
        for (int a = 0; a <= 6; a++) for (int b = 0; b <= 6; b++)
        {
            var model = new D20RollModel();
            var first = Typed(a, baseline); first.Chance = 50; first.OnlyIfFailed = true;
            var second = Typed(b, baseline); second.Chance = 25; second.Compare = 3; second.CompareValue = 5;
            model.Modifiers.Add(first); model.Modifiers.Add(second);
            double oracle = 0;
            for (int die = 1; die <= 20; die++) for (int x = 0; x <= 1; x++) for (int y = 0; y <= 1; y++)
            {
                // Engine save reroll condition runs before the ordinary save
                // bonus is read. Here no rerolls obscure the stacking oracle.
                bool onA = x == 1 && die != 20 && die < 11;
                bool onB = y == 1 && die <= 5;
                int total = baseline + Math.Max(baseline, Math.Max(onA ? a : 0, onB ? b : 0)) - baseline;
                bool failed = die != 20 && (die == 1 || die + total < 11);
                if (failed) oracle += .05 * .5 * (y == 1 ? .25 : .75);
            }
            Near(model.SaveFailure(baseline, 11, false), oracle, "joint chance/first-die activation and typed stacking oracle");
        }
        // A pure consuming effect can be tracked across two saves; its typed
        // temporary bonus does not survive consumption into the next save.
        var consuming = new D20RollModel { ConsumesEffect = true };
        var effect = Typed(4, 2); effect.FactKey = "one-use"; effect.ConsumeWhenApplied = true;
        consuming.Modifiers.Add(effect);
        var sequence = D20SaveSequence.Compute(consuming, 2, 11, false, consuming, 2, 11);
        Near(sequence.FirstFailure, .3, "first save gets only +2 incremental bonus");
        Near(sequence.BothFailure, .12, "second save no longer has consumed modifier");
        var never = Typed(100, 2); never.Chance = 0;
        var off = new D20RollModel(); off.Modifiers.Add(never);
        Near(off.SaveFailure(2, 11, false), .4, "zero activation does not alter baseline save");
        Console.WriteLine("PASS: " + count + " conditional typed saving throw assertions");
    }
}
