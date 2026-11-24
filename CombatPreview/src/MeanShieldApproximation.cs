using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatPreview
{
    // Deliberately bounded mean-state approximation. No roll distribution, game
    // objects or mutation of the supplied protection snapshots.
    internal sealed class MeanShieldLedger
    {
        sealed class Pool { internal double Remaining; }
        readonly Dictionary<string, Pool> pools = new Dictionary<string, Pool>();
        double clusteredAbsorbed;
        internal (double Normal, double Critical) Apply(DamageKind kind, double normal, double critical,
            double hit, double crit, IEnumerable<SpellProtection> protections, bool clustered)
        {
            Pool selected = null; double best = 0; int index = 0;
            // A constant wins ties. Unknown registration order is intentionally
            // replaced by this documented approximation rule.
            foreach (var source in protections ?? Array.Empty<SpellProtection>())
            {
                string key = source.Order >= 0 ? "order:" + source.Order : kind + ":unknown:" + source.Remaining + ":" + source.PerHit + ":" + index;
                index++; Pool pool = null;
                double available = source.PerHit;
                if (source.Remaining >= 0)
                {
                    if (!pools.TryGetValue(key, out pool)) pools[key] = pool = new Pool { Remaining = source.Remaining };
                    available = Math.Min(available, pool.Remaining);
                }
                if (clustered && kind == DamageKind.Physical) available = Math.Max(0, available - clusteredAbsorbed);
                if (available > best || available == best && best > 0 && selected != null && pool == null)
                { best = available; selected = pool; }
            }
            double absorbed = (hit - crit) * Math.Min(normal, best) + crit * Math.Min(critical, best);
            if (selected != null) selected.Remaining = Math.Max(0, selected.Remaining - absorbed);
            if (clustered && kind == DamageKind.Physical) clusteredAbsorbed += absorbed;
            return (Math.Max(0, normal - best), Math.Max(0, critical - best));
        }
    }
    internal sealed class MeanWeaponShieldApproximation
    {
        internal const string Assumptions = "평균 피해/평균 보호막 잔량 · 최대 감소 소스 선택(동률 상시 DR 우선) · 소모 분기 상관 생략";
        readonly DefenseProfile profile;
        readonly bool clustered;
        readonly MeanShieldLedger ledger = new MeanShieldLedger();
        internal MeanWeaponShieldApproximation(DefenseProfile profile, bool full)
        { this.profile = profile; clustered = full && profile?.ClusteredPhysicalDR == true; }
        internal MitigatedResult Apply(IList<DamageTerm> terms, int multiplier, double hit, double crit)
        {
            if (profile == null || !(profile.Complete || profile.PoolApproximationAllowed)
                || !(profile.PhysicalPool.HasValue || profile.PhysicalSequenceSources.Any(source => source.Remaining >= 0)
                    || profile.EnergyPools.Count > 0 || profile.EnergySequenceSources.Values.Any(sources => sources.Any(source => source.Remaining >= 0)))
                || terms == null || terms.Count == 0 || terms.Count > 64 || terms.Any(term => term == null || term.Kind == DamageKind.Unknown || !Enum.IsDefined(typeof(DamageKind), term.Kind)
                    || term.Rolls < 0 || term.Rolls > 100 || term.Sides < 1 || term.Sides > 100)
                || multiplier < 1 || multiplier > 10 || !(hit >= 0 && hit <= 1 && crit >= 0 && crit <= hit)) return null;
            var result = new MitigatedResult { Available = true, Approximate = true,
                Reason = "approximate-shield-mean-state", ApproximationAssumptions = Assumptions };
            foreach (var group in terms.GroupBy(term => term.Kind))
            {
                if (profile.Immunities.Contains(group.Key)) continue;
                double factor = group.Key == DamageKind.Physical ? profile.PhysicalDecline : 1;
                if (!(factor > 0 && factor <= 1)) return null;
                double Raw(int critical)
                {
                    double value = group.Sum(term => (term.Rolls * (term.Sides + 1) / 2.0 + term.Bonus) * (term.Multiplies ? critical : 1)) * factor;
                    return Math.Max(group.Key == DamageKind.Physical ? 1 : 0, factor == 1 ? value : Math.Floor(value));
                }
                int Maximum(int critical) => Math.Max(group.Key == DamageKind.Physical ? 1 : 0, (int)Math.Ceiling(group.Sum(term => (term.Rolls * term.Sides + term.Bonus)
                    * (term.Multiplies ? critical : 1)) * factor));
                var sources = SpellProtectionReader.Read(profile, group.Key)?.ToList() ?? new List<SpellProtection>();
                if (sources.Count == 0)
                {
                    if (group.Key == DamageKind.Physical && profile.PhysicalPool.HasValue)
                        sources.Add(new SpellProtection { Order = -1, Remaining = profile.PhysicalPool.Value.Remaining, PerHit = profile.PhysicalPool.Value.PerHit });
                    else if (profile.EnergyPools.TryGetValue(group.Key, out var pool))
                        sources.Add(new SpellProtection { Order = -1, Remaining = pool.Remaining, PerHit = pool.PerHit });
                    int constant = group.Key == DamageKind.Physical ? profile.Reduction.TryGetValue(group.Key, out int dr) ? dr : 0
                        : profile.ConstantEnergyResistance.TryGetValue(group.Key, out int resistance) ? resistance
                        : profile.EnergyPools.ContainsKey(group.Key) ? 0 : profile.Reduction.TryGetValue(group.Key, out int value) ? value : 0;
                    if (constant > 0) sources.Add(new SpellProtection { Order = -1, Remaining = -1, PerHit = constant });
                }
                if (sources.Any(source => source.PerHit < 0 || source.Remaining < -1)) return null;
                var means = ledger.Apply(group.Key, Raw(1), Raw(profile.CriticalImmune ? 1 : multiplier), hit, crit, sources, clustered);
                result.Normal.Mean += means.Normal; result.Critical.Mean += means.Critical;
                result.Normal.Max += Maximum(1); result.Critical.Max += Maximum(profile.CriticalImmune ? 1 : multiplier);
            }
            result.Expected = DamageMath.Expected(hit, crit, result.Normal.Mean, result.Critical.Mean); return result;
        }
    }
    internal static class MeanSpellShieldApproximation
    {
        internal static SpellSequenceResult Compute(SpellRollPart[] parts, int count, double hit, double penetration,
            double failure, long hp, int temporaryHp, int images = 0, double nearMiss = 0)
        {
            bool Probability(double value) => value >= 0 && value <= 1;
            bool Finite(double value) => value >= 0 && !double.IsInfinity(value);
            if (parts == null || parts.Length == 0 || parts.Length > 12 || count < 1 || count > 256 || hp < 0 || temporaryHp < 0
                || images < 0 || images > 64 || !Probability(hit) || !Probability(penetration) || !Probability(failure)
                || !Probability(nearMiss) || hit + nearMiss > 1.000000001
                || !parts.Any(part => part?.Protections?.Any(source => source.Remaining >= 0) == true)
                || parts.Any(part => part == null || part.Kind == DamageKind.Unknown || !Enum.IsDefined(typeof(DamageKind), part.Kind) || part.Dice < 0 || part.Dice > 1000
                    || part.Sides < 1 || part.Sides > 1000 || !Finite(part.FailedFactor) || !Finite(part.PassedFactor)
                    || !Finite(part.Vulnerability) || part.Vulnerability <= 0 || part.Protections?.Length > 32
                    || part.Protections?.Any(source => source == null || source.Remaining < -1 || source.PerHit < 0) == true)) return null;
            var ledger = new MeanShieldLedger();
            var mirrors = images > 0 ? new MirrorImageSequence(images) : null;
            var result = new SpellSequenceResult { Approximate = true };
            var sourceLists = parts.Select(part => part.Protections?.Length > 0 ? part.Protections
                : new[] { new SpellProtection { Order = -1, Remaining = -1, PerHit = part.Reduction } }).ToArray();
            double hpLeft = hp, temporaryLeft = temporaryHp;
            for (int application = 0; application < count; application++)
            {
                double chance = hit * (mirrors?.Apply(hit, nearMiss) ?? 1) * penetration;
                double deliveryDamage = 0;
                for (int partIndex = 0; partIndex < parts.Length; partIndex++)
                {
                    var part = parts[partIndex];
                    double mean = (part.Maximize ? (double)part.Dice * part.Sides : part.Dice * (part.Sides + 1) / 2.0) + part.Bonus;
                    double Mean(double saveFactor) => part.Immune || saveFactor == 0 ? 0
                        : !part.Empower && !part.Half && saveFactor == 1 && part.Vulnerability == 1 && part.BonusPercent == 0
                            ? Math.Max(1, mean)
                            : SpellDamageMath.HpDamage(mean, (part.Empower ? 1.5 : 1) * (part.Half ? .5 : 1) * saveFactor,
                                part.Vulnerability, part.BonusPercent);
                    double damage = failure * Mean(part.FailedFactor) + (1 - failure) * Mean(part.PassedFactor);
                    if (!Finite(damage)) return null;
                    var defended = ledger.Apply(part.Kind, damage, damage, chance, 0, sourceLists[partIndex], false);
                    result.ExpectedDamage += chance * defended.Normal;
                    deliveryDamage += defended.Normal;
                    result.Operations++;
                }
                double hpLost = chance * Math.Min(hpLeft, Math.Max(0, deliveryDamage - temporaryLeft));
                double tempLost = chance * Math.Min(temporaryLeft, deliveryDamage);
                hpLeft = Math.Max(0, hpLeft - hpLost); temporaryLeft = Math.Max(0, temporaryLeft - tempLost);
            }
            result.HpLoss = Math.Min(hp, Math.Max(0, hp - hpLeft));
            result.TemporaryHpLoss = Math.Min(temporaryHp, Math.Max(0, temporaryHp - temporaryLeft));
            return result;
        }
    }
}
