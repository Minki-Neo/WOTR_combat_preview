using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CombatPreview;

static class ContractTests
{
    static int checks;
    static void Check(bool condition, string reason)
    { checks++; if (!condition) throw new Exception(reason); }
    static void Near(double actual, double expected, string reason)
    { Check(!double.IsNaN(actual) && Math.Abs(actual - expected) < 1e-9, reason + ": " + actual); }
    static void Throws<T>(Action action, string reason) where T : Exception
    { bool threw = false; try { action(); } catch (T) { threw = true; } Check(threw, reason); }
    static BypassCondition Trait(string name) { return new BypassCondition(BypassOperator.Trait, name); }
    static readonly BypassCondition Never = new BypassCondition(BypassOperator.Never);
    static DefenseObservation Observation(RulesImplementation rules = RulesImplementation.Vanilla)
    {
        return new DefenseObservation { Rules = rules, Origin = SnapshotOrigin.Fixture, GameBuild = "fixture-1",
            RuleEvidence = "explicit fixture rules (not a game observation)", TargetId = "enemy-1", Scope = "one grouped weapon attack",
            Complete = true, GroupingVerified = true, TraitsComplete = true };
    }
    static DamageComponentSnapshot Component(DamageKind kind, params string[] traits)
    { return new DamageComponentSnapshot(new DamageTerm(1, kind == DamageKind.Physical ? 8 : 6, kind == DamageKind.Physical ? 15 : 0, kind == DamageKind.Physical) { Kind = kind }, true, traits); }
    static readonly VanillaDefenseSnapshotProvider Vanilla = new VanillaDefenseSnapshotProvider();
    public static void Run()
    {
        DefenseFixtures(); QualityFixtures(); DiagnosticsFixtures(); DiagnosticRegressionFixtures(); SafetyAndBoundaries();
        Console.WriteLine("PASS: " + checks + " defense/quality/diagnostic/safety assertions (fixtures, not game integration)");
    }
    static void DefenseFixtures()
    {
        var physical = Component(DamageKind.Physical, "form:slashing"); var fire = Component(DamageKind.Fire);
        var o = Observation();
        o.Reductions.Add(new ReductionObservation(DamageKind.Physical, 10, "main", Trait("material:adamantine")));
        o.Reductions.Add(new ReductionObservation(DamageKind.Fire, 5, "main", Never));
        var s = Vanilla.Read(o, new[] { physical, fire });
        Check(s.Quality == PredictionQuality.SupportedModel && s.Origin == SnapshotOrigin.Fixture, "fixture provenance retained");
        var result = MitigationMath.Calculate(new[] { physical.ToTerm(), fire.ToTerm() }, 2, .75, .075, s.ToProfile());
        Check(result.Available, "complete static model available");
        Near(result.Expected, 8.7125, "handoff regression");
        Near(result.Normal.Mean, 29.0 / 3, "typed resistance");
        Near(result.Critical.Mean, 175.0 / 6, "unmultiplied fire");
        var bypassed = Vanilla.Read(o, new[] { Component(DamageKind.Physical, "material:adamantine") });
        Check(bypassed.ToProfile().Reduction[DamageKind.Physical] == 0, "explicit physical bypass");
        // A second nonstacking DR selects maximum; an audited distinct group stacks.
        o.Reductions.Add(new ReductionObservation(DamageKind.Physical, 12, "main", Never));
        Check(Vanilla.Read(o, new[] { physical }).ToProfile().Reduction[DamageKind.Physical] == 12, "max DR, not sum");
        o.Reductions.Add(new ReductionObservation(DamageKind.Physical, 3, "explicit stacking adjustment", Never));
        Check(Vanilla.Read(o, new[] { physical }).ToProfile().Reduction[DamageKind.Physical] == 15, "explicit DR stacking");
        var all = new BypassCondition(BypassOperator.All, null, Trait("alignment:good"), Trait("material:cold-iron"));
        var either = new BypassCondition(BypassOperator.Any, null, Trait("enhancement:3"), all);
        Check(!all.Matches(new HashSet<string> { "alignment:good" }), "AND needs every trait");
        Check(all.Matches(new HashSet<string> { "alignment:good", "material:cold-iron" }), "AND bypass");
        Check(either.Matches(new HashSet<string> { "enhancement:3" }), "OR enhancement bypass");
        Check(either.Matches(new HashSet<string> { "enhancement:5" }), "higher enhancement meets threshold");
        Check(!either.Matches(new HashSet<string> { "enhancement:2" }), "lower enhancement does not meet threshold");
        Throws<ArgumentException>(() => Trait("enhancement:unknown"), "unknown enhancement threshold refused");
        Check(!either.Matches(new HashSet<string>()), "no bypass never guessed");
        Throws<ArgumentException>(() => new BypassCondition(BypassOperator.All), "empty AND refused");
        Throws<ArgumentException>(() => new ReductionObservation(DamageKind.Unknown, 2, "x", Never), "unknown DR refused");
        Check(Vanilla.Read(o, new[] { physical, Component(DamageKind.Physical, "material:adamantine") }).Quality == PredictionQuality.Unavailable,
            "heterogeneous physical bypass cannot be grouped");
        o.Immunities.Add(DamageKind.Fire);
        var immune = Vanilla.Read(o, new[] { fire });
        Near(MitigationMath.Calculate(new[] { fire.ToTerm() }, 2, 1, 0, immune.ToProfile()).Expected, 0, "verified immunity is zero");
        // Snapshot ownership: mutate all inputs and exported legacy model after capture.
        var exported = s.ToProfile(); exported.Reduction.Clear(); exported.Complete = false;
        o.Reductions.Clear(); o.Immunities.Clear(); o.GameBuild = "changed";
        Check(s.GameBuild == "fixture-1" && s.ToProfile().Reduction[DamageKind.Physical] == 10 && s.ToProfile().Complete,
            "snapshot has no shared mutable defense state");
        var term = new DamageTerm(1, 8, 15, true) { Kind = DamageKind.Physical };
        var traits = new[] { "material:silver" };
        var value = new DamageComponentSnapshot(term, true, traits);
        term.Bonus = 100; traits[0] = "changed";
        Check(value.Bonus == 15 && value.BypassTraits[0] == "material:silver", "component copies scalar/trait data");
        value.ToTerm().Bonus = 100;
        Check(value.ToTerm().Bonus == 15, "term exports do not mutate snapshot");
        Action<DefenseObservation>[] incomplete = {
            x => x.Complete = false, x => x.GroupingVerified = false, x => x.TraitsComplete = false,
            x => x.HasConsumablePool = true, x => x.HasSuppression = true, x => x.HasUnknownModifiers = true,
            x => x.RuleEvidence = "", x => x.GameBuild = "", x => x.TargetId = "", x => x.Scope = "",
            x => x.Rules = RulesImplementation.Unknown, x => x.Missing.Add("unknown custom effect"),
            x => x.Immunities.Add((DamageKind)999), x => x.Reductions.Add(null), x => x.Origin = SnapshotOrigin.Manual
        };
        foreach (var change in incomplete)
        {
            var input = Observation(); change(input);
            var unknown = Vanilla.Read(input, new[] { physical });
            Check(unknown.Quality == PredictionQuality.Unavailable && unknown.Missing.Count > 0, "incomplete observation carries reason");
            Check(!MitigationMath.Calculate(new[] { physical.ToTerm() }, 2, 1, 0, unknown.ToProfile()).Available, "unknown not converted to zero");
        }
        Check(Vanilla.Read(Observation(), null).Quality == PredictionQuality.Unavailable, "missing damage refused");
        Check(Vanilla.Read(Observation(), new[] { Component(DamageKind.Unknown) }).Quality == PredictionQuality.Unavailable, "unknown component refused");
        var unknownBypass = new DamageComponentSnapshot(physical.ToTerm(), false, null);
        Check(Vanilla.Read(Observation(), new[] { unknownBypass }).Quality == PredictionQuality.Unavailable, "unknown bypass refused");
        var ttt = new TabletopTweaksDefenseSnapshotProvider();
        Check(ttt.Read(Observation(), new[] { physical }).Quality == PredictionQuality.Unavailable, "no vanilla fallback in TTT provider");
        Check(Vanilla.Read(Observation(RulesImplementation.TabletopTweaks), new[] { physical }).Quality == PredictionQuality.Unavailable, "no TTT fallback in vanilla provider");
        Check(ttt.Read(Observation(RulesImplementation.TabletopTweaks), new[] { physical }).Quality == PredictionQuality.SupportedModel, "explicit TTT fixture contract only");
    }
    static void QualityFixtures()
    {
        var supported = new PredictionReport(PredictionQuality.SupportedModel, 3, "RAW");
        var partial = new PredictionReport(PredictionQuality.PartialEstimate, 4, "RAW", "sneak attack excluded");
        var unavailable = new PredictionReport(PredictionQuality.Unavailable, 999, "RAW", "unknown effect");
        Check(!unavailable.Expected.HasValue, "unavailable suppresses numeric value");
        var sum = PredictionReport.Total(new[] { supported, partial }, 2);
        Near(sum.Expected.Value, 7, "complete partial model sums all rows");
        Check(sum.Quality == PredictionQuality.PartialEstimate && sum.Reasons.Contains("sneak attack excluded"), "aggregate quality preserves omissions");
        Check(!PredictionReport.Total(new[] { supported, unavailable }, 2).Expected.HasValue, "one unavailable suppresses series total");
        Check(!PredictionReport.Total(new[] { supported }, 2).Expected.HasValue, "missing attack suppresses full attack total");
        Check(!PredictionReport.Total(new PredictionReport[0], 0).Expected.HasValue, "zero rows is not a successful full attack");
        Check(!PredictionReport.Total(new[] { supported, new PredictionReport(PredictionQuality.SupportedModel, 3, "MANUAL") }, 2).Expected.HasValue,
            "manual and raw sources cannot mix");
        Throws<ArgumentOutOfRangeException>(() => new PredictionReport(PredictionQuality.SupportedModel, double.NaN, "raw"), "invalid mean not displayed");
    }
    static DiagnosticContext Context(string revision = "buffs/equipment/actions/defenses-v1", bool complete = true, string turn = "turn-1")
    { return new DiagnosticContext("rider-1", "enemy-1", "longsword-1", turn, "scene-1", 0, revision, complete); }
    static DiagnosticPrediction Prediction(DiagnosticContext c, SnapshotOrigin origin = SnapshotOrigin.Fixture)
    { return new DiagnosticPrediction(c, 22, 28, 0, .75, .075, 6, 14, "physical:1d8+15;fire:1d6", "fixture:DR10/fire5", "physical10;fire5", origin); }
    static ActualAttackObservation Actual(DiagnosticContext c, double damage = 7)
    { return new ActualAttackObservation(c, 22, 28, true, true, damage, "physical:1d8+15;fire:1d6", "physical10;fire5", 0, true, false, hasWeaponAttack: true); }
    static void DiagnosticsFixtures()
    {
        long now = 0; var lines = new List<string>();
        var r = new DiagnosticRecorder(lines.Add, () => now);
        Check(r.RecordPrediction(Prediction(Context())) == null && r.Observe("a", Actual(Context())) == null && lines.Count == 0, "diagnostics default OFF");
        r.Enabled = true;
        var id = r.RecordPrediction(Prediction(Context()));
        var result = r.Observe("a", Actual(Context(), 100));
        Check(result.Status == ComparisonStatus.Compared && result.AttackBonusMatches == true && result.ArmorClassMatches == true
            && result.DamageComponentsMatch == true && result.ReductionsMatch == true, "deterministic fields compared independently of random damage");
        Check(lines.Count == 2 && id != null, "traceable prediction and actual pair");
        foreach (var line in lines) using (var doc = JsonDocument.Parse(line)) Check(doc.RootElement.GetProperty("schema").GetInt32() == 2, "valid JSONL record");
        using (var doc = JsonDocument.Parse(lines.Last()))
            Check(doc.RootElement.GetProperty("penalty").GetInt32() == 0 && doc.RootElement.GetProperty("fullAttack").GetBoolean(), "observed attack conditions retained");
        Check(r.Observe("a", Actual(Context())) == null && lines.Count == 2, "duplicate observed event ignored");
        r.RecordDamage("actual-damage-a", "a", Context(), 7, 20, 7, "incomplete cached defense evidence");
        using (var doc = JsonDocument.Parse(lines.Last()))
        {
            Check(doc.RootElement.GetProperty("rollEventId").GetString() == "a", "damage links to already observed roll");
            Check(doc.RootElement.GetProperty("reportedDamage").GetInt32() == 7, "observed damage recorded without recomputation");
        }
        int afterDamage = lines.Count;
        r.RecordDamage("actual-damage-a", "a", Context(), 8, 20, 8, "duplicate");
        Check(lines.Count == afterDamage, "duplicate damage event ignored");
        Check(r.Observe("b", Actual(Context())).Status == ComparisonStatus.NoPrediction, "prediction consumed once");
        r.RecordPrediction(Prediction(Context()));
        Check(r.Observe("c", Actual(Context("changed buff"))).Status == ComparisonStatus.ConditionsChanged, "state change invalidates comparison");
        r.RecordPrediction(Prediction(Context())); now = 1001;
        Check(r.Observe("d", Actual(Context())).Status == ComparisonStatus.SnapshotExpired, "stale prediction not compared");
        r.RecordPrediction(Prediction(Context()));
        Check(r.Observe("e", Actual(Context(complete: false))).Status == ComparisonStatus.UnverifiableConditions, "incomplete fingerprint not compared");
        r.RecordPrediction(Prediction(Context(), SnapshotOrigin.Manual));
        Check(r.Observe("f", Actual(Context())).Status == ComparisonStatus.ManualScenario, "manual not compared to target");
        r.RecordPrediction(Prediction(Context()));
        Check(r.Observe("g", Actual(Context(turn: "turn-2"))).Status == ComparisonStatus.NoPrediction, "different turn cannot reuse prediction");
        r.Invalidate();
        Check(r.Observe("h", Actual(Context())).Status == ComparisonStatus.NoPrediction, "scene/action invalidation clears pending");
        r.RecordPrediction(Prediction(Context()));
        var mismatch = r.Observe("i", new ActualAttackObservation(Context(), 23, 29, true, false, 0, "different damage", "different DR", hasWeaponAttack: true));
        Check(mismatch.AttackBonusMatches == false && mismatch.ArmorClassMatches == false && mismatch.DamageComponentsMatch == false && mismatch.ReductionsMatch == false,
            "deterministic discrepancy reported");
        now = 2002; r.RecordPrediction(Prediction(Context()));
        var automatic = r.Observe("j", new ActualAttackObservation(Context(), 99, 99, false, true, hasWeaponAttack: true));
        Check(!automatic.AttackBonusMatches.HasValue && !automatic.ArmorClassMatches.HasValue && !automatic.DamageComponentsMatch.HasValue, "automatic/unknown fields not compared");
        var weird = new DiagnosticContext("a\"\n\\\u0000한글😀", "b", "c", "d", "e", 0, "f", true);
        r.RecordPrediction(Prediction(weird));
        using (var doc = JsonDocument.Parse(lines.Last())) Check(doc.RootElement.GetProperty("attacker").GetString() == weird.Attacker, "arbitrary Unicode/control names escaped");
        using (var doc = JsonDocument.Parse("{\"name\":" + DiagnosticRecorder.Quote(new string('a', 511) + "😀tail") + "}"))
            Check(doc.RootElement.GetProperty("name").GetString() == new string('a', 511) + "[truncated]", "truncation cannot split a surrogate pair");
        using (var doc = JsonDocument.Parse("{\"name\":" + DiagnosticRecorder.Quote("\ud800") + "}"))
            Check(doc.RootElement.GetProperty("name").GetString() == "\ufffd", "unpaired UTF16 does not corrupt JSONL");
        int before = lines.Count;
        for (int i = 0; i < 300; i++) r.Observe("rate-" + i, Actual(Context()));
        Check(lines.Count - before <= 20 && r.Dropped > 0, "rate bounded at 20 records per second");
        r.Enabled = false; r.Enabled = true;
        Check(r.Observe("a", Actual(Context())).Status == ComparisonStatus.NoPrediction, "toggle clears pending and dedup state");
        var failed = new DiagnosticRecorder(line => { throw new IOException("disk full"); }, () => 0) { Enabled = true };
        Check(failed.RecordPrediction(Prediction(Context())) == null, "unwritten prediction cannot be linked later");
        Check(failed.Faulted && !failed.Enabled, "write failure disables diagnostics without throwing into game");
        failed.Enabled = true; Check(!failed.Enabled, "fault cannot silently resume writes");
        DiagnosticRecorder reentrant = null; int calls = 0;
        reentrant = new DiagnosticRecorder(line => { calls++; reentrant.Observe("nested", Actual(Context())); }, () => 0) { Enabled = true };
        reentrant.RecordPrediction(Prediction(Context()));
        Check(calls == 1, "sink reentrancy cannot recursively log");
        long observedTime = 100;
        var clockReset = new DiagnosticRecorder(line => { }, () => observedTime) { Enabled = true };
        clockReset.RecordPrediction(Prediction(Context())); observedTime = 0;
        Check(clockReset.Observe("clock-reset", Actual(Context())).Status == ComparisonStatus.SnapshotExpired, "clock regression invalidates pending prediction");
        string folder = Path.Combine(Path.GetTempPath(), "CombatPreview-diagnostics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "events.jsonl"); var log = new LocalDiagnosticLog(path, 16384);
            Check(!File.Exists(path), "no file created before enabled records");
            string line = "{\"label\":\"" + new string('한', 300) + "\"}";
            for (int i = 0; i < 100; i++) log.Write(line);
            Check(new FileInfo(path).Length <= 16384 && new FileInfo(path + ".prev").Length <= 16384, "two files bounded in UTF8 bytes");
            Check(Directory.GetFiles(folder).Length == 2, "rotation leaves only two files");
            foreach (var file in Directory.GetFiles(folder)) foreach (var entry in File.ReadLines(file)) using (JsonDocument.Parse(entry)) { }
            Check(true, "rotation keeps complete JSONL records");
        }
        finally { Directory.Delete(folder, true); }
    }
    static void DiagnosticRegressionFixtures()
    {
        // Regression: AddDamageResistanceBase initializes m_RemainPool for
        // non-pool components too; positive cache is NOT proof of a shield.
        var noPool = new ProtectionPoolSnapshot(false, 12);
        Check(noPool.Presence == ProtectionPoolPresence.Absent && !noPool.Remaining.HasValue, "unused positive pool cache is not consumable protection");
        var unknown = new ProtectionPoolSnapshot(null, 12);
        Check(unknown.Presence == ProtectionPoolPresence.Unknown && !unknown.Remaining.HasValue, "unknown UsePool does not infer presence");
        var empty = new ProtectionPoolSnapshot(true, 0);
        Check(empty.Presence == ProtectionPoolPresence.Present && empty.Remaining == 0, "empty pool remains a pool");
        Check(new ProtectionPoolSnapshot(true, 7).Remaining == 7, "verified pool flag retains observed balance");
        Check(!new ProtectionPoolSnapshot(true, -1).Remaining.HasValue, "negative balance is unavailable");
        var rows = new List<string>(); long now = 0;
        var r = new DiagnosticRecorder(rows.Add, () => now, "fixture-session-a") { Enabled = true };
        var metadata = new Dictionary<string, string> { { "gameVersion", "fixture-game" }, { "harmonyVersion", "fixture-harmony" } };
        r.SetRuntimeMetadata(metadata); metadata["gameVersion"] = "changed";
        r.RecordPrediction(Prediction(Context()));
        var fake = new ActualAttackObservation(Context(), 22, 28, true, true, isFake: true);
        Check(r.Observe("fake-a", fake) == null && r.IgnoredSimulated == 1 && rows.Count == 1, "fake roll cannot be logged or consume a prediction");
        Check(r.Observe("real-a", Actual(Context())).Status == ComparisonStatus.Compared, "real roll still matches after fake event");
        r.RecordPrediction(Prediction(Context()));
        var unassociated = new ActualAttackObservation(Context(), 22, 28, true, true);
        var result = r.Observe("unassociated-a", unassociated);
        Check(result.Status == ComparisonStatus.UnassociatedRoll && !result.AttackBonusMatches.HasValue, "unassociated roll not compared as a weapon attack");
        using (var doc = JsonDocument.Parse(rows.Last()))
            Check(doc.RootElement.GetProperty("kind").GetString() == "unassociated-observation", "unassociated records have a distinct kind");
        Check(r.Observe("real-b", Actual(Context())).Status == ComparisonStatus.Compared, "unassociated roll does not consume weapon prediction");
        var missing = new[] { new string('x', 500), new string('y', 500), "scope incomplete" };
        var details = new[] { "cached reduction=5; usePool=False; remainingPoolField=12" };
        r.RecordDamage("damage-a", "real-b", Context(), 4, 3, 4, "incomplete; inspect structured fields", missing, details);
        using (var doc = JsonDocument.Parse(rows.Last()))
        {
            Check(doc.RootElement.GetProperty("defenseMissing").GetArrayLength() == 3, "separate missing reasons survive aggregate length >512");
            Check(doc.RootElement.GetProperty("defenseObserved")[0].GetString() == details[0], "cached details are not hidden behind reasons");
            Check(doc.RootElement.GetProperty("runtime").GetProperty("gameVersion").GetString() == "fixture-game", "metadata copied before caller mutation");
            Check(doc.RootElement.GetProperty("sessionId").GetString() == "fixture-session-a", "session identity on damage records");
        }
        long last = 0;
        foreach (var line in rows) using (var doc = JsonDocument.Parse(line))
        {
            long seq = doc.RootElement.GetProperty("recordSequence").GetInt64();
            Check(seq > last && doc.RootElement.GetProperty("sessionId").GetString() == "fixture-session-a", "sequence and session carried by every record");
            Check(doc.RootElement.GetProperty("runtime").GetProperty("gameVersion").GetString() == "fixture-game", "runtime metadata survives missing session header or file rotation");
            last = seq;
        }
        var second = new DiagnosticRecorder(rows.Add, () => now, "fixture-session-b") { Enabled = true };
        second.Observe("real-a", Actual(Context()));
        using (var doc = JsonDocument.Parse(rows.Last()))
            Check(doc.RootElement.GetProperty("sessionId").GetString() == "fixture-session-b", "reused object IDs from a restart remain distinguishable");
        r.RecordDamage("budget", "real-b", Context(), 4, 3, 4, "incomplete", Enumerable.Range(0, 100).Select(i => "reason-" + i));
        using (var doc = JsonDocument.Parse(rows.Last()))
            Check(doc.RootElement.GetProperty("defenseMissing").GetArrayLength() == 33
                && doc.RootElement.GetProperty("defenseMissing")[32].GetString().Contains("truncated"), "bounded evidence arrays disclose truncation");
        r.Enabled = false;
        int ignored = r.IgnoredSimulated;
        Check(r.Observe("fake-off", fake) == null && r.IgnoredSimulated == ignored, "disabled observer does not process fake events");
    }
    static void SafetyAndBoundaries()
    {
        Check(!PredictionSafety.CanReplayEvents, "API preflight cannot enable live event replay");
        for (int i = 0; i < 3; i++) Throws<NotSupportedException>(PredictionSafety.RequireReadOnlyPrediction, "legacy replay denied across repeated attempts");
        var eight = Enumerable.Range(0, 8).Select(i => new D20Modifier { Chance = 50, Rerolls = 1, TakeBest = true }).ToList();
        Near(D20.Pmf(eight, d => d < 6).Sum(), 1, "eight-modifier boundary is normalized");
        eight.Add(new D20Modifier());
        Throws<NotSupportedException>(() => D20.Pmf(eight, d => false), "ninth modifier not silently dropped");
        Throws<NotSupportedException>(() => D20.Pmf(new List<D20Modifier> { new D20Modifier { Chance = 101 } }, d => false), "invalid d20 chance refused");
        Throws<NotSupportedException>(() => D20.Pmf(new List<D20Modifier> { new D20Modifier { Replace = true, Roll = 21 } }, d => false), "invalid replacement die refused");
        var complete = new DefenseProfile { Complete = true };
        Check(!MitigationMath.Calculate(new DamageTerm[] { null }, 2, .75, .075, complete).Available, "null damage unavailable");
        Check(!MitigationMath.Calculate(new[] { new DamageTerm(1, 6, 0, false) { Kind = (DamageKind)999 } }, 2, .75, .075, complete).Available, "new enum type unavailable");
    }
    public static void WriteDiagnosticFixture(string path)
    {
        if (File.Exists(path)) throw new IOException("Refusing to overwrite an existing report");
        long now = 0;
        var log = new LocalDiagnosticLog(path);
        var recorder = new DiagnosticRecorder(log.Write, () => now, "fixture-example") { Enabled = true };
        recorder.SetRuntimeMetadata(new Dictionary<string, string> { { "gameVersion", "FIXTURE, not a game run" } });
        var c = new DiagnosticContext("FIXTURE rider", "FIXTURE enemy", "FIXTURE longsword", "FIXTURE turn", "FIXTURE scene", 0, "fixture-condition-v1", true);
        recorder.RecordPrediction(Prediction(c));
        now = 10;
        recorder.Observe("fixture-observed-roll-1", Actual(c));
        recorder.RecordDamage("fixture-damage-1", "fixture-observed-roll-1", c, 7, 20, 7, "FIXTURE defense evidence, not game data", hasWeaponAttack: true);
        recorder.RecordPrediction(Prediction(c)); now = 20;
        recorder.Observe("fixture-observed-roll-2", Actual(new DiagnosticContext("FIXTURE rider", "FIXTURE enemy", "FIXTURE longsword", "FIXTURE turn", "FIXTURE scene", 0, "changed-buff", true)));
        Console.WriteLine("Wrote illustrative FIXTURE diagnostics (not a game observation): " + path);
    }
}
