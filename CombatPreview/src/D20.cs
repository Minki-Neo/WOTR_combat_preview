using System;
using System.Collections.Generic;

namespace CombatPreview
{
    // Distribution of a d20 roll after the d20 modifiers of ModifyD20.
    static class D20
    {
        // One outcome: the die kept and the saving throw bonus the applied modifiers gave (AddSavingThrowBonus).
        internal struct Outcome
        {
            public int Die;
            public int Bonus;
            public double P;
            internal int ConsumedMask;
        }

        // Every outcome of the roll. "failed" is the game's own test of the first die for "reroll only if failed".
        // Each modifier applies with its chance; the game lets each applying modifier set the roll in turn, so the last
        // one wins and a fixed roll (Replace) wins over any reroll.
        internal static List<Outcome> Outcomes(List<D20Modifier> modifiers, Func<int, bool> failed, IReadOnlyList<int> afterRoll = null, bool replaceOneWithMax = false, bool trackLifecycle = false)
        {
            var result = new Dictionary<(int Die, int Bonus, int Consumed), Outcome>();
            void Emit(int value, int bonus, double probability, int applied)
            {
                    int consumed = 0;
                    if (trackLifecycle)
                        for (int i = 0; i < (modifiers?.Count ?? 0); i++)
                            if ((applied & (1 << i)) != 0 && (modifiers[i].ConsumeWhenApplied || value == 20 && modifiers[i].ConsumeOn20)) consumed |= 1 << i;
                    Add(result, value, bonus, probability, consumed);
            }
            void Final(int die, int bonus, double chance, int net, bool replaced = false, int applied = 0)
            {
                // RuleRollDice.Override remains the returned result even when
                // an IWasRoll handler adds a subsequent reroll.
                if (replaced) { Emit(die, bonus, chance, applied); return; }
                if (afterRoll == null || afterRoll.Count == 0) { Emit(replaceOneWithMax && die == 1 ? 20 : die, bonus, chance, applied); return; }
                var values = new double[21]; values[die] = chance;
                foreach (int direction in afterRoll)
                {
                    net += direction;
                    var next = new double[21];
                    for (int kept = 1; kept <= 20; kept++) if (values[kept] != 0)
                        for (int rolled = 1; rolled <= 20; rolled++)
                        {
                            int value = replaceOneWithMax && rolled == 1 ? 20 : rolled;
                            next[net > 0 ? Math.Max(kept, value) : Math.Min(kept, value)] += values[kept] / 20;
                        }
                    values = next;
                }
                for (int kept = 1; kept <= 20; kept++) if (values[kept] != 0) Emit(replaceOneWithMax && kept == 1 ? 20 : kept, bonus, values[kept], applied);
            }
            if (modifiers != null && modifiers.Count > 8)
                throw new NotSupportedException("More than eight d20 modifiers: probability unavailable (not truncated)");
            int n = modifiers == null ? 0 : modifiers.Count;
            bool typedSaveBonuses = modifiers != null && modifiers.Exists(modifier => modifier?.SaveBonusDescriptor.HasValue == true);
            for (int i = 0; i < n; i++)
            {
                var m = modifiers[i];
                if (m == null || m.Chance < 0 || m.Chance > 100 || m.Rerolls < 0 || m.Compare < 0 || m.Compare > 5
                    || (m.Replace && (m.Roll < 1 || m.Roll > 20)))
                    throw new NotSupportedException("Unknown/invalid d20 modifier: probability unavailable");
            }
            for (int mask = 0; mask < (1 << n); mask++)
            {
                double p = 1;
                for (int i = 0; i < n; i++)
                {
                    bool on = (mask & (1 << i)) != 0;
                    p *= on ? modifiers[i].Chance / 100.0 : 1 - modifiers[i].Chance / 100.0;
                }
                if (p <= 0) continue;
                for (int f = 1; f <= 20; f++)
                {
                    // The first die is rolled before the modifiers that apply only if it failed.
                    D20Modifier replace = null;
                    int bonus = 0;
                    int applied = 0;
#if WOTR
                    // RuleRollDice: every applying modifier adds its rerolls, positive to keep the best, negative the
                    // worst, so opposite ones cancel out; a fixed roll overrides the result.
                    int net = 0;
                    for (int i = 0; i < n; i++)
                    {
                        var m = modifiers[i];
                        if ((mask & (1 << i)) == 0 || !m.Applies(replace?.Roll ?? f, failed)) continue;
                        applied |= 1 << i;
                        if (!typedSaveBonuses) bonus = checked(bonus + m.SaveBonus);
                        if (m.Replace) replace = m;
                        else net = checked(net + (m.TakeBest ? m.Rerolls : -m.Rerolls));
                    }
                    var reroll = net == 0 ? null : new D20Modifier { Rerolls = Math.Abs(net), TakeBest = net > 0 };
                    if (typedSaveBonuses) bonus = ConditionalSaveBonusMath.Sum(modifiers, applied);
#else
                    D20Modifier reroll = null;
                    for (int i = 0; i < n; i++)
                    {
                        var m = modifiers[i];
                        if ((mask & (1 << i)) == 0 || (m.OnlyIfFailed && !failed(f))) continue;
                        bonus += m.SaveBonus;
                        if (m.Replace) replace = m; else reroll = m;
                    }
#endif
                    double pf = p * 0.05;
                    int currentNet = reroll == null ? 0 : reroll.TakeBest ? reroll.Rerolls : -reroll.Rerolls;
                    if (replace != null && replace.Roll >= 1 && replace.Roll <= 20)
                        Final(replace.Roll, bonus, pf, currentNet, true, applied);
                    else if (reroll != null && reroll.Rerolls > 0)
                        for (int x = 1; x <= 20; x++)
                        {
                            double px = Kept(reroll, f, x, replaceOneWithMax);
                            if (px > 0) Final(x, bonus, pf * px, currentNet, applied: applied);
                        }
                    else
                        Final(f, bonus, pf, currentNet, applied: applied);
                }
            }
            return new List<Outcome>(result.Values);
        }

