using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.UnitLogic.Parts;
#if WOTR
using Kingmaker.UnitLogic;
#else
using Kingmaker.Controllers.Combat;
#endif

namespace CombatPreview
{
    // A spell that goes with an attack with a weapon instead of its own touch or ray, by the weapon's roll. A held charge
    // (Shocking Grasp) goes with a hit of a melee weapon of a magus with Spellstrike (MagusController) and of a natural
    // weapon of anyone (TouchSpellsController); a ray of an eldritch archer goes with the next ranged attack
    // (RuleAttackWithWeapon.LaunchProjectile). A magus casting such a spell on an enemy attacks with the weapon at once
    // (UnitUseAbility.CreateCastCommand).
    static class Spellstrike
    {
        // The spell a hit with this weapon brings, or null.
        internal static AbilityData WithAttack(UnitEntityData attacker, ItemEntityWeapon weapon)
        {
            var magus = attacker.Get<UnitPartMagus>();
            if (weapon.Blueprint.IsRanged) return magus?.EldritchArcherSpell;
            var touch = attacker.Get<UnitPartTouch>();
            if (touch == null || !weapon.Blueprint.IsMelee) return null;
            if (weapon.Blueprint.IsNatural) return touch.Ability.Data;
            bool spellstrike = magus != null && magus.Spellstrike.Active;
#if WOTR
            spellstrike &= weapon != attacker.Body.EmptyHandWeapon;
#endif
            return spellstrike ? touch.Ability.Data : null;
        }

        // The weapon that carries the spell cast on the target, or null when the spell goes by itself. A charge the
        // caster already holds for this spell replaces it: its parameters were taken when it was cast.
        internal static ItemEntityWeapon Weapon(ref AbilityData spell, UnitEntityData target)
        {
            var caster = spell.Caster.Unit;
            var magus = caster.Get<UnitPartMagus>();
            if (magus == null || !magus.Spellstrike.Active || !caster.IsEnemy(target)) return null;
            if (magus.EldritchArcher)
            {
                // A ray of a standard action from the magus's list, with a ranged weapon in hand (UnitUseAbility).
                var bow = caster.GetFirstWeapon();
                return spell.RuntimeActionType == UnitCommand.CommandType.Standard && bow != null && bow.Blueprint.IsRanged
                    && magus.IsSuitableForEldritchArcherSpellStrike(spell) ? bow : null;
            }
            var sticky = spell.Blueprint.StickyTouch;
            var touch = caster.Get<UnitPartTouch>();
            bool held = touch != null && (touch.Ability.Data == spell || (sticky != null && touch.Ability.Blueprint == sticky.TouchDeliveryAbility));
            if (sticky == null && !held) return null;
            if (held) spell = touch.Ability.Data;
            var hand = caster.GetThreatHand();
            return hand != null && magus.IsSpellFromMagusSpellList(spell) ? hand.MaybeWeapon : null;
        }
    }
}
