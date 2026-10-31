using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints.Facts;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
#if WOTR
using Fact = Kingmaker.EntitySystem.EntityFact;
#endif

namespace CombatPreview
{
    // One term of a sum, with where it comes from.
    class Part
    {
        public string Name;
        public int Value;

        public Part(string name, int value)
        {
            Name = name;
            Value = value;
        }

        public override string ToString() => Name + " " + Value.ToString("+0;-0;0");
    }

    // Attack bonus and armor class split into their terms, as the game adds them up. Taken while the rule's bonuses
    // are on (the temporary modifiers of the attack roll included); whatever the lists do not explain, such as the
    // difficulty adjustment for the party, ends up in "other".
    static class Breakdown
    {
        internal static string FactName(Fact fact)
        {
            if (fact == null) return null;
            string name = fact.Name;
            return string.IsNullOrEmpty(name) ? fact.Blueprint?.name : name;
        }

        static string ModifierName(ModifiableValue.Modifier m)
        {
            // Modifiers without a fact (size, the Dexterity in armor class) are named by their type alone.
            string name = FactName(m.Source) ?? m.ItemSource?.Name ?? m.SourceComponent;
            return name == null ? m.ModDescriptor.ToString() : name + " [" + m.ModDescriptor + "]";
        }

        // Modifiers of a stat, and a correction when same-type bonuses do not stack.
        static void AddStat(List<Part> parts, ModifiableValue stat, int effective)
        {
            int sum = 0;
            foreach (var m in stat.Modifiers)
            {
                parts.Add(new Part(ModifierName(m), m.ModValue));
                sum += m.ModValue;
            }
            if (effective != sum) parts.Add(new Part(stat.Type + ": not stacking", effective - sum));
        }

#if WOTR
        // A bonus of a rule: its fact, else the stat or the kind of bonus it stands for.
        static string BonusName(Modifier b)
        {
            string name = FactName(b.Fact) ?? (b.Stat != StatType.Unknown ? b.Stat.ToString() : b.Type.ToString());
            return name + " [" + b.Descriptor + "]";
        }

        // The attack bonus as RuleCalculateAttackBonus adds it up: the bonuses of the rule (base attack bonus, the
        // ability, the secondary weapon, enhancement, flanking, shooting into melee...) and the modifiers of the
        // additional attack bonus shown at the moment.
        internal static List<Part> AttackBonus(RuleCalculateAttackBonus rule, UnitEntityData unit)
        {
            var parts = new List<Part>();
            foreach (var b in rule.AllBonuses) parts.Add(new Part(BonusName(b), b.Value));
            if (rule.StatModifiersAtTheMoment != null)
                foreach (var m in rule.StatModifiersAtTheMoment) parts.Add(new Part(ModifierName(m), m.ModValue));
            AddOther(parts, rule.Result);
            return parts;
        }

        // The armor class as RuleCalculateAC shows it: the base and the modifiers that count against this attack (the
        // rule's own, as prone, among them).
        internal static List<Part> ArmorClass(RuleCalculateAC rule, UnitEntityData target)
        {
            var parts = new List<Part> { new Part("AC base", target.Stats.AC.BaseValue) };
            if (rule.StatModifiersAtTheMoment != null)
                foreach (var m in rule.StatModifiersAtTheMoment) parts.Add(new Part(ModifierName(m), m.ModValue));
            AddOther(parts, rule.Result);
            return parts;
        }

#else
        internal static List<Part> AttackBonus(RuleCalculateAttackBonus rule, UnitEntityData unit)
        {
            var parts = new List<Part> { new Part("BAB", unit.Stats.BaseAttackBonus.ModifiedValue) };
            var additional = unit.Stats.AdditionalAttackBonus;
            AddStat(parts, additional, additional.ModifiedValueWithDisabledBonuses() - additional.BaseValue);
            if (additional.BaseValue != 0) parts.Add(new Part("AdditionalAttackBonus base", additional.BaseValue));
            parts.Add(new Part(rule.AttackBonusStat.ToString(), rule.AttackBonusStatModifier));
            if (rule.SecondaryBonus != 0) parts.Add(new Part("secondary weapon", rule.SecondaryBonus));
            if (rule.BonusSources != null)
                foreach (var b in rule.BonusSources)
                    parts.Add(new Part(FactName(b.Source) ?? "?", b.Bonus));
            if (rule.AttackBonusPenalty != 0) parts.Add(new Part("attack penalty", -rule.AttackBonusPenalty));
            if (rule.ConcealmentBonus != 0) parts.Add(new Part("target's concealment", rule.ConcealmentBonus));
            if (rule.FlankingBonus != 0) parts.Add(new Part("flanking", rule.FlankingBonus));
            if (rule.ShootIntoCombatBonus != 0) parts.Add(new Part("shooting into melee", rule.ShootIntoCombatBonus));
            AddOther(parts, rule.Result);
            return parts;
        }