        static void Add(Dictionary<(int Die, int Bonus, int Consumed), Outcome> result, int die, int bonus, double p, int consumed)
        {
            var key = (die, bonus, consumed);
            Outcome o;
            result.TryGetValue(key, out o);
            o.Die = die;
            o.Bonus = bonus;
            o.ConsumedMask = consumed;
            o.P += p;
            result[key] = o;
        }

        // The first die f and n more dice, the best or the worst kept: the chance that x is kept.
        static double Kept(D20Modifier m, int f, int x, bool replaceOneWithMax = false)
        {
            int n = m.Rerolls;
            double Cdf(int die) => die >= 20 ? 1 : Math.Max(0, die - (replaceOneWithMax ? 1 : 0)) / 20.0;
            double Survival(int die) => die <= 1 ? 1 : die > 20 ? 0 : (21 - die + (replaceOneWithMax ? 1 : 0)) / 20.0;
            if (m.TakeBest)
                // P(max = x) = P(max <= x) - P(max <= x-1), max <= x when f <= x and every other die <= x.
                return (f <= x ? Math.Pow(Cdf(x), n) : 0) - (f <= x - 1 ? Math.Pow(Cdf(x - 1), n) : 0);
            // P(min = x) = P(min >= x) - P(min >= x+1).
            return (f >= x ? Math.Pow(Survival(x), n) : 0) - (f >= x + 1 ? Math.Pow(Survival(x + 1), n) : 0);
        }

        // pmf[1..20] of the die kept.
        internal static double[] Pmf(List<D20Modifier> modifiers, Func<int, bool> failed)
        {
            var pmf = new double[21];
            foreach (var o in Outcomes(modifiers, failed)) pmf[o.Die] += o.P;
            return pmf;
        }

        internal static double P(double[] pmf, Func<int, bool> pred)
        {
            double s = 0;
            for (int d = 1; d <= 20; d++) if (pred(d)) s += pmf[d];
            return s;
        }

        internal static double P(List<Outcome> outcomes, Func<int, int, bool> pred)
        {
            double s = 0;
            foreach (var o in outcomes) if (pred(o.Die, o.Bonus)) s += o.P;
            return s;
        }

        internal static string Describe(List<D20Modifier> modifiers)
        {
            if (modifiers == null || modifiers.Count == 0) return "";
            var parts = new List<string>();
            foreach (var m in modifiers)
            {
                string what = m.Replace ? "roll " + m.Roll
                    : (m.TakeBest ? "best of " : "worst of ") + (m.Rerolls + 1);
                if (m.OnlyIfFailed) what += " if failed";
                if (m.OnlyIfSuccess) what += " if succeeded";
                if (m.Compare > 0 && m.Compare <= 5) what += " if die " + new[] { "", ">", "<", "<=", ">=", "=" }[m.Compare] + " " + m.CompareValue;
                if (m.SaveBonus != 0) what += string.Format(" save {0:+0;-0}", m.SaveBonus);
                if (m.Chance < 100) what += " " + m.Chance + "%";
                parts.Add((m.Source ?? "?") + ": " + what);
            }
            return string.Join("; ", parts.ToArray());
        }
    }
}
