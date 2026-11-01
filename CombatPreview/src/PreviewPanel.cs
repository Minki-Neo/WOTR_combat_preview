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
        GUIStyle panelStyle, titleStyle, textStyle, mutedStyle, numberStyle, hitStyle, cardStyle, rowStyle;
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
            numberStyle = new GUIStyle(textStyle) { fontSize = 14, fontStyle = FontStyle.Bold };
            numberStyle.normal.textColor = new Color(.87f, .75f, .51f);
            hitStyle = new GUIStyle(numberStyle);
            hitStyle.normal.textColor = new Color(.65f, .80f, .55f);
            panelStyle = new GUIStyle { padding = new RectOffset(12, 12, 8, 8) };
            panelStyle.normal.background = panelTexture;
            panelStyle.onNormal.background = panelTexture;
            cardStyle = new GUIStyle { padding = new RectOffset(6, 6, 4, 4), margin = new RectOffset(0, 0, 1, 1) };
            cardStyle.normal.background = cardTexture;
            rowStyle = new GUIStyle(textStyle) { fontSize = 13, wordWrap = false, clipping = TextClipping.Clip, alignment = TextAnchor.MiddleLeft };
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
                float height = PanelPresentation.CompactHeight(rows, components, settings.PanelDetails, sh);
                panelHeight = height;
                float x = settings.X, y = settings.Y;
                if (settings.AnchorLeft) PanelPresentation.PlaceLeft(sw, sh, panelWidth, height, out x, out y);
                else if (settings.AnchorAboveActionBar)
                    PanelPresentation.Place(sw, sh, panelWidth, height, ActionBarTop() / scale, out x, out y);
                var rect = new Rect(Mathf.Clamp(x, 0, Mathf.Max(0, sw - panelWidth)), Mathf.Clamp(y, 0, Mathf.Max(0, sh - height)), panelWidth, height);
                var moved = GUI.Window(739261, rect, DrawPanel, "", panelStyle);
                if (Mathf.Abs(moved.x - rect.x) > .1f || Mathf.Abs(moved.y - rect.y) > .1f)
                { settings.AnchorAboveActionBar = false; settings.AnchorLeft = false; }
                settings.X = moved.x; settings.Y = moved.y;
                panelScreenRect = new Rect(moved.x * scale, moved.y * scale, moved.width * scale, moved.height * scale);
                UpdatePanelBlocker(panelScreenRect);
            }
            finally { GUI.matrix = old; }
        }
        void TableCell(string value, float width, GUIStyle style = null)
        { GUILayout.Label(value, style ?? rowStyle, GUILayout.Width(width)); }
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
            GUILayout.Label("전투 미리보기 · " + m_Target.CharacterName + "  |  " + (targetHealth ?? "체력 미확인"), titleStyle);
            GUILayout.Label((panelHeld ? "보관 중 · 대상 위로 돌아가면 갱신" : retention.Pinned ? "고정 중" : settings.PinKey + " 고정") + "  |  " + settings.DetailsKey + " 상세  |  ~ 기본 추정", mutedStyle);
            bool manual = settings.ManualScenario && settings.ScenarioConfirmed;
            double manualTotal = 0; bool manualComplete = manual;
            float available = Mathf.Max(120, panelWidth - 56);
            float actorW = available * .32f, hitW = available * .10f, diceW = available * .24f, meanW = available * .13f, expectedW = available * .21f;
            GUILayout.BeginHorizontal();
            TableCell("공격자 · 무기 / 타격", actorW, mutedStyle);
            TableCell("명중", hitW, mutedStyle);
            TableCell("피해 주사위", diceW, mutedStyle);
            TableCell("명중 평균", meanW, mutedStyle);
            TableCell("기댓값 후/전", expectedW, mutedStyle);
            GUILayout.EndHorizontal();
            panelScroll = GUILayout.BeginScrollView(panelScroll);
            int displayIndex = 0;
            foreach (var attack in panelAttacks)
            {
                var snapshot = attack.Snapshot;
                string identity = attack.Actor + " · " + (attack.Weapon ?? attack.Role);
                if (snapshot == null)
                { GUILayout.Label(identity + " — " + attack.Reason, rowStyle); manualComplete = false; continue; }
                bool observedTotals = snapshot.Input.Source.StartsWith("observed-");
                foreach (var row in snapshot.Rows)
                {
                    var scenario = Scenario(snapshot, row);
                    double? expected = scenario != null && scenario.Available ? scenario.Expected : (double?)null;
                    double? mean = expected.HasValue && row.Hit > 0 ? expected.Value / row.Hit : row.MeanOnHit;
                    GUILayout.BeginHorizontal(cardStyle);
                    TableCell("#" + (++displayIndex) + " " + identity, actorW);
                    TableCell((observedTotals ? "" : "~") + row.Hit.ToString("P0"), hitW, hitStyle);
                    TableCell(PanelPresentation.DiceLines(snapshot.Input), diceW, textStyle);
                    TableCell(PanelPresentation.Value(mean), meanW, numberStyle);
                    TableCell(PanelPresentation.Value(expected) + " / " + PanelPresentation.Value(row.ExpectedRaw), expectedW, numberStyle);
                    GUILayout.EndHorizontal();
                    if (settings.PanelDetails)
                        GUILayout.Label((observedTotals ? "관측" : "추정") + " AC " + row.AC + " · AB " + row.AB.ToString("+0;-0;0")
                            + " · " + AttackThreshold.Describe(row.AB, row.AC), rowStyle);
                    manualComplete &= expected.HasValue;
                    if (expected.HasValue) manualTotal += expected.Value;
                }
                if (settings.PanelDetails && snapshot.Input.AttackCountReasons.Count > 0)
                    GUILayout.Label("추가 공격: " + string.Join(", ", snapshot.Input.AttackCountReasons.ToArray()), rowStyle);
            }
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            GUILayout.Label("표시 공격 합계 (후/전)", rowStyle);
            GUILayout.Label((manualComplete ? PanelPresentation.Value(manualTotal) + " (수동)" : "—") + " / "
                + PanelPresentation.Value(PanelPresentation.TotalRaw(panelAttacks.Select(a => a.Snapshot))), numberStyle);
            if (GUILayout.Button(settings.PanelDetails ? "접기 " + settings.DetailsKey : "상세 " + settings.DetailsKey, GUILayout.Width(82))) settings.PanelDetails = !settings.PanelDetails;
            GUILayout.EndHorizontal();
            GUILayout.Label(manual ? "후=수동 방어 시나리오 · 실제 대상 방어 아님" : "후=DR·저항 반영(미확인 —) / 전=방어 전 · 평균은 치명 포함", mutedStyle);
            GUI.DragWindow(new Rect(0, 0, 10000, 28));
            if (!panelHeld && Event.current.type == EventType.Repaint) foreach (var attack in panelAttacks) if (attack.Snapshot != null) BetaRuntime.Shown(attack.Snapshot);
        }
    }
}
