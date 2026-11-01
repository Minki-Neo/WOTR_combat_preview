using System;
using HarmonyLib;
using Kingmaker.RuleSystem.Rules;

namespace CombatPreview
{
    // Explicit POSTFIX observers for the shared engine calculation pipeline. No PatchAll, prefixes, skipped originals,
    // changed args/results, rule constructors, handlers, or RNG calls.
    internal static class PassiveRuleResults
    {
        const string Owner = "CombatPreview.PassiveRuleResults";
        static Harmony harmony;
        static bool faulted;
        internal static bool Installed { get { return harmony != null && !faulted; } }
        internal static void Install()
        {
            if (harmony != null || !Compatibility.CanPreview) return;
            var instance = new Harmony(Owner);
            try
            {
                instance.Patch(AccessTools.Method(typeof(RuleCalculateAttackBonus), "OnTrigger"),
                    postfix: new HarmonyMethod(typeof(PassiveRuleResults), nameof(AfterAB)) { priority = Priority.Last });
                instance.Patch(AccessTools.Method(typeof(RuleCalculateAC), "OnTrigger"),
                    postfix: new HarmonyMethod(typeof(PassiveRuleResults), nameof(AfterAC)) { priority = Priority.Last });
                instance.Patch(AccessTools.Method(typeof(RuleCalculateAttacksCount), "OnTrigger"),
                    postfix: new HarmonyMethod(typeof(PassiveRuleResults), nameof(AfterCount)) { priority = Priority.Last });
                instance.Patch(AccessTools.Method(typeof(RuleCalculateAttackBonusWithoutTarget), "OnTrigger"),
                    postfix: new HarmonyMethod(typeof(PassiveRuleResults), nameof(AfterBaseAB)) { priority = Priority.Last });
                instance.Patch(AccessTools.Method(typeof(RuleCalculateWeaponStats), "OnTrigger"),
                    postfix: new HarmonyMethod(typeof(PassiveRuleResults), nameof(AfterWeapon)) { priority = Priority.Last });
                harmony = instance; faulted = false;
            }
            catch { instance.UnpatchAll(Owner); throw; }
        }
        internal static void Remove()
        { if (harmony != null) harmony.UnpatchAll(Owner); harmony = null; }
        static void AfterAB(RuleCalculateAttackBonus __instance)
        { Read(() => BetaRuntime.ObserveABResult(__instance)); }
        static void AfterAC(RuleCalculateAC __instance)
        { Read(() => BetaRuntime.ObserveACResult(__instance)); }
        static void AfterCount(RuleCalculateAttacksCount __instance)
        { Read(() => BetaRuntime.ObserveCountResult(__instance)); }
        static void AfterBaseAB(RuleCalculateAttackBonusWithoutTarget __instance)
        { Read(() => BetaRuntime.ObserveBaseABResult(__instance)); }
        static void AfterWeapon(RuleCalculateWeaponStats __instance)
        { Read(() => BetaRuntime.ObserveWeaponResult(__instance)); }
        static void Read(Action read)
        {
            if (faulted) return;
            try { read(); }
            catch (NotSupportedException) { }
            catch (Exception e) { faulted = true; Main.Error("Completed rule observer stopped", e); }
        }
    }
}
