using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatPreview
{
    internal static class D20SaveSequence
    {
        internal sealed class Result
        { internal double FirstFailure, BothFailure; internal double? SecondFailureIfReached => FirstFailure > 0 ? (double?)(BothFailure / FirstFailure) : null; }
        struct State { internal int Removed; internal bool Failed; internal double P; }
        internal static Result Compute(D20RollModel first, int firstBonus, int firstDc, bool persistent,
            D20RollModel second = null, int secondBonus = 0, int secondDc = 0)
        {
            var models = second == null ? new[] { first } : new[] { first, second };
            if (models.Any(model => model == null || !model.Complete
                || model.ConsumesEffect && (model.AfterRoll.Count > 0 || model.ReplaceOneWithMax))) return null;
            var keys = models.SelectMany(model => model.Modifiers).Where(modifier => modifier.ConsumeWhenApplied || modifier.ConsumeOn20)
                .Select(modifier => modifier.FactKey).Distinct().ToArray();
            if (keys.Length > 12 || keys.Any(string.IsNullOrEmpty)) return null;
            if (models.Any(model => model.ConsumesEffect && !model.Modifiers.Any(modifier => modifier.ConsumeWhenApplied || modifier.ConsumeOn20))) return null;
            var slots = keys.Select((key, index) => (key, index)).ToDictionary(pair => pair.key, pair => pair.index);
            var cache = new Dictionary<(int Stage, int Removed), State[]>();
            int operations = 0;
            State[] Save(int stage, int removed)
            {
                if (cache.TryGetValue((stage, removed), out var cached)) return cached;
                var model = models[stage]; int bonus = stage == 0 ? firstBonus : secondBonus, dc = stage == 0 ? firstDc : secondDc;
                var rollCache = new Dictionary<int, State[]>();
                State[] Roll(int removedNow)
                {
                    if (rollCache.TryGetValue(removedNow, out var known)) return known;
                    var modifiers = model.Modifiers.Where(modifier => modifier.FactKey == null
                        || !slots.TryGetValue(modifier.FactKey, out int bit) || (removedNow & (1 << bit)) == 0).ToList();
                    var outcomes = D20.Outcomes(modifiers, die => die != 20 && (die <= 1 || die < dc),
                        model.AfterRoll, model.ReplaceOneWithMax, true);
                    var states = outcomes.Select(outcome => {
                        int next = removedNow;
                        for (int i = 0; i < modifiers.Count; i++) if ((outcome.ConsumedMask & (1 << i)) != 0)
                            next |= 1 << slots[modifiers[i].FactKey];
                        return new State { Removed = next, P = outcome.P,
                            Failed = !(outcome.Die == 20 || outcome.Die > 1 && (long)outcome.Die + bonus + outcome.Bonus >= dc) };
                    }).ToArray();
                    rollCache[removedNow] = states; return states;
                }
                var probabilities = new Dictionary<(int Removed, bool Failed), double>();
                void Add(State value, double factor)
                {
                    if (++operations > 50000) return;
                    var key = (value.Removed, value.Failed);
                    probabilities.TryGetValue(key, out double old); probabilities[key] = old + value.P * factor;
                }
                foreach (var initial in Roll(removed))
                    if (!persistent || initial.Failed) Add(initial, 1);
                    else foreach (var repeated in Roll(initial.Removed)) Add(repeated, initial.P);
                if (operations > 50000) return null;
                var result = probabilities.Select(pair => new State { Removed = pair.Key.Removed, Failed = pair.Key.Failed, P = pair.Value }).ToArray();
                cache[(stage, removed)] = result; return result;
            }
            var initial = Save(0, 0);
            if (initial == null) return null;
            var answer = new Result { FirstFailure = initial.Where(state => state.Failed).Sum(state => state.P) };
            if (second != null)
                foreach (var state in initial.Where(state => state.Failed))
                {
                    var later = Save(1, state.Removed);
                    if (later == null) return null;
                    answer.BothFailure += state.P * later.Where(value => value.Failed).Sum(value => value.P);
                }
            return answer;
        }
    }
}
