using System;
using System.Reflection;
using HarmonyLib;
using Kingmaker.Enums;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.RuleSystem.Rules.Damage;
using Kingmaker.Settings;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.UnitLogic.Parts;

namespace CombatPreview
{
    // What the rules of Kingmaker and Wrath of the Righteous name or decide differently, Wrath's side.
    static class GameRules
    {
        // RuleAttackRoll.IsSuccessRoll tests the die it is given.
        internal const bool SuccessRollTestsDie = true;

        static readonly FieldInfo AttackMissChance = AccessTools.Field(typeof(RuleAttackRoll), "missChance");
        static readonly FieldInfo ConcealmentMissChance = AccessTools.Field(typeof(RuleConcealmentCheck), "missChance");

        internal static bool IsStarted(ActivatableAbility ability) => ability.IsStarted;

        internal static bool IsTriggered(RuleCalculateWeaponStats stats) => stats.IsTriggered;

        // The weapon's multiplier and the additional one (as BaseDamage gets them in RuleAttackWithWeapon.CreateDamage).
        internal static int CriticalMultiplier(RuleCalculateWeaponStats stats) =>
            stats.CriticalMultiplier + (stats.AdditionalCriticalMultiplier?.TotalValue ?? 0);

        internal static DiceFormula Dice(DamageDescription damage) => damage.ModifiedDiceFormula;

        // Set and modified by the handlers of the attack roll (RuleAttackRoll.SetMissChance, ModifyMissChance).
        internal static int MissChance(RuleAttackRoll roll) => ((MissChance)AttackMissChance.GetValue(roll)).Calculate();

        // RuleConcealmentCheck.OnTrigger: the concealment's chance (20 or 50) with what the handlers changed, a d100 over it.
        internal static double ConcealmentFail(RuleConcealmentCheck check, Concealment concealment, out bool reroll)
        {
            reroll = false;
            var chance = (MissChance)ConcealmentMissChance.GetValue(check);
            chance.Set(concealment == Concealment.Partial ? 20 : 50);
            return Math.Max(0, Math.Min(100, chance.Calculate())) / 100.0;
        }

        // The die of the attack roll (0 when it was not rolled).
        internal static int Die(RuleAttackRoll roll) => roll.D20?.Result ?? 0;

        // The die of a combat maneuver (0 when it was not rolled).
        internal static int Die(RuleCombatManeuver maneuver) => maneuver.InitiatorRoll?.Result ?? 0;

        static readonly FieldInfo SpellPenetrationBonus = AccessTools.Field(typeof(RuleSpellResistanceCheck), "m_AdditionalSpellPenetration");
        static readonly MethodInfo SpellResistanceSetter = AccessTools.PropertySetter(typeof(RuleSpellResistanceCheck), nameof(RuleSpellResistanceCheck.SpellResistance));

        // The spell penetration the handlers added (AddSpellPenetration).
        internal static int SpellPenetration(RuleSpellResistanceCheck check) => (SpellPenetrationBonus.GetValue(check) as ModifiableBonus)?.TotalValue ?? 0;

        internal static void SetSpellResistance(RuleSpellResistanceCheck check, int value) => SpellResistanceSetter.Invoke(check, new object[] { value });

        internal static int Result(RuleCalculateAC ac) => ac.Result;

        internal static bool AlwaysChance(UnitEntityData unit) => unit.State.Features.AlwaysChance;

        internal static bool CanCrit(RuleAttackRoll roll, UnitEntityData target)
        {
            var crits = (CriticalHitPower)SettingsRoot.Difficulty.EnemyCriticalHits;
            return (!roll.ImmuneToCriticalHit || roll.IgnoreCritImmunity)
                && (!target.IsPlayerFaction || crits == CriticalHitPower.Weak || crits == CriticalHitPower.Normal);
        }
    }
}
