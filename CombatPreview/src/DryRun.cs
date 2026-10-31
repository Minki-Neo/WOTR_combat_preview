using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Kingmaker;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.PubSubSystem;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.RuleSystem.Rules.Abilities;
using Kingmaker.UnitLogic.Mechanics.Components;
#if WOTR
using Kingmaker.EntitySystem;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.Designers.Mechanics.Buffs;
using Kingmaker.Designers.Mechanics.EquipmentEnchants;
using Kingmaker.Enums;
using Kingmaker.Utility;
#endif

namespace CombatPreview
{
    // Legacy handlers are retained for audit/reference only. Main installs none
    // of these patches. No context, temporary modifiers or RNG are touched here.
    static class DryRun
    {
        internal static bool Active => false;

        // The d20 modifiers met by the last dry RuleRollD20 (see Rerolls).
        internal static readonly List<D20Modifier> D20Modifiers = new List<D20Modifier>();

        internal static void Run(RulebookEvent evt, Action body)
        {
            // Audit found arbitrary pre-event handlers, shared fact runtimes and
            // incomplete RNG restoration. Reject before touching ANY game object.
            PredictionSafety.RequireReadOnlyPrediction();
        }

        internal static void AtEnd(Action cleanup)
        { PredictionSafety.RequireReadOnlyPrediction(); }

        internal static List<D20Modifier> Rerolls(RulebookEvent rule)
        {
            PredictionSafety.RequireReadOnlyPrediction();
            return null; // Unreachable; no game rule or shared modifier state is created.
        }
    }

    // Handlers that do something besides adding bonuses before a roll: while a rule runs dry they are not called.
    [HarmonyPatch]
    static class SilencedHandlers
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var m in Methods())
                if (m != null) yield return m;
                else Main.Log.Error("dry run: a handler to silence is missing in this version of the game");
        }

        static IEnumerable<MethodBase> Methods()
        {
            // Runs actions on the attacker and on itself when it is attacked.
#if WOTR
            yield return AccessTools.Method(typeof(AddTargetBeforeAttackRollTrigger), "OnEventAboutToTrigger", new[] { typeof(RuleAttackRoll) });
#else
            yield return AccessTools.Method(typeof(AddTargetBeforeAttackRollTrigger), "OnEventAboutToTrigger", new[] { typeof(RuleCalculateAttackBonus) });
#endif
            // AI of enemies with Power Attack: turns the ability on or off.
            yield return AccessTools.Method(typeof(PowerAttackWatcher), "OnEventAboutToTrigger", new[] { typeof(RuleAttackRoll) });
#if !WOTR
            // Metamagic for one spell: removes itself when the spell's parameters are calculated.
            yield return AccessTools.Method(typeof(AutoMetamagic), "OnEventAboutToTrigger", new[] { typeof(RuleCalculateAbilityParams) });
            yield return AccessTools.Method(typeof(MetamagicOnNextSpell), "OnEventAboutToTrigger", new[] { typeof(RuleCalculateAbilityParams) });
#endif
        }

        static bool Prefix() => !DryRun.Active;
    }

