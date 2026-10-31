using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Kingmaker.PubSubSystem;
using Kingmaker.RuleSystem.Rules;
using UnityEngine;
using UnityModManagerNet;

namespace CombatPreview
{
    public class Settings : UnityModManager.ModSettings
    {
        // Every attack with a weapon: the predicted chance before the roll and the real roll after it, in
        // CombatPreview.log in the mod folder.
        public bool LogAttacks = false;

        // The hit chance of the selected character under the enemy the pointer is on.
        public bool ShowOnHover = false;
        public bool Detailed = true;
        public float X = 30, Y = 160, Scale = 1, Width = 540;
        internal bool Enabled;
        public bool ManualScenario;
        public int ScenarioDR, ScenarioFire, ScenarioCold, ScenarioElectricity, ScenarioAcid, ScenarioSonic;
        public bool ScenarioBypassDR, ScenarioPhysicalImmune, ScenarioFireImmune;
        // Session-only acknowledgement; profiles never silently reactivate after restart.
        internal bool ScenarioConfirmed;

        // The mod works in turn-based combat only: in real time there is no time to read a chance.
        // Legacy prediction logging must never replay live rules.
        internal bool Logging => false;
        public bool LocalDiagnostics;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }
    }

    public static class Main
    {
        internal static UnityModManager.ModEntry.ModLogger Log;
        internal static Settings Settings;
        internal static PassiveAttackDiagnostics Diagnostics;
        static bool diagnosticsRegistered;
        static readonly HashSet<string> s_Errors = new HashSet<string>();

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Log = modEntry.Logger;
            Settings = UnityModManager.ModSettings.Load<Settings>(modEntry);
            Settings.Enabled = false;
            Settings.ScenarioConfirmed = false;
            Settings.ShowOnHover = false;
            modEntry.OnToggle = (e, enabled) =>
            {
                Settings.Enabled = enabled && diagnosticsRegistered;
                if (!enabled) { Diagnostics?.Reset(); HoverChance.Invalidate(); }
                return !enabled || diagnosticsRegistered;
            };
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = e => Settings.Save(e);
            ChanceLog.Open(Path.Combine(modEntry.Path, "CombatPreview.log"));
            try { Compatibility.Inspect(); }
            catch (Exception e)
            {
                Compatibility.CanPreview = false;
                Compatibility.Summary = "BLOCKED: compatibility inspection failed";
                Error("Compatibility inspection", e);
            }
            // No PatchAll: legacy DryRun suppression/cleanup patches would affect
            // the whole process and cannot establish a read-only sandbox.
            try
            {
                Diagnostics = new PassiveAttackDiagnostics(Path.Combine(modEntry.Path, "CombatPreview.diagnostics.jsonl"));
                EventBus.Subscribe(Diagnostics);
                diagnosticsRegistered = true;
                Settings.Enabled = true;
            }
            catch (Exception e)
            {
                Settings.Enabled = false;
                diagnosticsRegistered = false;
                if (Diagnostics != null)
                    try { EventBus.Unsubscribe(Diagnostics); }
                    catch (Exception cleanup) { Error("diagnostics unsubscribe", cleanup); }
                Diagnostics = null;
                Error("Passive diagnostics registration failed", e);
            }
            return true;
        }

        static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            GUILayout.Label(Compatibility.Summary);
            foreach (var issue in Compatibility.Issues) GUILayout.Label(issue);
            GUILayout.Label("Experimental build: game integration and state/RNG preservation are NOT validated.");
            GUILayout.Label(PredictionSafety.BlockReason);
            Settings.ShowOnHover = false;
            bool localDiagnostics = GUILayout.Toggle(Settings.LocalDiagnostics, "Local passive attack diagnostics (no extra rules/dice; max 2 MiB)");
            if (localDiagnostics != Settings.LocalDiagnostics) { Settings.LocalDiagnostics = localDiagnostics; Diagnostics?.Reset(); }
            GUILayout.Label("Diagnostics: observed attacks only; live prediction/condition fingerprint integration pending.");
            if (Settings.LocalDiagnostics && Diagnostics != null) GUILayout.Label(Diagnostics.Status);
            GUILayout.Label("Panel/manual model settings below are inactive while hover preview is blocked.");
            Settings.Detailed = GUILayout.Toggle(Settings.Detailed, "Detailed attack rows");
            GUILayout.Label("Panel scale");
            Settings.Scale = GUILayout.HorizontalSlider(Settings.Scale, 0.7f, 1.6f);
            GUILayout.Label("Panel width");
            Settings.Width = GUILayout.HorizontalSlider(Settings.Width, 420, 850);
            if (GUILayout.Button("Reset panel position")) { Settings.X = 30; Settings.Y = 160; }
            GUILayout.Label("Automatic target defense results are unavailable; the manual model is hypothetical.");
            Settings.ManualScenario = GUILayout.Toggle(Settings.ManualScenario, "Manual defense scenario (NOT actual target defenses)");
            if (Settings.ManualScenario)
            {
                Settings.ScenarioConfirmed = GUILayout.Toggle(Settings.ScenarioConfirmed, "I am entering effective, post-bypass values for a hypothetical target");
                EditReduction("Effective physical DR", ref Settings.ScenarioDR);
                EditReduction("Fire resistance", ref Settings.ScenarioFire);
                EditReduction("Cold resistance", ref Settings.ScenarioCold);
                EditReduction("Electricity resistance", ref Settings.ScenarioElectricity);
                EditReduction("Acid resistance", ref Settings.ScenarioAcid);
                EditReduction("Sonic resistance", ref Settings.ScenarioSonic);
                Settings.ScenarioBypassDR = GUILayout.Toggle(Settings.ScenarioBypassDR, "Weapon bypasses physical DR in this scenario");
                Settings.ScenarioPhysicalImmune = GUILayout.Toggle(Settings.ScenarioPhysicalImmune, "Physical immunity in scenario");
                Settings.ScenarioFireImmune = GUILayout.Toggle(Settings.ScenarioFireImmune, "Fire immunity in scenario");
                GUILayout.Label("Other reductions assumed zero in this MANUAL model. Pools, vulnerability and difficulty multipliers excluded.");
            }
        }

        static void EditReduction(string label, ref int value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label);
            int parsed;
            if (int.TryParse(GUILayout.TextField(value.ToString(), GUILayout.Width(80)), out parsed))
                value = Math.Max(0, Math.Min(1000000, parsed));
            GUILayout.EndHorizontal();
        }

        // Each kind of error once to the UMM log, every time to CombatPreview.log.
        internal static void Error(string what, Exception e)
        {
            ChanceLog.Write("ERROR " + what + ": " + e);
            if (s_Errors.Add(what + e.GetType().Name)) Log.Error(what + ": " + e);
        }
    }

    static class ChanceLog
    {
        static StreamWriter s_Writer;

        internal static void Open(string path)
        {
            try
            {
                // The log of the session before is kept beside it (the game is often restarted to switch the interface).
                var previous = Path.ChangeExtension(path, ".prev.log");
                if (File.Exists(path))
                {
                    if (File.Exists(previous)) File.Delete(previous);
                    File.Move(path, previous);
                }
                s_Writer = new StreamWriter(path, false) { AutoFlush = true };
                s_Writer.WriteLine("Combat Preview log, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            catch (Exception e)
            {
                Main.Log.Error("log file: " + e.Message);
            }
        }

        internal static void Write(string line)
        {
            try { s_Writer?.WriteLine(DateTime.Now.ToString("HH:mm:ss ") + line); }
            catch { }
        }
    }

    // Checks the prediction against the game: before every attack with a weapon the chance is calculated, after its
    // roll the real attack bonus, armor class and result are written next to it.
    class AttackCheck : IGlobalRulebookHandler<RuleAttackWithWeapon>, IGlobalRulebookHandler<RuleAttackRoll>
    {
        readonly Dictionary<RuleAttackWithWeapon, AttackPrediction> m_Pending = new Dictionary<RuleAttackWithWeapon, AttackPrediction>();
        static int s_Attacks, s_Hits, s_Mismatches;
        static double s_Expected;

        internal static string Summary()
        {
            return s_Attacks == 0 ? "No attacks checked yet."
                : string.Format("Attacks checked: {0}, hits {1}, expected {2:0.0}; AB/AC mismatches: {3}", s_Attacks, s_Hits, s_Expected, s_Mismatches);
        }

        public void OnEventAboutToTrigger(RuleAttackWithWeapon evt)
        {
            if (!Main.Settings.Logging || evt.Weapon == null || evt.Target == null) return;
            try
            {
                var p = AttackChance.Predict(evt.Initiator, evt.Target, evt.Weapon, evt.AttackBonusPenalty, evt);
                // A spell the hit brings (Spellstrike, a held charge, a ray of an eldritch archer).
                var spell = Spellstrike.WithAttack(evt.Initiator, evt.Weapon);
                if (spell != null) p.Notes.Add("carries " + spell.Name);
                m_Pending[evt] = p;
            }
            catch (Exception e)
            {
                Main.Error("attack prediction", e);
            }
        }

        internal static string ReasonName(Kingmaker.RuleSystem.RulebookEvent evt)
        {
            var reason = evt.Reason;
            return reason?.Ability?.Name ?? reason?.Fact?.Name ?? reason?.Context?.AssociatedBlueprint?.name ?? "?";
        }

        internal static string Name(Kingmaker.EntitySystem.Entities.UnitEntityData unit)
        {
            string name = unit.CharacterName;
            return string.IsNullOrEmpty(name) ? unit.Blueprint.name : name;
        }

        public void OnEventDidTrigger(RuleAttackWithWeapon evt)
        {
            m_Pending.Remove(evt);
        }

        public void OnEventAboutToTrigger(RuleAttackRoll evt)
        {
        }

        public void OnEventDidTrigger(RuleAttackRoll evt)
        {
            var attack = evt.RuleAttackWithWeapon;
            AttackPrediction p = null;
            if (attack == null) p = EarlyPredictions.Take(evt) as AttackPrediction;
            if (p == null && (attack == null || !m_Pending.TryGetValue(attack, out p)))
            {
                // An attack roll the prediction did not see (no attack with a weapon around it): written down so
                // that what is not covered yet shows.
                if (Main.Settings.Logging)
                    ChanceLog.Write(string.Format("{0} -> {1} [{2}]: not predicted ({3}), real: {4}, d20 {5} + {6} vs AC {7}",
                        Name(evt.Initiator), Name(evt.Target), evt.Weapon?.Blueprint.Name,
                        attack == null ? "no attack with a weapon, reason " + (evt.Reason?.Ability?.Name ?? evt.Reason?.Fact?.Name ?? "?") : "no prediction",
                        evt.Result, GameRules.Die(evt), evt.AttackBonus, evt.TargetAC));
                return;
            }
            if (attack != null) m_Pending.Remove(attack);
            try
            {
                s_Attacks++;
                s_Expected += p.Hit;
                if (evt.IsHit) s_Hits++;
                // AB and AC are only rolled against when the attack got past concealment and was not automatic.
                bool rolled = GameRules.Die(evt) > 0;
                bool mismatch = rolled && (evt.AttackBonus != p.AttackBonus || evt.TargetAC != p.TargetAC);
                if (mismatch) s_Mismatches++;
                var sb = new System.Text.StringBuilder();
                sb.AppendFormat("{0} -> {1} [{2}{3}]{4}: {5}", Name(evt.Initiator), Name(evt.Target), p.Weapon,
                    p.Penalty != 0 ? " -" + p.Penalty : "", attack == null ? " " + ReasonName(evt) : "", p);
                if (p.AttackBonusParts != null)
                    sb.AppendFormat("\n    AB {0} = {1}\n    AC {2} = {3}", p.AttackBonus, Breakdown.Format(p.AttackBonusParts),
                        p.TargetAC, Breakdown.Format(p.ArmorClassParts));
                sb.AppendFormat("\n    real: {0}{1}", evt.Result,
                    rolled ? string.Format(", d20 {0} + {1} vs AC {2}", GameRules.Die(evt), evt.AttackBonus, evt.TargetAC) : "");
                if (mismatch)
                {
                    // The real terms, taken while the roll's own bonuses are still on, and what differs by name.
                    var realAB = evt.AttackBonusRule != null ? Breakdown.AttackBonus(evt.AttackBonusRule, evt.Initiator) : null;
                    var realAC = evt.ACRule != null ? Breakdown.ArmorClass(evt.ACRule, evt.Target) : null;
                    sb.Append("\n    <<< MISMATCH");
                    if (evt.AttackBonus != p.AttackBonus)
                        sb.AppendFormat("\n    real AB {0} = {1}\n    AB differs: {2}", evt.AttackBonus, Breakdown.Format(realAB),
                            Breakdown.Diff(p.AttackBonusParts, realAB));
                    if (evt.TargetAC != p.TargetAC)
                        sb.AppendFormat("\n    real AC {0} = {1}\n    AC differs: {2}", evt.TargetAC, Breakdown.Format(realAC),
                            Breakdown.Diff(p.ArmorClassParts, realAC));
                }
                ChanceLog.Write(sb.ToString());
            }
            catch (Exception e)
            {
                Main.Error("attack check", e);
            }
        }
    }
}
