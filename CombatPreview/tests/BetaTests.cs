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
        Near(model.Rows[0].MeanOnHit.Value, 21.45, "conditional hit mean includes criticals, not misses");
        Check(AttackThreshold.Required(5, 20) == 15, "basic AB5 AC20 requires fifteen");
        Check(AttackThreshold.Required(9, 17) == 8, "observed AB9 AC17 requires eight and matches screenshot 65 percent");
        Check(AttackThreshold.Required(100, 1) == 2, "natural one still misses despite huge bonus");
        Check(AttackThreshold.Required(-100, 100) == 20 && AttackThreshold.Describe(-100, 100).Contains("자연 20"), "unreachable arithmetic threshold still allows natural twenty");
        Check(AttackThreshold.Required(int.MaxValue, int.MinValue) == 2 && AttackThreshold.Required(int.MinValue, int.MaxValue) == 20, "threshold arithmetic avoids overflow");
        var hold = new PanelRetention();
        hold.Hover("rider", "turn1", "enemy", 100);
        Check(hold.Keep("rider", "turn1", "enemy", true, false, 1000), "cursor can travel from enemy to panel during grace");
        Check(hold.Keep("rider", "turn1", "enemy", true, true, 5000), "pointer over panel retains target to click details");
        Check(!hold.Keep("rider", "turn1", "enemy", true, false, 1401), "unfocused unpinned panel expires");
        hold.TogglePin(); Check(hold.Keep("rider", "turn1", "enemy", true, false, 5000), "pinned panel can be inspected without hovering enemy");
        Check(!hold.Keep("rider", "turn2", "enemy", true, true, 5000), "pin cannot survive turn transition");
        Check(!hold.Keep("other", "turn1", "enemy", true, true, 5000), "pin cannot survive attacker transition");
        Check(!hold.Keep("rider", "turn1", "enemy", false, true, 5000), "dead/missing target invalidates pin");
        hold.Hover("rider", "turn1", "other-enemy", 5000); Check(!hold.Pinned, "changing hovered target releases old pin");
        hold.Clear(); Check(!hold.Keep("rider", "turn1", "other-enemy", true, true, 5000), "scene/ability/disable clears retained target");
        var completed = new CompletedRuleCache();
        completed.Put("rider-target-weapon-state", 9, 100);
        Check(completed.Read("rider-target-weapon-state", 101) == 9, "completed rule result is copied without adding bonuses twice");
        Check(!completed.Read("rider-target-other-weapon-state", 101).HasValue, "rule results never cross weapons");
        Check(!completed.Read("rider-target-weapon-state", 99).HasValue && !completed.Read("rider-target-weapon-state", 1601).HasValue, "completed rule clock/age invalidation");
        completed.Clear(); Check(!completed.Read("rider-target-weapon-state", 101).HasValue, "completed results clear on invalidation");
        var mountInput = new BetaInput("mount", "target", "bite", "turn", "mount-state", 16, 5, 20, 1, 6, 3, 20, 2, true, true, false, naturalWeapon: true);
        var mountModel = BetaModel.Build(mountInput, "mount-shown");
        Check(mountModel.Rows.Count == 1, "natural bite does not invent BAB iteratives");
        Near(PanelPresentation.TotalRaw(new[] { model, mountModel }).Value, model.Rows.Sum(r => r.ExpectedRaw.Value) + mountModel.Rows[0].ExpectedRaw.Value,
            "mounted total includes rider and mount rows");
        Check(!PanelPresentation.TotalRaw(new BetaSnapshot[] { model, null }).HasValue, "missing mount data makes combined total unknown rather than omitting mount");
        var mountedTracker = new BetaTracker();
        mountedTracker.Shown(model, 0); mountedTracker.Shown(mountModel, 0);
        mountedTracker.Begin("rider-attack", input, 0, true, 1);
        mountedTracker.Begin("mount-attack", mountInput, 1, true, 1, "bite");
        Check(mountedTracker.Finish("rider-attack", "actor", "target", "sword", true, 22, 28, 1, 8, 15, 2, 1).Preview.Id == model.Id,
            "mount display never overwrites rider association");
        var mountComparison = mountedTracker.Finish("mount-attack", "mount", "target", "bite", true, 5, 20, 1, 6, 3, 2, 1);
        Check(mountComparison.Preview.Id == mountModel.Id && !mountComparison.HasDifference,
            "natural attack with a global nonzero attack index associates to its own weapon row");
        var hoofInput = new BetaInput("mount", "target", "hoof-left", "turn", "hoof-state", 16, 0, 20, 1, 4, 1, 20, 2, true, true, false, naturalWeapon: true);
        var hoof = BetaModel.Build(hoofInput, "hoof-shown"); mountedTracker.Shown(hoof, 0);
        mountedTracker.Begin("hoof-attack", hoofInput, 2, true, 1, "hoof-left");
        Check(mountedTracker.Finish("hoof-attack", "mount", "target", "hoof-left", true, 0, 20, 1, 4, 1, 2, 1).Preview.Id == hoof.Id,
            "same mount's different limb uses its own weapon identity and global index");
        var terms = new List<DamageComponentSnapshot> {
            new DamageComponentSnapshot(new DamageTerm(1, 8, 4, true) { Kind = DamageKind.Physical }, false, null),
            new DamageComponentSnapshot(new DamageTerm(1, 6, 0, false) { Kind = DamageKind.Fire }, false, null)
        };
        var multiInput = new BetaInput("actor", "target", "sword", "turn", "multi", 16, 22, 28, 1, 8, 4, 19, 2,
            true, true, false, terms, "observed-existing-estimate/basic-primary-iteratives");
        var multi = BetaModel.Build(multiInput, "multi");
        Near(multi.Rows[0].Normal.Value.Mean, 12, "multiple dice normal hit mean");
        Near(multi.Rows[0].Critical.Value.Mean, 20.5, "extra fire dice do not multiply on critical");
        Near(multi.Rows[0].ExpectedRaw.Value, 9.6375, "multi-component expectation accounts for hit and criticals");
        Near(multi.Rows[0].MeanOnHit.Value, 12.85, "multi-component conditional mean");
        Check(PanelPresentation.DiceLines(multiInput) == "1D8+4 +\n1D6 (화염)", "damage components on distinct lines with join marker");
        terms.Clear(); Check(multiInput.Components.Count == 2, "input copies mutable component list");
        Near(PanelPresentation.TotalRaw(multi).Value, multi.Rows.Sum(r => r.ExpectedRaw.Value), "full basic attack sum includes every displayed row");
        Check(!PanelPresentation.TotalRaw(BetaModel.Build(Input(damage: false), "no-damage")).HasValue, "unknown total never becomes zero");
        float px, py;
        PanelPresentation.Place(2560, 1440, 820, 264, 1068, out px, out py);
        Check(px == 870 && py == 792, "screenshot-sized layout centers immediately above action bar");
        foreach (var size in new[] { new[] { 1280f, 720f }, new[] { 1920f, 1080f }, new[] { 3440f, 1440f } })
        {
            float width = Math.Min(820, size[0] - 12), height = size[1] * .65f, top = size[1] * .74f;
            PanelPresentation.Place(size[0], size[1], width, height, top, out px, out py);
            Check(px >= 0 && py >= 0 && px + width <= size[0] && py + height < top, "anchored panel fits common viewport");
        }
        var native = new ObservedPreviewCache();
        Check(ReferenceEquals(native.Read(input, 0), input), "no observed estimate uses basic model");
        native.Store(input, multiInput, 100);
        Check(ReferenceEquals(native.Read(input, 100), multiInput), "fresh matching game-created estimate used without replay");
        Check(ReferenceEquals(native.Read(input, 2101), input), "expired estimate cannot contaminate current preview");
        Check(ReferenceEquals(native.Read(input, 99), input), "observed estimate rejects clock regression");
        Check(native.Read(Input("changed"), 101).Signature == "changed", "changed state invalidates copied game estimate");
        Check(native.Read(Input(turn: "new-turn"), 101).Turn == "new-turn", "other turn cannot reuse estimate");
        Check(native.Read(Input(weapon: "other"), 101).Weapon == "other", "other weapon cannot reuse estimate");
        native.Clear(); Check(ReferenceEquals(native.Read(input, 101), input), "toggle/scene clears native estimate");
        bool rejected = false;
        try { native.Store(Input(weapon: "other"), multiInput, 100); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "cache refuses an observed estimate from another weapon");
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
            Check(doc.RootElement.GetProperty("rows")[0].GetProperty("requiredD20").GetInt32() == 6, "displayed d20 requirement logged");
        }
        using (var doc = JsonDocument.Parse(lines[2]))
            Check(doc.RootElement.GetProperty("rollEventId").GetString() == "roll-1" && doc.RootElement.GetProperty("differentFields").GetArrayLength() == 0,
                "traceable partial-model comparison");
        foreach (var line in lines) using (JsonDocument.Parse(line)) { }
        tracker.Shown(multi, 0); tracker.Begin("multi-attack", multiInput, 0, true, 1);
        var actualParts = new[] {
            multiInput.Components[0],
            new DamageComponentSnapshot(new DamageTerm(2, 6, 0, false) { Kind = DamageKind.Fire }, false, null)
        };
        var extraDifference = tracker.Finish("multi-attack", "actor", "target", "sword", true, 22, 28, 1, 8, 4, 2, 2, actualParts);
        Check(extraDifference.Differences.SequenceEqual(new[] { "Damage[2]" }), "extra damage component dice compared independently");
        recorder.RecordBetaPreview(multi);
        recorder.RecordBetaComparison(extraDifference, multi.Context(), "multi-roll");
        using (var doc = JsonDocument.Parse(lines[lines.Count - 2]))
        {
            Check(doc.RootElement.GetProperty("basis").GetString().StartsWith("observed-"), "copied estimate source explicit in log");
            Check(doc.RootElement.GetProperty("damageComponents").GetArrayLength() == 2, "all displayed dice logged");
            Near(doc.RootElement.GetProperty("rows")[0].GetProperty("meanOnHitRaw").GetDouble(), 12.85, "displayed conditional mean logged");
        }
        using (var doc = JsonDocument.Parse(lines.Last()))
            Check(doc.RootElement.GetProperty("actualDamageComponents")[1].GetProperty("rolls").GetInt32() == 2,
                "all actual component dice logged for review");
        var manualDefense = new DefenseProfile { Complete = true, Source = "MANUAL SCENARIO" };
        manualDefense.Reduction[DamageKind.Physical] = 4;
        recorder.RecordBetaPreview(multi, manualDefense);
        using (var doc = JsonDocument.Parse(lines.Last()))
        {
            var row = doc.RootElement.GetProperty("rows")[0];
            Near(row.GetProperty("expectedRaw").GetDouble(), 9.6375, "manual DR preserves original raw expectation in log");
            Near(row.GetProperty("expectedManualScenario").GetDouble(), 6.6375, "manual DR expectation logged separately");
            Near(row.GetProperty("meanOnHitManualScenario").GetDouble(), 8.85, "conditional mean includes manual DR when explicitly selected");
            Check(doc.RootElement.GetProperty("manualScenario").GetBoolean() && !doc.RootElement.GetProperty("afterDefenseAvailable").GetBoolean(),
                "manual defense never certifies actual target defense");
        }
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