        internal static List<Part> ArmorClass(RuleCalculateAC rule, UnitEntityData target)
        {
            var parts = new List<Part>();
            var ac = target.Stats.AC;
            parts.Add(new Part("AC base", ac.BaseValue));
            AddStat(parts, ac, ac.ModifiedValue - ac.BaseValue);
            if (rule.IsTargetFlatFooted || rule.AttackType.IsTouch())
                parts.Add(new Part((rule.IsTargetFlatFooted ? "flat-footed" : "") + (rule.AttackType.IsTouch() ? " touch" : "") + " (excluded types)",
                    (rule.IsTargetFlatFooted ? (rule.AttackType.IsTouch() ? ac.FlatFootedTouch : ac.FlatFooted) : ac.Touch) - ac.ModifiedValue));
            AddOther(parts, rule.TargetAC);
            return parts;
        }

#endif
        // A saving throw: the class base, the ability bonus, the modifiers, as RuleSavingThrow takes the stat.
        internal static List<Part> Save(ModifiableValue stat, int value)
        {
            var parts = new List<Part>();
            var save = stat as ModifiableValueSavingThrow;
            if (save != null)
            {
                parts.Add(new Part("base", save.BaseValue - save.BaseStat.Bonus));
                parts.Add(new Part(save.BaseStat.Type.ToString(), save.BaseStat.Bonus));
                AddStat(parts, stat, save.ModifiedValueWithDisabledBonuses() - save.BaseValue);
            }
            else parts.Add(new Part(stat.Type.ToString(), value));
            AddOther(parts, value);
            return parts;
        }

        static void AddOther(List<Part> parts, int total)
        {
            int sum = parts.Sum(x => x.Value);
            if (sum != total) parts.Add(new Part("other (not listed: difficulty, rule bonuses)", total - sum));
        }

        internal static string Format(List<Part> parts)
        {
            return parts == null ? "" : string.Join(", ", parts.Where(x => x.Value != 0 || x.Name == "BAB").Select(x => x.ToString()).ToArray());
        }

        // Terms that differ between the prediction and the real roll, by name.
        internal static string Diff(List<Part> predicted, List<Part> real)
        {
            var a = Sum(predicted);
            var b = Sum(real);
            var lines = new List<string>();
            foreach (var name in a.Keys.Union(b.Keys))
            {
                int x, y;
                a.TryGetValue(name, out x);
                b.TryGetValue(name, out y);
                if (x != y) lines.Add(string.Format("{0}: predicted {1:+0;-0;0}, real {2:+0;-0;0}", name, x, y));
            }
            return string.Join("; ", lines.ToArray());
        }

        static Dictionary<string, int> Sum(List<Part> parts)
        {
            var d = new Dictionary<string, int>();
            if (parts == null) return d;
            foreach (var p in parts)
            {
                int v;
                d.TryGetValue(p.Name, out v);
                d[p.Name] = v + p.Value;
            }
            return d;
        }
    }
}
