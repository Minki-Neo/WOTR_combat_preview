using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace CombatPreview
{
    partial class HoverChance
    {
        Vector2 panelScroll;
        GUIStyle panelStyle, titleStyle, textStyle, mutedStyle, numberStyle, hitStyle, cardStyle;
        Texture2D panelTexture, cardTexture;
        float panelWidth, panelHeight, nextBarSearch;
        RectTransform actionBar;
        GameObject inputBlocker;
        RectTransform blockerRect;
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
        { if (panelTexture != null) Destroy(panelTexture); if (cardTexture != null) Destroy(cardTexture); if (inputBlocker != null) Destroy(inputBlocker); }
        void HidePanelBlocker() { if (inputBlocker != null) inputBlocker.SetActive(false); }
        void UpdatePanelBlocker(Rect rect)
        {
            if (inputBlocker == null)
            {
                inputBlocker = new GameObject("CombatPreviewInputBlocker", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
                inputBlocker.transform.SetParent(transform, false);
                var canvas = inputBlocker.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 20000;
                var child = new GameObject("PanelHitArea", typeof(RectTransform), typeof(Image));
                child.transform.SetParent(inputBlocker.transform, false);
                var image = child.GetComponent<Image>(); image.color = Color.clear; image.raycastTarget = true;
                blockerRect = child.GetComponent<RectTransform>();
                blockerRect.anchorMin = blockerRect.anchorMax = Vector2.zero; blockerRect.pivot = Vector2.zero;
            }
            inputBlocker.SetActive(true);
            blockerRect.anchoredPosition = new Vector2(rect.x, Screen.height - rect.yMax);
            blockerRect.sizeDelta = new Vector2(rect.width, rect.height);
        }
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
            if (!BetaRuntime.Enabled || settings == null) { HidePanelBlocker(); return; }
            EnsurePanelStyles();
            if (BetaRuntime.AlertVisible)
                GUI.Box(new Rect(Screen.width * .5f - 330, 35, 660, 52), BetaRuntime.Alert, cardStyle);
            if (m_Target == null || (panelSnapshot == null && string.IsNullOrEmpty(panelReason))) { HidePanelBlocker(); return; }
            float scale = Mathf.Clamp(settings.Scale, .7f, 1.6f);
            var old = GUI.matrix;
            try
            {
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));
                float sw = Screen.width / scale, sh = Screen.height / scale;
                panelWidth = Mathf.Min(Mathf.Clamp(settings.Width, 620, 1100), sw - 12);
                int rows = Math.Max(1, panelAttacks.Sum(a => a.Snapshot?.Rows.Count ?? 1));
                int components = Math.Max(1, panelAttacks.Select(a => a.Snapshot?.Input.Components.Count ?? 1).DefaultIfEmpty(1).Max());
                float wanted = 188 + rows * Mathf.Max(90, components * 22 + 34) + (settings.PanelDetails ? rows * 55 + 60 : 0);
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
                panelScreenRect = new Rect(moved.x * scale, moved.y * scale, moved.width * scale, moved.height * scale);
                UpdatePanelBlocker(panelScreenRect);
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
            GUILayout.Label((panelHeld ? "표시 시점 정보 보관 중 · 대상 위로 돌아가면 갱신" : retention.Pinned ? "대상 고정 중" : "대상 고정 " + settings.PinKey) + "  |  상세 " + settings.DetailsKey, mutedStyle);
            bool manual = settings.ManualScenario && settings.ScenarioConfirmed;
            double manualTotal = 0;
            bool manualComplete = manual;
            panelScroll = GUILayout.BeginScrollView(panelScroll);
            foreach (var attack in panelAttacks)
            {
                GUILayout.Label(attack.Role + " · " + attack.Actor + "  |  " + (attack.Weapon ?? "공격 정보"), titleStyle);
                var snapshot = attack.Snapshot;
                if (snapshot == null)
                {
                    GUILayout.Label(attack.Reason, mutedStyle); manualComplete = false; continue;
                }
                bool observedTotals = snapshot.Input.Source.StartsWith("observed-");
                GUILayout.Label(observedTotals ? "최근 완료된 규칙의 AB/AC 관측값 · 현재 조건의 완전 일치는 미확정" : "기본 능력치 추정 · 최종 명중 보정/상황별 AC 미확인", mutedStyle);
                foreach (var row in snapshot.Rows)
                {
                    var scenario = Scenario(snapshot, row);
                    double? expected = scenario != null && scenario.Available ? scenario.Expected : (double?)null;
                    double? mean = expected.HasValue && row.Hit > 0 ? expected.Value / row.Hit : row.MeanOnHit;
                    float available = Mathf.Max(120, panelWidth - 70);
                    GUILayout.BeginHorizontal(cardStyle);
                    Cell("공격", "#" + (row.Index + 1), available * .08f, numberStyle);
                    Cell(observedTotals ? "명중률" : "추정 명중률", row.Hit.ToString("P0"), available * .13f, hitStyle);
                    Cell("피해 주사위", PanelPresentation.DiceLines(snapshot.Input), available * .31f, textStyle);
                    Cell("명중 시 평균", PanelPresentation.Value(mean), available * .20f, numberStyle, manual ? "수동 방어 · 치명 포함" : "방어 전 · 치명 포함");
                    Cell(manual ? "수동 방어 기댓값" : "방어 반영 기댓값", PanelPresentation.Value(expected), available * .28f, numberStyle,
                        expected.HasValue ? "실제 대상 방어 아님" : "방어 전 " + PanelPresentation.Value(row.ExpectedRaw));
                    GUILayout.EndHorizontal();
                    if (settings.PanelDetails)
                    {
                        GUILayout.Label((observedTotals ? "관측된 최종 AC " : "일반 AC 추정 ") + row.AC
                            + "  |  " + (observedTotals ? "관측된 명중 보정 합계 " : "기본 명중 보정 합계 ") + row.AB.ToString("+0;-0;0"), textStyle);
                        GUILayout.Label("d20 + (" + row.AB + ") ≥ " + row.AC + "  →  " + AttackThreshold.Describe(row.AB, row.AC), textStyle);
                        GUILayout.Label("자연 1 실패 / 자연 20 명중 · 은폐/재굴림/치명타 면역 등 미반영", mutedStyle);
                    }
                    manualComplete &= expected.HasValue;
                    if (expected.HasValue) manualTotal += expected.Value;
                }
            }
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            GUILayout.Label("표시 공격 합계 기댓값", titleStyle);
            GUILayout.Label(manualComplete ? PanelPresentation.Value(manualTotal) + " (수동 방어)"
                : "—  |  방어 전 " + PanelPresentation.Value(PanelPresentation.TotalRaw(panelAttacks.Select(a => a.Snapshot))), numberStyle);
            if (GUILayout.Button(settings.PanelDetails ? "접기 [" + settings.DetailsKey + "]" : "상세 [" + settings.DetailsKey + "]", GUILayout.Width(95))) settings.PanelDetails = !settings.PanelDetails;
            GUILayout.EndHorizontal();
            GUILayout.Label(manual ? "수동 시나리오 · 실제 대상 방어가 아닙니다." : "DR·저항·보호막 미확인: 방어 반영 기댓값은 —", mutedStyle);
            GUILayout.Label("기수/탈것의 읽힌 공격만 합산 · 추가 공격/보조손/조건부 효과는 일부 미확인", mutedStyle);
            GUI.DragWindow(new Rect(0, 0, 10000, 28));
            if (!panelHeld && Event.current.type == EventType.Repaint) foreach (var attack in panelAttacks) if (attack.Snapshot != null) BetaRuntime.Shown(attack.Snapshot);
        }
    }
}
