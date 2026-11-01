using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using Kingmaker.Blueprints.Facts;
using Kingmaker.Designers.Mechanics.Buffs;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Enums;
using Kingmaker.Items;
using Kingmaker.UnitLogic.Parts;

namespace CombatPreview
{
    internal static partial class BetaReader
    {
        internal static bool TwoHandedGrip(UnitEntityData unit, ItemEntityWeapon weapon)
        {
            if (weapon.Blueprint.IsNatural || weapon.Blueprint.IsRanged || weapon != Primary(unit)) return false;
            var set = unit.Body.CurrentHandsEquipmentSet;
            var raw = AccessTools.Field(set.GetType(), "m_GripType")?.GetValue(set);
            if (raw == null) throw new NotSupportedException("현재 무기 잡기 정보를 읽을 수 없습니다.");
            int mode = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
            if (mode == 1) return false;
            if (mode == 2) return true;
            if (mode != 0) throw new NotSupportedException("알 수 없는 무기 잡기 방식입니다.");
            // Reproduce the ordinary automatic grip choice on already loaded
            // equipment. No GetPreferredAutoGripType/MaybeWeapon/slot mutation.
            if (SlotItem(unit.Body.SecondaryHand) != null || !weapon.CanTakeTwoHands()) return false;
            if (weapon.CanTakeOneHand(unit))
            {
                if ((bool)unit.State.Features.FighterOnHandedGrip) return false;
                var magus = unit.Get<UnitPartMagus>();
                if (magus != null && magus.SpellCombat.Active) return false;
            }
            return true;
        }
        internal static int[] ActiveExtraAttackPattern(UnitEntityData unit, ItemEntityWeapon weapon,
            int bab, bool full, out string source, out List<string> reasons)
        {
            reasons = new List<string>(); source = "basic-bab";
            if (!full || weapon.Blueprint.IsNatural) return WeaponModelRules.BasicPattern(bab, full, weapon.Blueprint.IsNatural, 0, 0);
            int extra = 0, haste = 0, penalized = 0;
            foreach (var fact in unit.Facts.List)
            {
                if (!fact.Active || fact.Components.Count > 128) continue;
                foreach (var runtime in fact.Components)
                {
                    if (runtime == null) continue;
                    if (AccessTools.Field(runtime.GetType(), "<IsSubscribedOnEventBus>k__BackingField")?.GetValue(runtime) as bool? != true) continue;
                    var component = AccessTools.Field(runtime.GetType(), "<SourceBlueprintComponent>k__BackingField")?.GetValue(runtime);
                    var specific = component as BuffExtraAttackWeaponSpecific;
                    var generic = component as BuffExtraAttack;
                    int count = 0;
                    if (specific != null && specific.RangeType.IsSuitableWeapon(weapon)) { extra++; count = 1; }
                    else if (generic != null && generic.Number > 0 && generic.Number <= 8)
                    {
                        count = generic.Number;
                        if (generic.Penalized) penalized += count;
                        else if (generic.Haste) haste = Math.Max(haste, count);
                        else extra += count;
                    }
                    if (count == 0) continue;
                    var blueprint = AccessTools.Field(fact.GetType(), "<Blueprint>k__BackingField")?.GetValue(fact) as BlueprintUnitFact;
                    reasons.Add((blueprint == null ? "활성 추가 공격 효과" : blueprint.Name) + " +" + count);
                }
            }
            if (reasons.Count > 0) source = "active-extra-attack-components";
            return WeaponModelRules.CountPattern(1 + extra + haste, Math.Min(3, Math.Max(0, bab - 1) / 5) + penalized, false);
        }
    }
}
