using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Facts;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.EntitySystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Enums;
using Kingmaker.Enums.Damage;
using Kingmaker.Items;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Parts;
using Kingmaker.Utility;

namespace CombatPreview
{
    // Build-specific adapter: only copied fields and audited pure predicates.
    // Never invoke component handlers, ContextValue.Calculate, reference.Get or Ensure.
    internal static class CombatFields
    {
        internal static object Read(object o, string name) { return o == null ? null : AccessTools.Field(o.GetType(), name)?.GetValue(o); }
        internal static int Int(object o, string name, int fallback = 0) { var v = Read(o, name); return v == null ? fallback : Convert.ToInt32(v, CultureInfo.InvariantCulture); }
        internal static bool Bool(object o, string name) { return Read(o, name) as bool? == true; }
        internal static object Blueprint(object fact) { return Read(fact, "<Blueprint>k__BackingField"); }
        internal static object Component(object runtime) { return Read(runtime, "<SourceBlueprintComponent>k__BackingField"); }
        internal static int Rank(object fact) { return Int(fact, "<Rank>k__BackingField", 1); }
        internal static string Label(object fact) { return (Blueprint(fact) as BlueprintUnitFact)?.Name ?? "활성 효과"; }
        internal static bool Reference(object reference, object blueprint)
        {
            if (reference == null || blueprint == null) return false;
            var bp = blueprint as SimpleBlueprint;
            if (bp == null) return false;
            var cached = Read(reference, "<Cached>k__BackingField");
            if (cached != null) return cached == blueprint;
            var guid = Read(reference, "guid") as string;
            if (string.IsNullOrEmpty(guid)) guid = Read(reference, "deserializedGuid")?.ToString();
            return string.Equals(guid?.Replace("-", ""), bp.AssetGuid.ToString().Replace("-", ""), StringComparison.OrdinalIgnoreCase);
        }
        internal static bool HasFact(UnitEntityData unit, object reference, object parameter = null)
        { return reference != null && unit.Facts.List.Any(f => f.Active && Reference(reference, Blueprint(f)) && (parameter == null || Equals(Read(f, "<Param>k__BackingField"), parameter))); }
        internal static bool Scalar(ContextValue value, object fact, out int amount)
        {
            amount = 0;
            if (value == null) return false;
            if (value.ValueType == ContextValueType.Simple) { amount = value.Value; return true; }
            var context = Read(fact, "m_Context");
            int[] data; int index;
            if (value.ValueType == ContextValueType.Rank) { data = Read(context, "m_Ranks") as int[]; index = (int)value.ValueRank; }
            else if (value.ValueType == ContextValueType.Shared) { data = Read(context, "m_SharedValues") as int[]; index = (int)value.ValueShared; }
            else return false;
            if (data == null || index < 0 || index >= data.Length) return false;
            amount = data[index]; return true;
        }
        internal static string FactStamp(object fact)
        {
            var c = Read(fact, "m_Context");
            Func<string, string> array = name => { var a = Read(c, name) as int[]; return a == null ? "?" : string.Join(",", a); };
            return BetaIdentity.Id(fact) + ":" + Read(fact, "<IsActive>k__BackingField") + ":" + Rank(fact)
                + ":" + Read(fact, "m_IsOn") + ":" + Read(fact, "<IsStarted>k__BackingField") + ":" + Read(fact, "<IsSuppressed>k__BackingField") + ":" + array("m_Ranks") + ":" + array("m_SharedValues");
        }
    }
    internal sealed class WeaponTraits
    {
        internal int Enhancement, EnhancementTotal, Materials, Alignment, Form;
        internal bool Known = true;
        internal readonly List<string> Notes = new List<string>();
    }
    internal sealed class CombatModifiers
    {
        internal int Attack, Damage, Ability, PendingAttack, PendingDamage;
        internal readonly List<string> Notes = new List<string>();
    }
    internal static class ActiveCombatReader
    {
        internal static IEnumerable<EntityFact> Enchantments(ItemEntityWeapon weapon)
        {
            // Read the existing manager list. RawFacts/GetAll and ItemEntity.Enchantments
            // can populate caches, so none of these getters are invoked.
            var collection = CombatFields.Read(weapon, "m_Enchantments");
            var manager = CombatFields.Read(collection, "<Manager>k__BackingField");
            var list = CombatFields.Read(manager, "m_Facts") as IEnumerable;
            if (list == null) yield break;
            int count = 0;
            foreach (var item in list) { if (++count > 128) throw new NotSupportedException("무기 마법부여 판독 한도"); var f = item as EntityFact; if (f != null && f.Active) yield return f; }
        }
        internal static WeaponTraits Traits(ItemEntityWeapon weapon)
        {
            var t = new WeaponTraits();
            var physical = weapon.Blueprint.DamageType?.Physical;
            if (physical == null) t.Known = false;
            else
            {
                t.Form = CombatFields.Int(physical, "Form"); t.Materials = CombatFields.Int(physical, "Material");
                t.Enhancement = CombatFields.Int(physical, "Enhancement"); t.EnhancementTotal = CombatFields.Int(physical, "EnhancementTotal");
            }
            var enchantments = CombatFields.Read(weapon, "m_Enchantments");
            if (enchantments != null && CombatFields.Read(CombatFields.Read(enchantments, "<Manager>k__BackingField"), "m_Facts") == null)
            { t.Known = false; t.Notes.Add("무기 마법부여 목록 미확인"); }
            int enhancement = 0, stack = 0, total = 0;
            foreach (var fact in Enchantments(weapon))
                foreach (var runtime in fact.Components)
                {
                    var c = CombatFields.Component(runtime);
                    if (c == null) continue;
                    if (c.GetType() == typeof(WeaponEnhancementBonus))
                    { var e = (WeaponEnhancementBonus)c; if (e.Stack) stack += e.EnhancementBonus; else enhancement = Math.Max(enhancement, e.EnhancementBonus); total += e.EnhancementBonus; }
                    else if (c.GetType() == typeof(WeaponMaterial)) t.Materials |= (int)((WeaponMaterial)c).Material;
                    else if (c.GetType() == typeof(WeaponAlignment)) t.Alignment |= (int)((WeaponAlignment)c).Alignment;
                }
            t.Enhancement = Math.Max(t.Enhancement, enhancement + stack); t.EnhancementTotal = Math.Max(t.EnhancementTotal, total);
            t.Notes.Add("무기 강화 +" + t.Enhancement + " · 관통 강화 합계 " + t.EnhancementTotal + " · 재질 " + (PhysicalDamageMaterial)t.Materials + " · 성향 " + (DamageAlignment)t.Alignment);
            return t;
        }
        internal static WeaponTraits CompletedTraits(RuleCalculateWeaponStats stats)
        {
            var t = Traits(stats.Weapon);
            t.Enhancement = stats.Enhancement; t.EnhancementTotal = stats.EnhancementTotal; t.Alignment |= (int)stats.Alignment;
            var physical = stats.DamageDescription.FirstOrDefault(d => d?.TypeDescription?.Type.ToString() == "Physical")?.TypeDescription.Physical;
            if (physical != null) { t.Form = CombatFields.Int(physical, "Form"); t.Materials |= CombatFields.Int(physical, "Material"); }
            return t;
        }
        static bool Finesse(UnitEntityData unit, ItemEntityWeapon weapon)
        {
            if (weapon.Blueprint.Category.HasSubCategory(WeaponSubCategory.Finessable)) return true;
            // DamageGrace.HasEntry is an existing-list query; never Ensure a part.
            return unit.Get<UnitPartDamageGrace>()?.HasEntry(weapon.Blueprint.Category) == true;
        }
        static bool ParameterMatches(object fact, ItemEntityWeapon weapon)
        { return Equals(CombatFields.Read(CombatFields.Read(fact, "<Param>k__BackingField"), "WeaponCategory"), weapon.Blueprint.Category); }
        static bool Filters(object c, UnitEntityData unit, ItemEntityWeapon weapon, bool twoHands, bool damage)
        {
            bool specific = CombatFields.Bool(c, damage ? "OnlySpecificItemBlueprint" : "SpecificWeaponBlueprint");
            if (specific && !damage) return CombatFields.Reference(CombatFields.Read(c, "m_WeaponBlueprint"), weapon.Blueprint);
            if (specific && !CombatFields.Reference(CombatFields.Read(c, "m_WeaponBlueprint"), weapon.Blueprint)) return false;
            if (CombatFields.Bool(c, "OnlyFinessable") && !Finesse(unit, weapon))
            {
                if (!CombatFields.Bool(c, "CanBeUsedWithFightersFinesse")) return false;
                throw new NotSupportedException("비기교 무기의 전사 기교/훈련 필터");
            }
            if (CombatFields.Bool(c, "Ranged") != weapon.Blueprint.IsRanged || (CombatFields.Bool(c, "OnlyTwoHanded") && !weapon.Blueprint.IsTwoHanded)) return false;
            if (CombatFields.Bool(c, "m_checkGripStyle") && CombatFields.Int(c, "m_gripStyle") != (twoHands ? 2 : 1)) return false;
            if (damage && CombatFields.Bool(c, "DualWielding")) throw new NotSupportedException("쌍수 전용 효과");
            if (damage && CombatFields.Bool(c, "OnlyToOffHandBonus") && weapon != BetaReader.Secondary(unit)) return false;
            return true;
        }
        static int CachedEnhancement(ModifiableValue value)
        {
            var dict = CombatFields.Read(value, "ModifierList") as IDictionary;
            if (dict == null || !dict.Contains(ModifierDescriptor.Enhancement)) return 0;
            var mods = dict[ModifierDescriptor.Enhancement] as IEnumerable;
            int best = 0;
            if (mods != null) foreach (var m in mods) if (CombatFields.Int(m, "StackMode") != 1) best = Math.Max(best, CombatFields.Int(m, "ModValue"));
            return best;
        }
        internal static CombatModifiers Read(UnitEntityData unit, UnitEntityData target, ItemEntityWeapon weapon, int bab, bool full, bool twoHands, int initialAbility, WeaponTraits traits)
        {
            var result = new CombatModifiers { Ability = initialAbility };
            // Enhancement competes with an existing nonstacking enhancement stat bonus.
            result.Attack = Math.Max(0, traits.Enhancement - CachedEnhancement(unit.Stats.AdditionalAttackBonus));
            result.Damage = Math.Max(0, traits.Enhancement - CachedEnhancement(unit.Stats.AdditionalDamage));
            if (traits.Enhancement != 0) result.Notes.Add("무기 강화: AB +" + result.Attack + ", 피해 +" + result.Damage);
            foreach (var entry in PendingAttackEffects.Entries(unit, weapon, full, result.Notes))
            {
                var fact = entry.Fact; var c = entry.Component;
                int attack = 0, damage = 0; bool supported = true;
                try
                {
                    if (entry.Pending && c.GetType() == typeof(AddStatBonus))
                    {
                        var a = (AddStatBonus)c;
                        if (a.Descriptor != ModifierDescriptor.UntypedStackable && a.Descriptor != ModifierDescriptor.Penalty) throw new NotSupportedException("예약 능력치 보정 중첩");
                        int amount = SupportedCombatRules.AttackModifier(a.Value, bab, 1, a.ScaleByBasicAttackBonus);
                        if (a.Stat == StatType.AdditionalAttackBonus) attack = amount;
                        else if (a.Stat == StatType.AdditionalDamage) damage = amount;
                    }
                    else if (c.GetType() == typeof(WeaponParametersAttackBonus))
                    {
                        var a = (WeaponParametersAttackBonus)c;
                        if ((a.OnlyForFullAttack && !full) || !Filters(c, unit, weapon, twoHands, false)) continue;
                        if (a.Descriptor != ModifierDescriptor.UntypedStackable && a.Descriptor != ModifierDescriptor.Penalty) throw new NotSupportedException("유형별 중첩 AB " + a.Descriptor);
                        int value = a.AttackBonus;
                        if (a.UseContextIstead && ((entry.Pending && a.ContextAttackBonus?.ValueType != ContextValueType.Simple) || !CombatFields.Scalar(a.ContextAttackBonus, fact, out value))) throw new NotSupportedException("동적 명중 수치");
                        attack = SupportedCombatRules.AttackModifier(value, bab, entry.Pending ? 1 : CombatFields.Rank(fact), a.ScaleByBasicAttackBonus,
                            CombatFields.HasFact(unit, CombatFields.Read(c, "m_MultiplierFact")) ? a.Multiplier : 1);
                    }
                    else if (c.GetType() == typeof(WeaponParametersDamageBonus))
                    {
                        var d = (WeaponParametersDamageBonus)c;
                        if (!Filters(c, unit, weapon, twoHands, true)) continue;
                        int value = d.DamageBonus;
                        if (d.UseContextIstead && ((entry.Pending && d.ContextDamageBonus?.ValueType != ContextValueType.Simple) || !CombatFields.Scalar(d.ContextDamageBonus, fact, out value))) throw new NotSupportedException("동적 피해 수치");
                        damage = SupportedCombatRules.DamageModifier(value, bab, entry.Pending ? 1 : CombatFields.Rank(fact), d.ScaleByBasicAttackBonus, d.PowerAttackScaling,
                            weapon == BetaReader.Secondary(unit) || weapon.ForceSecondary, twoHands,
                            CombatFields.HasFact(unit, CombatFields.Read(c, "m_MythicBlueprint")), CombatFields.HasFact(unit, CombatFields.Read(c, "m_GreaterPowerAttackBlueprint")));
                    }
                    else if (c.GetType() == typeof(WeaponFocusParametrized))
                    {
                        var f = (WeaponFocusParametrized)c;
                        if (!ParameterMatches(fact, weapon)) continue;
                        if (f.Descriptor != ModifierDescriptor.UntypedStackable) throw new NotSupportedException("유형별 무기 중점화 중첩");
                        attack = CombatFields.HasFact(unit, CombatFields.Read(c, "m_MythicFocus"), CombatFields.Read(fact, "<Param>k__BackingField")) ? 2 : 1;
                    }
                    else if (c.GetType() == typeof(AttackStatReplacement))
                    {
                        var a = (AttackStatReplacement)c;
                        bool match = a.CheckWeaponTypes ? (CombatFields.Read(c, "m_WeaponTypes") as IEnumerable)?.Cast<object>().Any(r => CombatFields.Reference(r, weapon.Blueprint.Type)) == true
                            : weapon.Blueprint.Category.HasSubCategory(a.SubCategory) || (a.SubCategory == WeaponSubCategory.Finessable && Finesse(unit, weapon));
                        if (match) { int replacement = BetaReader.AbilityModifier(unit, a.ReplacementStat.ToString()); if (replacement > result.Ability) { result.Notes.Add(CombatFields.Label(fact) + ": 명중 능력치 " + a.ReplacementStat); result.Ability = replacement; } }
                    }
                    else if (c.GetType() == typeof(AttackTypeAttackBonus))
                    {
                        var a = (AttackTypeAttackBonus)c;
                        if ((a.CheckFact && !CombatFields.HasFact(unit, CombatFields.Read(c, "m_RequiredFact"))) || a.Type.IsSuitableWeapon(weapon.Blueprint) == a.AllTypesExcept) continue;
                        if (a.Descriptor != ModifierDescriptor.UntypedStackable) throw new NotSupportedException("유형별 공격 중첩");
                        int value; if (!CombatFields.Scalar(a.Value, fact, out value)) throw new NotSupportedException("동적 공격 유형 수치");
                        attack = checked(value * a.AttackBonus * CombatFields.Rank(fact));
                    }
                    else if (c.GetType() == typeof(DistanceAttackBonus))
                    {
                        var a = (DistanceAttackBonus)c;
                        if (!weapon.Blueprint.IsRanged || unit.DistanceTo(target) > a.Range.Meters + unit.Corpulence + target.Corpulence) continue;
                        if (a.Descriptor != ModifierDescriptor.UntypedStackable) throw new NotSupportedException("유형별 거리 보정 중첩");
                        attack = a.AttackBonus;
                    }
                    else supported = false;
                }
                catch (NotSupportedException e) { result.Notes.Add("미확인 " + CombatFields.Label(fact) + ": " + e.Message); continue; }
                if (supported && (attack != 0 || damage != 0))
                {
                    result.Attack += attack; result.Damage += damage;
                    if (entry.Pending) { result.PendingAttack += attack; result.PendingDamage += damage; }
                    result.Notes.Add(entry.Label + ": AB " + attack.ToString("+0;-0;0") + ", 피해 " + damage.ToString("+0;-0;0"));
                }
            }
            return result;
        }
    }
}
