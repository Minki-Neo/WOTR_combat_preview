using System;
using CombatPreview;

static class AttackActionTests
{
    static AttackActionInput Input() { return new AttackActionInput { Known = true, TargetMatches = true,
        StandardCurrent = 0, MoveCurrent = 0, StandardPredicted = 0, RangeKnown = true, InCurrentRange = true, RequestedFull = true }; }
    public static void Run()
    {
        int count = 0;
        Action<bool, string> check = (ok, reason) => { count++; if (!ok) throw new Exception(reason); };
        var input = Input(); input.Surprise = true; input.MoveCurrent = null;
        var plan = AttackActionPolicy.Decide(input);
        check(plan.Available && !plan.FullAttack && !plan.Excluded, "surprise standard action works without selected cursor attack or move attack slot");
        var surpriseModel = BetaModel.Build(new BetaInput("a", "b", "sword", "turn", "surprise", 16, 22, 20,
            1, 8, 5, 19, 2, plan.FullAttack, true, false), "surprise");
        check(surpriseModel.Rows.Count == 1 && surpriseModel.Rows[0].Penalty == 0, "surprise uses one highest-bonus attack despite BAB16");
        input.MovementRequired = true;
        check(AttackActionPolicy.Decide(input).Excluded, "surprise approach plus normal attack is excluded");
        input = Input(); input.Surprise = true; input.StandardCurrent = 5;
        check(AttackActionPolicy.Decide(input).Excluded, "surprise cannot spend a used standard action");
        input = Input(); input.PredictedAttack = true; input.PlannedType = 3;
        check(AttackActionPolicy.Decide(input).FullAttack, "ordinary confirmed full attack");
        input.Surprise = true;
        check(!AttackActionPolicy.Decide(input).FullAttack, "surprise never invents full attack from UI mode");
        input = Input(); input.PredictedAttack = true; input.PlannedType = 2; input.MoveCurrent = 5;
        check(AttackActionPolicy.Decide(input).Available && !AttackActionPolicy.Decide(input).FullAttack, "standard attack remains after normal movement");
        input.StandardCurrent = 5;
        check(AttackActionPolicy.Decide(input).Excluded, "spent turn excluded from total");
        input = Input(); input.TargetMatches = false;
        check(!AttackActionPolicy.Decide(input).Available && !AttackActionPolicy.Decide(input).Excluded, "another target's prediction is unknown not zero");
        input = Input(); input.PredictionsPending = true;
        check(!AttackActionPolicy.Decide(input).Available && !AttackActionPolicy.Decide(input).Excluded, "unselected pending prediction waits");
        input = Input(); input.RangeKnown = false;
        check(!AttackActionPolicy.Decide(input).Available && !AttackActionPolicy.Decide(input).Excluded, "unknown geometry does not fabricate single attack");
        input = Input(); input.OutOfRange = true;
        check(AttackActionPolicy.Decide(input).Excluded, "engine out-of-range result is an action exclusion");
        input = Input(); input.Overused = true;
        check(AttackActionPolicy.Decide(input).Available && !AttackActionPolicy.Decide(input).FullAttack,
            "aggregate Overused does not hide an in-range ordinary attack with standard action remaining");
        input.StandardCurrent = 5; input.MoveCurrent = 5;
        check(AttackActionPolicy.Decide(input).Excluded, "actually spent standard/move actions remain excluded");
        input = Input(); input.Overused = true; input.PlannedType = 1; input.MovementRequired = true; input.InCurrentRange = false;
        check(AttackActionPolicy.Decide(input).Excluded, "failed approach is not converted to an in-range attack");
        input = Input(); input.Overused = true; input.PredictedAttack = true; input.PlannedType = 3;
        check(AttackActionPolicy.Decide(input).Excluded, "contradictory selected attack does not gain an invented executable action");
        input = Input(); input.Overused = true; input.PlannedType = 1; input.StandardCurrent = 0; input.MoveCurrent = 0;
        check(AttackActionPolicy.Decide(input).Available, "logged movement Overused state retains independent standard attack when already in range");
        input = Input(); input.Overused = true; input.StandardCurrent = 4; input.MoveCurrent = 0;
        check(AttackActionPolicy.Decide(input).Excluded, "logged lost standard action is excluded, not mistaken for available movement attack");
        input = Input(); input.PredictedAttack = true; input.PlannedType = 4;
        check(!AttackActionPolicy.Decide(input).Available && !AttackActionPolicy.Decide(input).Excluded, "spell combat is not silently a normal attack");
        input = Input(); input.StandardCurrent = 5; input.PredictedAttack = true; input.PlannedType = 2;
        check(AttackActionPolicy.Decide(input).Excluded && !AttackActionPolicy.Decide(input).Available,
            "used standard action excludes attack even if old predicted single attack remains");
        input = Input(); input.StandardCurrent = 4; input.MoveCurrent = 0;
        check(AttackActionPolicy.Decide(input).Excluded, "lost standard action excluded even with movement available");
        input.PredictionsPending = true;
        check(AttackActionPolicy.Decide(input).Excluded, "pending prediction cannot restore a known lost standard action");
        input = Input(); input.StandardCurrent = null; input.PredictedAttack = true; input.PlannedType = 2;
        check(!AttackActionPolicy.Decide(input).Available && !AttackActionPolicy.Decide(input).Excluded,
            "unknown standard availability is not an available attack or a confirmed spent action");
        input = Input(); input.StandardCurrent = null; input.MoveCurrent = null;
        check(!AttackActionPolicy.Decide(input).Available && !AttackActionPolicy.Decide(input).Excluded, "missing action slots stay unknown");
        Console.WriteLine("PASS: " + count + " attack action availability assertions");
    }
}
