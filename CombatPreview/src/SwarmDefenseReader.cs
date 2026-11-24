using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.Enums;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.FactLogic;

namespace CombatPreview
{
    internal static class SwarmDefenseReader
    {
        internal static void Weapon(SwarmDamageResistance resistance, UnitEntityData actor, ItemEntityWeapon weapon,
            WeaponTraits traits, DefenseProfile profile, List<string> notes, List<string> missing)
        {
            if (weapon == null || weapon.Blueprint.Category == WeaponCategory.KineticBlast) return;
            var reference = CombatFields.Read(resistance, "ExceptionFact");
            var loaded = LoadedBlueprintReader.Read(reference);
            // Empty/unloaded references do not match an active caster fact.
            bool hasException = actor != null && CombatFields.HasFact(actor, reference);
            string guid = CombatFields.Read(reference, "guid") as string;
            if (actor == null && (loaded != null || !string.IsNullOrEmpty(guid) && guid.Trim('0', '-') != ""))
            { missing.Add("군체 관통 특성 확인에 필요한 공격자 없음"); return; }
            double factor = SwarmRules.WeaponPhysicalFactor(true, false, hasException, resistance.DiminutiveOrLower, traits.Form, traits.Known);
            if (double.IsNaN(factor)) { missing.Add("군체 피해 반감에 필요한 무기 피해 형태 미확인"); return; }
            if (factor == 0) { profile.Immunities.Add(DamageKind.Physical); notes.Add("군체: 무기 물리 피해 면역"); }
            else if (factor < 1) { profile.PhysicalDecline = System.Math.Min(profile.PhysicalDecline, factor); notes.Add("군체: 타격 형태 없는 무기 물리 피해 반감"); }
            else if (hasException) notes.Add("군체: 공격자 예외 특성으로 물리 방어 관통");
        }

        internal static double SpellMultiplier(AbilityData ability, UnitEntityData target)
        {
            if (ability == null || target == null) return 1;
            var components = ability.Blueprint.ComponentsArray;
            var projectile = DeliveryProjectile(ability);
            bool area = SwarmRules.AreaVulnerable(projectile != null && projectile.Type != AbilityProjectileType.Simple,
                components.Any(component => component is AbilityTargetsAround), false);
            if (!area) return 1;
            double multiplier = 1;
            foreach (var fact in target.Facts.List.Where(fact => fact.Active))
                foreach (var runtime in fact.Components)
                    if (CombatFields.Bool(runtime, "<IsSubscribedOnEventBus>k__BackingField")
                        && CombatFields.Component(runtime)?.GetType() == typeof(SwarmAoeVulnerability)) multiplier *= 1.5;
            return multiplier;
        }
        internal static AbilityDeliverProjectile DeliveryProjectile(AbilityData ability)
        {
            var delivery = ability.MagicHackData?.DeliverBlueprint ?? ability.Blueprint;
            return delivery.ComponentsArray.OfType<AbilityDeliverProjectile>().FirstOrDefault();
        }
    }
}
