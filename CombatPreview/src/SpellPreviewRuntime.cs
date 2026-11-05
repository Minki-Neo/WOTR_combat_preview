using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Parts;
using UnityEngine;

namespace CombatPreview
{
    // Observe game-owned calculations/targeting. Never call CalculateParams,
    // WouldCurrentlyTarget, action evaluators or Rulebook.Trigger ourselves.
    internal static class SpellPreviewRuntime
    {
        sealed class Parameters { internal int DC, Level; internal Metamagic Metamagic; internal string Stamp; internal float Time; }
        static readonly Dictionary<AbilityData, Parameters> parameters = new Dictionary<AbilityData, Parameters>();
        static readonly SpellTargetWindow<UnitEntityData> affected = new SpellTargetWindow<UnitEntityData>();
        internal static UnitEntityData Caster(AbilityData ability)
        {
            var reference = CombatFields.Read(ability, "m_CasterRef");
            var proxy = CombatFields.Read(reference, "m_Proxy");
            return CombatFields.Read(proxy, "<Entity>k__BackingField") as UnitEntityData;
        }
        static string ParameterStamp(AbilityData ability, UnitEntityData caster)
        {
            var meta = CombatFields.Read(ability, "<MetamagicData>k__BackingField");
            return BetaReader.NativeStamp(caster, null) + "|" + string.Join(":", new[] {
                CombatFields.Read(ability, "<OverrideDC>k__BackingField"), CombatFields.Read(ability, "m_OverrideSpellLevel"),
                CombatFields.Read(ability, "<OverrideCasterLevel>k__BackingField"), CombatFields.Read(ability, "<OverrideCasterModifier>k__BackingField"),
                CombatFields.Read(meta, "<MetamagicMask>k__BackingField"), CombatFields.Read(meta, "<SpellLevelCost>k__BackingField"),
                CombatFields.Read(meta, "<HeightenLevel>k__BackingField") }.Select(value => value?.ToString() ?? "none"));
        }
        internal static void Clear() { parameters.Clear(); affected.Clear(); }
        internal static void ObserveParameters(AbilityData __instance, AbilityParams __result)
        {
            if (!BetaRuntime.Enabled || __result == null) return;
            try
            {
                var caster = Caster(__instance); if (caster == null) return;
                if (parameters.Count >= 64 && !parameters.ContainsKey(__instance)) parameters.Clear();
                parameters[__instance] = new Parameters { DC = __result.DC, Level = __result.CasterLevel, Metamagic = __result.Metamagic,
                    Stamp = ParameterStamp(__instance, caster), Time = Time.unscaledTime };
            }
            catch (Exception e) { Main.Error("Spell parameter observer", e); }
        }
        internal static void ObserveTarget(AbilityData __instance, Vector3 __0, UnitEntityData __1, bool __result)
        {
            if (!BetaRuntime.Enabled || Game.Instance?.SelectedAbilityHandler?.SelectedAbility != __instance || __1 == null) return;
            affected.Observe(__instance, __0, __1, __result, Time.unscaledTime);
        }
        internal static UnitEntityData[] Targets(AbilityData ability)
        {
            return affected.Read(ability, Time.unscaledTime).Where(unit => unit.IsVisibleForPlayer && !unit.Descriptor.State.IsDead).ToArray();
        }

