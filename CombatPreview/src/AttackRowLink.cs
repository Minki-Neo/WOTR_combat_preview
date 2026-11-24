using System.Collections.Generic;
namespace CombatPreview
{
    public sealed class AttackRowLink
    {
        public string Status, CommandId;
        public int GlobalIndex, PlanIndex = -1, WeaponOrdinal = -1, Penalty;
        public bool Exact;
        public Dictionary<string, object> Data() => new Dictionary<string, object> {
            { "status", Status }, { "commandId", CommandId }, { "globalAttackIndex", GlobalIndex },
            { "planIndex", PlanIndex }, { "weaponOrdinal", WeaponOrdinal }, { "attackPenalty", Penalty },
            { "exactRuleIdentity", Exact }, { "scope", "Row identity only; does not certify equal combat conditions" }
        };
    }
    internal static class BetaInputEvidence
    {
        internal static Dictionary<string, object> Capture(BetaInput input) => input == null ? null : new Dictionary<string, object> {
            { "actorId", input.Actor }, { "targetId", input.Target }, { "weaponId", input.Weapon }, { "turnId", input.Turn },
            { "signature", input.Signature }, { "source", input.Source }, { "baseAb", input.AttackBonus }, { "ac", input.AC },
            { "rolls", input.Rolls }, { "sides", input.Sides }, { "bonus", input.Bonus },
            { "fullAttack", input.FullAttack }, { "criticalEdge", input.CriticalEdge }, { "criticalMultiplier", input.Multiplier },
            { "attackPenalties", input.AttackPenalties }, { "modifierNotes", input.ModifierNotes }, { "defenseNotes", input.DefenseNotes },
            { "pendingAttack", input.PendingAttack }, { "pendingDamage", input.PendingDamage },
            { "concealmentMiss", input.ConcealmentMiss }, { "mirrorImages", input.MirrorImages },
            { "d20Model", input.AttackRollModel?.Data() }
        };
    }
}
