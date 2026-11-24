using System;
using System.Collections.Generic;
using CombatPreview;
static class CombatRegression68Tests
{
    public static void Run()
    {
        int count = 0;
        void Check(bool value, string name) { count++; if (!value) throw new Exception(name); }
        var input = new BetaInput("actor", "target", "offhand", "turn", "unchanged", 16, 22, 28, 1, 8, 15, 19, 2,
            true, true, false, attackPenalties: new[] { 0, 0, 5 });
        BetaComparison Compare(AttackRowLink link, BetaInput now = null)
        {
            var tracker = new BetaTracker(); tracker.Shown(BetaModel.Build(input, "shown"), 0);
            tracker.Begin("actual", now ?? input, 3, true, 1, attackLink: link);
            return tracker.Finish("actual", "actor", "target", "offhand", true, 22, 28, 1, 8, 15, 2, 1);
        }
        var firstOffhand = Compare(new AttackRowLink { Exact = true, GlobalIndex = 3, PlanIndex = 3, WeaponOrdinal = 0, Penalty = 0 });
        Check(firstOffhand.Status == BetaComparisonStatus.ComparedPartialModel && firstOffhand.Row.Index == 0,
            "global fourth attack connects to first offhand row by exact rule identity");
        var haste = Compare(new AttackRowLink { Exact = true, GlobalIndex = 4, WeaponOrdinal = 1, Penalty = 0 });
        Check(haste.Status == BetaComparisonStatus.ComparedPartialModel && haste.Row.Index == 1, "same-penalty extra attack retains distinct weapon ordinal");
        Check(Compare(new AttackRowLink { Exact = false, WeaponOrdinal = 0 }).Status == BetaComparisonStatus.UnmodeledAttack,
            "unverified command association cannot certify a comparison");
        Check(Compare(new AttackRowLink { Exact = true, WeaponOrdinal = 1, Penalty = 5 }).Status == BetaComparisonStatus.UnmodeledAttack,
            "changed penalty cannot be matched to same ordinal");
        Check(Compare(new AttackRowLink { Exact = true, WeaponOrdinal = 8 }).Status == BetaComparisonStatus.UnmodeledAttack,
            "extra unmodeled natural attack does not reuse first row");
        var changed = input.WithConcealment(.5, "changed");
        var unequal = Compare(new AttackRowLink { Exact = true, WeaponOrdinal = 0, Penalty = 0 }, changed);
        Check(unequal.Status == BetaComparisonStatus.InputsChanged && unequal.Row == null && unequal.PreviousRow != null,
            "exact row association never overrides changed combat conditions");
        Check(DamageTrace.Json(DisplayedCombatEvidence.Capture(unequal)).Contains("\"concealmentMiss\":0.5"), "attack-start changed inputs retained alongside displayed prediction");
        var dice = new D20RollModel();
        var mutable = input.WithRollModel(dice, "roll-model");
        var tracker2 = new BetaTracker(); tracker2.Shown(BetaModel.Build(mutable, "freeze"), 0);
        tracker2.Begin("freeze", mutable, 0, true, 1, attackLink: new AttackRowLink { Exact = true, WeaponOrdinal = 0 });
        dice.AfterRoll.Add(-1);
        var frozen = tracker2.Finish("freeze", "actor", "target", "offhand", true, 22, 28, 1, 8, 15, 2, 1);
        Check(frozen.InputAtBegin.Value == DamageTrace.Json(BetaInputEvidence.Capture(input.WithRollModel(new D20RollModel(), "roll-model"))),
            "start evidence is frozen before live dice model mutates");
        var noIssue = new[] { new SpellCheckRecord { Kind = "attack", Status = "Available", Probability = 0 } };
        Check(PreviewIssuePresentation.Build(new Dictionary<string, object>(), noIssue) == null, "known zero probability is not unavailable");
        var unavailable = new[] { new SpellCheckRecord { Kind = "save", Status = "Unavailable", Reason = "MetamagicUnknown" } };
        var issue = PreviewIssuePresentation.Build(new Dictionary<string, object> { { "automaticMetamagicStatus", "AutoMetamagicDerivedType:SpecialAuto" } }, unavailable);
        Check((string)issue["sourceType"] == "SpecialAuto" && (string)issue["summary"] == "자동 메타매직 조건 미확인", "specific unsupported source retained without verbose normal UI");
        Check(!(bool)issue["zeroProbability"], "unknown is never presented as zero");
        for (int type = 0; type <= 6; type++)
        {
            Check(AutoMetamagicRules.Suitable(0, type, -1, false) == (type == 0), "spell-only filter");
            Check(AutoMetamagicRules.Suitable(-1, type, -1, false) == (type == 0 || type == 1), "spell or spelllike filter");
            Check(AutoMetamagicRules.Suitable(4, type, -1, false) == (type != 6), "nonphysical filter");
            Check(AutoMetamagicRules.Suitable(3, type, 2, false) == false, "ordinary any filter still excludes item casts");
        }
        Check(AutoMetamagicRules.Suitable(5, 0, 3, false) == true && AutoMetamagicRules.Suitable(5, 0, 2, false) == false, "potion-specific automatic metamagic");
        Check(AutoMetamagicRules.Suitable(6, 0, 2, false) == true && AutoMetamagicRules.Suitable(6, 0, -1, false) == false, "scroll-specific automatic metamagic");
        Check(AutoMetamagicRules.Suitable(2, 6, -1, true) == true, "kinetic blast suitability");
        Check(AutoMetamagicRules.Suitable(99, 0, -1, false) == null, "new enum values remain unknown");
        Check(AutoMetamagicRules.And(true, null, false) == false, "known exclusion defeats missing irrelevant filter");
        Check(AutoMetamagicRules.And(true, null, true) == null, "missing required filter remains unknown");
        Check(AutoMetamagicRules.Or(null, true) == true && AutoMetamagicRules.Or(null, false) == null, "loaded child match can resolve unknown parent, rejected child cannot");
        Console.WriteLine("PASS: " + count + " attack association / unavailable UI / automatic metamagic regressions");
    }
}
