using System;
using System.Collections.Generic;
namespace CombatPreview
{
    public static class SpellDamageMath
    {
        public static int? Rank(int level, string progression, int start, int step, int? minimum, int? maximum)
        {
            if (level < 0 || level > 10000) return null;
            long value;
            switch (progression) {
                case "AsIs": value = level; break;
                case "Div2": value = level / 2; break;
                case "OnePlusDiv2": value = 1 + (level - 1) / 2; break;
                case "StartPlusDivStep": if (step <= 0) return null; value = 1 + Math.Max(0, (level - start) / step); break;
                case "DelayedStartPlusDivStep": if (step <= 0) return null; value = level < start ? 0 : 1 + Math.Max(0, (level - start) / step); break;
                case "StartPlusDoubleDivStep": if (step <= 0) return null; value = 1 + 2L * Math.Max(0, (level - start) / step); break;
                case "DivStep": if (step <= 0) return null; value = level / step; break;
                case "OnePlusDivStep": if (step <= 0) return null; value = 1 + Math.Max(0, level / step); break;
                case "MultiplyByModifier": value = (long)level * step; break;
                case "HalfMore": value = level + level / 2; break;
                case "BonusValue": value = (long)level + step; break;
                case "DoublePlusBonusValue": value = 2L * level + step; break;
                case "Div2PlusStep": value = level / 2 + (long)step; break;
                case "SquaredValue": value = (long)level * level; break;
                default: return null;
            }
            if (minimum.HasValue) value = Math.Max(minimum.Value, value);
            if (maximum.HasValue) value = Math.Min(maximum.Value, value);
            return value < int.MinValue || value > int.MaxValue ? (int?)null : (int)value;
        }
        public static int? Projectiles(int configured, bool useRank, int? rank)
        { if (configured < 0 || configured > 256 || useRank && !rank.HasValue) return null; return useRank ? Math.Max(0, Math.Min(configured, rank.Value)) : configured; }
        public static double? Mean(int dice, int sides, int bonus)
        {
            return MeanTransformed(dice, sides, bonus, false, 1, 0);
        }

        public static double? MeanHitPoints(int dice, int sides, int bonus, bool maximize, bool empower, bool half, int reduction = 0, double saveMultiplier = 1, double vulnerability = 1, int bonusPercent = 0)
        {
            double multiplier = empower ? 1.5 : 1;
            if (half) multiplier *= .5;
            return MeanTransformed(dice, sides, bonus, maximize, multiplier * saveMultiplier, 1, reduction, vulnerability, bonusPercent);
        }

        public static double Expected(double applications, double hit, double penetration,
            double saveFailure, double onFailedSave, double onPassedSave)
        {
            return applications * hit * penetration
                * (saveFailure * onFailedSave + (1 - saveFailure) * onPassedSave);
        }

        internal static double InstantDeathChance(double penetration, double firstFailure, double secondFailure)
            => penetration * firstFailure * secondFailure;

        internal static double? Through(double? first, double? second)
            => first == 0 || second == 0 ? 0 : first * second;

        internal static double ExpectedLethalSaveHpLoss(double reachesFirstSave, double firstFailure,
            double secondFailure, long currentHp, double damageBranchHpLoss)
            => reachesFirstSave * firstFailure
                * (secondFailure * currentHp + (1 - secondFailure) * damageBranchHpLoss);

        internal static double? MeanCappedHpLoss(int dice, int sides, int bonus, bool maximize,
            bool empower, bool half, long currentHp, int temporaryHp, int reduction = 0, double saveMultiplier = 1, double vulnerability = 1, int bonusPercent = 0)
        {
            if (!(vulnerability > 0) || double.IsInfinity(vulnerability) || currentHp < 0 || temporaryHp < 0 || reduction < 0 || dice < 0 || dice > 1000 || sides < 1 || sides > 1000)
                return null;
            var rolls = maximize ? new[] { 1.0 } : RollDistribution(dice, sides);
            if (rolls == null) return null;
            double multiplier = (empower ? 1.5 : 1) * (half ? .5 : 1) * saveMultiplier;
            double mean = 0;
            for (int i = 0; i < rolls.Length; i++)
            {
                long roll = (maximize ? (long)dice * sides : dice + i) + bonus;
                double damage = HpDamage(roll, multiplier, vulnerability, bonusPercent) - reduction;
                mean += rolls[i] * Math.Min(currentHp, Math.Max(0, damage - temporaryHp));
            }
            return Math.Min(currentHp, mean);
        }

