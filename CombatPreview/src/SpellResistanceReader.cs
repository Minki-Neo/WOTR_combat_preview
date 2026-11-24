using System;
using System.Collections;
using System.Linq;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Enums;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Parts;

namespace CombatPreview
{
    internal static class SpellResistanceReader
    {
        internal static bool Applies(object source, AbilityData ability, UnitEntityData caster)
        {
            if (CombatFields.Read(source, "Alignment") is AlignmentComponent alignment
                && !SpellDescriptorOperations.HasAnyFlag(ability.SpellDescriptor, SpellDescriptorOperations.GetAlignmentDescriptor(alignment))
                && !caster.Alignment.ValueRaw.HasComponent(alignment)) return false;
            if (CombatFields.Read(source, "SpellDescriptor") is SpellDescriptor descriptor
                && !SpellDescriptorOperations.HasAnyFlag(ability.SpellDescriptor, descriptor)) return false;
            return !(CombatFields.Read(source, "SpellSchool") is SpellSchool school) || ability.SpellSchool == school;
        }

        internal static bool? Immune(AbilityData ability, UnitEntityData caster, UnitEntityData target)
        {
            var part = target.Get<UnitPartSpellResistance>();
            if (part == null) return false;
            if (!(CombatFields.Read(part, "m_SpellImmunities") is IList entries)) return null;
            if (ability.Blueprint.IgnoreSpellResistanceForAlly && !target.IsEnemy(caster)) return false;
            bool unknown = false;
            foreach (var entry in entries)
            {
                if (CombatFields.Read(entry, "Alignment") is AlignmentComponent alignment && alignment != 0
                    && !caster.Alignment.ValueRaw.HasComponent(alignment)) continue;
                var ignore = CombatFields.Read(entry, "CasterIgnoreImmunityFact");
                if (ignore != null && caster.Facts.List.Any(fact => fact.Active && ReferenceEquals(CombatFields.Blueprint(fact), ignore))) continue;
                bool exception = (CombatFields.Read(entry, "Exceptions") as BlueprintAbility[])?.Contains(ability.Blueprint) == true;
                switch (CombatFields.Read(entry, "Type")?.ToString())
                {
                    case "Simple": if (!exception && ability.Blueprint.SpellResistance) return true; break;
                    case "Specific": if (exception) return true; break;
                    case "AllSpells": if (!exception && ability.Blueprint.IsSpell) return true; break;
                    case "SpellDescriptor":
                        var descriptor = (SpellDescriptor)CombatFields.Read(entry, "SpellDescriptor");
                        var ignored = CombatFields.Read(part, "<IgnoreSpellDescriptorImmunities>k__BackingField") is SpellDescriptor mask ? mask : 0;
                        if (!exception && SpellDescriptorOperations.HasAnyFlag(ability.SpellDescriptor, descriptor & ~ignored)) return true;
                        break;
                    case "SingleTarget":
                        var components = ability.Blueprint.ComponentsArray;
                        var projectile = SwarmDefenseReader.DeliveryProjectile(ability);
                        bool multi = components.Any(component => component is AbilityTargetsAround || component is AbilityDeliverChain
                            || component is AbilityDeliverTouch || component is AbilityDeliverAttackWithWeapon)
                            || projectile != null && (projectile.Type != AbilityProjectileType.Simple || projectile.NeedAttackRoll);
                        if (!multi && ability.Blueprint.AoERadius.Meters == 0) return true;
                        break;
                    default: unknown = true; break;
                }
            }
            return unknown ? (bool?)null : false;
        }
    }
}
