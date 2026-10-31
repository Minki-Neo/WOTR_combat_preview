using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.IO;
using CombatPreview;

static class BetaTests
{
    static int assertions;
    static void Check(bool yes, string reason) { assertions++; if (!yes) throw new Exception(reason); }
    static void Near(double actual, double expected, string reason)
    { Check(!double.IsNaN(actual) && Math.Abs(actual - expected) < 1e-9, reason); }
    static BetaInput Input(string signature = "state-a", bool full = true, bool damage = true, string weapon = "sword", string turn = "turn", bool defense = false)
    { return new BetaInput("actor", "target", weapon, turn, signature, 16, 22, 28, 1, 8, 15, 19, 2, full, damage, defense); }
    static BetaComparison Finish(BetaTracker tracker, string id = "attack", long now = 10, bool rolled = true,
        int ab = 22, int ac = 28, int? rolls = 1, int? sides = 8, int? bonus = 15, int count = 1)
    { return tracker.Finish(id, "actor", "target", "sword", rolled, ab, ac, rolls, sides, bonus, now, count); }
    public static void Run()
    {
        var input = Input(); var model = BetaModel.Build(input, "shown-1");
        Check(model.Rows.Count == 4, "BAB16 basic primary iteratives");
        double[] hits = { .75, .5, .25, .05 };
        for (int i = 0; i < 4; i++)
        {
            Check(model.Rows[i].Penalty == i * 5 && model.Rows[i].AB == 22 - i * 5, "basic iterative penalty");
            Near(model.Rows[i].Hit, hits[i], "hand-checked hit thresholds");
        }
        Near(model.Rows[0].Crit, .075, "unconditional threat and confirmation");
        Near(model.Rows[0].Normal.Value.Mean, 19.5, "basic normal weapon mean");
        Near(model.Rows[0].Critical.Value.Mean, 39, "basic critical mean");
        Near(model.Rows[0].ExpectedRaw.Value, 16.0875, "raw expected value");
        Check(!model.Context().Complete, "beta never certifies complete game conditions");
        Check(BetaModel.Build(Input(full: false), "single").Rows.Count == 1, "single attack remains single");
        Check(!BetaModel.Build(Input(damage: false), "unknown").Rows[0].ExpectedRaw.HasValue, "unknown damage is unavailable, not zero");
        Near(BetaModel.Build(Input(defense: true), "defense").Rows[0].ExpectedRaw.Value, 16.0875, "raw model never silently subtracts or assumes defenses");
        // Independent 400-pair attack/confirmation enumeration across thresholds.
        for (int ab = -2; ab <= 12; ab++) for (int ac = 8; ac <= 26; ac += 3)
        {
            int hitPairs = 0, critPairs = 0;
            for (int a = 1; a <= 20; a++) for (int confirm = 1; confirm <= 20; confirm++)
                if (a == 20 || (a != 1 && a + ab >= ac))
                { hitPairs++; if (a >= 19 && confirm + ab >= ac) critPairs++; }
            var m = BetaModel.Build(new BetaInput("a", "t", "w", "turn", "s", 1, ab, ac, 1, 6, 0, 19, 2, false, false, false), "oracle");
            Near(m.Rows[0].Hit, hitPairs / 400.0, "enumerated attack probability");
            Near(m.Rows[0].Crit, critPairs / 400.0, "enumerated confirmation probability");
        }
        var tracker = new BetaTracker();
        Check(Finish(tracker) == null, "no displayed preview means no comparison");
        tracker.Shown(model, 0); tracker.Begin("attack", input, 0, true, 1);
        var equal = Finish(tracker);
        Check(equal.Status == BetaComparisonStatus.ComparedPartialModel && !equal.HasDifference && equal.ComparedFields.Count == 5,
            "deterministic fields match within partial model");
        Check(Finish(tracker) == null, "consume real attack only once");
        tracker.Shown(model, 0); tracker.Begin("attack", input, 0, true, 1);
        var mismatch = Finish(tracker, ab: 24, ac: 30, bonus: 17, count: 2);
        Check(mismatch.HasDifference && mismatch.Differences.Contains("AB") && mismatch.Differences.Contains("AC")
            && mismatch.Differences.Contains("WeaponBonus") && mismatch.Differences.Contains("DamageComponents"), "deterministic differences identified");
        tracker.Shown(model, 0); tracker.Begin("attack", Input("buff-changed"), 0, true, 1);
        Check(Finish(tracker).Status == BetaComparisonStatus.InputsChanged, "changed model input is not called a mismatch");
        tracker.Shown(model, 0); tracker.Begin("attack", input, 0, true, 2001);
        Check(Finish(tracker, now: 2010).Status == BetaComparisonStatus.PreviewExpired, "display freshness bounded");
        tracker.Shown(model, 0); tracker.Begin("attack", input, 0, true, 1);
        Check(Finish(tracker, now: 10002).Status == BetaComparisonStatus.PreviewExpired, "delayed completion expires");
        tracker.Shown(model, 100); tracker.Begin("attack", input, 0, true, 99);
        Check(Finish(tracker, now: 100).Status == BetaComparisonStatus.PreviewExpired, "clock regression expires preview");
        tracker.Shown(model, 0); tracker.Begin("attack", input, 0, false, 1);
        Check(Finish(tracker).Status == BetaComparisonStatus.AttackModeChanged, "single/full transition not called a bug");
        tracker.Shown(model, 0); tracker.Begin("attack", input, 4, true, 1);
        Check(Finish(tracker).Status == BetaComparisonStatus.UnmodeledAttack, "haste/extra attacks are not silently folded into a base row");
        tracker.Shown(model, 0); tracker.Begin("attack", input, 0, true, 1, "offhand");
        Check(Finish(tracker).Status == BetaComparisonStatus.PairChanged, "offhand or changed weapon not compared as main hand");
        tracker.Shown(model, 0); tracker.Begin("attack", Input(turn: "other-turn"), 0, true, 1);
        Check(Finish(tracker).Status == BetaComparisonStatus.PairChanged, "turn transition invalidates association");
        tracker.Shown(model, 0); tracker.Begin("attack", input, 0, true, 1);
        Check(tracker.Finish("attack", "actor", "redirected-target", "sword", true, 22, 28, 1, 8, 15, 10).Status == BetaComparisonStatus.PairChanged,
            "redirected target cannot be treated as unchanged");
        tracker.Shown(model, 0); tracker.Begin("attack", input, 0, true, 1); tracker.Invalidate();
        Check(Finish(tracker) == null, "UI disable/scene invalidation clears pending");
        tracker.Shown(model, 0); tracker.Begin("attack", input, 0, true, 1); tracker.Cancel("attack");
        Check(Finish(tracker) == null, "actual attack completion cleans abandoned pending");
        tracker.Shown(model, 0); tracker.Begin("attack", input, 0, true, 1);
        var noFields = Finish(tracker, rolled: false, rolls: null, sides: null, bonus: null, count: -1);
        Check(noFields.Status == BetaComparisonStatus.NoComparableFields && !noFields.HasDifference, "zero comparable fields is not a passing comparison");
        var lines = new List<string>();
        var recorder = new DiagnosticRecorder(lines.Add, () => 20, "beta-fixture") { Enabled = true };
        Check(recorder.RecordBetaPreview(model), "displayed model JSON recorded");
        var actual = new ActualAttackObservation(model.Context(0), 22, 28, true, true, hasWeaponAttack: true);
        Check(recorder.Observe("roll-1", actual, model.Id).Status == ComparisonStatus.BetaPreviewAssociated, "actual roll links to the displayed beta model");
        recorder.RecordBetaComparison(equal, model.Context(0), "roll-1");
        // This random/mitigated damage differs from ExpectedRaw; it never becomes
        // an expected-value mismatch or affects deterministic comparison status.
        recorder.RecordDamage("damage-1", "roll-1", model.Context(0), 4, 18, 4, "defense unsupported");
        Check(lines.Count == 4 && !equal.HasDifference, "random damage sample not compared with expectation");
        using (var doc = JsonDocument.Parse(lines[0]))
        {
            Check(doc.RootElement.GetProperty("quality").GetString() == "PartialEstimate", "beta scope explicit in report");
            Check(!doc.RootElement.GetProperty("fullConditionsVerified").GetBoolean() && !doc.RootElement.GetProperty("afterDefenseAvailable").GetBoolean(),
                "neither complete conditions nor actual mitigation is certified");
            Check(doc.RootElement.GetProperty("rows").GetArrayLength() == 4, "exact displayed basic rows persisted");
        }
        using (var doc = JsonDocument.Parse(lines[2]))
            Check(doc.RootElement.GetProperty("rollEventId").GetString() == "roll-1" && doc.RootElement.GetProperty("differentFields").GetArrayLength() == 0,
                "traceable partial-model comparison");
        foreach (var line in lines) using (JsonDocument.Parse(line)) { }
        Console.WriteLine("PASS: " + assertions + " read-only beta math/association/logging assertions (not in-game UI validation)");
    }
    public static void WriteExample(string path)
    {
        if (File.Exists(path)) throw new IOException("Refusing to overwrite an existing beta example");
        long now = 0;
        var log = new LocalDiagnosticLog(path);
        var recorder = new DiagnosticRecorder(log.Write, () => now, "beta-example-fixture") { Enabled = true };
        recorder.SetRuntimeMetadata(new Dictionary<string, string> { { "gameVersion", "FIXTURE, not a game run" } });
        var snapshot = BetaModel.Build(Input(), "beta-example");
        var tracker = new BetaTracker(); tracker.Shown(snapshot, now); recorder.RecordBetaPreview(snapshot);
        now = 1; tracker.Begin("actual-attack", snapshot.Input, 0, true, now);
        now = 2;
        var actual = new ActualAttackObservation(snapshot.Context(0), 24, 28, true, true, hasWeaponAttack: true);
        recorder.Observe("roll-example", actual, snapshot.Id);
        var difference = Finish(tracker, "actual-attack", now, ab: 24);
        recorder.RecordBetaComparison(difference, snapshot.Context(0), "roll-example");
        recorder.RecordDamage("damage-example", "roll-example", snapshot.Context(0), 4, 19, 4, "FIXTURE: mitigation unsupported; no sample-vs-mean comparison");
        Console.WriteLine("Wrote illustrative beta model/difference/actual fixture (not game validation): " + path);
    }
}
