using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace CombatPreview
{
    partial class HoverChance
    {
        Vector2 panelScroll;
        GUIStyle panelStyle, titleStyle, textStyle, mutedStyle, numberStyle, hitStyle, cardStyle;
        Texture2D panelTexture, cardTexture;
        float panelWidth, panelHeight, nextBarSearch;
        RectTransform actionBar;
        readonly Vector3[] barCorners = new Vector3[4];
        static readonly Color Gold = new Color(.72f, .61f, .40f);
        static readonly Color Ink = new Color(.91f, .87f, .76f);
        static Texture2D Gradient(Color top, Color bottom)
        {
            var texture = new Texture2D(1, 32, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < 32; y++) texture.SetPixel(0, y, Color.Lerp(bottom, top, y / 31f));
            texture.Apply(false, true); return texture;
        }
        void EnsurePanelStyles()
        {
            if (panelStyle != null) return;
            // Original procedural backgrounds, not extracted game/third-party art.
            panelTexture = Gradient(new Color(.12f, .105f, .10f, .98f), new Color(.045f, .04f, .055f, .98f));
            cardTexture = Gradient(new Color(.18f, .155f, .135f, .95f), new Color(.075f, .068f, .075f, .96f));
            textStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true, richText = false, margin = new RectOffset(0, 0, 0, 0) };
            textStyle.normal.textColor = Ink;
            titleStyle = new GUIStyle(textStyle) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            titleStyle.normal.textColor = Gold;
            mutedStyle = new GUIStyle(textStyle) { fontSize = 12 };
            mutedStyle.normal.textColor = new Color(.68f, .66f, .62f);
            numberStyle = new GUIStyle(textStyle) { fontSize = 20, fontStyle = FontStyle.Bold };
            numberStyle.normal.textColor = new Color(.87f, .75f, .51f);
            hitStyle = new GUIStyle(numberStyle);
            hitStyle.normal.textColor = new Color(.65f, .80f, .55f);
            panelStyle = new GUIStyle { padding = new RectOffset(16, 16, 12, 12) };
            panelStyle.normal.background = panelTexture;
            panelStyle.onNormal.background = panelTexture;
            cardStyle = new GUIStyle { padding = new RectOffset(10, 10, 10, 10), margin = new RectOffset(0, 0, 3, 3) };
            cardStyle.normal.background = cardTexture;
        }
        void DisposePanel()
        { if (panelTexture != null) Destroy(panelTexture); if (cardTexture != null) Destroy(cardTexture); }
        float ActionBarTop()
        {
            if (Time.unscaledTime >= nextBarSearch)
            {
                actionBar = null; nextBarSearch = Time.unscaledTime + 1;
                foreach (string name in new[] { "Kingmaker.UI.TurnBasedMode.PredictionPanelPCView", "Kingmaker.UI.MVVM._PCView.ActionBar.ActionBarPCView" })
                {
                    var type = AccessTools.TypeByName(name);
                    if (type == null) continue;
                    foreach (var obj in UnityEngine.Object.FindObjectsOfType(type))
                    {
                        var component = obj as Component;
                        var rect = component == null ? null : component.transform as RectTransform;
                        float top;
                        if (rect != null && ReadBarTop(rect, out top)) { actionBar = rect; break; }
                    }
                    if (actionBar != null) break;
                }
            }
            float value;
            return actionBar != null && ReadBarTop(actionBar, out value) ? value : Screen.height * .74f;
        }
        bool ReadBarTop(RectTransform rect, out float top)
        {
            top = 0;
            if (!rect.gameObject.activeInHierarchy) return false;
            var canvas = rect.GetComponentInParent<Canvas>();
            if (canvas == null) return false;
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            rect.GetWorldCorners(barCorners);
            var a = RectTransformUtility.WorldToScreenPoint(camera, barCorners[0]);
            var b = RectTransformUtility.WorldToScreenPoint(camera, barCorners[2]);
            top = Screen.height - Mathf.Max(a.y, b.y);
            return Mathf.Abs(b.x - a.x) > Screen.width * .15f && Mathf.Abs(b.y - a.y) < Screen.height * .4f
                && top > Screen.height * .45f && top < Screen.height - 10;
        }
        void OnGUI()
        {
            var settings = Main.Settings;
            if (!BetaRuntime.Enabled || settings == null) return;
            EnsurePanelStyles();
            if (BetaRuntime.AlertVisible)
                GUI.Box(new Rect(Screen.width * .5f - 330, 35, 660, 52), BetaRuntime.Alert, cardStyle);
            if (m_Target == null || (panelSnapshot == null && string.IsNullOrEmpty(panelReason))) return;
            float scale = Mathf.Clamp(settings.Scale, .7f, 1.6f);
            var old = GUI.matrix;
            try
            {
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));
                float sw = Screen.width / scale, sh = Screen.height / scale;
                panelWidth = Mathf.Min(Mathf.Clamp(settings.Width, 620, 1100), sw - 12);
                int rows = panelSnapshot?.Rows.Count ?? 1;
                int components = panelSnapshot?.Input.Components.Count ?? 1;
                float wanted = 188 + rows * Mathf.Max(76, components * 22 + 20) + (settings.PanelDetails ? 125 : 0);
                float height = Mathf.Min(wanted, sh * .72f);
                panelHeight = height;
                float x = settings.X, y = settings.Y;
                if (settings.AnchorAboveActionBar)
                    PanelPresentation.Place(sw, sh, panelWidth, height, ActionBarTop() / scale, out x, out y);
                var rect = new Rect(Mathf.Clamp(x, 0, Mathf.Max(0, sw - panelWidth)), Mathf.Clamp(y, 0, Mathf.Max(0, sh - height)), panelWidth, height);
                var moved = GUI.Window(739261, rect, DrawPanel, "", panelStyle);
                if (Mathf.Abs(moved.x - rect.x) > .1f || Mathf.Abs(moved.y - rect.y) > .1f)
                    settings.AnchorAboveActionBar = false;
                settings.X = moved.x; settings.Y = moved.y;
            }
            finally { GUI.matrix = old; }
        }
        void Cell(string heading, string value, float width, GUIStyle valueStyle, string detail = null)
        {
            GUILayout.BeginVertical(GUILayout.Width(width));
            GUILayout.Label(heading, mutedStyle);
            GUILayout.Label(value, valueStyle);
            if (detail != null) GUILayout.Label(detail, mutedStyle);
            GUILayout.EndVertical();
        }
        MitigatedResult Scenario(BetaSnapshot snapshot, BetaRow row)
        {
            if (!Main.Settings.ManualScenario || !Main.Settings.ScenarioConfirmed || !snapshot.Input.DamageKnown) return null;
            return MitigationMath.Calculate(snapshot.Input.Components.Select(c => c.ToTerm()).ToArray(), snapshot.Input.Multiplier,
                row.Hit, row.Crit, DefenseReader.Scenario(Main.Settings));
        }
        void DrawPanel(int id)
        {
            var color = GUI.color;
            try
            {
                GUI.color = Gold;
                GUI.DrawTexture(new Rect(0, 0, panelWidth, 1), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(0, 0, 1, panelHeight), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(panelWidth - 1, 0, 1, panelHeight), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(0, panelHeight - 1, panelWidth, 1), Texture2D.whiteTexture);
            }
            finally { GUI.color = color; }
            var settings = Main.Settings;
            GUILayout.Label("◆  전투 미리보기  ◆", titleStyle);
            GUILayout.Label(attacker.CharacterName + "  →  " + m_Target.CharacterName + "    |    " + (targetHealth ?? "체력 확인 불가"), textStyle);
            if (panelSnapshot == null)
            {
                GUILayout.Label(panelReason, mutedStyle);
                GUI.DragWindow(new Rect(0, 0, 10000, 28)); return;
            }
            var snapshot = panelSnapshot;
            bool manual = settings.ManualScenario && settings.ScenarioConfirmed;
            var scenarios = snapshot.Rows.Select(r => Scenario(snapshot, r)).ToArray();
            GUILayout.Label((m_Full ? "주무기 기본 풀어택 · " + snapshot.Rows.Count + "회" : "주무기 단타")
                + (snapshot.Input.Source.StartsWith("observed-") ? "  |  최근 게임 추정값 기반" : "  |  기본 능력치 추정"), mutedStyle);
            panelScroll = GUILayout.BeginScrollView(panelScroll);
            foreach (var row in snapshot.Rows)
            {
                var scenario = scenarios[row.Index];
                double? expected = scenario != null && scenario.Available ? scenario.Expected : (double?)null;
                double? mean = expected.HasValue && row.Hit > 0 ? expected.Value / row.Hit : row.MeanOnHit;
                float available = Mathf.Max(120, panelWidth - 70);
                GUILayout.BeginHorizontal(cardStyle);
                Cell("공격", "#" + (row.Index + 1), available * .08f, numberStyle);
                Cell("명중률", row.Hit.ToString("P0"), available * .13f, hitStyle);
                Cell("피해 주사위", PanelPresentation.DiceLines(snapshot.Input), available * .31f, textStyle);
                Cell("명중 시 평균", PanelPresentation.Value(mean), available * .20f, numberStyle, manual ? "수동 방어 · 치명 포함" : "방어 전 · 치명 포함");
                Cell(manual ? "수동 방어 기댓값" : "방어 반영 기댓값", PanelPresentation.Value(expected), available * .28f, numberStyle,
                    expected.HasValue ? "실제 대상 방어 아님" : "방어 전 " + PanelPresentation.Value(row.ExpectedRaw));
                GUILayout.EndHorizontal();
                if (settings.PanelDetails)
                    GUILayout.Label(string.Format("AB {0:+0;-0;0} / AC {1}  ·  치명 {2}–20 / ×{3}  ·  일반 명중 평균 {4}",
                        row.AB, row.AC, snapshot.Input.CriticalEdge, snapshot.Input.Multiplier,
                        row.Normal.HasValue ? PanelPresentation.Value(row.Normal.Value.Mean) : "—"), mutedStyle);
            }
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            GUILayout.Label("합계 기댓값", titleStyle);
            double manualTotal = 0; bool all = manual;
            foreach (var scenario in scenarios)
            { all &= scenario != null && scenario.Available; if (scenario != null && scenario.Available) manualTotal += scenario.Expected; }
            GUILayout.Label(all ? PanelPresentation.Value(manualTotal) + " (수동 방어)" : "—  |  방어 전 " + PanelPresentation.Value(PanelPresentation.TotalRaw(snapshot)), numberStyle);
            if (GUILayout.Button(settings.PanelDetails ? "접기" : "상세", GUILayout.Width(50))) settings.PanelDetails = !settings.PanelDetails;
            GUILayout.EndHorizontal();
            GUILayout.Label(manual ? "수동 시나리오 · 실제 대상의 DR/저항을 판독한 값이 아닙니다."
                : "DR·저항·보호막 미확인: 방어 반영 기댓값은 —로 표시합니다.", mutedStyle);
            GUILayout.Label("부분 추정 · 추가 공격/보조손/조건부 효과 미반영", mutedStyle);
            if (settings.PanelDetails)
            {
                GUILayout.Label("평균 = 명중을 조건으로 치명타까지 가중한 피해. 기댓값 = 빗나감까지 포함한 공격당 평균.", mutedStyle);
                GUILayout.Label("가속·연타·탈것 추가 타격, 치명타 보정/면역, 은폐·난이도 등은 미확정입니다.", mutedStyle);
                GUILayout.Label("제목을 드래그해 이동 · 중앙 배치는 UMM 설정에서 복원", mutedStyle);
            }
            GUI.DragWindow(new Rect(0, 0, 10000, 28));
            if (Event.current.type == EventType.Repaint) BetaRuntime.Shown(snapshot);
        }
    }
}
