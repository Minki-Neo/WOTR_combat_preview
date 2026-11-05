using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CombatPreview;

static class NativeDefenseTests
{
    static int assertions;
    static void Check(bool ok, string why) { assertions++; if (!ok) throw new Exception(why); }
    static void Near(double actual, double expected, string why) { Check(Math.Abs(actual - expected) < 1e-9, why); }
    static BetaInput Input(DefenseProfile defense = null, string stamp = "off")
    { return new BetaInput("actor", "enemy", "weapon", "turn", stamp, 4, 7, 17, 1, 8, 6, 19, 2, true, true, true, defense: defense, modifierNotes: new[] { "fallback PA" }); }
    public static void Run()
    {
        foreach (int bab in new[] { 0, 1, 3, 4, 7, 8, 12, 16, 20, 40 })
        {
            int scale = 1 + bab / 4;
            Check(SupportedCombatRules.AttackModifier(-1, bab, 1, true) == -scale, "PA penalty scales at every BAB multiple of four");
            Check(SupportedCombatRules.DamageModifier(2, bab, 1, true, true, false, false, false, false) == 2 * scale, "one-handed PA damage");
            Check(SupportedCombatRules.DamageModifier(2, bab, 1, true, true, false, true, false, false) == 3 * scale, "two-handed PA damage");
            Check(SupportedCombatRules.DamageModifier(2, bab, 1, true, true, true, true, false, false) == scale, "secondary uses half before two-hand branch");
            Check(SupportedCombatRules.DamageModifier(2, bab, 1, true, true, false, true, true, false) == 5 * scale, "mythic PA two-handed five thirds");
            Check(SupportedCombatRules.DamageModifier(2, bab, 2, true, true, false, true, true, true) == 12 * scale, "greater PA doubles before fact-rank scaling");
            Check(SupportedCombatRules.AttackModifier(-1, bab, 2, false, 3) == -6, "constant ability/fact rank/multiplier");
        }
        for (int immune = 0; immune < 8; immune++)
            for (int form = 0; form < 8; form++)
                Check(SupportedCombatRules.PhysicalImmunity(immune, form, true) == (form != 0 && (form & immune) == form), "physical immunity requires every damage form, combining masks");
        Check(SupportedCombatRules.PhysicalImmunity(1, 0, false) == null, "unknown weapon forms do not silently bypass immunity");
        Check(SupportedCombatRules.PhysicalImmunity(0, 0, false) == false, "no immunity does not require weapon form metadata");
        var physicalImmune = new DefenseProfile { Complete = true, Source = "physical-immunity-fixture" };
        physicalImmune.Immunities.Add(DamageKind.Physical);
        var physicalTerms = new[] { new DamageTerm(1, 8, 10, true) { Kind = DamageKind.Physical } };
        var immuneResult = MitigationMath.Calculate(physicalTerms, 3, .8, .1, physicalImmune);
        Check(immuneResult.Available && immuneResult.Expected == 0 && immuneResult.Normal.Max == 0 && immuneResult.Critical.Max == 0, "physical immunity yields zero even for criticals and minimum physical damage");
        var mixedImmune = MitigationMath.Calculate(new[] { physicalTerms[0], new DamageTerm(1, 6, 0, false) { Kind = DamageKind.Fire } }, 3, .8, .1, physicalImmune);
        Near(mixedImmune.Expected, 2.8, "physical immunity preserves additional fire damage");
        var allImmune = new DefenseProfile { Complete = true, Source = "complete-immunity-fixture" };
        SupportedCombatRules.AddCompleteImmunity(allImmune);
        foreach (DamageKind kind in Enum.GetValues(typeof(DamageKind)))
        {
            if (kind == DamageKind.Unknown) continue;
            Check(MitigationMath.Calculate(new[] { new DamageTerm(1, 6, 4, true) { Kind = kind } }, 2, 1, .1, allImmune).Expected == 0, "complete immunity covers each supported damage type");
        }
        Check(SupportedCombatRules.DefenseStamp(allImmune) != SupportedCombatRules.DefenseStamp(physicalImmune), "immunity change invalidates defense cache");
        Check(!SupportedCombatRules.Bypass(false, new bool?[0]).Value, "DR dash always applies");
        Check(SupportedCombatRules.Bypass(true, new bool?[] { false, true, null }) == true, "OR accepts a deciding known bypass");
        Check(SupportedCombatRules.Bypass(false, new bool?[] { false, true, null }) == false, "AND deciding mismatch keeps DR even with unknown other clause");
        Check(SupportedCombatRules.Bypass(false, new bool?[] { true, null }) == null, "AND unresolved clause never assumes bypass");
        Check(SupportedCombatRules.Bypass(true, new bool?[] { false, null }) == null, "OR unresolved clause never assumes DR");
        Check(SupportedCombatRules.Bypass(false, new bool?[] { true, true }) == true, "AND bypass requires both");
        Check(SupportedCombatRules.MaskBypass(3, 1, true) == true, "any intersecting form inside one flag mask bypasses");
        Check(SupportedCombatRules.MaskBypass(4, 1, true) == false && SupportedCombatRules.MaskBypass(4, 4, false) == null, "unknown masks retain uncertainty");
        for (int e = 0; e <= 6; e++)
        {
            Check(SupportedCombatRules.MaterialBypass(1, 0, e, 3, 4, true) == (e >= 3), "+3 bypasses cold iron");
            Check(SupportedCombatRules.MaterialBypass(2, 0, e, 3, 4, true) == (e >= 3), "+3 bypasses silver");
            Check(SupportedCombatRules.MaterialBypass(4, 0, e, 3, 4, true) == (e >= 4), "+4 bypasses adamantite");
            Check(SupportedCombatRules.AlignmentBypass(1, 0, e, true) == (e >= 5), "+5 total bypasses alignment");
        }
        Check(SupportedCombatRules.MaterialBypass(4, 4, 0, 3, 4, true) == true, "actual material works without enhancement");
        Check(SupportedCombatRules.AlignmentBypass(3, 2, 0, true) == true, "actual alignment mask intersects");
        var defense = new DefenseProfile { Complete = true, Source = "supported-current-DR" };
        SupportedCombatRules.AddReduction(defense, DamageKind.Physical, 2);
        SupportedCombatRules.AddReduction(defense, DamageKind.Physical, 5);
        SupportedCombatRules.AddReduction(defense, DamageKind.Physical, 3);
        Check(defense.Reduction[DamageKind.Physical] == 5, "nonstacking physical DR chooses maximum, not sum");
        var input = Input(defense);
        defense.Reduction[DamageKind.Physical] = 99;
        Check(input.Defense.Reduction[DamageKind.Physical] == 5, "input takes an immutable defense snapshot");
        var copy = input.Defense; copy.Reduction.Clear();
        Check(input.Defense.Reduction[DamageKind.Physical] == 5, "defense getter cannot mutate input snapshot");
        var model = BetaModel.Build(input, "dr5"); var row = model.Rows[0];
        Check(row.AfterDefense.Available, "automatic DR is connected to visible beta rows");
        Near(row.AfterDefense.Normal.Mean, 5.5, "1d8+6 minus DR5 average");
        Near(row.AfterDefense.Critical.Mean, 16, "DR applied once after critical multiplication, not multiplied");
        Near(row.AfterDefense.Expected, DamageMath.Expected(row.Hit, row.Crit, 5.5, 16), "after-defense expected damage includes hit and critical probabilities");
        Check(row.AfterDefense.Expected < row.ExpectedRaw, "DR reduces expectation");
        var zero = BetaModel.Build(Input(new DefenseProfile { Complete = true, Source = "DR bypassed" }), "bypass");
        Near(zero.Rows[0].AfterDefense.Expected, zero.Rows[0].ExpectedRaw.Value, "successful bypass retains raw mean");
        var pool = new DefenseProfile { Complete = true, HasConsumablePool = true, Source = "pool" };
        Check(!BetaModel.Build(Input(pool), "pool").Rows[0].AfterDefense.Available, "pool never mistaken for static DR");
        var unknown = new DefenseProfile { Complete = false, UnavailableReason = "unknown bypass" };
        Check(BetaModel.Build(Input(unknown), "unknown").Rows[0].AfterDefense.Reason == "unknown bypass", "unresolved defense keeps explicit reason");
        var energy = new DefenseProfile { Complete = true, Source = "mixed" };
        SupportedCombatRules.AddReduction(energy, DamageKind.Physical, 5); SupportedCombatRules.AddReduction(energy, DamageKind.Fire, 10);
        energy.Immunities.Add(DamageKind.Electricity);
        var terms = new[] { new DamageTerm(1, 8, 6, true) { Kind = DamageKind.Physical },
            new DamageTerm(1, 6, 0, false) { Kind = DamageKind.Fire }, new DamageTerm(1, 6, 0, false) { Kind = DamageKind.Fire },
            new DamageTerm(3, 6, 0, false) { Kind = DamageKind.Electricity } };
        var mixed = MitigationMath.Calculate(terms, 2, 1, 0, energy);
        Near(mixed.Normal.Mean, 5.5 + 4.0 / 36, "same-energy components share one resistance; electrical immunity independent");
        var nativeTerms = new[] { new DamageComponentSnapshot(new DamageTerm(1, 8, 12, true) { Kind = DamageKind.Physical }, false, null) };
        var nativeNotes = new[] { "engine total, includes arbitrary feat/mod effect" };
        var native = new NativeCombatValues(5, 17, 3, nativeTerms, nativeNotes);
        nativeTerms[0] = null; nativeNotes[0] = "changed";
        var applied = native.Apply(input);
        Check(applied.AttackBonus == 5 && applied.Bonus == 12 && applied.CriticalEdge == 17 && applied.Multiplier == 3, "engine totals replace fallback numbers including crit properties");
        Check(applied.AttackObserved && !applied.PairObserved && applied.DamageObserved, "target-independent AB never mislabeled as final target AC");
        Check(applied.ModifierNotes[0] != "changed" && !applied.ModifierNotes.Contains("fallback PA"), "engine modifier evidence copied and fallback bonuses never reapplied");
        Check(applied.AttackPenalties.SequenceEqual(input.AttackPenalties), "engine totals preserve the attack pattern");
        Near(BetaModel.Build(applied, "native-dr").Rows[0].AfterDefense.Normal.Mean, 11.5, "engine damage and automatic DR compose end to end");
        var damageOnly = new NativeCombatValues(null, 19, 2, native.Components, null).Apply(input);
        Check(!damageOnly.AttackObserved && damageOnly.DamageObserved && damageOnly.AttackBonus == input.AttackBonus, "damage-only engine result does not pretend observed accuracy");
        var pending = new BetaInput("actor", "enemy", "weapon", "turn", "on", 4, 5, 17, 1, 8, 12, 19, 2, true, true, true,
            defense: input.Defense, pendingAttack: -2, pendingDamage: 6);
        var pendingNative = new NativeCombatValues(7, 19, 2, new[] { new DamageComponentSnapshot(new DamageTerm(1, 8, 6, true) { Kind = DamageKind.Physical }, false, null) }, new[] { "engine before attack buff starts" });
        var withPending = pendingNative.Apply(pending);
        Check(withPending.AttackBonus == 5 && withPending.Bonus == 12, "deferred power attack applies once on top of engine output before attack-start buff exists");
        Check(!withPending.AttackObserved && !withPending.DamageObserved, "engine plus deferred projection is visibly an estimate rather than an observed final total");
        var pendingDamageOnly = new NativeCombatValues(null, 19, 2, pendingNative.Components, null).Apply(pending);
        Check(pendingDamageOnly.AttackBonus == 5 && pendingDamageOnly.Bonus == 12, "damage-only engine output does not reapply pending AB already in fallback");
        var afterStarted = new NativeCombatValues(5, 19, 2, withPending.Components, null).Apply(input);
        Check(afterStarted.AttackBonus == 5 && afterStarted.Bonus == 12, "when the engine includes active buff no pending delta is added");
        var multiPhysical = new BetaInput("actor", "enemy", "weapon", "turn", "mixed", 4, 7, 17, 1, 8, 6, 19, 2, true, true, true,
            new[] { new DamageComponentSnapshot(new DamageTerm(1, 8, 6, true) { Kind = DamageKind.Physical }, false, null), new DamageComponentSnapshot(new DamageTerm(1, 6, 0, false) { Kind = DamageKind.Physical }, false, null) }, defense: input.Defense);
        Check(!BetaModel.Build(multiPhysical, "mixed").Rows[0].AfterDefense.Available, "different physical bypass groups never silently merge under one weapon profile");
        var changedDefense = new DefenseProfile { Complete = true, Source = "same snapshot defense changed" };
        Check(native.Apply(input, changedDefense).Signature != native.Apply(input, input.Defense).Signature, "native model cache invalidates when effective bypass/defense changes without AB/dice change");
        var cache = new NativeCombatValueCache();
        cache.Put("off|actor|weapon|turn|grip|rank", native, 100);
        Check(cache.Read("off|actor|weapon|turn|grip|rank", 100) != null, "fresh native engine values available");
        foreach (string stamp in new[] { "on", "other actor", "other weapon", "other turn", "other grip", "new rank" })
            Check(cache.Read(stamp, 101) == null, "context/toggle mismatch does not reuse native result");
        Check(cache.Read("off|actor|weapon|turn|grip|rank", 99) == null && cache.Read("off|actor|weapon|turn|grip|rank", 1601) == null, "native cache has bounded lifetime and monotonic clock checks");
        cache.Put("off|actor|weapon|turn|grip|rank", new NativeCombatValues(null, native.Edge, native.Multiplier, native.Components, null, native.Enhancement, native.EnhancementTotal, native.Materials, native.Alignment, native.Form, native.TraitsKnown), 101);
        Check(cache.Read("off|actor|weapon|turn|grip|rank", 101).AttackBonus == 5, "weapon-stat callback cannot erase a just completed AB calculation");
        Check(cache.ReadStatus("off|actor|weapon|turn|grip|rank", 1601) == "Expired", "identical callbacks do not extend old AB lifetime");
        var changedDice = new NativeCombatValues(null, 20, 2, new[] { new DamageComponentSnapshot(new DamageTerm(1, 4, 3, true) { Kind = DamageKind.Physical }, false, null) }, null);
        cache.Put("off|actor|weapon|turn|grip|rank", changedDice, 102);
        Check(cache.Read("off|actor|weapon|turn|grip|rank", 102).Components[0].Sides == 4, "new completed weapon dice replace older AB-associated dice");
        Check(!cache.Read("off|actor|weapon|turn|grip|rank", 102).AttackBonus.HasValue, "changed weapon stats never inherit an unrelated AB total");
        Check(cache.ReadStatus("other", 102) == "ContextMiss" && cache.ReadStatus("off|actor|weapon|turn|grip|rank", 101) == "ClockRegression", "cache miss and clock regression are distinguished");
        cache.Clear(); Check(cache.Read("off|actor|weapon|turn|grip|rank", 101) == null, "disable/scene clear drops native results");
        Check(cache.ReadStatus("empty", 103) == "NotObserved", "empty cache is distinct from changed context");
        var log = new List<string>(); var recorder = new DiagnosticRecorder(log.Add, () => 1, "fixture") { Enabled = true };
        recorder.RecordBetaPreview(model, nativeCacheStatus: "Expired"); recorder.RecordBetaPreview(BetaModel.Build(applied, "engine"));
        using (var json = JsonDocument.Parse(log.Single(s => s.Contains("\"previewId\":\"dr5\""))))
        {
            var root = json.RootElement; Check(root.GetProperty("nativeCacheStatus").GetString() == "Expired", "cache rejection reason is serialized"); Check(root.GetProperty("afterDefenseAvailable").GetBoolean(), "DR availability logged truthfully");
            Check(!root.GetProperty("fullConditionsVerified").GetBoolean(), "supported DR model does not certify all combat/mod conditions");
            Near(root.GetProperty("rows")[0].GetProperty("expectedAfterDefense").GetDouble(), row.AfterDefense.Expected, "logged expectation equals shown beta row calculation");
        }
        using (var json = JsonDocument.Parse(log.Single(s => s.Contains("\"previewId\":\"engine\""))))
        { Check(json.RootElement.GetProperty("attackObserved").GetBoolean() && !json.RootElement.GetProperty("acObserved").GetBoolean(), "mixed engine/estimated fields are distinct in diagnostics"); }
        Console.WriteLine("PASS: " + assertions + " native-result/active-modifier/automatic-DR assertions (not game execution)");
    }
}
