using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text;

namespace CombatPreview
{
    internal sealed class SpellProtection
    {
        internal int Order, Remaining, PerHit;
    }
    internal static class ProtectedSpellSequenceMath
    {
        sealed class Outcome { internal int[] Damage; internal double Chance; }
        // Immutable numerical input keys, including source order and pool balances.
        // A failed budget result is cached too, so hovering cannot retry the same
        // expensive distribution on every refresh. No game objects are retained.
        static readonly Dictionary<string, SpellSequenceResult> cache = new Dictionary<string, SpellSequenceResult>();
        static readonly Queue<string> cacheOrder = new Queue<string>();
        internal static SpellSequenceResult Compute(SpellRollPart[] parts, int count, double hit, double penetration,
            double failure, long hp, int temporaryHp, int images, double nearMiss)
        {
            var key = new StringBuilder();
            void Add(object value) { key.Append(Convert.ToString(value, CultureInfo.InvariantCulture)); key.Append('|'); }
            Add(count); Add(hit.ToString("R", CultureInfo.InvariantCulture)); Add(penetration.ToString("R", CultureInfo.InvariantCulture));
            Add(failure.ToString("R", CultureInfo.InvariantCulture)); Add(hp); Add(temporaryHp); Add(images);
            Add(nearMiss.ToString("R", CultureInfo.InvariantCulture)); Add(parts.Length);
            foreach (var part in parts)
            {
                Add(part.Dice); Add(part.Sides); Add(part.Bonus); Add(part.Reduction); Add(part.SharedRoll);
                Add(part.BonusPercent);
                Add(part.Maximize); Add(part.Empower); Add(part.Half); Add(part.Immune);
                Add(part.Vulnerability.ToString("R", CultureInfo.InvariantCulture));
                Add(part.FailedFactor.ToString("R", CultureInfo.InvariantCulture)); Add(part.PassedFactor.ToString("R", CultureInfo.InvariantCulture));
                Add(part.Protections?.Length ?? 0);
                foreach (var source in part.Protections ?? new SpellProtection[0])
                { if (source == null) return null; Add(source.Order); Add(source.Remaining); Add(source.PerHit); }
            }
            string stamp = key.ToString();
            if (cache.TryGetValue(stamp, out var previous)) return Copy(previous);
            var result = ComputeUncached(parts, count, hit, penetration, failure, hp, temporaryHp, images, nearMiss);
            if (cacheOrder.Count >= 32) cache.Remove(cacheOrder.Dequeue());
            cacheOrder.Enqueue(stamp); cache[stamp] = Copy(result);
            return result;
        }
        static SpellSequenceResult Copy(SpellSequenceResult value) => value == null ? null : new SpellSequenceResult {
            HpLoss = value.HpLoss, TemporaryHpLoss = value.TemporaryHpLoss, KillChance = value.KillChance, ExpectedDamage = value.ExpectedDamage, Operations = value.Operations };
        static SpellSequenceResult ComputeUncached(SpellRollPart[] parts, int count, double hit, double penetration,
            double failure, long hp, int temporaryHp, int images, double nearMiss)
        {
            const int budget = 250000;
            int operations = 0;
            var allSources = parts.SelectMany(part => part.Protections ?? new SpellProtection[0]).ToArray();
            if (allSources.Any(source => source.Order < 0 || source.Remaining < -1 || source.PerHit < 0)) return null;
            var pooled = allSources.Where(source => source.Remaining >= 0).GroupBy(source => source.Order).OrderBy(group => group.Key).ToArray();
            if (pooled.Length > 32 || pooled.Any(group => group.Select(source => source.Remaining).Distinct().Count() != 1)) return null;
            var slots = pooled.Select((group, index) => (group.Key, index)).ToDictionary(pair => pair.Key, pair => pair.index);
            var initial = pooled.Select(group => (int)Math.Min(group.First().Remaining,
                (long)count * parts.Sum(part => (part.Protections ?? new SpellProtection[0]).Where(source => source.Order == group.Key)
                    .Select(source => (long)source.PerHit).DefaultIfEmpty(0).Max()))).ToArray();
            var poolStates = new List<int[]>();
            var poolKeys = new Dictionary<string, int>();
            int Intern(int[] remaining)
            {
                string key = string.Join(",", remaining);
                if (poolKeys.TryGetValue(key, out int id)) return id;
                poolKeys[key] = id = poolStates.Count; poolStates.Add(remaining); return id;
            }
            var outcomes = new List<Outcome>();
            foreach (bool saved in new[] { false, true })
            {
                double probability = saved ? 1 - failure : failure;
                if (probability == 0) continue;
                var raw = new List<Outcome> { new Outcome { Damage = new int[parts.Length], Chance = probability } };
                foreach (var group in parts.Select((part, index) => (part, index)).GroupBy(pair => pair.part.SharedRoll))
                {
                    var first = group.First().part;
                    if (group.Any(pair => pair.part.Dice != first.Dice || pair.part.Sides != first.Sides || pair.part.Maximize != first.Maximize)) return null;
                    var rolls = first.Maximize ? new[] { 1.0 } : SpellDamageMath.RollDistribution(first.Dice, first.Sides);
                    if (rolls == null) return null;
                    var next = new Dictionary<string, Outcome>();
                    foreach (var input in raw)
                        for (int roll = 0; roll < rolls.Length; roll++)
                        {
                            if (++operations > budget) return null;
                            if (rolls[roll] <= 0) continue;
                            var values = (int[])input.Damage.Clone();
                            long sum = first.Maximize ? (long)first.Dice * first.Sides : first.Dice + roll;
                            foreach (var pair in group)
                            {
                                var part = pair.part;
                                double factor = saved ? part.PassedFactor : part.FailedFactor;
                                double damage = part.Immune || factor == 0 ? 0 : SpellDamageMath.HpDamage(sum + part.Bonus,
                                    (part.Empower ? 1.5 : 1) * (part.Half ? .5 : 1) * factor, part.Vulnerability, part.BonusPercent);
                                if (damage > int.MaxValue) return null;
                                values[pair.index] = (int)damage;
                            }
                            string key = string.Join(",", values);
                            if (!next.TryGetValue(key, out var entry)) next[key] = entry = new Outcome { Damage = values };
                            entry.Chance += input.Chance * rolls[roll];
                        }
                    raw = next.Values.ToList();
                }
                outcomes.AddRange(raw);
            }
            var transitions = new Dictionary<(int Pool, int Roll), (int Pool, long Damage)>();
            var states = new Dictionary<(int Pool, int Images, long Total), double> { { (Intern(initial), images, 0), 1 } };
            var result = new SpellSequenceResult();
            for (int application = 0; application < count; application++)
            {
                var next = new Dictionary<(int Pool, int Images, long Total), double>();
                foreach (var state in states)
                {
                    double body = state.Key.Images == 0 ? hit : hit / (state.Key.Images + 1);
                    double consume = state.Key.Images == 0 ? 0 : hit * state.Key.Images / (state.Key.Images + 1) + nearMiss;
                    Add(next, state.Key, state.Value * (1 - consume - body * penetration));
                    if (consume > 0) Add(next, (state.Key.Pool, state.Key.Images - 1, state.Key.Total), state.Value * consume);
                    for (int index = 0; index < outcomes.Count; index++)
                    {
                        if (++operations > budget) return null;
                        var outcome = outcomes[index];
                        if (!transitions.TryGetValue((state.Key.Pool, index), out var transition))
                        {
                            var remaining = (int[])poolStates[state.Key.Pool].Clone();
                            long damage = 0;
                            for (int partIndex = 0; partIndex < parts.Length; partIndex++)
                            {
                                int raw = outcome.Damage[partIndex], absorbed = 0, selected = -1, best = 0;
                                var sources = parts[partIndex].Protections;
                                if (sources == null || sources.Length == 0) absorbed = Math.Min(raw, parts[partIndex].Reduction);
                                else foreach (var source in sources.OrderBy(source => source.Order))
                                {
                                    if (++operations > budget) return null;
                                    int available = source.Remaining < 0 ? source.PerHit : Math.Min(source.PerHit, remaining[slots[source.Order]]);
                                    // GetBestAndStackableDR compares full Reduction,
                                    // before ApplyChunkReduction caps it to the roll.
                                    if (available <= best) continue;
                                    best = available; absorbed = Math.Min(raw, available);
                                    selected = source.Remaining < 0 ? -1 : slots[source.Order];
                                }
                                if (selected >= 0) remaining[selected] -= absorbed;
                                damage += raw - absorbed;
                            }
                            transition = (Intern(remaining), damage);
                            transitions[(state.Key.Pool, index)] = transition;
                        }
                        double chance = state.Value * body * penetration * outcome.Chance;
                        result.ExpectedDamage += chance * transition.Damage;
                        Add(next, (transition.Pool, state.Key.Images, Math.Min(hp + temporaryHp, state.Key.Total + transition.Damage)), chance);
                    }
                }
                states = next;
            }
            foreach (var state in states)
            {
                result.HpLoss += state.Value * Math.Max(0, state.Key.Total - temporaryHp);
                result.TemporaryHpLoss += state.Value * Math.Min(state.Key.Total, temporaryHp);
                if (state.Key.Total >= hp + temporaryHp) result.KillChance += state.Value;
            }
            result.Operations = operations;
            return result;
        }
        static void Add(Dictionary<(int Pool, int Images, long Total), double> states, (int Pool, int Images, long Total) key, double chance)
        { if (chance <= 0) return; states.TryGetValue(key, out double previous); states[key] = previous + chance; }
    }
}