        internal static double? MeanIndependentHitsHpLoss(int dice, int sides, int bonus, bool maximize,
            bool empower, bool half, int reduction, double damageFactor, int count, double hit, long hp, int tempHp, double vulnerability = 1, int bonusPercent = 0)
        {
            long cap = hp + tempHp;
            if (!(vulnerability > 0) || double.IsInfinity(vulnerability) || hp < 0 || hp > 1024 || tempHp < 0 || cap > 1024 || count < 2 || count > 32
                || dice < 0 || dice > 1000 || sides < 1 || sides > 1000 || !(hit >= 0 && hit <= 1)) return null;
            if (hp == 0 || hit == 0) return 0;
            var rolls = maximize ? new[] { 1.0 } : RollDistribution(dice, sides);
            if (rolls == null || count * (cap + 1) * (rolls.Length + 1) > 30000) return null;
            double multiplier = (empower ? 1.5 : 1) * (half ? .5 : 1) * damageFactor;
            var outcomes = new Dictionary<int, double> { { 0, 1 - hit } };
            for (int i = 0; i < rolls.Length; i++)
            {
                long roll = (maximize ? (long)dice * sides : dice + i) + bonus;
                int damage = (int)Math.Min(cap, Math.Max(0, HpDamage(roll, multiplier, vulnerability, bonusPercent) - reduction));
                outcomes.TryGetValue(damage, out double previous);
                outcomes[damage] = previous + rolls[i] * hit;
            }
            var totals = new double[(int)cap + 1];
            var next = new double[totals.Length];
            totals[0] = 1;
            for (int application = 0; application < count; application++)
            {
                Array.Clear(next, 0, next.Length);
                for (int damage = 0; damage < totals.Length; damage++)
                {
                    if (totals[damage] <= 0) continue;
                    foreach (var outcome in outcomes)
                        next[Math.Min((int)cap, damage + outcome.Key)] += totals[damage] * outcome.Value;
                }
                var previous = totals;
                totals = next;
                next = previous;
            }
            double mean = 0;
            for (int damage = tempHp + 1; damage < totals.Length; damage++)
                mean += totals[damage] * (damage - tempHp);
            return Math.Min(hp, mean);
        }

        static double? MeanTransformed(int dice, int sides, int bonus, bool maximize, double multiplier, int minimum, int reduction = 0, double vulnerability = 1, int bonusPercent = 0)
        {
            if (reduction < 0 || dice < 0 || dice > 1000 || sides < 1 || sides > 1000) return null;
            if (!(vulnerability > 0) || double.IsInfinity(vulnerability)) return null;
            if (bonusPercent != 0)
            {
                var rolls = maximize ? new[] { 1.0 } : RollDistribution(dice, sides);
                if (rolls == null) return null;
                double total = 0;
                for (int index = 0; index < rolls.Length; index++)
                    total += rolls[index] * Math.Max(0, HpDamage((maximize ? (long)dice * sides : dice + index) + bonus,
                        multiplier, vulnerability, bonusPercent) - reduction);
                return total;
            }
            double Transform(long value) => Math.Max(0, Math.Max(minimum, Math.Floor(Math.Max(minimum, value * multiplier) * vulnerability)) - reduction);
            long largest = (long)dice * sides + bonus;
            if (maximize || dice == 0 || sides == 1) return Transform(largest);
            if (vulnerability != 1)
            {
                var rolls = RollDistribution(dice, sides);
                if (rolls == null) return null;
                double sum = 0;
                for (int i = 0; i < rolls.Length; i++) sum += rolls[i] * Transform((long)dice + i + bonus);
                return sum;
            }
            if (largest * multiplier <= minimum) return Math.Max(0, minimum - reduction);
            if (largest * multiplier <= reduction && minimum <= reduction) return 0;
            if (multiplier == 1 && (long)dice + bonus >= Math.Max(minimum, reduction)) return dice * (sides + 1) / 2.0 + bonus - reduction;

            var distribution = RollDistribution(dice, sides);
            if (distribution == null) return null;
            double mean = 0;
            for (int sum = 0; sum < distribution.Length; sum++)
                mean += distribution[sum] * Math.Max(0, Math.Max(minimum, Math.Floor(((long)dice + sum + bonus) * multiplier)) - reduction);
            return mean;
        }

        internal static double HpDamage(double raw, double factor, double vulnerability = 1, int bonusPercent = 0)
        {
            if (bonusPercent == 0) return Math.Max(1, Math.Floor(Math.Max(1, raw * factor) * vulnerability));
            // The engine clamps before BonusPercent and performs these stages as
            // singles. Keeping percent separate avoids a second minimum clamp
            // and integer-boundary errors from a combined double multiplier.
            float value = Math.Max(1f, (float)(raw * factor));
            value *= 1f + bonusPercent / 100f;
            value *= (float)vulnerability;
            return Math.Max(1, Math.Floor(value));
        }
        public static double? MeanContextDice(int dice, int sides, int bonus, bool maximize, bool empower,
            double sharedModifier, double damageMultiplier, int minimum)
        {
            if (dice < 0 || dice > 1000 || sides < 1 || sides > 1000
                || double.IsNaN(sharedModifier) || double.IsInfinity(sharedModifier)) return null;
            var rolls = maximize ? new[] { 1.0 } : RollDistribution(dice, sides);
            if (rolls == null) return null;
            double mean = 0;
            for (int index = 0; index < rolls.Length; index++)
            {
                long value = (maximize ? (long)dice * sides : dice + index) + bonus;
                if (empower && sides > 1) value += value / 2;
                double shared = Math.Truncate(value * sharedModifier);
                if (shared < int.MinValue || shared > int.MaxValue) return null;
                mean += rolls[index] * Math.Max(minimum, Math.Floor(shared * damageMultiplier));
            }
            return mean;
        }