        static int Cached(ModifiableValue value)
        { var v = CombatFields.Read(value, "<ModifiedValue>k__BackingField"); if (!(v is int)) throw new NotSupportedException("능력치 캐시 미확인"); return (int)v; }
        internal static List<string> Describe(AbilityData ability, UnitEntityData target)
        {
            var rows = new List<string>(); var caster = Caster(ability);
            if (caster == null) { rows.Add("시전자 정보 미확인"); return rows; }
            Parameters p;
            bool current = parameters.TryGetValue(ability, out p) && Time.unscaledTime >= p.Time && Time.unscaledTime - p.Time <= 1.5f
                && p.Stamp == ParameterStamp(ability, caster);
            var components = ability.Blueprint.ComponentsArray;
            var sticky = components.OfType<AbilityEffectStickyTouch>().FirstOrDefault();
            if (sticky != null)
            {
                var reference = CombatFields.Read(sticky, "m_TouchDeliveryAbility");
                var delivery = CombatFields.Read(reference, "<Cached>k__BackingField") as Kingmaker.UnitLogic.Abilities.Blueprints.BlueprintAbility;
                if (delivery != null) components = delivery.ComponentsArray;
                else rows.Add("접촉 전달 주문의 상세 효과가 아직 로드되지 않았습니다.");
            }
            if (ability.MagicHackData != null)
            { rows.Add("복합 합성 주문의 판정 조합은 미지원입니다."); return rows; }
            var projectile = components.OfType<AbilityDeliverProjectile>().FirstOrDefault();
            bool touch = components.OfType<AbilityDeliverTouch>().Any();
            var weaponReference = CombatFields.Read(projectile, "m_Weapon");
            var projectileWeapon = CombatFields.Read(weaponReference, "<Cached>k__BackingField") as Kingmaker.Blueprints.Items.Weapons.BlueprintItemWeapon;
            bool knownTouchProjectile = projectileWeapon != null && CombatFields.Read(CombatFields.Read(projectileWeapon, "m_Type"), "<Cached>k__BackingField") != null
                && (projectileWeapon.AttackType == Kingmaker.RuleSystem.AttackType.Touch || projectileWeapon.AttackType == Kingmaker.RuleSystem.AttackType.RangedTouch);
            if (projectile != null && projectile.NeedAttackRoll && (!knownTouchProjectile || projectile.IsHandOfTheApprentice || projectile.ReplaceAttackRollBonusStat))
                rows.Add("주문 공격 명중 — 특수 무기/능력치 판정 미지원");
            if (touch && !current) rows.Add("접촉 명중 — 원거리화 여부를 포함한 최신 주문 매개변수 대기");
            if (touch && current || projectile != null && projectile.NeedAttackRoll && knownTouchProjectile && !projectile.IsHandOfTheApprentice && !projectile.ReplaceAttackRollBonusStat)
            {
                bool ranged = touch ? current && (p.Metamagic & Metamagic.Reach) != 0 : projectileWeapon.AttackType == Kingmaker.RuleSystem.AttackType.RangedTouch;
                int stat = Cached(ranged ? caster.Stats.Dexterity : caster.Stats.Strength);
                int ab = Cached(caster.Stats.BaseAttackBonus) + (int)Math.Floor((stat - 10) / 2.0) + Cached(caster.Stats.AdditionalAttackBonus);
                var ac = CombatFields.Read(target.Stats.AC, "<Touch>k__BackingField");
                if (ac is int) rows.Add((ranged ? "원거리 접촉" : "근접 접촉") + " 명중 ~" + SpellPreviewMath.AttackHit(ab, (int)ac).ToString("P0") + " (기본 AB " + ab + " / 접촉 AC " + ac + ")");
                else rows.Add("접촉 명중 — 접촉 AC 미확인");
                rows.Add("명중은 기본 추정: 주문별 능력치 대체·조건부 보너스·은폐/거리 페널티 미반영");
            }
            var runs = components.OfType<AbilityEffectRunAction>().ToArray();
            var saves = new List<SavingThrowType>();
            var unsafeSaves = new HashSet<SavingThrowType>();
            int budget = 256;
            foreach (var run in runs)
            {
                if (run.SavingThrowType != SavingThrowType.Unknown) saves.Add(run.SavingThrowType);
                FindSaves(run.Actions, saves, unsafeSaves, 0, ref budget);
            }
            if (budget <= 0) { foreach (var type in saves) unsafeSaves.Add(type); rows.Add("내성 효과 탐색 한도 초과 · 일부 분기 미확인"); }
            if (saves.Count == 0) rows.Add("내성 — 직접 내성 판정을 찾지 못함 (없음으로 확정하지 않음)");
            foreach (var type in saves.Distinct())
            {
                ModifiableValue stat = type == SavingThrowType.Fortitude ? target.Stats.SaveFortitude : type == SavingThrowType.Reflex ? target.Stats.SaveReflex : type == SavingThrowType.Will ? target.Stats.SaveWill : null;
                string label = type == SavingThrowType.Fortitude ? "인내" : type == SavingThrowType.Reflex ? "반사" : type == SavingThrowType.Will ? "의지" : type.ToString();
                if (unsafeSaves.Contains(type)) { rows.Add(label + " 내성 실패 — 별도/조건부 DC가 있어 계산 불가"); continue; }
                if (!current || stat == null) { rows.Add(label + " 내성 실패 — 주문 DC의 최신 게임 계산 결과 대기"); continue; }
                int bonus = Cached(stat);
                rows.Add(label + " 내성 실패 ~" + SpellPreviewMath.SaveFailure(bonus, p.DC, (p.Metamagic & Metamagic.Persistent) != 0).ToString("P0") + " (DC " + p.DC + " / 내성 " + bonus.ToString("+0;-0;0") + ")");
            }
            if (!ability.Blueprint.SpellResistance) rows.Add("주문 저항: 적용하지 않는 주문 (효과 면역은 별도)");
            else
            {
                var srPart = target.Get<UnitPartSpellResistance>();
                var srList = CombatFields.Read(srPart, "m_SRs") as IList;
                var immunities = CombatFields.Read(srPart, "m_SpellImmunities") as IList;
                if (immunities != null && immunities.Count > 0) rows.Add("주문 면역/조건부 저항: 별도 판정 필요 — 최종 적용률 미확인");
                if (srPart == null) rows.Add("주문 저항: 등록된 SR 없음 (면역 여부는 별도)");
                else if (!current || srList == null || srList.Cast<object>().Any(s => CombatFields.Read(s, "Alignment") != null || CombatFields.Read(s, "SpellDescriptor") != null || CombatFields.Read(s, "SpellSchool") != null))
                    rows.Add("주문 저항 관통 — 시전자 레벨 또는 조건부 SR 미확인");
                else
                {
                    int value = srList.Cast<object>().Select(s => (int)CombatFields.Read(s, "Value")).DefaultIfEmpty(0).Max();
                    var bonuses = CombatFields.Read(srPart, "Bonuses") as IDictionary;
                    if (bonuses != null) foreach (DictionaryEntry bonus in bonuses) value += (int)bonus.Value;
                    value = Math.Max(0, value - (int)(CombatFields.Read(srPart, "<AllSRPenalty>k__BackingField") ?? 0) - ((p.Metamagic & Metamagic.Piercing) != 0 ? 5 : 0));
                    rows.Add("SR " + value + " · 기본 시전자 레벨 관통 ~" + SpellPreviewMath.ResistancePass(p.Level, value).ToString("P0") + " (CL " + p.Level + "; 주문 관통 피트/특수 대체 미반영)");
                }
            }

            return rows;
        }
        static void FindSaves(Kingmaker.ElementsSystem.ActionList actions, List<SavingThrowType> saves, HashSet<SavingThrowType> unsafeSaves, int depth, ref int budget)
        {
            if (actions?.Actions == null) return;
            if (depth > 4) { budget = 0; return; }
            foreach (var action in actions.Actions)
            {
                if (--budget <= 0) return;
                var save = action as ContextActionSavingThrow;
                if (save != null) { var type = CombatFields.Read(save, "Type"); if (type is SavingThrowType) {
                    var saveType = (SavingThrowType)type; saves.Add(saveType);
                    var conditional = CombatFields.Read(save, "m_ConditionalDCIncrease") as Array;
                    if (save.HasCustomDC || save.UseDCFromContextSavingThrow || save.FromBuff || save.MakeSaveAlsoForMagicHack || conditional == null || conditional.Length > 0) unsafeSaves.Add(saveType);
                } }
                if (action == null) continue;
                foreach (var field in action.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
                    if (field.FieldType == typeof(Kingmaker.ElementsSystem.ActionList)) FindSaves(field.GetValue(action) as Kingmaker.ElementsSystem.ActionList, saves, unsafeSaves, depth + 1, ref budget);
            }
        }
    }
}
