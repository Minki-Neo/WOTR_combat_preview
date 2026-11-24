using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatPreview
{
    internal sealed class SpellRollPart
    {
        internal int Dice, Sides, Bonus, Reduction, SharedRoll, BonusPercent;
        internal DamageKind Kind;
        internal bool Maximize, Empower, Half, Immune;
        internal SpellProtection[] Protections;
        internal double FailedFactor = 1, PassedFactor;
        internal double Vulnerability = 1;
    }

    internal sealed class SpellSequenceResult
    {
        internal double HpLoss, TemporaryHpLoss, KillChance, ExpectedDamage;
        internal int Operations;
        internal bool Approximate;
    }

    // Finite distributions with one HP/temporary-HP budget for the entire spell.
    // Shared raw rolls, save outcomes and mirror depletion remain correlated.
    internal static class SpellSequenceMath
    {
        const int Budget = 250000;
        internal static SpellSequenceResult Compute(SpellRollPart[] parts, int applications,
            double hit, double penetration, double failure, long hp, int temporaryHp,
            int images = 0, double nearMiss = 0)
        {
            if (parts == null || parts.Length == 0 || parts.Length > 12 || applications < 1 || applications > 32
                || hp < 0 || temporaryHp < 0 || hp > long.MaxValue - temporaryHp || images < 0 || images > 16
                || !Probability(hit) || !Probability(penetration) || !Probability(failure)
                || !Probability(nearMiss) || hit + nearMiss > 1.000000001) return null;
            if (parts.Any(part => part == null || part.Dice < 0 || part.Dice > 1000 || part.Sides < 1 || part.Sides > 1000
                || part.Reduction < 0 || !Finite(part.FailedFactor) || !Finite(part.PassedFactor)
                || !Finite(part.Vulnerability) || part.Vulnerability <= 0)) return null;
            if (hp == 0) return new SpellSequenceResult();
            if (parts.Any(part => part.Protections?.Length > 0))
                return ProtectedSpellSequenceMath.Compute(parts, applications, hit, penetration, failure, hp, temporaryHp, images, nearMiss);
            double largest = parts.Sum(part => part.Immune ? 0 : Math.Max(0, SpellDamageMath.HpDamage((long)part.Dice * part.Sides + part.Bonus,
                (part.Empower ? 1.5 : 1) * (part.Half ? .5 : 1) * Math.Max(part.FailedFactor, part.PassedFactor), part.Vulnerability, part.BonusPercent) - part.Reduction));
            if (double.IsInfinity(largest) || double.IsNaN(largest)) return null;
            long fullCap = hp + temporaryHp;
            double needed = Math.Min(fullCap, largest * applications);
            if (needed > 4096) return null;
            int cap = (int)needed, operations = 0;
            if (hp == 0) return new SpellSequenceResult();
            var failed = Distribution(parts, false, cap, ref operations);
            var passed = Distribution(parts, true, cap, ref operations);
            if (failed == null || passed == null) return null;
            var damage = new Dictionary<int, double>();
            foreach (var roll in failed) Add(damage, roll.Key, roll.Value * failure);
            foreach (var roll in passed) Add(damage, roll.Key, roll.Value * (1 - failure));
            double meanDamage = 0, expectedDamage = 0;
            foreach (var part in parts)
            {
                double? Mean(double factor) => part.Immune || factor == 0 ? 0
                    : SpellDamageMath.MeanHitPoints(part.Dice, part.Sides, part.Bonus, part.Maximize, part.Empower, part.Half, part.Reduction, factor, part.Vulnerability, part.BonusPercent);
                var failedMean = Mean(part.FailedFactor); var passedMean = Mean(part.PassedFactor);
                if (!failedMean.HasValue || !passedMean.HasValue) return null;
                meanDamage += failure * failedMean.Value + (1 - failure) * passedMean.Value;
            }
            // State encodes total absorbed HP+temporary HP and remaining images.
            int stride = cap + 1;
            var state = new Dictionary<int, double> { { images * stride, 1 } };
            for (int application = 0; application < applications; application++)
            {
                var next = new Dictionary<int, double>();
                foreach (var entry in state)
                {
                    int left = entry.Key / stride, total = entry.Key % stride;
                    double realHit = left == 0 ? hit : hit / (left + 1);
                    double consume = left == 0 ? 0 : hit * left / (left + 1) + nearMiss;
                    expectedDamage += entry.Value * realHit * penetration * meanDamage;
                    Add(next, entry.Key, entry.Value * (1 - consume - realHit * penetration));
                    if (consume > 0) Add(next, (left - 1) * stride + total, entry.Value * consume);
                    foreach (var roll in damage)
                    {
                        if (++operations > Budget) return null;
                        Add(next, left * stride + Math.Min(cap, total + roll.Key),
                            entry.Value * realHit * penetration * roll.Value);
                    }
                }
                state = next;
            }
            var result = new SpellSequenceResult { Operations = operations, ExpectedDamage = expectedDamage };
            foreach (var entry in state)
            {
                int total = entry.Key % stride;
                result.HpLoss += entry.Value * Math.Max(0, total - temporaryHp);
                result.TemporaryHpLoss += entry.Value * Math.Min(total, temporaryHp);
                if (total == fullCap) result.KillChance += entry.Value;
            }
            return result;
        }

        static Dictionary<int, double> Distribution(SpellRollPart[] parts, bool saved, int cap, ref int operations)
        {
            var total = new Dictionary<int, double> { { 0, 1 } };
            foreach (var group in parts.GroupBy(part => part.SharedRoll))
            {
                var first = group.First();
                if (group.Any(part => part.Dice != first.Dice || part.Sides != first.Sides || part.Maximize != first.Maximize)
                    || group.Any(part => part.Reduction < 0 || !Finite(part.FailedFactor) || !Finite(part.PassedFactor))) return null;
                var rolls = first.Maximize ? new[] { 1.0 } : SpellDamageMath.RollDistribution(first.Dice, first.Sides);
                if (rolls == null) return null;
                var outcomes = new Dictionary<int, double>();
                for (int index = 0; index < rolls.Length; index++)
                {
                    long raw = first.Maximize ? (long)first.Dice * first.Sides : first.Dice + index;
                    long sum = 0;
                    foreach (var part in group)
                    {
                        if (++operations > Budget) return null;
                        double factor = saved ? part.PassedFactor : part.FailedFactor;
                        if (!part.Immune && factor > 0)
                            sum += (long)Math.Min(cap, Math.Max(0, SpellDamageMath.HpDamage(raw + part.Bonus,
                                (part.Empower ? 1.5 : 1) * (part.Half ? .5 : 1) * factor, part.Vulnerability, part.BonusPercent) - part.Reduction));
                    }
                    Add(outcomes, (int)Math.Min(cap, sum), rolls[index]);
                }
                var next = new Dictionary<int, double>();
                foreach (var entry in total)
                    foreach (var outcome in outcomes)
                    {
                        if (++operations > Budget) return null;
                        Add(next, Math.Min(cap, entry.Key + outcome.Key), entry.Value * outcome.Value);
                    }
                total = next;
            }
            return total;
        }
        static bool Probability(double value) => value >= 0 && value <= 1;
        static bool Finite(double value) => value >= 0 && !double.IsInfinity(value) && !double.IsNaN(value);
        static void Add(Dictionary<int, double> values, int key, double amount)
        {
            if (amount <= 0) return;
            values.TryGetValue(key, out double previous); values[key] = previous + amount;
        }
    }
}
