using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatPreview
{
    public enum DamageKind { Unknown, Physical, Fire, Cold, Electricity, Acid, Sonic, PositiveEnergy, NegativeEnergy, Divine, Force, Holy, Unholy, Direct }
    // These values must already account for weapon bypass and rule-specific stacking.
    // This DTO never holds game objects and never consumes a live protection pool.
    public sealed class DefenseProfile
    {
        public bool Complete;
        public string Source = "Unavailable";
        public string UnavailableReason = "Automatic defense adapter has not been validated for this game build.";
        public readonly Dictionary<DamageKind, int> Reduction = new Dictionary<DamageKind, int>();
        public readonly HashSet<DamageKind> Immunities = new HashSet<DamageKind>();
        public bool HasConsumablePool;
        internal readonly Dictionary<DamageKind, (int Remaining, int PerHit)> EnergyPools =
            new Dictionary<DamageKind, (int Remaining, int PerHit)>();
        internal readonly Dictionary<DamageKind, List<(int Order, int Remaining, int PerHit)>> EnergySequenceSources =
            new Dictionary<DamageKind, List<(int Order, int Remaining, int PerHit)>>();
        internal readonly HashSet<DamageKind> MultipleEnergyPools = new HashSet<DamageKind>();
        internal bool IncorporealEnergyDivisor;
        internal double PhysicalDecline = 1;
        internal bool CriticalImmune;
        internal bool ClusteredPhysicalDR;
        internal bool PoolApproximationAllowed;
        internal readonly Dictionary<DamageKind, int> ConstantEnergyResistance = new Dictionary<DamageKind, int>();
        internal (int Remaining, int PerHit)? PhysicalPool;
        internal readonly List<(int Order, int Remaining, int PerHit)> PhysicalSequenceSources = new List<(int Order, int Remaining, int PerHit)>();
    }
    public sealed class MitigatedResult
    {
        public bool Available;
        public string Reason;
        public DamageSummary Normal, Critical;
        public double Expected;
        public bool Approximate;
        public string ApproximationAssumptions;
    }
    public static class MitigationMath
    {
        internal sealed class PhysicalPoolSequence
        {
            readonly int perHit;
            readonly int initialImages;
            double[] normal, critical;
            double[,] remaining, next;

            internal PhysicalPoolSequence(DamageTerm physical, int multiplier, int attacks,
                (int Remaining, int PerHit) pool, int images, double physicalFactor = 1)
            {
                perHit = Math.Min(pool.PerHit, pool.Remaining);
                initialImages = images;
                if (perHit < 1 || perHit > 256 || images < 0 || images > 64) return;
                int capacity = Math.Min(pool.Remaining, perHit * attacks);
                if ((long)(capacity + 1) * (perHit + 1) * attacks * (images + 1) > 100000) return;
                normal = SpellDamageMath.PhysicalAbsorption(physical, 1, perHit, physicalFactor);
                critical = SpellDamageMath.PhysicalAbsorption(physical, multiplier, perHit, physicalFactor);
                if (normal == null || critical == null) return;
                remaining = new double[images + 1, capacity + 1];
                next = new double[images + 1, capacity + 1];
                remaining[images, capacity] = 1;
            }

            internal MitigatedResult Apply(MitigatedResult result, double hit, double crit,
                double hitBeforeImages, double critBeforeImages, double nearMiss, bool ignoreImages = false)
            {
                if (!result.Available) { remaining = null; return result; }
                if (remaining == null) return new MitigatedResult { Reason = "물리 보호막 계산 한도 초과" };
                Array.Clear(next, 0, next.Length);
                double normalAbsorption = 0, criticalAbsorption = 0, targetWeight = 0;
                int capacity = remaining.GetLength(1) - 1;
                int minimumLeft = capacity, maximumLeft = 0;
                crit = Math.Min(hit, crit);
                critBeforeImages = Math.Min(hitBeforeImages, critBeforeImages);
                for (int images = 0; images <= initialImages; images++)
                {
                    double targetShare = ignoreImages ? 1 : 1.0 / (images + 1);
                    double imageRemoved = images > 0 && !ignoreImages ? hitBeforeImages * (1 - targetShare) + nearMiss : 0;
                    double targetHit = hitBeforeImages * targetShare;
                    for (int left = 0; left <= capacity; left++)
                    {
                        double weight = remaining[images, left];
                        if (weight <= 0) continue;
                        minimumLeft = Math.Min(minimumLeft, left);
                        maximumLeft = Math.Max(maximumLeft, left);
                        next[images, left] += weight * Math.Max(0, 1 - targetHit - imageRemoved);
                        if (images > 0) next[images - 1, left] += weight * imageRemoved;
                        double body = weight * targetShare;
                        targetWeight += body;
                        for (int damage = 0; damage <= perHit; damage++)
                        {
                            int blocked = Math.Min(damage, left);
                            normalAbsorption += body * normal[damage] * blocked;
                            criticalAbsorption += body * critical[damage] * blocked;
                            next[images, left - blocked] += body
                                * (normal[damage] * (hitBeforeImages - critBeforeImages) + critical[damage] * critBeforeImages);
                        }
                    }
                }
                var previous = remaining;
                remaining = next;
                next = previous;
                result.Normal = Adjust(result.Normal, normal, normalAbsorption / targetWeight);
                result.Critical = Adjust(result.Critical, critical, criticalAbsorption / targetWeight);
                result.Expected = DamageMath.Expected(hit, crit, result.Normal.Mean, result.Critical.Mean);
                result.Reason = "readonly-physical-pool-sequence";
                return result;

                DamageSummary Adjust(DamageSummary summary, double[] distribution, double absorption)
                {
                    int minimum = Array.FindIndex(distribution, probability => probability > 0);
                    int maximum = Array.FindLastIndex(distribution, probability => probability > 0);
                    summary.Min -= Math.Min(maximumLeft, minimum);
                    summary.Max -= Math.Min(minimumLeft, maximum);
                    summary.Mean = Math.Max(0, summary.Mean - absorption);
                    return summary;
                }
            }
            internal void SetDamage(DamageTerm physical, int multiplier, double physicalFactor = 1)
            {
                if (remaining == null) return;
                if (physical == null)
                {
                    normal = new double[perHit + 1]; normal[0] = 1;
                    critical = normal;
                }
                else
                {
                    normal = SpellDamageMath.PhysicalAbsorption(physical, 1, perHit, physicalFactor);
                    critical = SpellDamageMath.PhysicalAbsorption(physical, multiplier, perHit, physicalFactor);
                    if (normal == null || critical == null) remaining = null;
                }
            }
            internal void Invalidate() { remaining = null; }
        }

        // Frozen-state, constant DR/resistance only. Same-type dice are combined once
        // per attack. Mixed physical bypass profiles need a different grouping adapter.
        public static MitigatedResult Calculate(IList<DamageTerm> terms, int multiplier,
            double hit, double crit, DefenseProfile defense)
        {
            var result = new MitigatedResult();
            if (defense == null || !defense.Complete)
            { result.Reason = defense == null ? "No defense snapshot" : defense.UnavailableReason; return result; }
            if (defense.HasConsumablePool)
            { result.Reason = "Consumable protection requires sequential-state simulation"; return result; }
            if (terms == null || terms.Count == 0 || terms.Any(t => t == null || t.Kind == DamageKind.Unknown || !Enum.IsDefined(typeof(DamageKind), t.Kind)))
            { result.Reason = "Unknown or empty damage type"; return result; }
            if (multiplier < 1 || multiplier > 10) throw new ArgumentOutOfRangeException("multiplier");
            DamageMath.Expected(hit, crit, 0, 0); // Validate even when every type is immune.
            if (terms.Count > 64) throw new InvalidOperationException("Too many damage components");
            foreach (var term in terms)
                if (term.Rolls < 0 || term.Rolls > 100 || term.Sides < 1 || term.Sides > 100)
                    throw new ArgumentOutOfRangeException("terms");
            foreach (var pair in defense.Reduction)
                if (pair.Value < 0 || pair.Value > 1000000) throw new ArgumentOutOfRangeException("Reduction");
            result.Normal = Summarize(terms, 1, defense);
            result.Critical = Summarize(terms, defense.CriticalImmune ? 1 : multiplier, defense);
            result.Expected = DamageMath.Expected(hit, crit, result.Normal.Mean, result.Critical.Mean);
            result.Available = true;
            result.Reason = defense.Source;
            return result;
        }
        static DamageSummary Summarize(IList<DamageTerm> terms, int multiplier, DefenseProfile defense)
        {
            var sum = new DamageSummary();
            foreach (var group in terms.GroupBy(t => t.Kind))
            {
                int reduction;
                defense.Reduction.TryGetValue(group.Key, out reduction);
                if (defense.Immunities.Contains(group.Key)) continue;
                // Physical minimum-one is applied before defense. Energy can fall to zero.
                var summary = DamageMath.SummarizeReduced(group.ToList(), multiplier,
                    group.Key == DamageKind.Physical ? 1 : 0, reduction,
                    group.Key == DamageKind.Physical ? defense.PhysicalDecline : 1);
                sum.Min = checked(sum.Min + summary.Min); sum.Max = checked(sum.Max + summary.Max);
                sum.Mean += summary.Mean;
            }
            return sum;
        }
    }
}