#if WOTR
    // Handlers that keep something for the attack in their "about to trigger" of the attack roll (a bonus for the armor
    // class or the attack bonus rule that follows, concealment ignored, an armor class modifier) and clear it in their
    // "did trigger": on a dry run they are cleared at its end the same way. Found by a search of every handler "about
    // to trigger" of the rules the mod runs dry for assignments and calls beyond bonuses; each "did trigger" only
    // clears.
    [HarmonyPatch]
    static class CleanedUpHandlers
    {
        static readonly Type[] Types =
        {
            typeof(ACBonusAgainstTarget), typeof(ACBonusAgainstBuffOwner), typeof(ACBonusAgainstFactOwner),
            typeof(ACBonusAgainstFactOwnerMultiple), typeof(ACBonusAgainstSize), typeof(ACBonusUnlessFactMultiple),
            typeof(ACContextBonusAgainstFactOwner), typeof(AttackBonusAgainstFactOwner), typeof(AttackBonusConditional),
            typeof(ShareFavoredEnemies), typeof(IgnoreConcealmentAgainstFactOwner), typeof(IgnorePartialConcealmentOnRangedAttacks),
            typeof(ACBonusAgainstSpellsWithDescriptor), typeof(ACBonusAgainstFactOwnerEquipment),
        };

        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var type in Types)
            {
                var m = AccessTools.Method(type, "OnEventAboutToTrigger", new[] { typeof(RuleAttackRoll) });
                if (m != null) yield return m;
                else Main.Log.Error("dry run: no attack roll handler in " + type.Name);
            }
        }

        static readonly Dictionary<Type, PropertyInfo> s_Runtime = new Dictionary<Type, PropertyInfo>();

        // The handler keeps what it set in the runtime of its fact, which it reaches only while that runtime is the
        // context of the event: "did trigger" is called in it, as the event bus calls it (RulebookSubscribersList).
        static void Postfix(object __instance, RuleAttackRoll evt)
        {
            var handler = __instance as IRulebookHandler<RuleAttackRoll>;
            if (!DryRun.Active || handler == null) return;
            var runtime = Runtime(__instance);
            DryRun.AtEnd(() =>
            {
                using (runtime?.RequestEventContext()) handler.OnEventDidTrigger(evt);
            });
        }

        // The runtime of a fact's component, to call it later in its context.
        internal static ISubscriptionProxy Runtime(object component)
        {
            PropertyInfo property;
            var type = component.GetType();
            if (!s_Runtime.TryGetValue(type, out property)) s_Runtime[type] = property = AccessTools.Property(type, "Runtime");
            return property?.GetValue(component, null) as ISubscriptionProxy;
        }
    }

    // Metamagic for one spell adds its metamagic to the parameters and remembers the spell, to remove itself when that
    // spell is cast: on a dry run the metamagic counts and the spell it remembered before is put back.
    [HarmonyPatch]
    static class OneSpellMetamagic
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(AutoMetamagic), "OnEventAboutToTrigger", new[] { typeof(RuleCalculateAbilityParams) });
            yield return AccessTools.Method(typeof(MetamagicOnNextSpell), "OnEventAboutToTrigger", new[] { typeof(RuleCalculateAbilityParams) });
        }

        static void Prefix(object __instance, out object __state)
        {
            __state = DryRun.Active ? Traverse.Create(__instance).Property("Data").Field("Spell").GetValue() : null;
        }

        static void Postfix(object __instance, object __state)
        {
            if (!DryRun.Active) return;
            var runtime = CleanedUpHandlers.Runtime(__instance);
            DryRun.AtEnd(() =>
            {
                using (runtime?.RequestEventContext()) Traverse.Create(__instance).Property("Data").Field("Spell").SetValue(__state);
            });
        }
    }

#else
    // Handlers that change the game before a roll and undo it after the roll, in their "did trigger": on a dry run
    // what they did is undone at its end. Found by a search of every handler "about to trigger" of the rules the mod
    // runs dry for assignments and calls beyond bonuses (attack roll, attack bonus, armor class, weapon stats,
    // concealment, d20, flat-footed, saving throw, spell resistance, ability parameters).
    static class CleanedUpHandlers
    {
        static readonly FieldInfo IgnorePartialAttack = AccessTools.Field(typeof(IgnorePartialConcealmentOnRangedAttacks), "m_Attack");
        static readonly FieldInfo SpellsACModifier = AccessTools.Field(typeof(ACBonusAgainstSpellsWithDescriptor), "m_Mod");

        // Ignores partial concealment for the attacker until the attack ends.
        [HarmonyPatch(typeof(IgnorePartialConcealmentOnRangedAttacks), "OnEventAboutToTrigger", new[] { typeof(RuleAttackRoll) })]
        static class IgnorePartial
        {
            static void Postfix(IgnorePartialConcealmentOnRangedAttacks __instance, RuleAttackRoll evt)
            {
                if (DryRun.Active && IgnorePartialAttack.GetValue(__instance) == evt)
                    DryRun.AtEnd(() => __instance.OnEventDidTrigger(evt));
            }
        }

        // An armor class modifier against spells, removed when the attack ends.
        [HarmonyPatch(typeof(ACBonusAgainstSpellsWithDescriptor), "OnEventAboutToTrigger", new[] { typeof(RuleAttackRoll) })]
        static class SpellsAC
        {
            static void Prefix(ACBonusAgainstSpellsWithDescriptor __instance, out object __state)
            {
                __state = SpellsACModifier.GetValue(__instance);
            }

            static void Postfix(ACBonusAgainstSpellsWithDescriptor __instance, object __state)
            {
                var added = SpellsACModifier.GetValue(__instance) as ModifiableValue.Modifier;
                if (!DryRun.Active || added == null || added == __state) return;
                DryRun.AtEnd(() =>
                {
                    added.Remove();
                    SpellsACModifier.SetValue(__instance, __state);
                });
            }
        }
    }