        static readonly Dictionary<long, double[]> rollCache = new Dictionary<long, double[]>();
        internal static double[] RollDistribution(int dice, int sides)
        {
            if (dice < 0 || dice > 1000 || sides < 1 || sides > 1000) return null;
            long key = (long)dice * 1001 + sides;
            if (rollCache.TryGetValue(key, out var cached)) return cached;
            int width = dice * (sides - 1);
            if ((long)dice * width > 16384) return null;
            var distribution = new double[width + 1];
            var next = new double[width + 1];
            distribution[0] = 1;
            for (int roll = 1; roll <= dice; roll++)
            {
                double window = 0;
                int previousWidth = (roll - 1) * (sides - 1);
                for (int sum = 0; sum <= roll * (sides - 1); sum++)
                {
                    if (sum <= previousWidth) window += distribution[sum];
                    if (sum >= sides && sum - sides <= previousWidth) window -= distribution[sum - sides];
                    next[sum] = window / sides;
                }
                var previous = distribution;
                distribution = next;
                next = previous;
            }
            // Immutable to callers. Bound retained distributions and avoid rebuilding
            // the same dice for each save branch, damage component and GUI refresh.
            if (rollCache.Count >= 64) rollCache.Clear();
            rollCache[key] = distribution;
            return distribution;
        }

        internal static double[] PhysicalAbsorption(DamageTerm term, int criticalMultiplier, int cap, double factor = 1)
        {
            int multiplier = term.Multiplies ? criticalMultiplier : 1;
            int dice = term.Rolls * multiplier;
            var rolls = RollDistribution(dice, term.Sides);
            if (rolls == null) return null;
            var absorbed = new double[cap + 1];
            for (int i = 0; i < rolls.Length; i++)
                absorbed[Math.Min(cap, Math.Max(1, (int)Math.Floor((dice + i + term.Bonus * multiplier) * factor)))] += rolls[i];
            return absorbed;
        }

