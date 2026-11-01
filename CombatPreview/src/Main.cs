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
        public bool PanelDetails;
        public string DetailsKey = "F6", PinKey = "F7";
        public float X = 24, Y = 160, Scale = 1, Width = 720;
        public bool AnchorAboveActionBar;
        public bool AnchorLeft = true;
        public bool UseObservedPreview = true;
        public int PanelLayoutVersion;
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
        public bool CompareBeta = true;
        public bool BetaAlerts;

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
        static BetaAttackHook betaHook;
        static bool diagnosticsRegistered;
        static readonly HashSet<string> s_Errors = new HashSet<string>();

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Log = modEntry.Logger;
            Settings = UnityModManager.ModSettings.Load<Settings>(modEntry);
            if (Settings.PanelLayoutVersion < 2)
            {
                Settings.AnchorAboveActionBar = false; Settings.AnchorLeft = true;
                Settings.Width = 720; Settings.X = 24; Settings.Y = 160;
                Settings.PanelLayoutVersion = 2;
            }
            Settings.Enabled = false;
            Settings.ScenarioConfirmed = false;
            modEntry.OnToggle = (e, enabled) =>
            {
                Settings.Enabled = enabled && diagnosticsRegistered;
                SyncRuleObservers();
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
                BetaRuntime.Initialize();
                betaHook = new BetaAttackHook();
                EventBus.Subscribe(betaHook);
                HoverChance.Create();
                diagnosticsRegistered = true;
                Settings.Enabled = true;
                SyncRuleObservers();
            }
            catch (Exception e)
            {
                Settings.Enabled = false;
                try { PassiveRuleResults.Remove(); } catch (Exception cleanup) { Error("rule observer cleanup", cleanup); }
                diagnosticsRegistered = false;
                if (Diagnostics != null)
                    try { EventBus.Unsubscribe(Diagnostics); }
                    catch (Exception cleanup) { Error("diagnostics unsubscribe", cleanup); }
                if (betaHook != null)
                    try { EventBus.Unsubscribe(betaHook); }
                    catch (Exception cleanup) { Error("beta hook unsubscribe", cleanup); }
                HoverChance.DestroyOwned();
                betaHook = null;
                Diagnostics = null;
                Error("Passive diagnostics registration failed", e);
            }
            return true;
        }

        static void SyncRuleObservers()
        {
            try
            {
                if (Settings.Enabled && Settings.ShowOnHover && Settings.UseObservedPreview) PassiveRuleResults.Install();
                else PassiveRuleResults.Remove();
            }
            catch (Exception e) { Error("Completed rule observer installation", e); }
        }

        static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            GUILayout.Label(Compatibility.Summary);
            foreach (var issue in Compatibility.Issues) GUILayout.Label(issue);
            GUILayout.Label("읽기 전용 베타: 기본 주무기 모델의 부분 추정입니다. 실제 게임 표시/비교는 베타 검증 중입니다.");
            bool show = GUILayout.Toggle(Settings.ShowOnHover, "베타 무기 미리보기 켜기 (기본 OFF)");
            if (show != Settings.ShowOnHover) { Settings.ShowOnHover = show && Compatibility.CanPreview; HoverChance.Invalidate(); SyncRuleObservers(); }
            Settings.CompareBeta = GUILayout.Toggle(Settings.CompareBeta, "표시 모델과 실제 AB/AC/무기 구성의 차이를 로컬 로그에 기록");
            Settings.BetaAlerts = GUILayout.Toggle(Settings.BetaAlerts, "모델 차이 알림 표시 (미반영 효과일 수 있음; 팝업으로 전투를 막지 않음)");
            bool observed = GUILayout.Toggle(Settings.UseObservedPreview, "이미 실행된 AB/AC 계산 결과와 피해 구성 읽기 (추가 규칙 실행 없음)");
            if (observed != Settings.UseObservedPreview) { Settings.UseObservedPreview = observed; HoverChance.Invalidate(); SyncRuleObservers(); }
            GUILayout.Label("완료된 규칙 관측: " + (PassiveRuleResults.Installed ? "ON" : "OFF") + " | AB " + BetaRuntime.ObservedAB + " / AC " + BetaRuntime.ObservedAC + " / 공격 수 " + BetaRuntime.ObservedCounts);
            GUILayout.BeginHorizontal();
            GUILayout.Label("상세 키"); Settings.DetailsKey = GUILayout.TextField(Settings.DetailsKey, GUILayout.Width(70));
            GUILayout.Label("대상 고정 키"); Settings.PinKey = GUILayout.TextField(Settings.PinKey, GUILayout.Width(70));
            GUILayout.EndHorizontal();
            bool left = GUILayout.Toggle(Settings.AnchorLeft, "좌측에 압축 배치");
            if (left != Settings.AnchorLeft) { Settings.AnchorLeft = left; if (left) Settings.AnchorAboveActionBar = false; }
            bool centered = GUILayout.Toggle(Settings.AnchorAboveActionBar, "하단 행동 막대 위 중앙 배치");
            if (centered != Settings.AnchorAboveActionBar) { Settings.AnchorAboveActionBar = centered; if (centered) Settings.AnchorLeft = false; }
            GUILayout.Label("양손 잡기와 확인된 추가 공격 효과를 반영합니다. 모든 추가 타격/제조 보조손 및 DR·저항·보호막 적용 피해는 미확인입니다.");
            bool localDiagnostics = GUILayout.Toggle(Settings.LocalDiagnostics, "Local passive attack diagnostics (no extra rules/dice; max 2 MiB)");
            if (localDiagnostics != Settings.LocalDiagnostics) { Settings.LocalDiagnostics = localDiagnostics; Diagnostics?.Reset(); }
            GUILayout.Label("베타 차이 기록: " + BetaRuntime.DifferenceCount + " | 기대 피해와 한 번의 무작위 피해는 불일치로 판정하지 않습니다.");
            if ((Settings.LocalDiagnostics || Settings.ShowOnHover && Settings.CompareBeta) && Diagnostics != null) GUILayout.Label(Diagnostics.Status);
            Settings.PanelDetails = GUILayout.Toggle(Settings.PanelDetails, "미리보기 상세 계산 표시");
            GUILayout.Label("Panel scale");
            Settings.Scale = GUILayout.HorizontalSlider(Settings.Scale, 0.7f, 1.6f);
            GUILayout.Label("Panel width");
            Settings.Width = GUILayout.HorizontalSlider(Settings.Width, 620, 1100);
            if (GUILayout.Button("패널 위치 초기화 (좌측 압축)")) { Settings.AnchorLeft = true; Settings.AnchorAboveActionBar = false; Settings.Width = 720; Settings.Scale = 1; }
            GUILayout.Label("자동 방어 적용 피해는 계산 불가입니다. 수동 시나리오는 실제 대상 방어가 아닙니다.");
            Settings.ManualScenario = GUILayout.Toggle(Settings.ManualScenario, "수동 물리 방어 시나리오 (실제 대상 방어 아님)");
            if (Settings.ManualScenario)
            {
                Settings.ScenarioConfirmed = GUILayout.Toggle(Settings.ScenarioConfirmed, "I am entering effective, post-bypass values for a hypothetical target");
                EditReduction("Effective physical DR", ref Settings.ScenarioDR);
                Settings.ScenarioBypassDR = GUILayout.Toggle(Settings.ScenarioBypassDR, "Weapon bypasses physical DR in this scenario");
                Settings.ScenarioPhysicalImmune = GUILayout.Toggle(Settings.ScenarioPhysicalImmune, "Physical immunity in scenario");
                GUILayout.Label("현재 수동 모델은 기본 물리 피해만 계산합니다. 보호막·취약성·난이도 배율은 제외합니다.");
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
