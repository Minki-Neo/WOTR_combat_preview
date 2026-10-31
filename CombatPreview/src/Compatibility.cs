using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.UnitLogic.Mechanics.Components;
using Kingmaker.Designers.Mechanics.Buffs;

namespace CombatPreview
{
    internal static class Compatibility
    {
        internal static string GameVersion = "unknown";
        internal static string Summary = "Not checked";
        internal static bool CanPreview;
        internal static bool HasTabletopTweaks;
        internal static readonly List<string> Issues = new List<string>();
        internal static void Inspect()
        {
            Issues.Clear();
            var version = AccessTools.TypeByName("Kingmaker.GameVersion");
            try
            {
                var getter = version == null ? null : AccessTools.Method(version, "GetVersion", Type.EmptyTypes);
                if (getter != null) GameVersion = getter.Invoke(null, null) as string ?? "unknown";
            }
            catch (Exception e) { Main.Log.Log("Version unavailable: " + e.GetType().Name); }
            Field("Kingmaker.RuleSystem.Rulebook", "m_RuleContext");
            Field("Kingmaker.RuleSystem.Rules.RuleAttackRoll", "missChance");
            Field("Kingmaker.RuleSystem.Rules.RuleConcealmentCheck", "missChance");
            Field(typeof(WeaponParametersDamageBonus).FullName, "m_checkGripStyle");
            Field(typeof(WeaponParametersDamageBonus).FullName, "m_gripStyle");
            Method("Kingmaker.UnitLogic.Commands.UnitAttack", "EnumerateFullAttack");
            Method("Kingmaker.RuleSystem.Rules.RuleCalculateWeaponStats", "OnTrigger");
            Method("Kingmaker.View.UnitEntityView", "set_MouseHighlighted");
            HasTabletopTweaks = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name.StartsWith("TabletopTweaks", StringComparison.OrdinalIgnoreCase));
            var game = AccessTools.TypeByName("Kingmaker.Game");
            if (game != null)
                Main.Log.Log("Game assembly: " + game.Assembly.FullName + " MVID=" + game.Module.ModuleVersionId);
            Main.Log.Log("Harmony: " + typeof(Harmony).Assembly.GetName().Version);
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies().Where(a =>
                a.GetName().Name.StartsWith("TabletopTweaks", StringComparison.OrdinalIgnoreCase)
                || a.GetName().Name.IndexOf("UnityModManager", StringComparison.OrdinalIgnoreCase) >= 0))
                Main.Log.Log("Loaded: " + a.FullName);
            foreach (string name in new[] { "RuleCalculateDamage", "RulePrepareDamage" })
            {
                var type = AccessTools.TypeByName("Kingmaker.RuleSystem.Rules.Damage." + name);
                var method = type == null ? null : AccessTools.Method(type, "OnTrigger");
                if (method == null) continue;
                var info = Harmony.GetPatchInfo(method);
                if (info != null) Main.Log.Log(name + " patch owners: " + string.Join(", ", info.Owners.ToArray()));
            }
            bool apiPresent = Issues.Count == 0;
            CanPreview = apiPresent && PredictionSafety.CanReplayEvents;
            Issues.Add(PredictionSafety.BlockReason);
            Summary = "Game " + GameVersion + " | " + (CanPreview ? "API preflight passed; runtime UNVERIFIED" : (apiPresent ? "BLOCKED: unsafe legacy event replay" : "BLOCKED: missing API and unsafe event replay"));
            Main.Log.Log(Summary);
            foreach (var issue in Issues) Main.Log.Error(issue);
        }
        static void Field(string typeName, string name)
        {
            var type = AccessTools.TypeByName(typeName);
            if (type == null || AccessTools.Field(type, name) == null) Issues.Add("Missing field: " + typeName + "." + name);
        }
        static void Method(string typeName, string name)
        {
            var type = AccessTools.TypeByName(typeName);
            if (type == null || AccessTools.Method(type, name) == null) Issues.Add("Missing method: " + typeName + "." + name);
        }
    }
}
