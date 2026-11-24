using System;
using System.Linq;
using CombatPreview;
static class ShieldApproximationTests
{
    static int count;
    static void Check(bool value, string message) { count++; if (!value) throw new Exception(message); }
    static void Near(double value, double expected, string message)
        => Check(!double.IsNaN(value) && Math.Abs(value - expected) < 1e-8, message + ": " + value + " != " + expected);
    public static void Run()
    {
        var fixedDamage = new[] { new DamageTerm(0, 1, 10, true) { Kind = DamageKind.Physical } };
        var profile = new DefenseProfile { Complete = false, PoolApproximationAllowed = true, PhysicalPool = (8, 5) };
        profile.PhysicalSequenceSources.Add((0, -1, 3)); profile.PhysicalSequenceSources.Add((1, 8, 5));
        var mean = new MeanWeaponShieldApproximation(profile, false);
        Near(mean.Apply(fixedDamage, 1, 1, 0).Expected, 5, "largest protection applies first");
        Near(mean.Apply(fixedDamage, 1, 1, 0).Expected, 7, "constant wins tie after expected pool depletion");
        Near(mean.Apply(fixedDamage, 1, 1, 0).Expected, 7, "tie priority preserves the pool for later");
        Check(profile.PhysicalPool.Value.Remaining == 8, "approximation never consumes live/snapshot pool inputs");
        Check(SupportedCombatRules.Copy(profile).PoolApproximationAllowed, "approximation eligibility survives defensive copies");
        profile.PoolApproximationAllowed = false;
        Check(new MeanWeaponShieldApproximation(profile, false).Apply(fixedDamage, 1, 1, 0) == null,
            "unknown non-pool defense effects are not silently dropped");
        var clustered = new DefenseProfile { Complete = true, PhysicalPool = (100, 10), ClusteredPhysicalDR = true };
        BetaSnapshot Model(DefenseProfile defense) => BetaModel.Build(new BetaInput("archer", "target", "bow", "turn", "shield-test",
            10, 100, 1, 0, 1, 12, 20, 1, true, true, true, attackPenalties: new[] { 0, 0 }, defense: defense)
            .WithConcealment(0, "shield-test", 0, true), "shield-test");
        var model = Model(clustered);
        Check(model.Rows.All(row => row.AfterDefense.Available && row.AfterDefense.Approximate), "clustered plus stoneskin now has labeled numeric rows");
        Near(model.Rows.Sum(row => row.AfterDefense.Expected), 14, "clustered pooled DR is not subtracted anew on every hit");
        clustered.ClusteredPhysicalDR = false;
        Check(Model(clustered).Rows.All(row => row.AfterDefense.Available && !row.AfterDefense.Approximate),
            "already supported exact protection calculation keeps priority");
        for (int remaining = 1; remaining <= 8; remaining++) for (int perHit = 1; perHit <= 8; perHit++)
        foreach (double hit in new[] { 0.0, .25, .5, 1.0 })
        {
            var shield = new DefenseProfile { Complete = true, PhysicalPool = (remaining, perHit) };
            var approximation = new MeanWeaponShieldApproximation(shield, false);
            var terms = new[] { new DamageTerm(1, 6, 0, true) { Kind = DamageKind.Physical } };
            for (int attack = 0; attack < 3; attack++)
            {
                var result = approximation.Apply(terms, 1, hit, 0);
                Check(result.Available && result.Approximate && result.Expected >= 0 && result.Expected <= 3.5 * hit + 1e-9,
                    "mean depletion is finite and bounded by unmitigated expected damage");
            }
        }
        var depleted = new SpellProtection { Order = 0, Remaining = 0, PerHit = 10 };
        SpellRollPart Part(int damage, SpellProtection protection) => new SpellRollPart { Kind = DamageKind.Fire,
            Dice = 0, Sides = 1, Bonus = damage, FailedFactor = 1, PassedFactor = .5, Protections = new[] { protection } };
        var gated = MeanSpellShieldApproximation.Compute(new[] { Part(10, depleted) }, 1, .8, .5, .25, 1000, 0);
        Near(gated.ExpectedDamage, 2.5, "spell attack, SR and save gates are applied once");
        Near(gated.HpLoss, 2.5, "large target HP retains gated mean loss");
        var overkill = MeanSpellShieldApproximation.Compute(new[] { Part(100, depleted) }, 1, .1, 1, 1, 20, 0);
        Near(overkill.HpLoss, 2, "conditional HP cap occurs before hit probability weighting");
        var shared = new SpellProtection { Order = 17, Remaining = 10, PerHit = 10 };
        var fire = Part(8, shared); var cold = Part(8, shared); cold.Kind = DamageKind.Cold;
        var mixed = MeanSpellShieldApproximation.Compute(new[] { fire, cold }, 1, 1, 1, 1, 1000, 0);
        Near(mixed.ExpectedDamage, 6, "same source order shares one residual budget across parts");
        cold.Protections = new[] { new SpellProtection { Order = 18, Remaining = 10, PerHit = 10 } };
        Near(MeanSpellShieldApproximation.Compute(new[] { fire, cold }, 1, 1, 1, 1, 1000, 0).ExpectedDamage, 0,
            "different source orders preserve separate protection budgets");
        var many = new[] { Part(10, new SpellProtection { Order = 1, Remaining = 100, PerHit = 10 }) };
        Check(SpellSequenceMath.Compute(many, 64, 1, 1, 1, 1000, 5) == null, "fixture exceeds exact delivery limit");
        var fallback = MeanSpellShieldApproximation.Compute(many, 64, 1, 1, 1, 1000, 5);
        Near(fallback.ExpectedDamage, 540, "bounded mean fallback still shows long protected sequences");
        Near(fallback.TemporaryHpLoss, 5, "approximate sequence includes expected temp absorption");
        Near(fallback.HpLoss, 535, "temporary HP is consumed separately from protection");
        Check(fallback.Approximate && fallback.Operations == 64, "fallback is explicitly approximate with bounded linear work");
        Check(MeanSpellShieldApproximation.Compute(many, 1, double.NaN, 1, 1, 100, 0) == null,
            "unknown probability is not converted to a guessed number");
        many[0].Kind = DamageKind.Unknown;
        Check(MeanSpellShieldApproximation.Compute(many, 1, 1, 1, 1, 100, 0) == null, "unknown damage type stays unknown");
        Console.WriteLine("PASS: " + count + " shield mean approximation / exact-priority assertions");
    }
}
