using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatPreview
{
    // Target-independent completed engine output. No hand-maintained feat list.
    public sealed class NativeCombatValues
    {
        public readonly int? AttackBonus;
        public readonly int Enhancement, EnhancementTotal, Materials, Alignment, Form;
        public readonly bool TraitsKnown;
        public readonly int Edge, Multiplier;
        public readonly IReadOnlyList<DamageComponentSnapshot> Components;
        public readonly IReadOnlyList<string> Modifiers;
        public NativeCombatValues(int? ab, int edge, int multiplier, IEnumerable<DamageComponentSnapshot> components, IEnumerable<string> modifiers, int enhancement = 0, int enhancementTotal = 0, int materials = 0, int alignment = 0, int form = 0, bool traitsKnown = false)
        {
            AttackBonus = ab; Edge = edge; Multiplier = multiplier;
            Enhancement = enhancement; EnhancementTotal = enhancementTotal; Materials = materials; Alignment = alignment; Form = form; TraitsKnown = traitsKnown;
            if (edge < 2 || edge > 20 || multiplier < 1 || multiplier > 10) throw new ArgumentOutOfRangeException("critical");
            var copy = components == null ? new DamageComponentSnapshot[0] : components.ToArray();
            if (copy.Length == 0 || copy.Length > 32 || copy.Any(c => c == null)) throw new ArgumentException("Missing native damage components");
            Components = Array.AsReadOnly(copy); Modifiers = Array.AsReadOnly((modifiers ?? new string[0]).ToArray());
        }
        public BetaInput Apply(BetaInput input, DefenseProfile defense = null, IEnumerable<string> defenseNotes = null)
        {
            var components = SupportedCombatRules.ProjectPendingDamage(Components, input.PendingDamage);
            var first = components[0];
            return new BetaInput(input.Actor, input.Target, input.Weapon, input.Turn,
                input.Signature + "|defense:" + SupportedCombatRules.DefenseStamp(defense ?? input.Defense) + "|engine:" + AttackBonus + ":" + Edge + ":" + Multiplier + ":" + string.Join(";", Components.Select(c => c.Kind + ":" + c.Rolls + ":" + c.Sides + ":" + c.Bonus)),
                input.BAB, AttackBonus.HasValue ? checked(AttackBonus.Value + input.PendingAttack) : input.AttackBonus, input.AC, first.Rolls, first.Sides, first.Bonus, Edge, Multiplier,
                input.FullAttack, true, input.DefensePartPresent, components,
                AttackBonus.HasValue ? "observed-weapon-rules/estimated-target-ac" : "observed-weapon-damage/estimated-attack-ac", input.NaturalWeapon,
                input.AttackPenalties, input.AttackCountSource, input.AttackCountReasons, input.Grip, defense ?? input.Defense,
                Modifiers.Concat(new[] { "게임 공통 규칙 결과 사용: 활성 피트 보정 재합산 안 함" })
                    .Concat(input.PendingAttack != 0 || input.PendingDamage != 0 ? new[] { "공격 시 예약 효과: AB " + input.PendingAttack + ", 피해 " + input.PendingDamage } : new string[0]),
                defenseNotes ?? input.DefenseNotes, input.PendingAttack, input.PendingDamage);
        }
    }
    public sealed class NativeCombatValueCache
    {
        sealed class Entry { internal NativeCombatValues Value; internal long Time; }
        readonly Dictionary<string, Entry> values = new Dictionary<string, Entry>();
        public void Put(string stamp, NativeCombatValues value, long now)
        {
            if (values.Count >= 64 && !values.ContainsKey(stamp)) values.Clear();
            Entry old;
            // A separate weapon-stat callback must not erase the more complete AB result.
            if (!value.AttackBonus.HasValue && values.TryGetValue(stamp, out old) && now >= old.Time && now - old.Time <= 1500 && old.Value.AttackBonus.HasValue
                && SameDamage(old.Value, value)) return;
            values[stamp] = new Entry { Value = value, Time = now };
        }
        static bool SameDamage(NativeCombatValues a, NativeCombatValues b)
        {
            return a.Edge == b.Edge && a.Multiplier == b.Multiplier && a.Enhancement == b.Enhancement
                && a.EnhancementTotal == b.EnhancementTotal && a.Materials == b.Materials && a.Alignment == b.Alignment
                && a.Form == b.Form && a.TraitsKnown == b.TraitsKnown && a.Components.Count == b.Components.Count
                && a.Components.Zip(b.Components, (x, y) => x.Kind == y.Kind && x.Rolls == y.Rolls && x.Sides == y.Sides
                    && x.Bonus == y.Bonus && x.Multiplies == y.Multiplies).All(equal => equal);
        }
        public string ReadStatus(string stamp, long now)
        {
            Entry e;
            if (!values.TryGetValue(stamp, out e)) return values.Count == 0 ? "NotObserved" : "ContextMiss";
            if (now < e.Time) return "ClockRegression";
            return now - e.Time > 1500 ? "Expired" : "Hit";
        }
        public NativeCombatValues Read(string stamp, long now)
        { Entry e; return ReadStatus(stamp, now) == "Hit" && values.TryGetValue(stamp, out e) ? e.Value : null; }
        public void Clear() { values.Clear(); }
    }
}
