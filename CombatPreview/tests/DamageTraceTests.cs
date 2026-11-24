using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CombatPreview;

static class DamageTraceTests
{
    public static void Run()
    {
        int count = 0;
        Action<bool, string> check = (ok, reason) => { count++; if (!ok) throw new Exception(reason); };
        string memo;
        ScopedReadMemo<string, string>.Put("unit", "outside");
        check(!ScopedReadMemo<string, string>.TryGet("unit", out memo), "no state cached outside capture");
        using (ScopedReadMemo<string, string>.Begin())
        {
            ScopedReadMemo<string, string>.Put("unit", "buff-before");
            check(ScopedReadMemo<string, string>.TryGet("unit", out memo) && memo == "buff-before", "reuse within synchronous read");
            using (ScopedReadMemo<string, string>.Begin())
            {
                check(!ScopedReadMemo<string, string>.TryGet("unit", out memo), "nested callback cannot reuse outer state");
                ScopedReadMemo<string, string>.Put("unit", "nested-buff");
            }
            check(ScopedReadMemo<string, string>.TryGet("unit", out memo) && memo == "buff-before", "nested scope restores outer read");
        }
        check(!ScopedReadMemo<string, string>.TryGet("unit", out memo), "state released after capture");
        try { using (ScopedReadMemo<string, string>.Begin()) { ScopedReadMemo<string, string>.Put("unit", "exception"); throw new InvalidOperationException(); } }
        catch (InvalidOperationException) { }
        check(!ScopedReadMemo<string, string>.TryGet("unit", out memo), "exception also releases memoized state");
        using (ScopedReadMemo<string, string>.Begin())
            check(!ScopedReadMemo<string, string>.TryGet("unit", out memo), "new callback sees no stale buff state");
        var profile = new DefenseProfile { Complete = true };
        profile.Reduction[DamageKind.Physical] = 5;
        // Actual low and high samples use the same rule, never the expected mean.
        check(DamageTrace.ReplayStaticDefense(13, DamageKind.Physical, profile) == 8, "halved observed sample then DR");
        check(DamageTrace.ReplayStaticDefense(3, DamageKind.Physical, profile) == 0, "DR clips low actual roll");
        check(DamageTrace.ReplayStaticDefense(27, DamageKind.Physical, profile) == 22, "high actual roll is not a mismatch by itself");
        check(DamageTrace.ReconstructFinal(13, 5, null) == 8, "component arithmetic");
        check(DamageTrace.ReconstructFinal(13, 5, 4) == 4, "maximum cap");
        check(DamageTrace.ReconstructFinal(2, 5, null) == 0, "nonnegative floor");
        profile.Immunities.Add(DamageKind.Fire);
        check(DamageTrace.ReplayStaticDefense(30, DamageKind.Fire, profile) == 0, "typed immunity");
        check(DamageTrace.ReplayStaticDefense(30, DamageKind.Cold, profile) == 30, "physical DR not applied to cold");
        foreach (var kind in new[] { DamageKind.Unknown, DamageKind.Physical })
        {
            profile.Complete = kind == DamageKind.Unknown;
            bool rejected = false;
            try { DamageTrace.ReplayStaticDefense(13, kind, profile); } catch (NotSupportedException) { rejected = true; }
            check(rejected, "unknown and incomplete are never zero-defense");
        }
        profile.Complete = true; profile.HasConsumablePool = true;
        bool poolRejected = false;
        try { DamageTrace.ReplayStaticDefense(13, DamageKind.Physical, profile); } catch (NotSupportedException) { poolRejected = true; }
        check(poolRejected, "pool requires sequential model");
        var trace = new Dictionary<string, object> {
            { "missing", new[] { "unknown\n\"source" } }, { "amount", (int?)null },
            { "fullReplayVerified", false }, { "components", new[] { new Dictionary<string, object> { { "roll", 27 } } } },
            { "invalid", double.NaN }
        };
        trace["enum"] = DamageKind.Physical;
        using (var doc = JsonDocument.Parse(DamageTrace.Json(trace)))
        {
            check(doc.RootElement.GetProperty("amount").ValueKind == JsonValueKind.Null, "missing value stays null");
            check(doc.RootElement.GetProperty("missing")[0].GetString() == "unknown\n\"source", "JSON escaping");
            check(!doc.RootElement.GetProperty("fullReplayVerified").GetBoolean(), "no full accuracy claim");
            check(doc.RootElement.GetProperty("components")[0].GetProperty("roll").GetInt32() == 27, "structured components");
            check(doc.RootElement.GetProperty("invalid").ValueKind == JsonValueKind.Null, "finite JSON numbers only");
            check(doc.RootElement.GetProperty("enum").GetString() == "Physical", "enum evidence serialized");
        }
        var rows = new List<string>();
        var recorder = new DiagnosticRecorder(rows.Add, () => 0); recorder.Enabled = true;
        recorder.RecordDamage("damage", "roll", new DiagnosticContext("a", "b", "w", "t", "s", 0, "", false),
            8, 27, 8, "evidence", damageTrace: trace);
        using (var doc = JsonDocument.Parse(rows[0]))
        {
            check(doc.RootElement.GetProperty("rollEventId").GetString() == "roll", "trace retains real roll link");
            check(doc.RootElement.GetProperty("damageTrace").GetProperty("components")[0].GetProperty("roll").GetInt32() == 27, "trace emitted in damage record");
        }
        var original = new System.Collections.Generic.Dictionary<string, object> { ["numbers"] = Enumerable.Range(0, 300).Select(value => (object)value).ToArray(), ["bonus"] = 12 };
        var frozen = FrozenDiagnosticJson.Capture(original);
        original["bonus"] = 10;
        using (var captured = System.Text.Json.JsonDocument.Parse(DamageTrace.Json(new System.Collections.Generic.Dictionary<string, object> { ["frozenDisplayedInputs"] = frozen })))
        {
            check(captured.RootElement.GetProperty("frozenDisplayedInputs").GetProperty("bonus").GetInt32() == 12, "displayed raw snapshot is immutable");
            check(captured.RootElement.GetProperty("frozenDisplayedInputs").GetProperty("numbers").GetArrayLength() == 300, "nested snapshot is structured JSON, not a truncated 512-character string");
        }
        Console.WriteLine("PASS: " + count + " damage trace assertions");
    }
}
