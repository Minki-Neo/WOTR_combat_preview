using System;
using System.Linq;
using CombatPreview;
static class StatefulFeatureTests
{
    static int count;
    static void Near(double actual, double expected, string message)
    { count++; if (double.IsNaN(actual) || Math.Abs(actual - expected) > 1e-8) throw new Exception(message + ": " + actual + " != " + expected); }
    static D20RollModel Misfortune()
    {
        var model = new D20RollModel { ConsumesEffect = true };
        model.Modifiers.Add(new D20Modifier { FactKey = "misfortune", Source = "misfortune", Rerolls = 1,
            TakeBest = false, Compare = 5, CompareValue = 20, ConsumeWhenApplied = true });
        return model;
    }
    public static void Run()
    {
        var first = Misfortune(); var second = Misfortune();
        var single = D20SaveSequence.Compute(first, 15, 25, false, second, 12, 25);
        Near(single.FirstFailure, .4725, "natural 20-only misfortune first save");
        Near(single.BothFailure, .297, "second save loses the effect only on branches that triggered it");
        var persistent = D20SaveSequence.Compute(first, 15, 25, true, second, 12, 25);
        Near(persistent.FirstFailure, .721125, "persistent reroll sees consumed first-roll sources");
        Near(persistent.BothFailure, .6209325, "persistent two-save chain preserves removal correlation");
        Near(persistent.BothFailure * .8, .496746, "sample spell SR and instant-death branch weight");
        var runtimeMarker = BlueprintReferenceIdentity.Select(Guid.Parse("9661368281d2417e884223bcc74c7f9b"), null);
        first.Complete = second.Complete = D20ActionPolicy.PassiveMarker(runtimeMarker, true, false, false, -1);
        var logged = D20SaveSequence.Compute(first, 17, 25, true, second, 17, 25);
        // Independent raw-die oracle for the 0.5.53 seq242 inputs. These are
        // conditional model values, not observed in-game kill frequencies.
        Near(logged.FirstFailure, .599375, "logged persistent Will with consumable misfortune");
        Near(logged.BothFailure, .3583890625, "logged two-save lifecycle with runtime-only cooldown reference");
        Near(logged.SecondFailureIfReached.Value, .5979379562043794, "second failure conditioned on first failure");
        Near(logged.BothFailure * .85, .304630703125, "logged SR applies once to instant-death branch");
        Near((logged.FirstFailure - logged.BothFailure) * .85, .204838046875, "logged damage-only branch");
        Near(SpellDamageMath.ExpectedLethalSaveHpLoss(.85, logged.FirstFailure,
            logged.SecondFailureIfReached.Value, 225, 10.5), 70.6927076953125, "logged HP expectation includes instant death and damage");
        // Exhaustive independent first/save-repeat oracle with a one-use
        // natural-20 replacement. This enumerates actual dice, not model PMFs.
        for (int bonus = -2; bonus <= 6; bonus++) for (int dc = 8; dc <= 16; dc++)
        {
            double failure = 0;
            void Roll(bool available, Action<int, bool, double> emit)
            {
                for (int die = 1; die <= 20; die++)
                    if (die == 20 && available)
                        for (int reroll = 1; reroll <= 20; reroll++) emit(reroll, false, 1 / 400.0);
                    else emit(die, available, 1 / 20.0);
            }
            bool Failed(int die) => die != 20 && (die == 1 || die + bonus < dc);
            Roll(true, (die, remains, probability) => {
                if (Failed(die)) failure += probability;
                else Roll(remains, (again, unused, later) => { if (Failed(again)) failure += probability * later; });
            });
            Near(D20SaveSequence.Compute(first, bonus, dc, true).FirstFailure, failure, "independent persistent lifecycle oracle");
        }
        var missing = Misfortune(); missing.Complete = false;
        Near(D20SaveSequence.Compute(missing, 15, 25, true) == null ? 1 : 0, 1, "unknown actions cannot produce a sequence");
        // Constant physical DR is a single depletion budget over landed full-
        // attack hits. Independently enumerate d3 damage and hit/miss choices.
        for (int dr = 1; dr <= 6; dr++) for (int bonus = 0; bonus <= 3; bonus++)
        {
            var defense = new DefenseProfile { Complete = true, ClusteredPhysicalDR = true };
            defense.Reduction[DamageKind.Physical] = dr;
            var input = new BetaInput("archer", "skeleton", "bow", "turn", "cluster", 10, 0, 11, 1, 3, bonus,
                20, 1, true, true, true, attackPenalties: new[] { 0, 0 }, defense: defense).WithConcealment(0, "cluster");
            var snapshot = BetaModel.Build(input, "cluster");
            double expected = 0;
            foreach (bool a in new[] { false, true }) foreach (bool b in new[] { false, true })
            for (int da = 1; da <= 3; da++) for (int db = 1; db <= 3; db++)
                expected += Math.Max(0, (a ? da + bonus : 0) + (b ? db + bonus : 0) - dr) / 36.0;
            Near(snapshot.Rows.Sum(row => row.AfterDefense.Expected), expected, "clustered misses do not spend DR and small hits consume only their amount");
            Near(input.Defense.Reduction[DamageKind.Physical], dr, "clustered prediction never mutates defense input");
            Near(SupportedCombatRules.Copy(defense).ClusteredPhysicalDR ? 1 : 0, 1, "feature survives defense copies");
        }
        var shared = new DefenseProfile { Complete = true, ClusteredPhysicalDR = true };
        shared.Reduction[DamageKind.Physical] = 5;
        BetaSnapshot Certain(int damage, int multiplier, int images) => BetaModel.Build(new BetaInput("archer", "skeleton", "bow", "turn", "cluster-certain",
            10, 100, 1, 0, 1, damage, 20, multiplier, true, true, true, attackPenalties: new[] { 0, 0 }, defense: shared)
            .WithConcealment(0, "cluster-certain", images, true), "cluster-certain");
        Near(Certain(8, 1, 1).Rows.Sum(row => row.AfterDefense.Expected), 5,
            "mirror-hit branches do not consume clustered DR before a body hit");
        Near(Certain(4, 3, 0).Rows.Sum(row => row.AfterDefense.Expected), 3.8,
            "clustered critical damage consumes only actual blocked damage");
        shared.Immunities.Add(DamageKind.Physical);
        Near(Certain(8, 3, 0).Rows.Sum(row => row.AfterDefense.Expected), 0, "clustered attack never bypasses physical immunity");
        Console.WriteLine("PASS: " + count + " clustered DR / consumed save assertions");
    }
}
