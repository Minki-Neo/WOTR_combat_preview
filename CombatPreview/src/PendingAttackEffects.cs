using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.Enums;
using Kingmaker.EntitySystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;
using Kingmaker.Utility;

namespace CombatPreview
{
    internal sealed class ModifierEntry
    {
        internal EntityFact Fact;
        internal object Component;
        internal bool Pending;
        internal string Label;
    }
    internal static class PendingAttackEffects
    {
        static BlueprintBuff CachedBuff(object source)
        { return CombatFields.Read(CombatFields.Read(source, "m_Buff"), "<Cached>k__BackingField") as BlueprintBuff; }
        internal static IEnumerable<ModifierEntry> Entries(UnitEntityData unit, ItemEntityWeapon weapon, bool full, List<string> notes)
        {
            var list = new List<ModifierEntry>();
            var existing = new HashSet<object>(unit.Facts.List.Where(f => f.Active).Select(CombatFields.Blueprint));
            var visited = new HashSet<object>();
            Action<BlueprintBuff, EntityFact, int> visit = null;
            visit = (buff, source, depth) => {
                if (buff == null || existing.Contains(buff) || !visited.Add(buff)) return;
                if (depth > 3 || visited.Count > 32) { notes.Add("미확인: 공격 예약 효과 연결 한도"); return; }
                if (buff.ComponentsArray == null) { notes.Add("미확인: 예약 버프 구성 미로드"); return; }
                foreach (var component in buff.ComponentsArray)
                {
                    list.Add(new ModifierEntry { Fact = source, Component = component, Pending = true, Label = buff.Name + " (공격 시 적용 예정)" });
                    var trigger = component as AddInitiatorAttackWithWeaponTrigger;
                    if (trigger != null) Follow(trigger, source, depth, weapon, full, visit, notes);
                }
            };
            foreach (var fact in unit.Facts.List)
            {
                if (!fact.Active && !(fact is ActivatableAbility && CombatFields.Bool(fact, "m_IsOn"))) continue;
                foreach (var runtime in fact.Components)
                {
                    var c = CombatFields.Component(runtime);
                    if (!fact.Active || c == null || !CombatFields.Bool(runtime, "<IsSubscribedOnEventBus>k__BackingField")) continue;
                    list.Add(new ModifierEntry { Fact = fact, Component = c, Label = CombatFields.Label(fact) });
                    var trigger = c as AddInitiatorAttackWithWeaponTrigger;
                    if (trigger != null) Follow(trigger, fact, 0, weapon, full, visit, notes);
                }
                var toggle = fact as ActivatableAbility;
                var bp = CombatFields.Blueprint(toggle) as BlueprintActivatableAbility;
                if (toggle == null || bp == null || !CombatFields.Bool(toggle, "m_IsOn") || CombatFields.Bool(toggle, "<IsStarted>k__BackingField")
                    || CombatFields.Bool(toggle, "<TemporarilyDisabled>k__BackingField") || !bp.ActivateOnUnitAction || bp.ActivateOnUnitActionType != AbilityActivateOnUnitActionType.Attack) continue;
                var buff = CachedBuff(bp);
                if (buff == null) notes.Add("미확인: " + bp.Name + " 예약 버프가 아직 로드되지 않음");
                else visit(buff, fact, 0);
            }
            return list;
        }
        static void Follow(AddInitiatorAttackWithWeaponTrigger trigger, EntityFact source, int depth, ItemEntityWeapon weapon, bool full,
            Action<BlueprintBuff, EntityFact, int> visit, List<string> notes)
        {
            if (!trigger.TriggerBeforeAttack || !trigger.ActionsOnInitiator || trigger.Action?.Actions == null || (trigger.OnlyOnFullAttack && !full)) return;
            if (trigger.OnlyOnFirstAttack || CombatFields.Bool(trigger, "CheckPhysicalDamageForm") || CombatFields.Bool(trigger, "OnlySuccess"))
            { notes.Add("미확인: 첫 공격/결과 전용 예약 효과"); return; }
            var bp = weapon.Blueprint;
            if (trigger.CheckWeaponBlueprint)
            { if (!CombatFields.Reference(CombatFields.Read(trigger, "m_WeaponBlueprint"), bp)) return; }
            else
            {
                var type = CombatFields.Read(trigger, "m_WeaponType");
                if (type != null && !CombatFields.Reference(type, bp.Type) && !string.IsNullOrEmpty(CombatFields.Read(type, "guid") as string)) return;
                if (trigger.CheckWeaponCategory && trigger.Category != bp.Category) return;
                if (trigger.CheckWeaponRangeType && !trigger.RangeType.IsSuitableWeapon(weapon)) return;
                if (trigger.CheckWeaponGroup && !bp.FighterGroup.Contains(trigger.Group)) return;
                if (trigger.AllNaturalAndUnarmed && !bp.IsNatural && !bp.IsUnarmed) return;
            }
            foreach (var action in trigger.Action.Actions)
            {
                var apply = action as ContextActionApplyBuff;
                if (apply == null) { notes.Add("미확인: 조건부/특수 공격 예약 액션 " + action?.GetType().Name); continue; }
                var buff = CachedBuff(apply);
                if (buff == null) notes.Add("미확인: 공격 예약 대상 버프 미로드");
                else visit(buff, source, depth + 1);
            }
        }
    }
}
