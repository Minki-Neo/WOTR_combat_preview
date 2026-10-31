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
        }

        // Every outcome of the roll. "failed" is the game's own test of the first die for "reroll only if failed".
        // Each modifier applies with its chance; the game lets each applying modifier set the roll in turn, so the last
        // one wins and a fixed roll (Replace) wins over any reroll.
        internal static List<Outcome> Outcomes(List<D20Modifier> modifiers, Func<int, bool> failed)
        {
            var result = new Dictionary<long, Outcome>();
            if (modifiers != null && modifiers.Count > 8)
                throw new NotSupportedException("More than eight d20 modifiers: probability unavailable (not truncated)");
            int n = modifiers == null ? 0 : modifiers.Count;
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
#if WOTR
                    // RuleRollDice: every applying modifier adds its rerolls, positive to keep the best, negative the
                    // worst, so opposite ones cancel out; a fixed roll overrides the result.
                    int net = 0;
                    for (int i = 0; i < n; i++)
                    {
                        var m = modifiers[i];
                        if ((mask & (1 << i)) == 0 || !m.Applies(f, failed)) continue;
                        bonus = checked(bonus + m.SaveBonus);
                        if (m.Replace) replace = m;
                        else net = checked(net + (m.TakeBest ? m.Rerolls : -m.Rerolls));
                    }
                    var reroll = net == 0 ? null : new D20Modifier { Rerolls = Math.Abs(net), TakeBest = net > 0 };
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
                    if (replace != null && replace.Roll >= 1 && replace.Roll <= 20)
                        Add(result, replace.Roll, bonus, pf);
                    else if (reroll != null && reroll.Rerolls > 0)
                        for (int x = 1; x <= 20; x++)
                        {
                            double px = Kept(reroll, f, x);
                            if (px > 0) Add(result, x, bonus, pf * px);
                        }
                    else
                        Add(result, f, bonus, pf);
                }
            }
            return new List<Outcome>(result.Values);
        }

        static void Add(Dictionary<long, Outcome> result, int die, int bonus, double p)
        {
            long key = ((long)bonus << 8) | (uint)die;
            Outcome o;
            result.TryGetValue(key, out o);
            o.Die = die;
            o.Bonus = bonus;
            o.P += p;
            result[key] = o;
        }

        // The first die f and n more dice, the best or the worst kept: the chance that x is kept.
        static double Kept(D20Modifier m, int f, int x)
        {
            int n = m.Rerolls;
            if (m.TakeBest)
                // P(max = x) = P(max <= x) - P(max <= x-1), max <= x when f <= x and every other die <= x.
                return (f <= x ? Math.Pow(x / 20.0, n) : 0) - (f <= x - 1 ? Math.Pow((x - 1) / 20.0, n) : 0);
            // P(min = x) = P(min >= x) - P(min >= x+1).
            return (f >= x ? Math.Pow((21 - x) / 20.0, n) : 0) - (f >= x + 1 ? Math.Pow((20 - x) / 20.0, n) : 0);
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
