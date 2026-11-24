using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatPreview
{
    internal sealed class D20RollModel
    {
        internal readonly List<D20Modifier> Modifiers = new List<D20Modifier>();
        internal readonly List<int> AfterRoll = new List<int>();
        internal readonly List<string> Notes = new List<string>();
        internal bool Complete = true;
        internal bool ConsumesEffect;
        internal bool ReplaceOneWithMax;
        internal double Probability(Func<int, bool> failed, Func<int, int, bool> success)
            => Math.Max(0, Math.Min(1, D20.Outcomes(Modifiers, failed, AfterRoll, ReplaceOneWithMax)
                .Where(outcome => success(outcome.Die, outcome.Bonus)).Sum(outcome => outcome.P)));
        internal double Attack(int bonus, int ac, bool alwaysChance)
            => Probability(die => !SpellPreviewMath.AttackHits(die, bonus, ac, alwaysChance),
                (die, extra) => SpellPreviewMath.AttackHits(die, bonus + extra, ac, alwaysChance));
        internal double SaveFailure(int bonus, int dc, bool persistent)
        {
            // The saving throw's stat is read after its D20 is triggered. Its
            // conditional reroll test at this stage has not read that stat yet.
            double success = Probability(die => die != 20 && (die <= 1 || die < dc),
                (die, extra) => die == 20 || die > 1 && (long)die + bonus + extra >= dc);
            return 1 - (persistent ? success * success : success);
        }
        internal double Resistance(int bonus, int sr)
            => Probability(die => true, (die, extra) => (long)die + bonus + extra >= sr);
        internal Dictionary<string, object> Data() => new Dictionary<string, object> {
            { "complete", Complete }, { "consumesEffect", ConsumesEffect }, { "replaceOneWithMax", ReplaceOneWithMax }, { "sources", Notes.ToArray() },
            { "modifiers", Modifiers.Select(modifier => new Dictionary<string, object> {
                { "source", modifier.Source }, { "rerolls", modifier.Rerolls }, { "takeBest", modifier.TakeBest },
                { "onlyIfFailed", modifier.OnlyIfFailed }, { "onlyIfSuccess", modifier.OnlyIfSuccess },
                { "chance", modifier.Chance }, { "replace", modifier.Replace }, { "roll", modifier.Roll },
                { "compare", modifier.Compare }, { "compareValue", modifier.CompareValue }, { "saveBonus", modifier.SaveBonus },
                { "saveBonusDescriptor", modifier.SaveBonusDescriptor }, { "saveBonusBaselinePositive", modifier.SaveBonusBaselinePositive },
                { "saveBonusBaselineNegative", modifier.SaveBonusBaselineNegative },
                { "factKey", modifier.FactKey }, { "consumeWhenApplied", modifier.ConsumeWhenApplied }, { "consumeOn20", modifier.ConsumeOn20 }
            }).ToArray() }, { "afterRollDirections", AfterRoll.ToArray() }
        };
    }
}
