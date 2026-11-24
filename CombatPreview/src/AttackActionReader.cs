using System;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.TurnBasedMode.Controllers;
using UnityEngine;

namespace CombatPreview
{
    internal static partial class BetaReader
    {
        static int? Activity(CombatAction action, string field)
        {
            var value = CombatFields.Read(action, field);
            return value == null ? (int?)null : Convert.ToInt32(value);
        }
        internal static AttackActionPlan AttackPlan(UnitEntityData actor, UnitEntityData target)
        {
            var state = ActionState(actor);
            var turn = Game.Instance?.TurnBasedCombatController?.CurrentTurn;
            var input = new AttackActionInput { Known = state != null && turn != null };
            if (!input.Known) return AttackActionPolicy.Decide(input);
            input.Surprise = state.IsSurprise || CombatFields.Bool(turn, "m_ActingInSurpriseRound");
            input.Overused = state.Overused; input.OutOfRange = state.IsOutOfRange;
            input.TargetMatches = ReferenceEquals(CombatFields.Read(turn, "m_HighlightedUnit"), target);
            input.PredictionsPending = CombatFields.Bool(turn, "m_NeedNewPredictions");
            input.StandardCurrent = Activity(state.Standard, "m_AttackActivityStateCurrent");
            input.StandardPredicted = Activity(state.Standard, "m_AttackActivityStatePredicted");
            input.MoveCurrent = Activity(state.Move, "m_AttackActivityStateCurrent");
            input.MovePredicted = Activity(state.Move, "m_AttackActivityStatePredicted");
            input.PredictedAttack = state.IsAttack();
            var type = CombatFields.Read(state.Standard, "<Type>k__BackingField");
            if (state.Standard?.AttackActivityStatePredicted != CombatAction.ActivityState.WillBeUsed)
                type = CombatFields.Read(state.Move, "<Type>k__BackingField");
            input.PlannedType = type == null || Convert.ToInt32(type) == 0 ? (int?)null : Convert.ToInt32(type);
            input.RequestedFull = FullMode(actor);
            var distance = CombatFields.Read(state.Move, "PredictedMoveDistance") as float?;
            var standardDistance = CombatFields.Read(state.Standard, "PredictedMoveDistance") as float?;
            input.RangeKnown = state.ApproachRadius > 0 && !float.IsNaN(state.ApproachRadius) && !float.IsInfinity(state.ApproachRadius);
            input.InCurrentRange = input.RangeKnown && (actor.Position - state.ApproachPoint).sqrMagnitude <= state.ApproachRadius * state.ApproachRadius;
            input.MovementRequired = distance > .01f || standardDistance > .01f || input.RangeKnown && !input.InCurrentRange;
            return AttackActionPolicy.Decide(input);
        }
    }
}
