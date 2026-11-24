using System.Collections.Generic;
using System;
using System.Linq;
using CombatPreview;

static class ActiveProbabilityTests
{
    static int count;
    static void Near(double actual, double expected, string name)
    { count++; if (double.IsNaN(actual) || Math.Abs(actual - expected) > 1e-9) throw new Exception(name + ": " + actual + " != " + expected); }
    static D20RollModel Model(int rerolls, bool best, bool failed = false) {
        var model = new D20RollModel();
        model.Modifiers.Add(new D20Modifier { Rerolls = rerolls, TakeBest = best, OnlyIfFailed = failed }); return model;
    }
    sealed class RuntimeGuidFixture { public Guid m_Guid; }
    sealed class ReferenceFixture { public string guid; public RuntimeGuidFixture deserializedGuid; }
    public static void Run()
    {
        // Independent enumeration of the actual dice, including natural 1/20,
        // tests the active weapon/touch/save/SR API rather than only old Pmf.
        for (int bonus = -5; bonus <= 15; bonus += 5)
        for (int dc = 1; dc <= 36; dc += 5)
        foreach (bool best in new[] { false, true })
        foreach (bool conditional in new[] { false, true })
        {
            var model = Model(1, best, conditional);
            double attacks = 0, saveSuccess = 0, sr = 0;
            for (int a = 1; a <= 20; a++) for (int b = 1; b <= 20; b++)
            {
                bool FirstAttack(int die) => die == 20 || die > 1 && die + bonus >= dc;
                int Pick(int die) => best ? Math.Max(die, b) : Math.Min(die, b);
                int attackDie = conditional && FirstAttack(a) ? a : Pick(a);
                if (FirstAttack(attackDie)) attacks += 1 / 400.0;
                // The game's conditional save reroll tests before StatValue is read.
                int saveDie = conditional && (a == 20 || a > 1 && a >= dc) ? a : Pick(a);
                if (saveDie == 20 || saveDie > 1 && saveDie + bonus >= dc) saveSuccess += 1 / 400.0;
                if (Pick(a) + bonus >= dc) sr += 1 / 400.0;
            }
            Near(model.Attack(bonus, dc, false), attacks, "conditional attack enumeration");
            Near(model.SaveFailure(bonus, dc, false), 1 - saveSuccess, "conditional save enumeration");
            Near(model.SaveFailure(bonus, dc, true), 1 - saveSuccess * saveSuccess, "persistent plus reroll");
            Near(model.Resistance(bonus, dc), sr, "SR lacks natural-1/20 and successable conditional gate");
        }
        var favorable = new D20RollModel(); favorable.AfterRoll.Add(1);
        Near(favorable.Resistance(0, 11), .75, "Favorable Magic SR");
        favorable.AfterRoll.Clear(); favorable.AfterRoll.Add(-1);
        Near(favorable.SaveFailure(0, 11, false), .75, "Favorable Magic enemy save");
        var sequential = Model(1, true); sequential.AfterRoll.Add(-1);
        Near(sequential.Resistance(0, 11), .375, "opposing after-roll is not pre-roll cancellation");
        var cancelled = Model(1, true); cancelled.Modifiers.Add(new D20Modifier { Rerolls = 1, TakeBest = false });
        Near(cancelled.Resistance(0, 11), .5, "opposing before-roll cancellation");
        sequential = Model(1, false); sequential.AfterRoll.Add(1); sequential.AfterRoll.Add(1);
        Near(sequential.Resistance(0, 11), .5625, "after-roll signed count crosses zero");
        var fixedRoll = new D20RollModel(); fixedRoll.Modifiers.Add(new D20Modifier { Replace = true, Roll = 20 }); fixedRoll.AfterRoll.Add(-1);
        Near(fixedRoll.Resistance(0, 20), 1, "Override survives post-roll modification");
        fixedRoll.Modifiers.Add(new D20Modifier { Replace = true, Roll = 1, OnlyIfFailed = true });
        Near(fixedRoll.Attack(0, 11, false), 1, "later conditional handler sees preceding override instead of raw first die");
        var mixed = Model(1, true); mixed.Modifiers[0].Chance = 50;
        Near(mixed.Resistance(0, 11), .625, "chance-weighted lucky activation");
        foreach (bool best in new[] { false, true })
        foreach (int threshold in new[] { 2, 11, 20, 21 })
        {
            var trickster = Model(1, best); trickster.ReplaceOneWithMax = true;
            double oracle = 0;
            for (int a = 1; a <= 20; a++) for (int b = 1; b <= 20; b++)
            {
                // The game compares the initial raw die to a transformed new die,
                // then its Result getter transforms a retained raw 1.
                int rolled = b == 1 ? 20 : b;
                int kept = best ? Math.Max(a, rolled) : Math.Min(a, rolled);
                if ((kept == 1 ? 20 : kept) >= threshold) oracle += 1 / 400.0;
            }
            Near(trickster.Resistance(0, threshold), oracle, "Trickster plus reroll raw/result order");
            Near(D20.Outcomes(trickster.Modifiers, die => true, null, true).Sum(outcome => outcome.P), 1, "transformed probability mass");
        }
        var naturalOne = new D20RollModel { ReplaceOneWithMax = true };
        Near(naturalOne.Attack(0, 20, false), .1, "natural 1 and 20 both hit");
        Near(RuleBonusMath.Sum(new[] { new ProjectedModifier { Descriptor = 1, Value = -5 }, new ProjectedModifier { Descriptor = 1, Value = -2 } }), -2,
            "DC/CL nonstacking negative bonus takes highest rather than stat penalty rule");
        Near(RuleBonusMath.Sum(new[] { new ProjectedModifier { Descriptor = 1, Value = 3 }, new ProjectedModifier { Descriptor = 1, Value = -2 },
            new ProjectedModifier { Descriptor = 25, Value = 1, Stacks = true }, new ProjectedModifier { Descriptor = 25, Value = 2, Stacks = true } }), 6,
            "DC/CL positive competes with same descriptor and untyped bonuses stack");
        BetaInput Input(string actor, string weapon, D20RollModel rolls) => new BetaInput(actor, "enemy", weapon, "turn", weapon,
            1, 0, 11, 1, 6, 0, 20, 2, false, true, false, defense: new DefenseProfile { Complete = true })
            .WithConcealment(0, weapon).WithRollModel(rolls, weapon);
        var ordinary = BetaModel.Build(Input("actor", "one", Model(1, true)), "one");
        Near(ordinary.Rows[0].Hit, .75, "active Beta weapon path uses advantage");
        var consuming = Model(1, true); consuming.ConsumesEffect = true;
        var one = BetaModel.Build(Input("actor", "one", consuming), "one");
        var two = BetaModel.Build(Input("actor", "two", consuming), "two");
        var chain = BetaModel.BuildSequence(new[] { one, two });
        Near(chain[0].Rows[0].RollProbabilityAvailable ? 1 : 0, 1, "first consuming roll known");
        Near(chain[1].Rows[0].RollProbabilityAvailable ? 1 : 0, 0, "second weapon cannot reuse consumed source");
        Near(chain[0].Rows[0].AfterDefense.Available ? 1 : 0, 0, "consuming confirmation uncertainty not a damage value");
        var unknown = new D20RollModel { Complete = false };
        one = BetaModel.Build(Input("actor", "one", unknown).WithConcealment(0, "one", 1), "one");
        two = BetaModel.Build(Input("friend", "two", Model(1, true)).WithConcealment(0, "two", 1), "two");
        chain = BetaModel.BuildSequence(new[] { one, two });
        Near(chain[1].Rows[0].RollProbabilityAvailable ? 1 : 0, 0, "unknown first hit invalidates later mirror state");
        Near(chain[1].Rows[0].ExpectedRaw.HasValue ? 1 : 0, 0, "no invented fair-die raw damage fallback");
        var readiness = new PreviewReadiness(10, 20);
        var state = readiness.Observe(10, false, false);
        Near(state["firstParametersReadyMs"] == null ? 1 : 0, 1, "not ready is not zero latency");
        state = readiness.Observe(10.2, true, false);
        Near((double)state["firstParametersReadyMs"], 200, "parameter latency");
        state = readiness.Observe(10.35, true, true);
        Near((double)state["firstHpProjectionReadyMs"], 350, "projection latency");
        state = readiness.Observe(10.5, false, false);
        Near((int)state["readinessRegressions"], 1, "ready-to-missing regression");
        state = readiness.Observe(11, true, true);
        Near((double)state["firstHpProjectionReadyMs"], 350, "first ready time survives rehover refresh");
        foreach (int bit in new[] { 0, 8, 31, 40, 63 })
        {
            ulong flag = 1UL << bit;
            Near(SpellParameterRules.DescriptorMatches(flag, flag) ? 1 : 0, 1, "descriptor high-bit match");
            Near(SpellParameterRules.DescriptorMatches(flag, ~flag) ? 1 : 0, 0, "unrelated descriptors do not match");
            Near(SpellParameterRules.DescriptorMatches(flag | 2, flag) ? 1 : 0, 1, "any descriptor suffices");
        }
        Near(SpellParameterRules.DescriptorMatches(0, ulong.MaxValue) ? 1 : 0, 0, "missing descriptor is not all");
        string selectedStat = null;
        Near(SpellParameterRules.TrySelectStat(ref selectedStat, "Intelligence") ? 1 : 0, 1, "replacement stat");
        Near(SpellParameterRules.TrySelectStat(ref selectedStat, "Intelligence") ? 1 : 0, 1, "same replacement is order-independent");
        Near(SpellParameterRules.TrySelectStat(ref selectedStat, "Charisma") ? 1 : 0, 0, "conflicting order is unknown");
        Near(selectedStat == "Intelligence" ? 1 : 0, 1, "rejected replacement leaves snapshot intact");
        Near(SpellParameterRules.TrySelectStat(ref selectedStat, "SaveWill") ? 1 : 0, 0, "noncasting stat rejected");
        // AfterDeserialize and runtime-created references retain only this Guid.
        var markerGuid = Guid.Parse("9661368281d2417e884223bcc74c7f9b");
        var staleGuid = Guid.Parse("82eaed601b044a55a7b2851c5451f386");
        Near(BlueprintReferenceIdentity.Select(markerGuid, null) == markerGuid ? 1 : 0, 1, "deserialized cooldown identity survives cleared text");
        Near(BlueprintReferenceIdentity.Select(markerGuid, staleGuid.ToString()) == markerGuid ? 1 : 0, 1, "runtime identity wins stale serialized text");
        Near(BlueprintReferenceIdentity.Select(Guid.Empty, markerGuid.ToString()).HasValue ? 1 : 0, 0, "runtime empty is authoritative");
        Near(BlueprintReferenceIdentity.Select(null, markerGuid.ToString("D")) == markerGuid ? 1 : 0, 1, "legacy serialized identity");
        Near(BlueprintReferenceIdentity.Select(null, "invalid").HasValue ? 1 : 0, 0, "invalid reference is not a cooldown marker");
        Near(BlueprintReferenceIdentity.Select(null, null).HasValue ? 1 : 0, 0, "missing identity stays unknown");
        object Field(object instance, string field) => instance?.GetType().GetField(field,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)?.GetValue(instance);
        var runtimeReference = new ReferenceFixture { guid = null, deserializedGuid = new RuntimeGuidFixture { m_Guid = markerGuid } };
        Near(BlueprintReferenceIdentity.Read(runtimeReference, Field) == markerGuid ? 1 : 0, 1, "production field traversal with cleared serialized guid");
        Near(runtimeReference.guid == null && runtimeReference.deserializedGuid.m_Guid == markerGuid ? 1 : 0, 1, "identity read never fills or changes a reference");
        Near(BlueprintReferenceIdentity.Read(null, Field).HasValue ? 1 : 0, 0, "null reference traversal");
        var resolvedMarker = BlueprintReferenceIdentity.Read(runtimeReference, Field);
        Near(D20ActionPolicy.PassiveMarker(resolvedMarker, true, false, false, -1) ? 1 : 0, 1, "uncached runtime cooldown is a passive followup");
        Near(D20ActionPolicy.PassiveMarker(staleGuid, true, false, false, -1) ? 1 : 0, 1, "fortune cooldown is also audited");
        Near(D20ActionPolicy.PassiveMarker(resolvedMarker, true, true, true, 1) ? 1 : 0, 0, "loaded modified marker overrides vanilla metadata");
        Near(D20ActionPolicy.PassiveMarker(resolvedMarker, true, true, false, 0) ? 1 : 0, 0, "loaded nonbuff is never a passive buff");
        Near(D20ActionPolicy.PassiveMarker(resolvedMarker, false, false, false, -1) ? 1 : 0, 0, "unrecognized game build cannot use audited unloaded metadata");
        Near(D20ActionPolicy.PassiveMarker(Guid.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"), true, false, false, -1) ? 1 : 0, 0, "arbitrary unloaded buff remains unsupported");
        Near(D20ActionPolicy.PassiveMarker(null, true, false, false, -1) ? 1 : 0, 0, "missing identity is unknown");
        Near(D20ActionPolicy.PassiveMarker(null, true, true, true, 0) ? 1 : 0, 1, "loaded empty marker can be read without reference identity");
        var fair = new D20RollModel();
        Near(fair.Resistance(20, 20) == 1 ? 1 : 0, 1, "SR certainty remains exactly one");
        Near(fair.Resistance(0, 100) == 0 ? 1 : 0, 1, "impossible SR remains exactly zero");
        Near(fair.SaveFailure(100, 0, true) >= 0 ? 1 : 0, 1, "save result cannot have negative probability");
        var noExpanded = new System.Collections.Generic.HashSet<int>();
        Near(SpellSchoolMath.Focus(2, 3, new[] { 3 }, noExpanded), 4, "same-school mythic focus doubles");
        Near(SpellSchoolMath.Focus(2, 3, new[] { 2 }, noExpanded), 2, "different-school mythic focus does not double");
        var expandedSchools = new System.Collections.Generic.HashSet<int> { 3 };
        Near(SpellSchoolMath.Focus(2, 3, new[] { 2 }, expandedSchools), 4, "expanded runtime context accepts other mythic focus school");
        Near(SpellSchoolMath.Focus(2, 3, Array.Empty<int>(), expandedSchools), 2, "expanded arsenal alone is not mythic focus");
        Near(SpellSchoolMath.Focus(2, 3, new[] { 2, 3 }, expandedSchools), 4, "multiple mythic focus facts do not multiply repeatedly");
        Near(SpellSchoolMath.Transfer(3, new[] { (1, 1), (1, 2), (2, 2), (3, 100) }), 3, "sum per donor school then max; own school excluded");
        Near(SpellSchoolMath.Transfer(3, new[] { (1, 2), (2, 2) }), 2, "other donor schools are not summed together");
        Near(SpellSchoolMath.Transfer(3, new[] { (3, 4) }), 0, "own-school focus is not transferred back");
        Near(SpellSchoolMath.Transfer(3, Array.Empty<(int, int)>()), 0, "no donors");
        Near(SpellSchoolMath.Transfer(3, new[] { (1, -3), (2, -2) }), -2, "negative donor totals preserve engine maximum semantics");
        for (int target = 1; target <= 3; target++)
        for (int firstBonus = -1; firstBonus <= 3; firstBonus++)
        for (int secondBonus = -1; secondBonus <= 3; secondBonus++)
        {
            var donations = new[] { (School: 1, Bonus: firstBonus), (School: 1, Bonus: secondBonus), (School: 2, Bonus: 2), (School: 3, Bonus: 1) };
            int oracle = target == 1 ? 2 : target == 2 ? Math.Max(firstBonus + secondBonus, 1) : Math.Max(firstBonus + secondBonus, 2);
            Near(SpellSchoolMath.Transfer(target, donations), oracle, "independent donor-school totals");
            Near(SpellSchoolMath.Transfer(target, donations.Reverse()), oracle, "feature order does not change transfer");
        }
        Near(SpellParameterRules.InheritedParametersValid(25, 14, 128, 128) ? 1 : 0, 1, "inherited persistent parameters retain known meta");
        Near(SpellParameterRules.InheritedParametersValid(25, 14, 128, 0) ? 1 : 0, 0, "inherited metamagic mismatch cannot use selected damage model");
        Near(SpellParameterRules.InheritedParametersValid(25, 0, 0, 0) ? 1 : 0, 0, "uninitialized source caster level rejected");
        Near(SpellParameterRules.InheritedParametersValid(-1, 14, 0, 0) ? 1 : 0, 0, "uninitialized source DC rejected");
        object ContextField(object instance, string name) => instance is System.Collections.Generic.Dictionary<string, object> fields
            && fields.TryGetValue(name, out var value) ? value : null;
        var parentContext = new System.Collections.Generic.Dictionary<string, object> { ["m_Params"] = "parent" };
        var childContext = new System.Collections.Generic.Dictionary<string, object> { ["<ParentContext>k__BackingField"] = parentContext };
        Near(Equals(ContextParameterRules.Read(childContext, ContextField), "parent") ? 1 : 0, 1, "context uses nearest inherited stored parameters");
        childContext["m_Params"] = "own";
        Near(Equals(ContextParameterRules.Read(childContext, ContextField), "own") ? 1 : 0, 1, "own stored parameters precede parent");
        childContext.Remove("m_Params"); parentContext.Remove("m_Params"); parentContext["<ParentContext>k__BackingField"] = childContext;
        Near(ContextParameterRules.Read(childContext, ContextField) == null ? 1 : 0, 1, "cyclic context cannot hang preview");
        Near(ContextParameterRules.Read(null, ContextField) == null ? 1 : 0, 1, "missing context stays unknown");
        ContextParameterSnapshot? replacement = null;
        var savedContext = new ContextParameterSnapshot { Dc = 25, Level = 14, SpellLevel = 4, Metamagic = 128 };
        Near(ContextParameterRules.Merge(ref replacement, savedContext, 128) ? 1 : 0, 1, "known context replacement");
        Near(ContextParameterRules.Merge(ref replacement, savedContext, 128) ? 1 : 0, 1, "identical overrides do not depend on event order");
        var conflictingContext = savedContext; conflictingContext.Dc = 24;
        Near(ContextParameterRules.Merge(ref replacement, conflictingContext, 128) ? 1 : 0, 0, "conflicting context overrides require observed input");
        Near(replacement.Value.Dc, 25, "rejected context does not change prior snapshot");
        replacement = null;
        Near(ContextParameterRules.Merge(ref replacement, savedContext, 0) ? 1 : 0, 0, "new context metamagic cannot silently use old damage model");
        Near(ContextParameterRules.Merge(ref replacement, savedContext, 129) ? 1 : 0, 1, "context metamagic already included in selection");
        replacement = null; var invalidContext = savedContext; invalidContext.Level = 0;
        Near(ContextParameterRules.Merge(ref replacement, invalidContext, 128) ? 1 : 0, 0, "uninitialized context level rejected");
        Near(SpellTargetEligibility.Allows(0, false, true, false, true, true, false, false, false) ? 1 : 0, 0, "enemy-only area excludes allies before calculation");
        Near(SpellTargetEligibility.Allows(0, true, false, false, true, true, false, false, false) ? 1 : 0, 1, "enemy-only area keeps enemies");
        Near(SpellTargetEligibility.Allows(2, false, true, false, true, true, false, false, false) ? 1 : 0, 1, "friendly-fire area must retain ally damage");
        Near(SpellTargetEligibility.Allows(1, false, true, false, true, false, true, true, false) ? 1 : 0, 1, "allied healing/buff area retained");
        Near(SpellTargetEligibility.Allows(null, false, true, false, false, true, false, false, false) ? 1 : 0, 0, "direct hostile-only spell cannot re-add ally through hover fallback");
        Near(SpellTargetEligibility.Allows(null, false, false, true, false, false, true, false, false) ? 1 : 0, 0, "direct self targeting obeys self flag");
        Near(SpellTargetEligibility.Allows(null, false, true, false, true, true, false, false, false) ? 1 : 0, 1, "unknown point delivery is retained conservatively");
        Near(SpellTargetEligibility.Allows(0, false, true, false, true, true, false, false, true) ? 1 : 0, 1, "composite/unknown delivery is not pruned by one component");
        var enemyQueue = new PreviewWorkQueue<string>();
        var candidates = Enumerable.Range(0, 40).Select(index => "ally" + index).Concat(new[] { "enemy" });
        var eligible = candidates.Where(name => SpellTargetEligibility.Allows(0, name == "enemy", name != "enemy", false, true, true, false, false, false)).Take(32);
        enemyQueue.Begin(eligible, "ally0");
        Near(enemyQueue.PendingCount, 1, "filter runs before target cap so allies cannot crowd out enemies");
        Near(enemyQueue.Take(out var keptTarget) && keptTarget == "enemy" ? 1 : 0, 1, "priority cannot re-add excluded ally");
        var hpBranch = new System.Collections.Generic.Dictionary<string, object> { ["expectedHpLoss"] = 12.0 };
        var hpMetadata = new System.Collections.Generic.Dictionary<string, object> {
            ["stagedOutcome"] = new System.Collections.Generic.Dictionary<string, object> { ["expectedHpLoss"] = null }, ["branchPreview"] = hpBranch };
        var projectedHp = HealthProjectionSelection.Select(hpMetadata, 50, 100, 0, out var authoritativeHp, out var approximateHp, out var healthBasis);
        Near(projectedHp.Value, 12, "missing stage HP does not mask valid full branch HP");
        Near(authoritativeHp && !approximateHp ? 1 : 0, 1, "valid distribution retains exact priority");
        hpBranch["expectedHpLoss"] = null; hpBranch["expectedDamage"] = 20.0;
        projectedHp = HealthProjectionSelection.Select(hpMetadata, 50, 100, 5, out _, out approximateHp, out _);
        Near(projectedHp.Value, 15, "known full expectation gets immediate temporary-HP mean display");
        Near(approximateHp ? 1 : 0, 1, "mean-state estimate is explicitly approximate");
        hpMetadata["secondaryTargetEffects"] = new System.Collections.Generic.Dictionary<string, object> { ["status"] = "Unmodeled" };
        Near(HealthProjectionSelection.Select(hpMetadata, 50, 100, 0, out _, out _, out _).HasValue ? 1 : 0, 0, "unknown collateral cannot become invented HP");
        hpMetadata.Remove("secondaryTargetEffects"); hpBranch.Remove("expectedDamage");
        Near(HealthProjectionSelection.Select(hpMetadata, 50, 100, 0, out _, out _, out _).HasValue ? 1 : 0, 0, "unknown lethal odds do not use damage-only fallback");
        Near(HealthProjectionSelection.MeanLoss(50, 100, 30, 20).Value, 0, "mean below temporary HP does not reduce main HP");
        Near(HealthProjectionSelection.MeanLoss(50, 100, 0, 200).Value, 50, "mean projection capped at current HP");
        Near(HealthProjectionSelection.MeanLoss(50, 100, 0, double.NaN).HasValue ? 1 : 0, 0, "invalid mean rejected");
        Near(HealthProjectionSelection.MeanTemporaryLoss(30, 20).Value, 20, "mean temp absorption capped separately");
        Near(HealthProjectionSelection.MeanTemporaryLoss(30, 80).Value, 30, "temp cannot absorb above its budget");
        Near(HealthProjectionSelection.MeanTemporaryLoss(30, double.NaN).HasValue ? 1 : 0, 0, "unknown temp mean stays unknown");
        var temporaryMetadata = new Dictionary<string, object> { { "branchPreview", new Dictionary<string, object> {
            { "expectedTemporaryHpLoss", 2.5 }, { "expectedHpLoss", 10.0 } } } };
        Near(HealthProjectionSelection.TemporaryLoss(temporaryMetadata, 5, "conditional-action-tree-distribution").Value, 2.5,
            "branch temp uses exact weighted absorption, including bypassing death branches");
        Near(HealthProjectionSelection.TemporaryLoss(temporaryMetadata, 5, "secondary-effects-unresolved").HasValue ? 1 : 0, 0,
            "unknown secondary effects cannot certify temp absorption");
        Near(PanelPresentation.HealthLossPixels(.05f, 100), 1.5, "positive subpixel loss remains visible");
        Near(PanelPresentation.HealthLossPixels(0, 100), 0, "zero damage has no loss marker");
        Near(PanelPresentation.HealthLossPixels(.05f, .5f), .5, "marker cannot extend past current HP");
        Console.WriteLine("PASS: " + count + " active probability / readiness assertions");
    }
}
