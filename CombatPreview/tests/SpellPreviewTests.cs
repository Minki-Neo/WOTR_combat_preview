using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CombatPreview;
static class SpellPreviewTests
{
    static int count;
    static void Check(bool value, string reason) { count++; if (!value) throw new Exception(reason); }
    static void Near(double actual, double expected, string reason) { Check(Math.Abs(actual - expected) < 1e-10, reason); }
    public static void Run()
    {
        Check(ReferenceMatchPolicy.Status(false, true, 0, true, true) == "NotObserved", "no reference is not agreement");
        Check(ReferenceMatchPolicy.Status(true, false, 0, true, true) == "IdentityMismatch", "different target rejected");
        Check(ReferenceMatchPolicy.Status(true, true, -.01, true, true) == "Expired", "clock regression rejected");
        Check(ReferenceMatchPolicy.Status(true, true, .351, true, true) == "Expired", "stale result rejected");
        Check(ReferenceMatchPolicy.Status(true, true, double.NaN, true, true) == "Expired", "invalid clock rejected");
        Check(ReferenceMatchPolicy.Status(true, true, .1, false, true) == "StateChanged", "changed state rejected");
        Check(ReferenceMatchPolicy.Status(true, true, .1, true, false) == "ReferenceChangedState", "reference state mutation rejected");
        Check(ReferenceMatchPolicy.Status(true, true, .35, true, true) == "ComparablePartialModel", "fresh same-state result only partial comparison");
        Check(SpellDamageMath.Rank(5, "AsIs", 0, 0, null, 5) == 5, "CL5 burning hands cap");
        Check(SpellDamageMath.Rank(12, "AsIs", 0, 0, null, 5) == 5, "CL12 burning hands remains 5d4");
        Check(SpellDamageMath.Rank(12, "AsIs", 0, 0, null, 10) == 10, "CL12 fireball caps at 10d6");
        Check(SpellDamageMath.Rank(1, "OnePlusDiv2", 0, 0, null, 5) == 1, "magic missile starts at one");
        Check(SpellDamageMath.Rank(5, "OnePlusDiv2", 0, 0, null, 5) == 3, "magic missile CL5 three missiles");
        Check(SpellDamageMath.Rank(20, "OnePlusDiv2", 0, 0, null, 5) == 5, "magic missile caps at five");
        Check(SpellDamageMath.Rank(7, "StartPlusDivStep", 3, 4, null, 3) == 2, "scorching ray CL7 second ray");
        Check(SpellDamageMath.Rank(11, "StartPlusDivStep", 3, 4, null, 3) == 3, "scorching ray CL11 third ray");
        Check(SpellDamageMath.Rank(5, "DivStep", 0, 0, null, null) == null, "invalid rank divisor unavailable");
        Check(SpellDamageMath.Rank(5, "unknown", 0, 1, null, null) == null, "unknown progression not fabricated");
        Check(SpellDamageMath.Projectiles(5, true, 3) == 3, "rank restricts configured missiles");
        Check(SpellDamageMath.Projectiles(3, true, 7) == 3, "rank cannot create missing projectile slots");
        Check(SpellDamageMath.Projectiles(5, true, null) == null, "unknown count is not one");
        Check(SpellDamageMath.Projectiles(1, false, null) == 1, "fixed projectile independent of CL");
        Near(SpellDamageMath.Mean(5, 6, 0).Value, 17.5, "5d6 base mean");
        Near(SpellDamageMath.Through(SpellPreviewMath.SaveFailure(10, 20),
            SpellDamageMath.MeanHitPoints(5, 6, 0, false, false, false)).Value,
            7.875, "pit initial fall weights 5d6 by the entrance save, not future rounds");
        Near(SpellDamageMath.Through(0, SpellDamageMath.MeanHitPoints(5, 6, 0, false, false, false)).Value,
            0, "pit flight immunity prevents initial falling damage");
        Near(SpellDamageMath.MeanHitPoints(12, 6, 0, false, false, true).Value, 20.75,
            "Hellfire Ray half damage floors the shared twelve-die roll");
        Near(SpellDamageMath.Expected(2, .75, .8, 1, 20.75 + 20.75, 0), 49.8,
            "two Hellfire Rays weight both half-damage components by hit and SR once");
        Near(SpellPreviewMath.AttackHit(20, 16, true), 1, "Always a Chance allows a natural one with enough attack bonus");
        Near(SpellPreviewMath.AttackHit(20, 22, true), .95, "Always a Chance still compares a natural one to AC");
        Near(SpellPreviewMath.RepeatedMiss(.5, 1), .25, "total concealment reroll misses only if both rolls miss");
        Near(SpellPreviewMath.RepeatedMiss(.2, 2), .008, "partial concealment and two retries");
        Near(SpellPreviewMath.ResistancePass(8 + 2 + 2, 23), .5, "caster level and separate stacking penetration feats");
        Near(SpellDamageMath.Mean(1, 4, 1).Value * 3, 10.5, "three magic missiles raw mean");
        Near(SpellDamageMath.Mean(4, 6, 0).Value * 3, 42, "three rays raw mean excludes hit probability");
        Near(SpellDamageMath.Mean(1, 6, -3).Value, 1, "negative bonus clamps each roll before averaging");
        Near(SpellDamageMath.Mean(2, 2, -3).Value, .25, "negative bonus over two dice");
        Near(SpellDamageMath.MeanHitPoints(1, 4, 1, false, true, false).Value, 5,
            "empower multiplies the rolled sum and bonus before flooring");
        Near(SpellDamageMath.MeanHitPoints(1, 4, 1, true, true, false).Value, 7,
            "maximize and empower use the maximum roll and multiplier");
        Near(SpellDamageMath.MeanHitPoints(1, 4, 0, false, false, true).Value, 1.25,
            "half damage rounds per outcome with the game minimum");
        Near(SpellDamageMath.MeanHitPoints(1, 4, 0, false, false, true, 1).Value, .25,
            "registered resistance applies after halving and minimum");
        Near(SpellDamageMath.Expected(2, .75, .8, .5, 10, 4), 8.4,
            "expected spell damage weights both save outcomes and delivery gates");
        Near(SpellDamageMath.Expected(3, 1, 0, 1, 8, 0), 0,
            "unpenetrated spell resistance prevents HP damage");
        double ScreamExpected(int bonus) => SpellDamageMath.Expected(1, 1, .80, .35,
            SpellDamageMath.MeanHitPoints(4, 6, bonus, false, false, false).Value,
            SpellDamageMath.MeanHitPoints(4, 6, bonus, false, false, false, 0, .5).Value);
        Near(ScreamExpected(0), 7.43, "logged Ear-Piercing Scream uses 4d6, SR and Fortitude half damage");
        Near(ScreamExpected(4), 9.59, "enhanced Ear-Piercing Scream uses 4d6+4 under the same checks");
        Near(SpellDamageMath.Through(.80, .35).Value, .28, "unconditional chance to reach failed Fortitude");
        Near(SpellDamageMath.Through(.80, 1 - .35).Value, .52, "unconditional chance to reach half damage");
        Near(SpellDamageMath.Through(0, null).Value, 0, "blocked spell never reaches an unknown save");
        Check(SpellDamageMath.Through(.80, null) == null, "unknown downstream chance stays unknown");
        Near(SpellDamageMath.InstantDeathChance(1, .85, .85), .7225,
            "instant death requires both saves to fail");
        Near(SpellDamageMath.ExpectedLethalSaveHpLoss(1, .85, .85, 30, 10.5), 23.01375,
            "instant death removes remaining HP and a passed second save contributes damage");
        Near(SpellDamageMath.ExpectedLethalSaveHpLoss(0, .85, .85, 30, 10.5), 0,
            "SR blocks all branches");
        Near(SpellDamageMath.MeanCappedHpLoss(3, 6, 0, false, false, false, 3, 3).Value, 633 / 216.0,
            "cap individual damage outcomes after temporary HP");
        Near(SpellDamageMath.MeanCappedHpLoss(1, 4, 0, false, false, false, 1, 0).Value, 1,
            "spell overkill caps each result at remaining HP");
        Near(SpellDamageMath.MeanCappedHpLoss(1, 4, 0, false, false, false, 2, 1, 1).Value, .75,
            "subtract energy resistance and temporary HP before capping HP loss");
        Near(SpellDamageMath.MeanCappedHpLoss(0, 1, 12, false, false, false, 20, 0, 0, .5).Value, 6,
            "observed incorporeal ray damage is halved");
        Near(SpellDamageMath.MeanCappedHpLoss(0, 1, 12, false, false, false, 20, 0, 2, .25).Value, 1,
            "saving throw and incorporeal halves both precede resistance");
        Near(SpellDamageMath.MeanIndependentHitsHpLoss(0, 1, 3, false, false, false, 0, 1, 2, .5, 2, 3).Value, .5,
            "multiple rays share the same temporary HP and cap cumulative overkill");
        Near(SpellDamageMath.MeanHitPoints(1, 4, 0, false, false, false, 0, .75).Value, 1.75,
            "favorable magic save multiplier applies before flooring");
        Near(SpellDamageMath.ExpectedThroughPool(0, 1, 8, false, false, false, 2, 1, 10, 10).Value, 6,
            "second strike sees depleted shared pool");
        Near(SpellDamageMath.ExpectedThroughPool(0, 1, 8, false, false, false, 2, .5, 10, 10).Value, 1.5,
            "misses preserve pool for subsequent strikes");
        Near(SpellDamageMath.ExpectedThroughPool(1, 2, 0, false, false, false, 2, 1, 2, 2).Value, 1,
            "pool is consumed by the realized damage distribution");
        Near(SpellDamageMath.ExpectedThroughPool(0, 1, 8, false, false, false, 3, 1, 20, 4).Value, 12,
            "per-hit absorption cap limits a larger pool");
        Near(SpellDamageMath.ExpectedThroughPool(0, 1, 8, false, false, false, 2, 1, 10, 10, 4).Value, 4,
            "constant resistance takes over when pool falls below its value");
        Near(SpellDamageMath.ExpectedThroughPool(0, 1, 8, false, false, false, 2, 1, 100, 10, 4).Value, 0,
            "trimming excess pool preserves resistance priority");
        Check(!SpellDamageMath.ExpectedThroughPool(0, 1, 8, false, false, false, 2, 1, 4, 10, 4).HasValue,
            "unknown tie order cannot certify subsequent absorption");
        Near(SpellDamageMath.ExpectedThroughOrderedPools(0, 1, 8, false, false, false, 2, 1, 1,
            1, 1, new[] { (5, 5), (5, 5) }).Value, 6, "depleted pool yields to the next registered pool");
        Near(SpellDamageMath.ExpectedThroughOrderedPools(0, 1, 8, false, false, false, 2, .5, 1,
            1, 1, new[] { (10, 10) }).Value, 1.5, "failed delivery gate does not consume protection");
        Near(SpellDamageMath.ExpectedThroughOrderedPools(0, 1, 8, false, false, false, 2, 1, .5,
            1, .5, new[] { (10, 10) }).Value, 2.5, "per-hit save outcomes alter depletion");
        Near(SpellDamageMath.ExpectedThroughOrderedPools(0, 1, 8, false, false, false, 4, 1, 1,
            1, 1, new[] { (5, 5), (5, 5), (5, 5), (5, 5) }).Value, 12,
            "all four registered pools can absorb damage in sequence");
        Near(SpellDamageMath.ExpectedThroughOrderedPools(0, 1, 8, false, false, false, 4, .5, 1,
            1, 1, new[] { (5, 5), (5, 5), (5, 5), (5, 5) }).Value, 6,
            "equivalent remaining balances merge across missed deliveries");
        Near(SpellDamageMath.MeanContextDice(1, 4, 1, false, true, .5, 1, 1).Value, 2.25,
            "shared initializer truncates after its multiplier and the context empower");
        var referenceLog = new List<string>();
        var referenceRecorder = new DiagnosticRecorder(referenceLog.Add, () => 0, "reference-test") { Enabled = true };
        var referenceInput = new BetaInput("c", "t", "w", "turn", "s", 1, 4, 15, 1, 6, 0, 20, 2, false, true, false);
        referenceRecorder.RecordBetaPreview(BetaModel.Build(referenceInput, "reference-preview"), externalReference: new Dictionary<string, object> {
            { "status", "StateChanged" }, { "independentGroundTruth", false }, { "differences", new object[0] } });
        using (var doc = JsonDocument.Parse(referenceLog[0])) {
            var reference = doc.RootElement.GetProperty("hcoReference");
            Check(reference.GetProperty("status").GetString() == "StateChanged", "reference rejection reason survives log serialization");
            Check(!reference.GetProperty("independentGroundTruth").GetBoolean(), "reference never certifies actual accuracy");
            Check(reference.GetProperty("differences").GetArrayLength() == 0, "rejected reference has no numeric difference");
        }
        var attackCheck = new SpellCheckRecord { Kind = "attack", Type = "RangedTouch", Status = "Estimated", Probability = .7, Bonus = 4, Threshold = 11 };
        var saveCheck = new SpellCheckRecord { Kind = "save", Type = "Will", Status = "Estimated", Probability = .35, Bonus = 8, Threshold = 16 };
        var unavailable = new SpellCheckRecord { Kind = "sr", Type = "SpellResistance", Status = "Unavailable", Reason = "NotObserved" };
        var structuredLines = new List<string>();
        var structuredRecorder = new DiagnosticRecorder(structuredLines.Add, () => 10, "structured-spell") { Enabled = true };
        var structuredContext = new DiagnosticContext("caster", "target", "ability", "turn", "unknown", 0, "", false);
        structuredRecorder.RecordSpellPreview(structuredContext, "two checks", new[] { "attack and save are separate" }, false,
            new[] { attackCheck, saveCheck, unavailable }, new Dictionary<string, object> { { "parameterStatus", "Current" }, { "dc", 16 } });
        using (var doc = JsonDocument.Parse(structuredLines[0]))
        {
            var checks = doc.RootElement.GetProperty("checkDetails");
            Near(checks[0].GetProperty("probability").GetDouble(), .7, "spell attack chance retained separately");
            Near(checks[1].GetProperty("probability").GetDouble(), .35, "spell save failure retained separately");
            Check(checks[1].GetProperty("meaning").GetString() == "target-fails-save", "save direction is explicit");
            Check(checks[2].GetProperty("probability").ValueKind == JsonValueKind.Null, "unknown SR is not zero or guaranteed success");
            Check(doc.RootElement.GetProperty("spellMetadata").GetProperty("dc").GetInt32() == 16, "DC source metadata is structured");
        }
        structuredRecorder.RecordSpellObservation("save-event", structuredContext, new Dictionary<string, object> {
            { "checkKind", "save" }, { "dc", 16 }, { "bonus", 8 }, { "die", 4 }, { "passed", false }, { "castContextId", "cast-1" }
        });
        using (var doc = JsonDocument.Parse(structuredLines[1]))
        {
            Check(doc.RootElement.GetProperty("kind").GetString() == "spell-observation", "completed spell checks are separate from weapon observations");
            Check(!doc.RootElement.GetProperty("details").GetProperty("passed").GetBoolean(), "actual save outcome recorded");
            Check(doc.RootElement.GetProperty("comparison").GetString().Contains("no verified"), "source IDs do not falsely certify preview association");
        }
        Near(SpellPreviewMath.SaveFailure(5, 16), .5, "DC16 against +5: fail on 1–10");
        Near(SpellPreviewMath.SaveFailure(5, 16, true), .75, "persistent requires both saves to succeed");
        Near(SpellPreviewMath.SaveFailure(100, 1), .05, "save natural one fails");
        Near(SpellPreviewMath.SaveFailure(-100, 100), .95, "save natural twenty succeeds");
        Near(SpellPreviewMath.SaveFailure(int.MaxValue, int.MinValue), .05, "large save bounds do not overflow");
        Near(SpellPreviewMath.SaveFailure(int.MinValue, int.MaxValue), .95, "large negative save does not overflow");
        Near(SpellPreviewMath.AttackHit(7, 15), .65, "touch AB7 vs AC15 hits on 8–20");
        Near(SpellPreviewMath.AttackHit(100, 1), .95, "attack natural one misses");
        Near(SpellPreviewMath.AttackHit(-100, 100), .05, "attack natural twenty hits");
        Near(SpellPreviewMath.ResistancePass(5, 16), .5, "SR16 vs CL5 passes on 11–20");
        Near(SpellPreviewMath.ResistancePass(20, 21), 1, "SR checks have no natural-one failure");
        Near(SpellPreviewMath.ResistancePass(0, 21), 0, "SR checks have no natural-twenty success");
        Near(SpellPreviewMath.ResistancePass(int.MaxValue, int.MinValue), 1, "penetration bounds do not overflow");
        Near(SpellPreviewMath.ResistancePass(int.MinValue, int.MaxValue), 0, "negative penetration bounds do not overflow");
        var area = new SpellTargetWindow<string>();
        area.Observe("fireball", "point1", "enemy", true, 1);
        area.Observe("fireball", "point1", "ally", true, 1);
        Check(area.Read("fireball", 1.1).Length == 2, "AoE includes allies as well as enemies reported by game");
        Check(area.Read("cone", 1.1).Length == 0, "other selected spell cannot borrow area targets");
        Check(area.Read("fireball", 1.26).Length == 0, "stale targeting is not shown as current membership");
        Check(area.Read("fireball", .9).Length == 0, "clock regression rejects future target evidence");
        area.Observe("fireball", "point1", "enemy", false, 1.1);
        Check(area.Read("fireball", 1.1).SequenceEqual(new[] { "ally" }), "game exclusion removes the target immediately");
        area.Observe("fireball", "point2", "new enemy", true, 1.15);
        Check(area.Read("fireball", 1.15).SequenceEqual(new[] { "new enemy" }), "moving aim clears old area members");
        area.Observe("cone", "point2", "cone enemy", true, 1.16);
        Check(area.Read("fireball", 1.16).Length == 0, "switching spell invalidates previous area");
        area.Clear(); Check(area.Read("cone", 1.17).Length == 0, "disable clears area evidence");
        var lines = new List<string>();
        var recorder = new DiagnosticRecorder(lines.Add, () => 10, "spell-fixture") { Enabled = true };
        var context = new DiagnosticContext("caster", "target", "ability", "turn", "unknown", 0, "", false);
        Check(recorder.RecordSpellPreview(context, "의지 주문 \"표본\"", new[] { "의지 내성 실패 ~50%" }, true), "spell preview writes meaningful diagnostics");
        using (var json = JsonDocument.Parse(lines.Single()))
        {
            var root = json.RootElement;
            Check(root.GetProperty("kind").GetString() == "spell-preview", "spell logs are distinct from weapon events");
            Check(root.GetProperty("observedTargetMembership").GetBoolean(), "AoE membership provenance retained");
            Check(!root.GetProperty("fullConditionsVerified").GetBoolean(), "conditional spell preview never certifies all effects");
            Check(root.GetProperty("checks")[0].GetString() == "의지 내성 실패 ~50%", "displayed checks and recorded checks agree");
        }
        Console.WriteLine("PASS: " + count + " spell attack/save/SR/diagnostic assertions (not game execution)");
    }
}
