using System.Collections.Generic;
using System.Linq;

namespace CombatPreview
{
    internal static class DisplayedCombatEvidence
    {
        internal static Dictionary<string, object> Capture(BetaComparison comparison)
        {
            var preview = comparison?.Preview;
            if (preview == null) return null;
            var input = preview.Input; var defense = input.Defense;
            var row = comparison.Row ?? comparison.PreviousRow;
            return new Dictionary<string, object> {
                { "attackLink", comparison.AttackLink?.Data() }, { "inputAtAttackBegin", comparison.InputAtBegin },
                { "healthProjection", comparison.HealthCapturedAtBegin ? comparison.HealthAtBegin : preview.DisplayedHealth },
                { "previewId", preview.Id }, { "comparisonStatus", comparison.Status.ToString() },
                { "rowIndex", row == null ? null : (object)row.Index },
                { "rowAssociation", comparison.Row != null ? "ComparableRow" : row != null ? "PreviousDisplayedRowOnly" : "NoRetainedRow" },
                { "actorId", input.Actor }, { "targetId", input.Target }, { "weaponId", input.Weapon },
                { "inputSignature", input.Signature }, { "source", input.Source },
                { "ab", row == null ? null : (object)row.AB }, { "ac", row == null ? null : (object)row.AC },
                { "hitChance", row == null ? null : (object)row.Hit }, { "criticalChance", row == null ? null : (object)row.Crit },
                { "criticalEdge", input.CriticalEdge }, { "criticalMultiplier", input.Multiplier },
                { "damageKnown", input.DamageKnown }, { "fullAttack", input.FullAttack },
                { "attackPenalties", input.AttackPenalties.ToArray() }, { "attackCountSource", input.AttackCountSource },
                { "rows", preview.Rows.Select(value => new Dictionary<string, object> {
                    { "index", value.Index }, { "ab", value.AB }, { "ac", value.AC }, { "hitChance", value.Hit },
                    { "critChance", value.Crit }, { "expectedRaw", value.ExpectedRaw },
                    { "defenseAvailable", value.AfterDefense?.Available },
                    { "meanNormal", value.AfterDefense?.Available == true ? (object)value.AfterDefense.Normal.Mean : null },
                    { "meanCritical", value.AfterDefense?.Available == true ? (object)value.AfterDefense.Critical.Mean : null },
                    { "expectedAfterDefense", value.AfterDefense?.Available == true ? (object)value.AfterDefense.Expected : null }
                }).ToArray() },
                { "pendingAttack", input.PendingAttack }, { "pendingDamage", input.PendingDamage },
                { "damageComponents", input.Components.Select(part => new Dictionary<string, object> {
                    { "kind", part.Kind.ToString() }, { "rolls", part.Rolls }, { "sides", part.Sides },
                    { "bonus", part.Bonus }, { "multiplies", part.Multiplies }
                }).ToArray() },
                { "modifierNotes", input.ModifierNotes.ToArray() }, { "defenseNotes", input.DefenseNotes.ToArray() },
                { "d20Model", input.AttackRollModel?.Data() }, { "concealmentMiss", input.ConcealmentMiss }, { "mirrorImages", input.MirrorImages },
                { "defense", defense == null ? null : new Dictionary<string, object> {
                    { "complete", defense.Complete }, { "unavailableReason", defense.UnavailableReason }, { "source", defense.Source },
                    { "reductionByKind", defense.Reduction.ToDictionary(pair => pair.Key.ToString(), pair => (object)pair.Value) },
                    { "immunities", defense.Immunities.Select(kind => kind.ToString()).ToArray() },
                    { "physicalDecline", defense.PhysicalDecline }, { "criticalImmune", defense.CriticalImmune },
                    { "clusteredPhysicalDR", defense.ClusteredPhysicalDR }, { "hasConsumablePool", defense.HasConsumablePool },
                    { "incorporealEnergyDivisor", defense.IncorporealEnergyDivisor }, { "poolApproximationAllowed", defense.PoolApproximationAllowed },
                    { "constantEnergyResistance", defense.ConstantEnergyResistance.ToDictionary(pair => pair.Key.ToString(), pair => (object)pair.Value) },
                    { "energyPools", defense.EnergyPools.ToDictionary(pair => pair.Key.ToString(), pair => (object)new Dictionary<string, object> {
                        { "remaining", pair.Value.Remaining }, { "perHit", pair.Value.PerHit } }) },
                    { "physicalPool", defense.PhysicalPool.HasValue ? new Dictionary<string, object> {
                        { "remaining", defense.PhysicalPool.Value.Remaining }, { "perHit", defense.PhysicalPool.Value.PerHit } } : null },
                    { "physicalSources", defense.PhysicalSequenceSources.Select(source => new Dictionary<string, object> {
                        { "order", source.Order }, { "remaining", source.Remaining }, { "perHit", source.PerHit } }).ToArray() },
                    { "energySources", defense.EnergySequenceSources.ToDictionary(pair => pair.Key.ToString(), pair => (object)pair.Value.Select(source => new Dictionary<string, object> {
                        { "order", source.Order }, { "remaining", source.Remaining }, { "perHit", source.PerHit } }).ToArray()) }
                } },
                { "scope", "Original displayed model retained even when conditions changed. Unequal inputs are evidence, not automatically proof of a calculation bug." }
            };
        }
    }
}
