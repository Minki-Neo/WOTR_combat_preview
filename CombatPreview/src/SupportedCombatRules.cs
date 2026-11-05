using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatPreview
{
    // Copied scalars only. These calculations never dispatch a game rule or consume a pool.
    public static class SupportedCombatRules
    {
        public static int AttackModifier(int value, int bab, int rank, bool scale, int multiplier = 1)
        { return checked(value * (scale ? 1 + bab / 4 : 1) * rank * multiplier); }
        public static int DamageModifier(int value, int bab, int rank, bool scale, bool powerScaling,
            bool secondary, bool twoHands, bool mythic, bool greater)
        {
            int amount = checked((value + (mythic ? 1 : 0)) * (scale ? 1 + bab / 4 : 1));
            if (powerScaling) amount = secondary ? amount / 2 : !twoHands ? amount : greater ? amount * 2 : mythic ? amount * 5 / 3 : amount * 3 / 2;
            return checked(amount * rank);
        }
        // Any intersection within a material/form/alignment mask, AND/OR between clauses.
        // Unknown is retained unless the other clauses already decide the result.
        public static bool? Bypass(bool any, IEnumerable<bool?> clauses)
        {
            var values = clauses.ToArray();
            if (values.Length == 0) return false;
            if (any && values.Any(v => v == true)) return true;
            if (!any && values.Any(v => v == false)) return false;
            if (values.Any(v => !v.HasValue)) return null;
            return !any;
        }
        public static bool? MaskBypass(int required, int actual, bool known)
        { return known ? (required & actual) != 0 : (bool?)null; }
        public static bool? MaterialBypass(int required, int actual, int enhancement, int coldIronAndSilver, int adamantite, bool known)
        {
            if (enhancement >= 3) actual |= coldIronAndSilver;
            if (enhancement >= 4) actual |= adamantite;
            return MaskBypass(required, actual, known);
        }
        public static bool? AlignmentBypass(int required, int actual, int enhancementTotal, bool known)
        { return known ? (required & actual) != 0 || enhancementTotal >= 5 : (bool?)null; }
        // Game physical immunity combines all active masks and requires coverage
        // of every form on the incoming damage (not just one intersecting bit).
        public static bool? PhysicalImmunity(int immuneForms, int damageForms, bool formsKnown)
        {
            if (immuneForms == 0) return false;
            if (!formsKnown) return null;
            return damageForms != 0 && (damageForms & ~immuneForms) == 0;
        }
        public static void AddCompleteImmunity(DefenseProfile profile)
        {
            foreach (DamageKind kind in Enum.GetValues(typeof(DamageKind)))
                if (kind != DamageKind.Unknown) profile.Immunities.Add(kind);
        }
        public static void AddReduction(DefenseProfile profile, DamageKind kind, int value)
        {
            if (value < 0 || value > 1000000) throw new ArgumentOutOfRangeException("value");
            int existing; profile.Reduction.TryGetValue(kind, out existing);
            profile.Reduction[kind] = Math.Max(existing, value);
        }
        public static DamageComponentSnapshot[] ProjectPendingDamage(IReadOnlyList<DamageComponentSnapshot> original, int bonus)
        {
            var copy = original.ToArray();
            if (bonus == 0) return copy;
            int index = Array.FindIndex(copy, c => c.Kind == DamageKind.Physical);
            if (index < 0) throw new NotSupportedException("예약 무기 피해를 적용할 물리 구성이 없습니다.");
            var part = copy[index];
            copy[index] = new DamageComponentSnapshot(new DamageTerm(part.Rolls, part.Sides, checked(part.Bonus + bonus), part.Multiplies)
                { Kind = part.Kind }, part.BypassKnown, part.BypassTraits);
            return copy;
        }
        public static string DefenseStamp(DefenseProfile p)
        {
            return p == null ? "unavailable" : p.Complete + ":" + p.HasConsumablePool + ":" + p.UnavailableReason
                + ":" + string.Join(",", p.Reduction.OrderBy(x => x.Key).Select(x => x.Key + "=" + x.Value))
                + ":" + string.Join(",", p.Immunities.OrderBy(x => x));
        }
        public static DefenseProfile Copy(DefenseProfile source)
        {
            if (source == null) return null;
            var copy = new DefenseProfile { Complete = source.Complete, Source = source.Source,
                UnavailableReason = source.UnavailableReason, HasConsumablePool = source.HasConsumablePool };
            foreach (var p in source.Reduction) copy.Reduction.Add(p.Key, p.Value);
            foreach (var k in source.Immunities) copy.Immunities.Add(k);
            return copy;
        }
    }
}
