using System;
using System.Collections.Generic;
using System.Linq;
using CombatPreview;
static class PreviewSchedulingTests
{
    static int count;
    static void Check(bool value, string message) { count++; if (!value) throw new Exception(message); }
    public static void Run()
    {
        foreach (double cost in new[] { .001, .005 })
        {
            var targets = Enumerable.Range(0, 32).Select(value => "target-" + value).ToArray();
            var queue = new PreviewWorkQueue<string>();
            queue.Begin(targets, targets[19]);
            var seen = new HashSet<string>(); int frames = 0;
            while (queue.PendingCount > 0)
            {
                int processed = 0; double elapsed = 0;
                queue.Reconcile(targets, targets[19]);
                while (PreviewWorkQueue<string>.CanContinue(processed, elapsed) && queue.Take(out var target))
                {
                    if (seen.Count == 0) Check(target == targets[19], "cursor target is evaluated first");
                    Check(seen.Add(target), "stable observations never requeue completed targets");
                    processed++; elapsed += cost;
                }
                Check(processed == (cost > .003 ? 1 : 2), "per-frame count and soft time budget");
                frames++;
            }
            Check(seen.Count == 32 && frames == (cost > .003 ? 32 : 16), "all AoE targets eventually complete without one-frame burst");
        }
        var changed = new PreviewWorkQueue<string>();
        changed.Begin(new[] { "old", "keep", "keep" }, null);
        changed.Reconcile(new[] { "keep", "new" }, "new");
        Check(changed.PendingCount == 2 && changed.Take(out var first) && first == "new", "new priority enters and obsolete target leaves");
        Check(changed.Take(out var second) && second == "keep", "remaining original member is not starved");
        changed.Reconcile(new[] { "keep", "new" }, "new");
        Check(changed.PendingCount == 0, "membership refresh is not a cycle reset");
        changed.Clear(); changed.Begin(new[] { "keep" }, null);
        Check(changed.PendingCount == 1, "new ability/turn can restart a clean cycle");
        Check(PreviewWorkQueue<string>.CanContinue(0, 1), "one heavy target must still make progress");
        Check(!PreviewWorkQueue<string>.CanContinue(1, .004), "heavy first target ends this frame's work");
        Check(!PreviewWorkQueue<string>.CanContinue(2, 0), "fast targets still respect the hard count cap");
        var refresh = new PreviewWorkQueue<string>();
        refresh.Begin(new[] { "hover", "other", "last" }, "hover");
        refresh.Take(out _);
        refresh.RefreshPriority("hover");
        Check(refresh.Take(out var reacquired) && reacquired == "hover", "completed hover can refresh without waiting for area cycle");
        Check(refresh.PendingCount == 2, "priority refresh preserves other work");
        refresh.RefreshPriority("excluded");
        Check(refresh.PendingCount == 2, "refresh cannot re-add excluded targets");
        refresh.RefreshPriority("other"); refresh.RefreshPriority("other");
        Check(refresh.PendingCount == 2, "repeated priority requests do not duplicate work");
        long now = 0; int written = 0; bool enumerated = false;
        var recorder = new DiagnosticRecorder(line => written++, () => now, "budget-test") { Enabled = true };
        var context = new DiagnosticContext("caster", "target", "spell", "turn", "scene", 0, "", false);
        for (int i = 0; i < 20; i++) Check(recorder.RecordSpellPreview(context, "spell", new[] { "ok" }, true), "initial log allowance");
        IEnumerable<string> Expensive()
        { enumerated = true; yield return "would serialize metadata"; }
        Check(!recorder.RecordSpellPreview(context, "spell", Expensive(), true), "excess preview is rate limited");
        Check(!enumerated && recorder.Dropped == 1 && written == 20, "dropped preview never enumerates payload before rate check");
        now = 1000;
        Check(recorder.RecordSpellPreview(context, "spell", Expensive(), true) && enumerated && written == 21, "next log window restores serialization");
        var rawLines = new List<string>();
        var separateBudget = new DiagnosticRecorder(rawLines.Add, () => 0, "raw-budget") { Enabled = true };
        for (int i = 0; i < 20; i++) separateBudget.RecordSpellPreview(context, "spell", new[] { "preview" }, true);
        separateBudget.RecordSpellObservation("actual-save", context, new Dictionary<string, object> { ["checkKind"] = "save", ["dc"] = 25 });
        Check(rawLines.Count == 21 && separateBudget.DroppedRaw == 0, "preview budget exhaustion cannot consume actual-event budget");
        for (int i = 0; i < 256; i++) separateBudget.RecordSpellObservation("actual-" + i, context, new Dictionary<string, object> { ["checkKind"] = "save" });
        Check(separateBudget.DroppedRaw == 1, "raw cap is counted separately");
        Console.WriteLine("PASS: " + count + " AoE scheduling / early log budget assertions");
    }
}
