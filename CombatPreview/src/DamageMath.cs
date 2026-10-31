using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatPreview
{
    public sealed class DamageTerm
    {
        public int Rolls, Sides, Bonus;
        public bool Multiplies;
        public DamageKind Kind = DamageKind.Unknown;
        public DamageTerm(int rolls, int sides, int bonus, bool multiplies)
        { Rolls = rolls; Sides = sides; Bonus = bonus; Multiplies = multiplies; }
        public override string ToString()
        {
            string dice = Rolls > 0 ? Rolls + "d" + Sides : "";
            return dice + (Bonus == 0 ? (dice == "" ? "0" : "") : Bonus.ToString(dice == "" ? "0;-0" : "+0;-0"));
        }
    }
    public struct DamageSummary
    {
        public int Min, Max;
        public double Mean;
    }
    // Deterministic convolution: does not use RNG or game APIs. Models a minimum
    // of one damage for the whole raw bundle, matching the inherited estimator.
    public static class DamageMath
    {
        public static DamageSummary Summarize(IList<DamageTerm> terms, int criticalMultiplier)
        { return SummarizeReduced(terms, criticalMultiplier, 1, 0); }
        public static DamageSummary SummarizeReduced(IList<DamageTerm> terms, int criticalMultiplier, int floor, int reduction)
        {
            if (floor < 0 || reduction < 0) throw new ArgumentOutOfRangeException("reduction");
            if (terms == null || terms.Count == 0) throw new ArgumentException("Empty damage bundle");
            if (criticalMultiplier < 1 || criticalMultiplier > 10) throw new ArgumentOutOfRangeException("criticalMultiplier");
            var pmf = new double[] { 1.0 };
            int bonus = 0, work = 0;
            foreach (var t in terms)
            {
                if (t.Rolls < 0 || t.Rolls > 100 || t.Sides < 1 || t.Sides > 100)
                    throw new ArgumentOutOfRangeException("terms");
                int mult = t.Multiplies ? criticalMultiplier : 1;
                bonus = checked(bonus + t.Bonus * mult);
                for (int r = 0; r < t.Rolls * mult; r++)
                {
                    work = checked(work + pmf.Length * t.Sides);
                    if (work > 2000000) throw new InvalidOperationException("Damage distribution exceeds preview budget");
                    var next = new double[pmf.Length + t.Sides];
                    for (int i = 0; i < pmf.Length; i++)
                        for (int d = 1; d <= t.Sides; d++) next[i + d] += pmf[i] / t.Sides;
                    pmf = next;
                }
            }
            var result = new DamageSummary { Min = int.MaxValue, Max = int.MinValue };
            for (int i = 0; i < pmf.Length; i++)
            {
                if (pmf[i] == 0) continue;
                int value = Math.Max(0, Math.Max(floor, checked(i + bonus)) - reduction);
                result.Min = Math.Min(result.Min, value); result.Max = Math.Max(result.Max, value);
                result.Mean += value * pmf[i];
            }
            return result;
        }
        // Hit includes critical hits; Crit is unconditional confirmed-critical probability.
        public static double Expected(double hit, double crit, double normalMean, double critMean)
        {
            if (double.IsNaN(hit) || double.IsNaN(crit) || hit < 0 || hit > 1 || crit < 0 || crit > hit + 1e-10)
                throw new ArgumentOutOfRangeException("probability");
            if (double.IsNaN(normalMean) || double.IsInfinity(normalMean) || normalMean < 0
                || double.IsNaN(critMean) || double.IsInfinity(critMean) || critMean < 0)
                throw new ArgumentOutOfRangeException("damage mean");
            // Floating point tolerance cannot make the normal-hit weight negative.
            crit = Math.Min(crit, hit);
            return (hit - crit) * normalMean + crit * critMean;
        }
    }
}
