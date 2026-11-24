using System;
using System.Collections.Generic;
using System.Linq;
using CombatPreview;

static class ConditionLifetimeTests
{
    static int assertions;
    static void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
    public static void Run()
    {
        // Independent source-token oracle: immunity suppresses a condition,
        // without consuming any of its underlying source tokens.
        for (int sources = 0; sources <= 2; sources++)
        for (int immunity = 0; immunity <= 2; immunity++)
        for (int sequence = 0; sequence < 64; sequence++)
        {
            var counts = new ProjectedConditionCounts(new sbyte[] { (sbyte)sources }, new sbyte[] { (sbyte)immunity });
            var activeSources = Enumerable.Range(0, sources).ToList();
            var immuneSources = Enumerable.Range(0, immunity).ToList();
            int code = sequence;
            for (int step = 0; step < 3; step++, code /= 4)
            {
                int action = code % 4;
                var tokens = action >= 2 ? immuneSources : activeSources;
                if (action % 2 == 0) tokens.Add(10 + step);
                else if (tokens.Count > 0) tokens.RemoveAt(0);
                Check(counts.Change(0, action % 2 == 0 ? 1 : -1, action >= 2), "valid source update");
                Check(counts.Sources(0) == activeSources.Count, "source count matches token ledger");
                Check(counts.Active(0) == (activeSources.Any() && !immuneSources.Any()), "only the last source or immunity changes activity");
                Check(counts.Immune(0) == immuneSources.Any(), "immunity has its own lifetime");
            }
        }
        var raw = new sbyte[] { 1, 2 };
        var original = new ProjectedConditionCounts(raw, new sbyte[2]);
        raw[0] = 0;
        var sibling = original.Copy();
        original.Change(0, -1);
        Check(sibling.Active(0) == true && original.Active(0) == false, "branch and live arrays remain detached");
        Check(original.Sources(1) == 2 && sibling.Sources(1) == 2, "unrelated conditions remain unchanged");
        Check(!original.Change(-1, 1) && !original.Change(2, 1) && !original.Change(0, 0), "invalid updates rejected");
        Check(original.Active(2) == null, "missing slots remain unknown");
        var overflow = new ProjectedConditionCounts(new sbyte[] { 127 }, new sbyte[1]);
        Check(!overflow.Change(0, 1) && overflow.Sources(0) == 127, "byte overflow cannot fabricate a state");
        var corrupt = new ProjectedConditionCounts(new sbyte[] { 1 }, new sbyte[] { -1 });
        Check(corrupt.Active(0) == null && !corrupt.Change(0, 1), "corrupt immunity remains unknown");
        Check(ProjectedConditionCounts.Transition(true, false, true) == StatusTransition.None, "self status buff avoids recursive activation");
        Check(ProjectedConditionCounts.Transition(true, false, false) == StatusTransition.Activate, "active condition with no status can activate later");
        Check(ProjectedConditionCounts.Transition(false, true, true) == StatusTransition.Deactivate, "self guard never prevents deactivation");
        Check(ProjectedConditionCounts.Transition(true, true, false) == StatusTransition.None, "second source does not duplicate status");
        var ledger = new ProjectedBuffLedger();
        ledger.Seed("unrelated", "status", "caster-a", 1);
        ledger.Seed("owned", "status", "caster-b", 1);
        var copied = ledger.Copy();
        Check(ledger.RemoveToken("owned").Removed && !ledger.Removed("unrelated"), "status removes its exact owned buff instance");
        Check(ledger.RemoveToken("owned") == null && !copied.Removed("owned"), "owned removal is idempotent and branch-local");
        Console.WriteLine("PASS: " + assertions + " condition lifetime assertions");
    }
}
