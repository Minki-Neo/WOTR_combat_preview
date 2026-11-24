using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.Designers.Mechanics.WeaponEnchants;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Items;
using Kingmaker.RuleSystem;

namespace CombatPreview
{
    internal static class WeaponFallbackReader
    {
        static WeaponSizeDice[][] scaleRows;
        static WeaponSizeDice[][] ScaleRows()
        {
            if (scaleRows != null) return scaleRows;
            var data = CombatFields.Field(typeof(WeaponDamageScaleTable), "Data")?.GetValue(null);
            if (!(CombatFields.Read(data, "Rows") is IEnumerable rows)) return null;
            var snapshot = new List<WeaponSizeDice[]>();
            foreach (var row in rows)
            {
                if (!(CombatFields.Read(row, "<Items>k__BackingField") is IEnumerable raw)) return null;
                var items = new List<WeaponSizeDice>();
                foreach (var item in raw)
                {
                    if (!(CombatFields.Read(item, "Dice") is DiceFormula dice)) return null;
                    items.Add(new WeaponSizeDice { Size = CombatFields.Int(item, "Size"), Rolls = dice.Rolls, Type = (int)dice.Dice,
                        Sides = dice.Rolls == 0 ? 1 : dice.Dice.Sides() });
                }
                snapshot.Add(items.ToArray());
            }
            return scaleRows = snapshot.ToArray();
        }
        internal static int RangedAttribute(UnitEntityData actor, ItemEntityWeapon weapon, List<string> notes)
        {
            int? chosen = null; float multiplier = 1;
            foreach (var fact in ActiveCombatReader.Enchantments(weapon))
            foreach (var runtime in fact.Components)
            {
                var component = CombatFields.Component(runtime);
                StatType attribute; float factor;
                bool forced = component?.GetType() == typeof(WeaponDamageMultiplierStatReplacement);
                if (forced)
                { var replacement = (WeaponDamageMultiplierStatReplacement)component; attribute = replacement.Stat; factor = replacement.Multiplier; }
                else if (component?.GetType() == typeof(WeaponDamageStatReplacement))
                {
                    var replacement = (WeaponDamageStatReplacement)component;
                    if (replacement.RequiresFinesse && !actor.State.Features.WeaponFinesse) continue;
                    attribute = replacement.Stat; factor = multiplier;
                }
                else continue;
                var stat = actor.Stats.GetStat(attribute) as ModifiableValueAttributeStat;
                if (!(CombatFields.CachedStat(stat) is int score)) continue;
                int bonus = (int)Math.Floor((score - 10) / 2.0);
                if (!forced && chosen.HasValue && chosen >= bonus) continue;
                if (forced && chosen.HasValue && (chosen != bonus || multiplier != factor))
                    throw new NotSupportedException("원거리 피해 능력치 치환 중첩 순서 미확인");
                chosen = bonus; multiplier = factor;
                notes.Add(CombatFields.Label(fact) + ": 피해 능력치 " + attribute + " " + bonus.ToString("+0;-0;0") + " ×" + multiplier);
            }
            if (chosen.HasValue) return WeaponFallbackMath.AttributeDamage(chosen.Value, multiplier);
            // Unmodified bows apply a negative Strength modifier, but do not
            // gain a positive one. Composite/stat enchantments were handled above.
            if (weapon.Blueprint.Category.ToString() == "Longbow" || weapon.Blueprint.Category.ToString() == "Shortbow")
                return Math.Min(0, BetaReader.AbilityModifier(actor, "Strength"));
            return 0;
        }
        internal static DiceFormula Dice(UnitEntityData actor, ItemEntityWeapon weapon, List<string> notes)
        {
            var basis = weapon.Blueprint.BaseDamage;
            // Polymorphs and NPC overridden dice have a different base-size rule.
            // Preserve the current fallback there rather than inventing that context.
            if (actor.Body.IsPolymorphed || !actor.IsPlayerFaction) return basis;
            if (!(CombatFields.Read(actor.State, "m_Size") is Kingmaker.Enums.Size size)) return basis;
            int shift = 0;
            foreach (var fact in actor.Facts.List.Where(fact => fact.Active))
            foreach (var runtime in fact.Components)
            {
                if (!CombatFields.Bool(runtime, "<IsSubscribedOnEventBus>k__BackingField")) continue;
                var component = CombatFields.Component(runtime); int change = 0;
                if (component?.GetType() == typeof(RangedWeaponSizeChange) && weapon.Blueprint.IsRanged)
                {
                    var rule = (RangedWeaponSizeChange)component;
                    if (rule.WeaponTypes == null) throw new NotSupportedException("원거리 크기 보정 필터 없음");
                    if (rule.WeaponTypes.Capacity != 0 && !rule.WeaponTypes.Any(reference => CombatFields.Reference(reference, weapon.Blueprint.Type))) continue;
                    change = WeaponFallbackMath.SizeChange(rule.SizeCategoryChange, true);
                }
                else if (component?.GetType() == typeof(MeleeWeaponSizeChange) && weapon.Blueprint.IsMelee)
                    change = WeaponFallbackMath.SizeChange(((MeleeWeaponSizeChange)component).SizeCategoryChange, true);
                else if (component?.GetType() == typeof(WeaponSizeChange))
                {
                    var rule = (WeaponSizeChange)component;
                    if (rule.CheckWeaponCategory && rule.Category != weapon.Blueprint.Category) continue;
                    change = WeaponFallbackMath.SizeChange(rule.SizeCategoryChange, false);
                }
                if (change != 0) { shift = checked(shift + change); notes.Add(CombatFields.Label(fact) + ": 무기 유효 크기 " + change.ToString("+0;-0;0")); }
            }
            int desired = WeaponFallbackMath.EffectiveSize((int)size, shift);
            if (desired == 4) return basis;
            // Read the engine's immutable scale table, without triggering a rule
            // or invoking weapon/wielder/size getters that populate live caches.
            var scaled = WeaponFallbackMath.Scale(basis.Rolls, basis.Rolls == 0 ? 1 : basis.Dice.Sides(), 4, desired, ScaleRows());
            if (scaled.HasValue)
            {
                var result = new DiceFormula(scaled.Value.Rolls, (DiceType)scaled.Value.Type);
                notes.Add("무기 크기 주사위: " + basis + " → " + result + " (유효 크기 " + desired + ")"); return result;
            }
            notes.Add("무기 크기 주사위 테이블 미일치: 기본식 유지");
            return basis;
        }
    }
}
