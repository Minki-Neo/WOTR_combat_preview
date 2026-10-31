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
    }
    public sealed class MitigatedResult
    {
        public bool Available;
        public string Reason;
        public DamageSummary Normal, Critical;
        public double Expected;
    }
    public static class MitigationMath
    {
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
            result.Critical = Summarize(terms, multiplier, defense);
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
                    group.Key == DamageKind.Physical ? 1 : 0, reduction);
                sum.Min = checked(sum.Min + summary.Min); sum.Max = checked(sum.Max + summary.Max);
                sum.Mean += summary.Mean;
            }
            return sum;
        }
    }
}
