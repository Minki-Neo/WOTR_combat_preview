using System.Collections.Generic;
using System.Linq;
using Kingmaker.EntitySystem.Entities;

namespace CombatPreview
{
    internal static class SpellSequenceProjection
    {
        internal static void Append(Dictionary<string, object> metadata, UnitEntityData target, SpellDamageConditions conditions)
        {
            if (conditions == null || !metadata.TryGetValue("spellDamage", out var raw)
                || !(raw is Dictionary<string, object> model) || !(model["terms"] is List<Dictionary<string, object>> terms)
                || !BetaReader.ReadHealth(target, out long hp, out _, out int temporaryHp)) return;
            var hpTerms = terms.Where(term => Equals(term["hpDamage"], true)).ToArray();
            if (hpTerms.Length == 0 || !Equals(model["scanTruncated"], false)
                || !Equals(model["unloadedLinks"], 0) || !(model["deliveryCount"] is int count)) return;
            var parts = new List<SpellRollPart>();
            bool poolOnlyIncomplete = false;
            foreach (var term in hpTerms)
            {
                if (!Equals(term["branchCandidate"], false) || !Equals(term["path"], "cast")
                    || !Equals(term["meanRepeats"], 1.0) || Equals(term["alreadyHalved"], true)
                    || term["minimumHpAfterDamage"] != null
                    || !(term["dice"] is int dice) || !(term["sides"] is int sides) || !(term["bonus"] is int bonus)
                    || !Equals(term["usesSharedDefinition"], false)) return;
                if (!System.Enum.TryParse((string)term["damageType"], out DamageKind kind) || kind == DamageKind.Unknown) return;
                var traits = new WeaponTraits { Known = true, Form = (int)term["physicalForm"] };
                var defense = AutomaticDefenseReader.Read(target, null, traits, out _, energyOnly: kind != DamageKind.Physical,
                    readEnergyPools: true, incomingKind: kind);
                if (!defense.Complete && !defense.PoolApproximationAllowed || conditions.HasSave && !Equals(term["halfIfSaved"], true)) return;
                poolOnlyIncomplete |= !defense.Complete;
                defense.Reduction.TryGetValue(kind, out int reduction);
                double decline = defense.IncorporealEnergyDivisor && kind != DamageKind.PositiveEnergy && kind != DamageKind.NegativeEnergy
                    && kind != DamageKind.Holy && kind != DamageKind.Unholy && kind != DamageKind.Divine ? .5 : 1;
                parts.Add(new SpellRollPart { Kind = kind, Dice = dice, Sides = System.Math.Max(1, sides), Bonus = bonus, Reduction = reduction,
                    SharedRoll = (int)term["sharedRollGroup"], Maximize = Equals(term["maximize"], true),
                    Empower = Equals(term["empower"], true), Half = Equals(term["effectiveHalf"], true),
                    Immune = defense.Immunities.Contains(kind), Protections = SpellProtectionReader.Read(defense, kind),
                    Vulnerability = SpellTargetScaling.Factor(term),
                    BonusPercent = SpellTargetScaling.Percent(term),
                    FailedFactor = decline * (conditions.ImprovedEvasion ? conditions.SavedMultiplier : 1),
                    PassedFactor = !conditions.HasSave ? decline : conditions.Evasion ? 0 : decline * conditions.SavedMultiplier });
            }
            using (PreviewPerformance.Measure("spell-sequence-distribution"))
            {
                var result = poolOnlyIncomplete ? null : SpellSequenceMath.Compute(parts.ToArray(), count,
                    conditions.MirrorImages > 0 ? conditions.HitBeforeImages : conditions.Hit,
                    conditions.Penetration, conditions.SaveFailure, hp, System.Math.Max(0, temporaryHp),
                    conditions.MirrorImages, conditions.MirrorImages > 0 ? conditions.MirrorNearMiss : 0);
                if (result == null) result = MeanSpellShieldApproximation.Compute(parts.ToArray(), count,
                    conditions.MirrorImages > 0 ? conditions.HitBeforeImages : conditions.Hit,
                    conditions.Penetration, conditions.SaveFailure, hp, System.Math.Max(0, temporaryHp),
                    conditions.MirrorImages, conditions.MirrorImages > 0 ? conditions.MirrorNearMiss : 0);
                model["sequenceProjection"] = new Dictionary<string, object> {
                    { "status", result == null ? "DistributionBudgetOrUnsupportedInputs" : result.Approximate ? "ApproximateShieldMean" : "Computed" },
                    { "approximate", result?.Approximate }, { "assumptions", result?.Approximate == true ? MeanWeaponShieldApproximation.Assumptions + " · 공유 굴림/내성 상관 및 HP 상한 근사" : null },
                    { "expectedHpLoss", result?.HpLoss }, { "expectedTemporaryHpLoss", result?.TemporaryHpLoss }, { "killChanceFromDamage", result != null && !result.Approximate ? (object)result.KillChance : null },
                    { "expectedDamage", result?.ExpectedDamage },
                    { "protectionSources", parts.Select((part, index) => new Dictionary<string, object> {
                        { "part", index }, { "sources", (part.Protections ?? new SpellProtection[0]).Select(source =>
                            new Dictionary<string, object> { { "order", source.Order }, { "remaining", source.Remaining }, { "perHit", source.PerHit } }).ToArray() }
                    }).ToArray() },
                    { "operations", result?.Operations },
                    { "scope", "supported direct damage; shared rolls and save within each delivery; independent delivery gates; current defenses" }
                };
                if (result != null) model[result.Approximate ? "expectedApproximateTotal" : "expectedSupportedTotal"] = result.ExpectedDamage;
            }
        }
    }
}
