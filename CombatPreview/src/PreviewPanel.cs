using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CombatPreview
{
    partial class HoverChance
    {
        List<AttackPrediction> panelRows;
        Vector2 panelScroll;
        void OnGUI()
        {
            var settings = Main.Settings;
            if (!Compatibility.CanPreview || panelFrame != Time.frameCount || settings == null || !settings.Enabled || !settings.ShowOnHover || panelRows == null || panelRows.Count == 0) return;
            float scale = Mathf.Clamp(settings.Scale, .7f, 1.6f);
            var old = GUI.matrix;
            try
            {
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));
                float width = Mathf.Min(Mathf.Clamp(settings.Width, 420, 850), Screen.width / scale);
                float height = Mathf.Min(settings.Detailed ? 560 : 320, Screen.height / scale);
                var rect = new Rect(Mathf.Clamp(settings.X, 0, Mathf.Max(0, Screen.width / scale - width)),
                    Mathf.Clamp(settings.Y, 0, Mathf.Max(0, Screen.height / scale - height)), width, height);
                rect = GUI.Window(739261, rect, DrawPanel, "Combat Preview | EXPERIMENTAL");
                settings.X = rect.x; settings.Y = rect.y;
            }
            finally { GUI.matrix = old; }
        }
        static void DrawReport(string label, PredictionReport report)
        {
            GUILayout.Label(label + ": " + (report == null ? "Unavailable" : report.Quality.ToString())
                + (report != null && report.Expected.HasValue ? string.Format(" | E {0:0.00}", report.Expected.Value) : " | no value"));
            if (report != null && report.Quality == PredictionQuality.Unavailable)
                foreach (var reason in report.Reasons) GUILayout.Label(reason);
        }
        void DrawPanel(int id)
        {
            GUILayout.Label(m_Target.CharacterName + " | " + (m_Full ? "Full attack" : "Single attack"));
            bool scenario = Main.Settings.ManualScenario && Main.Settings.ScenarioConfirmed;
            DrawReport("RAW model sum (before defense)", PredictionReport.Total(panelRows.Select(p => p.RawReport).ToList(), panelRows.Count));
            DrawReport("ACTUAL TARGET after defense", PredictionReport.Total(panelRows.Select(p => p.TargetReport).ToList(), panelRows.Count));
            if (scenario) DrawReport("MANUAL SCENARIO sum (hypothetical)", PredictionReport.Total(panelRows.Select(p => p.ScenarioReport).ToList(), panelRows.Count));
            GUILayout.Label("Frozen-state model. Sneak attack / conditional effects / mount attacks excluded.");
            panelScroll = GUILayout.BeginScrollView(panelScroll);
            for (int i = 0; i < panelRows.Count; i++)
            {
                var p = panelRows[i];
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(string.Format("#{0}  {1}  AB {2:+0;-0;0} vs AC {3}  | Hit {4:P0}  | E {5:0.0}",
                    i + 1, p.Weapon, p.AttackBonus, p.AutoHit || p.AutoMiss ? "n/a" : p.TargetAC.ToString(), p.Hit, p.Expected));
                DrawReport("RAW", p.RawReport);
                DrawReport("TARGET", p.TargetReport);
                if (scenario) DrawReport("MANUAL SCENARIO", p.ScenarioReport);
                if (Main.Settings.Detailed)
                {
                    GUILayout.Label(string.Join(" + ", p.DamageTerms.Select(t => t.ToString()).ToArray()));
                    GUILayout.Label(string.Format("Min / mean / max: {0} / {1:0.0} / {2}   Crit {3:P1} x{4}",
                        p.NormalDamage.Min, p.NormalDamage.Mean, p.NormalDamage.Max, p.Crit, p.CriticalMultiplier));
                    if (p.MirrorImages > 0) GUILayout.Label("Mirror Image count frozen across attacks.");
                    foreach (var reason in (p.RawReport?.Reasons ?? new string[0]).Concat(p.TargetReport?.Reasons ?? new string[0]).Distinct())
                        GUILayout.Label(reason);
                    foreach (var note in p.Notes) GUILayout.Label(note);
                }
                GUILayout.EndVertical();
            }
            GUILayout.EndScrollView();
            GUILayout.Label("Conditional on-hit effects, mount attacks and target death are not simulated.");
            GUI.DragWindow(new Rect(0, 0, 10000, 24));
        }
    }
}
