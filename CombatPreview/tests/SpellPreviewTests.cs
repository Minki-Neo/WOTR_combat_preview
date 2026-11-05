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