#endif
#if WOTR
    // ModifyD20 on a dry roll: instead of setting the reroll (and pre-rolling the die for its conditions, rolling for
    // "with chance", running its actions) its parameters are written down, for the rule the roll is asked for.
    [HarmonyPatch(typeof(ModifyD20), "OnEventAboutToTrigger", new[] { typeof(RuleRollD20) })]
    static class D20ModifiersOnDryRoll
    {
        static readonly MethodInfo CheckRule = AccessTools.Method(typeof(ModifyD20), "CheckRule");
        static readonly MethodInfo FactGetter = AccessTools.PropertyGetter(typeof(UnitFactComponentDelegate<ModifyD20.ComponentData>), "Fact");
        static readonly MethodInfo ContextGetter = AccessTools.PropertyGetter(typeof(UnitFactComponentDelegate<ModifyD20.ComponentData>), "Context");

        static bool Prefix(ModifyD20 __instance, RuleRollD20 evt)
        {
            if (!DryRun.Active) return true;
            try
            {
                var rule = Rulebook.CurrentContext.PreviousEvent;
                if (evt.IsFake || rule == null || !(bool)CheckRule.Invoke(__instance, new object[] { rule })) return false;
                var fact = FactGetter.Invoke(__instance, null) as EntityFact;
                var context = ContextGetter.Invoke(__instance, null) as MechanicsContext;
                // The alignment condition as ModifyD20 tests it.
                if (__instance.AgainstAlignment && !rule.Initiator.Alignment.ValueRaw.HasComponent(__instance.Alignment)
                    && ((__instance.TargetAlignment && !rule.GetRuleTarget().Alignment.ValueRaw.HasComponent(__instance.Alignment))
                        || (!__instance.TargetAlignment && !rule.Initiator.Alignment.ValueRaw.HasComponent(__instance.Alignment))))
                    return false;
                bool successable = rule is ISuccessable;
                var m = new D20Modifier
                {
                    Source = fact?.Name,
                    Replace = __instance.Replace,
                    Roll = __instance.Replace ? __instance.RollResult.Calculate(context) : 0,
                    Rerolls = __instance.RollsAmount,
                    TakeBest = __instance.TakeBest,
                    OnlyIfFailed = successable && __instance.RerollOnlyIfFailed,
                    OnlyIfSuccess = successable && !__instance.RerollOnlyIfFailed && __instance.RerollOnlyIfSuccess,
                };
                if (!m.OnlyIfFailed && !m.OnlyIfSuccess && __instance.RollCondition != ModifyD20.RollConditionType.None)
                {
                    m.Compare = (int)__instance.RollCondition;
                    m.CompareValue = __instance.ValueToCompareRoll.Calculate(context);
                }
                // Both bonuses go on the stats while the rule is resolved: the saving throw reads its stat after the roll.
                if (__instance.AddSavingThrowBonus) m.SaveBonus += __instance.Value.Calculate(fact?.MaybeContext);
                if (__instance.AddBonus) m.SaveBonus += __instance.Bonus.Calculate(fact?.MaybeContext);
                if (__instance.WithChance)
                    m.Chance = Math.Max(0, Math.Min(100, __instance.Chance.Calculate(fact?.MaybeContext)));
                DryRun.D20Modifiers.Add(m);
            }
            catch (Exception e)
            {
                Main.Error("d20 modifier", e);
            }
            return false;
        }
    }
#else
    // ModifyD20 on a dry roll: instead of setting the reroll (and rolling dice for "only if failed" and "with chance")
    // its parameters are written down, for the rule the roll is asked for.
    [HarmonyPatch(typeof(ModifyD20), "OnEventAboutToTrigger", new[] { typeof(RuleRollD20) })]
    static class D20ModifiersOnDryRoll
    {
        static readonly MethodInfo CheckRule = AccessTools.Method(typeof(ModifyD20), "CheckRule");

        static bool Prefix(ModifyD20 __instance, RuleRollD20 evt)
        {
            if (!DryRun.Active) return true;
            try
            {
                var rule = Rulebook.CurrentContext.PreviousEvent;
                if (rule == null || !(bool)CheckRule.Invoke(__instance, new object[] { rule })) return false;
                var m = new D20Modifier
                {
                    Source = __instance.Fact?.Name,
                    Replace = __instance.Replace,
                    Roll = __instance.Roll,
                    Rerolls = __instance.RollsAmount,
                    TakeBest = __instance.TakeBest,
                    OnlyIfFailed = __instance.RerollOnlyIfFailed,
                };
                if (__instance.AddSavingThrowBonus) m.SaveBonus = __instance.Value.Calculate(__instance.Fact?.MaybeContext);
                if (__instance.WithChance)
                    m.Chance = Math.Max(0, Math.Min(100, __instance.Chance.Calculate(__instance.Fact?.MaybeContext)));
                DryRun.D20Modifiers.Add(m);
            }
            catch (Exception e)
            {
                Main.Error("d20 modifier", e);
            }
            return false;
        }
    }
#endif
}