        public static double? ExpectedThroughPool(int dice, int sides, int bonus, bool maximize, bool empower,
            bool half, int applications, double hit, int pool, int perHit, int constantResistance = 0, double damageFactor = 1)
        {
            if (applications < 1 || applications > 32 || hit < 0 || hit > 1 || double.IsNaN(hit)
                || pool < 0 || perHit < 0 || constantResistance < 0 || dice < 0 || dice > 1000 || sides < 1 || sides > 1000)
                return null;

            var rolls = maximize ? new[] { 1.0 } : RollDistribution(dice, sides);
            if (rolls == null) return null;
            double multiplier = (empower ? 1.5 : 1) * (half ? .5 : 1) * damageFactor;
            var absorption = new System.Collections.Generic.Dictionary<int, double> { { 0, 1 - hit } };
            double rawMean = 0, constantAbsorption = 0;
            for (int i = 0; i < rolls.Length; i++)
            {
                long roll = maximize ? (long)dice * sides : dice + i;
                double damage = Math.Max(1, Math.Floor((roll + bonus) * multiplier));
                int absorbed = (int)Math.Min(Math.Min(damage, perHit), pool);
                double chance = rolls[i] * hit;
                rawMean += damage * chance;
                constantAbsorption += Math.Min(damage, constantResistance) * chance;
                absorption.TryGetValue(absorbed, out double previous);
                absorption[absorbed] = previous + chance;
            }

            int maxAbsorption = 0;
            foreach (int amount in absorption.Keys) maxAbsorption = Math.Max(maxAbsorption, amount);
            int budgetedPool = (int)Math.Min(pool, (long)applications * maxAbsorption + (constantResistance > 0 ? perHit : 0));
            if ((budgetedPool + 1L) * applications * absorption.Count > 200000) return null;
            var remaining = new double[budgetedPool + 1];
            var next = new double[remaining.Length];
            remaining[budgetedPool] = 1;
            double meanAbsorbed = 0;
            for (int i = 0; i < applications; i++)
            {
                Array.Clear(next, 0, next.Length);
                for (int left = 0; left <= budgetedPool; left++)
                {
                    if (remaining[left] <= 0) continue;
                    int capacity = Math.Min(left, perHit);
                    if (constantResistance > 0 && capacity <= constantResistance)
                    {
                        // Equal candidates use registration order. Only the final strike
                        // can ignore the tie because no later hit depends on the pool.
                        if (capacity == constantResistance && i + 1 < applications) return null;
                        next[left] += remaining[left];
                        meanAbsorbed += remaining[left] * constantAbsorption;
                        continue;
                    }
                    foreach (var outcome in absorption)
                    {
                        int spent = Math.Min(left, outcome.Key);
                        double chance = remaining[left] * outcome.Value;
                        next[left - spent] += chance;
                        meanAbsorbed += spent * chance;
                    }
                }
                var previous = remaining;
                remaining = next;
                next = previous;
            }
            return Math.Max(0, applications * rawMean - meanAbsorbed);
        }
        public static double? ExpectedThroughOrderedPools(int dice, int sides, int bonus, bool maximize,
            bool empower, bool half, int applications, double hit, double saveFailure, double failedFactor,
            double passedFactor, (int Remaining, int PerHit)[] orderedSources)
        {
            if (dice < 0 || dice > 1000 || sides < 1 || sides > 1000 || applications < 1 || applications > 32
                || !(hit >= 0 && hit <= 1) || !(saveFailure >= 0 && saveFailure <= 1)
                || orderedSources == null || orderedSources.Length == 0 || orderedSources.Length > 256) return null;
            var rolls = maximize ? new[] { 1.0 } : RollDistribution(dice, sides);
            if (rolls == null) return null;

            var damage = new System.Collections.Generic.Dictionary<int, double> { { 0, 1 - hit } };
            double multiplier = (empower ? 1.5 : 1) * (half ? .5 : 1);
            for (int i = 0; i < rolls.Length; i++)
            {
                long roll = maximize ? (long)dice * sides : dice + i;
                AddDamage(roll, failedFactor, rolls[i] * hit * saveFailure);
                AddDamage(roll, passedFactor, rolls[i] * hit * (1 - saveFailure));
            }

            void AddDamage(long roll, double factor, double chance)
            {
                if (chance <= 0) return;
                int value = factor == 0 ? 0 : (int)Math.Min(int.MaxValue,
                    Math.Max(1, Math.Floor((roll + bonus) * multiplier * factor)));
                damage.TryGetValue(value, out double previous);
                damage[value] = previous + chance;
            }

            var start = new int[orderedSources.Length];
            for (int i = 0; i < orderedSources.Length; i++)
            {
                if (orderedSources[i].Remaining < -1 || orderedSources[i].PerHit < 0) return null;
                start[i] = orderedSources[i].Remaining;
            }
            var states = new Dictionary<int[], double>(PoolBalanceComparer.Instance) { { start, 1 } };
            double expected = 0;
            int budget = 20000;
            for (int application = 0; application < applications; application++)
            {
                var next = new Dictionary<int[], double>(PoolBalanceComparer.Instance);
                foreach (var state in states)
                {
                    budget -= orderedSources.Length;
                    if (budget <= 0) return null;
                    int chosen = -1, reduction = 0;
                    for (int i = 0; i < orderedSources.Length; i++)
                    {
                        int left = state.Key[i];
                        int available = left < 0 ? orderedSources[i].PerHit : Math.Min(left, orderedSources[i].PerHit);
                        if (available > reduction) { reduction = available; chosen = i; }
                    }
                    foreach (var outcome in damage)
                    {
                        if (--budget <= 0) return null;
                        double chance = state.Value * outcome.Value;
                        if (chance <= 0) continue;
                        int absorbed = Math.Min(outcome.Key, reduction);
                        var balance = state.Key;
                        if (absorbed > 0 && chosen >= 0 && balance[chosen] >= 0)
                        {
                            balance = (int[])balance.Clone();
                            balance[chosen] -= absorbed;
                        }
                        next.TryGetValue(balance, out double previous);
                        next[balance] = previous + chance;
                        if (next.Count > 1024) return null;
                        expected += (outcome.Key - absorbed) * chance;
                    }
                }
                states = next;
            }
            return expected;
        }

        sealed class PoolBalanceComparer : IEqualityComparer<int[]>
        {
            internal static readonly PoolBalanceComparer Instance = new PoolBalanceComparer();

            public bool Equals(int[] first, int[] second)
            {
                if (ReferenceEquals(first, second)) return true;
                if (first == null || second == null || first.Length != second.Length) return false;
                for (int i = 0; i < first.Length; i++)
                    if (first[i] != second[i]) return false;
                return true;
            }

            public int GetHashCode(int[] balance)
            {
                unchecked
                {
                    int hash = 17;
                    foreach (int value in balance) hash = hash * 31 + value;
                    return hash;
                }
            }
        }
    }
}
