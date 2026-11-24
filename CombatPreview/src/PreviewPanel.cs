using System;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace CombatPreview
{
    partial class HoverChance
    {
        Vector2 panelScroll;
        GUIStyle panelStyle, titleStyle, textStyle, mutedStyle, numberStyle, hitStyle, cardStyle, rowStyle, healthStyle;
        Texture2D panelTexture, cardTexture;
        float panelWidth, panelHeight, nextBarSearch;
        RectTransform actionBar;
        RectTransform portraitBar;
        RectTransform portraitLeftBar;
        int portraitScreenWidth, portraitScreenHeight;
        float nextPortraitSearch;
        float portraitLeft;
        GameObject inputBlocker;
        RectTransform blockerRect;
        readonly Vector3[] barCorners = new Vector3[4];
        internal static readonly Type[] PortraitTypes = {
            typeof(Kingmaker.UI.MVVM._PCView.Party.UnitPortraitPartView),
            typeof(Kingmaker.UI.MVVM._PCView.Party.PartyCharacterPCView)
        };
        static readonly List<RectTransform> registeredPortraits = new List<RectTransform>();
        internal static void RegisterPortrait(Component __instance)
        {
            var rect = __instance?.transform as RectTransform;
            if (rect == null) return;
            registeredPortraits.RemoveAll(entry => entry == null);
            if (!registeredPortraits.Contains(rect) && registeredPortraits.Count < 64) registeredPortraits.Add(rect);
            if (instance != null) instance.nextPortraitSearch = 0;
        }
        static IEnumerable<RectTransform> PortraitCandidates()
        {
            registeredPortraits.RemoveAll(entry => entry == null);
            if (registeredPortraits.Count > 0)
            {
                foreach (var rect in registeredPortraits) yield return rect;
                yield break;
            }
            // Fallback for enabling the mod after the HUD was already bound.
            foreach (var type in PortraitTypes)
                foreach (var obj in UnityEngine.Object.FindObjectsOfType(type))
                    if (obj is Component component && component.transform is RectTransform rect)
                    { RegisterPortrait(component); yield return rect; }
        }
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
            textStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true, richText = false, margin = new RectOffset(0, 0, 0, 0) };
            textStyle.normal.textColor = Ink;
            textStyle.fixedHeight = 0;
            textStyle.padding = new RectOffset(0, 0, 3, 5);
            textStyle.clipping = TextClipping.Overflow;
            titleStyle = new GUIStyle(textStyle) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            titleStyle.normal.textColor = Gold;
            mutedStyle = new GUIStyle(textStyle) { fontSize = 13 };
            mutedStyle.normal.textColor = new Color(.68f, .66f, .62f);
            numberStyle = new GUIStyle(textStyle) { fontSize = 16, fontStyle = FontStyle.Bold };
            numberStyle.normal.textColor = new Color(.87f, .75f, .51f);
            healthStyle = new GUIStyle(numberStyle) { alignment = TextAnchor.MiddleCenter, padding = new RectOffset() };
            healthStyle.normal.textColor = Color.white;
            hitStyle = new GUIStyle(numberStyle);
            hitStyle.fontSize = 19;
            hitStyle.alignment = TextAnchor.MiddleCenter;
            hitStyle.normal.textColor = new Color(.65f, .80f, .55f);
            panelStyle = new GUIStyle { padding = new RectOffset(12, 12, 8, 8) };
            panelStyle.normal.background = panelTexture;
            panelStyle.onNormal.background = panelTexture;
            cardStyle = new GUIStyle { padding = new RectOffset(6, 6, 4, 4), margin = new RectOffset(0, 0, 1, 1) };
            cardStyle.normal.background = cardTexture;
            rowStyle = new GUIStyle(textStyle) { fontSize = 15, wordWrap = false, clipping = TextClipping.Clip, alignment = TextAnchor.MiddleLeft };
        }
        void DisposePanel()
        { if (panelTexture != null) Destroy(panelTexture); if (cardTexture != null) Destroy(cardTexture); if (inputBlocker != null) Destroy(inputBlocker); }
        void HidePanelBlocker() { if (inputBlocker != null && inputBlocker.activeSelf) inputBlocker.SetActive(false); }
        void UpdatePanelBlocker(Rect rect)
        {
            var position = new Vector2(rect.x, Screen.height - rect.yMax);
            var size = new Vector2(rect.width, rect.height);
            if (inputBlocker != null && inputBlocker.activeSelf && blockerRect.anchoredPosition == position && blockerRect.sizeDelta == size) return;
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
            blockerRect.anchoredPosition = position;
            blockerRect.sizeDelta = size;
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
        float PortraitTop()
        {
            float cachedTop;
            if (portraitScreenWidth == Screen.width && portraitScreenHeight == Screen.height
                && portraitBar != null && ReadPortraitTop(portraitBar, out cachedTop))
            {
                portraitLeft = Screen.width * .30f;
                float unused;
                if (portraitLeftBar != null && ReadPortraitTop(portraitLeftBar, out unused))
                {
                    var canvas = portraitLeftBar.GetComponentInParent<Canvas>();
                    var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                    portraitLeft = Mathf.Min(portraitLeft, RectTransformUtility.WorldToScreenPoint(camera, barCorners[0]).x);
                }
                return cachedTop;
            }
            if (!ReferenceEquals(portraitBar, null) || portraitScreenWidth != Screen.width || portraitScreenHeight != Screen.height)
                nextPortraitSearch = 0;
            if (Time.unscaledTime >= nextPortraitSearch)
            {
                using (PreviewPerformance.Measure("ui-portrait-search"))
                {
                nextPortraitSearch = Time.unscaledTime + 10;
                portraitBar = portraitLeftBar = null;
                portraitScreenWidth = Screen.width; portraitScreenHeight = Screen.height;
                portraitLeft = Screen.width * .30f;
                float highest = Screen.height;
                foreach (var rect in PortraitCandidates())
                {
                        float top;
                        if (rect != null && ReadPortraitTop(rect, out top))
                        {
                            var canvas = rect.GetComponentInParent<Canvas>();
                            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                            var left = RectTransformUtility.WorldToScreenPoint(camera, barCorners[0]).x;
                            if (left > Screen.width * .15f && left <= portraitLeft) { portraitLeft = left; portraitLeftBar = rect; }
                            if (top < highest) { portraitBar = rect; highest = top; }
                        }
                }
                }
            }
            float value;
            return portraitBar != null && ReadPortraitTop(portraitBar, out value) ? value : Screen.height * .88f;
        }
        bool ReadPortraitTop(RectTransform rect, out float top)
        {
            top = 0;
            if (!rect.gameObject.activeInHierarchy) return false;
            var canvas = rect.GetComponentInParent<Canvas>();
            if (canvas == null) return false;
            rect.GetWorldCorners(barCorners);
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var a = RectTransformUtility.WorldToScreenPoint(camera, barCorners[0]);
            var b = RectTransformUtility.WorldToScreenPoint(camera, barCorners[2]);
            top = Screen.height - Mathf.Max(a.y, b.y);
            return Mathf.Abs(b.x - a.x) >= 24 && Mathf.Abs(b.y - a.y) >= 24
                && Mathf.Abs(b.y - a.y) < Screen.height * .4f && top > Screen.height * .5f
                && top < Screen.height - 24 && Mathf.Max(a.x, b.x) > 0 && Mathf.Min(a.x, b.x) < Screen.width;
        }
        void OnGUI()
        { using (PreviewPerformance.Measure("panel-OnGUI")) DrawUI(); }
        void DrawUI()
        {
            var settings = Main.Settings;
            if (!BetaRuntime.Enabled || settings == null) { HidePanelBlocker(); return; }
            using (PreviewPerformance.Measure("ui-style-init")) EnsurePanelStyles();
            if (BetaRuntime.AlertVisible)
                GUI.Box(new Rect(Screen.width * .5f - 330, 35, 660, 52), BetaRuntime.Alert, cardStyle);
            if (panelSpellAbility == null && (m_Target == null || panelSnapshot == null && string.IsNullOrEmpty(panelReason))) { HidePanelBlocker(); return; }
            float scale = Mathf.Clamp(settings.Scale, .7f, 1.6f);
            var old = GUI.matrix;
            try
            {
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));
                float sw = Screen.width / scale, sh = Screen.height / scale;
                panelWidth = Mathf.Min(Mathf.Clamp(settings.Width, 620, 1100), sw - 12);
                int rows = Math.Max(1, panelAttacks.Sum(a => a.Snapshot?.Rows.Count ?? 1));
                int components = Math.Max(1, panelAttacks.Select(a => a.Snapshot?.Input.Components.Count ?? 1).DefaultIfEmpty(1).Max());
                float portraitTop = settings.AnchorLeft ? Mathf.Min(PortraitTop(), Screen.height * .88f) / scale : sh * .70f;
                if (settings.AnchorLeft) panelWidth = Mathf.Min(panelWidth, Mathf.Max(240, portraitLeft / scale - 52));
                float height = panelSpellAbility != null ? SpellPanelHeight(settings.PanelDetails) : PanelPresentation.CompactHeight(rows, components, settings.PanelDetails, sh);
                height += panelSpellAbility == null ? panelAttacks.Count * 74 : 0;
                if (panelSpellAbility == null) height += 62;
                height = Mathf.Min(height, Mathf.Min(480, sh - 24));
                if (settings.AnchorLeft) height = Mathf.Min(height, Mathf.Max(120, portraitTop - 24));
                panelHeight = height;
                float x = settings.X, y = settings.Y;
                if (settings.AnchorLeft) PanelPresentation.PlaceLeft(sw, sh, panelWidth, height, portraitTop, out x, out y);
                else if (settings.AnchorAboveActionBar)
                    PanelPresentation.Place(sw, sh, panelWidth, height, ActionBarTop() / scale, out x, out y);
                var rect = new Rect(Mathf.Clamp(x, 0, Mathf.Max(0, sw - panelWidth)), Mathf.Clamp(y, 0, Mathf.Max(0, sh - height)), panelWidth, height);
                Rect moved;
                using (PreviewPerformance.Measure(Event.current.type == EventType.Layout ? "ui-window-layout" : Event.current.type == EventType.Repaint ? "ui-window-repaint" : "ui-window-input"))
                    moved = GUI.Window(739261, rect, DrawPanel, "", panelStyle);
                if (Mathf.Abs(moved.x - rect.x) > .1f || Mathf.Abs(moved.y - rect.y) > .1f)
                { settings.AnchorAboveActionBar = false; settings.AnchorLeft = false; }
                settings.X = moved.x; settings.Y = moved.y;
                panelScreenRect = new Rect(moved.x * scale, moved.y * scale, moved.width * scale, moved.height * scale);
                using (PreviewPerformance.Measure("ui-input-blocker")) UpdatePanelBlocker(panelScreenRect);
            }
            finally { GUI.matrix = old; }
        }
        void TableCell(string value, float width, GUIStyle style = null)
        {
            var cell = style ?? rowStyle;
            var key = new CellKey { Text = value, Style = cell, Width = width };
            CellDisplay display;
            if (!cellDisplays.TryGetValue(key, out display))
            {
                if (cellDisplays.Count >= 512) cellDisplays.Clear();
                var content = new GUIContent(value);
                display = new CellDisplay { Content = content, Options = new[] { GUILayout.Width(width),
                    GUILayout.MinHeight(Mathf.Max(cell.fontSize + 12, cell.CalcHeight(content, width) + 4)) } };
                cellDisplays[key] = display;
            }
            GUILayout.Label(display.Content, cell, display.Options);
        }
        struct CellKey : IEquatable<CellKey>
        {
            internal string Text; internal GUIStyle Style; internal float Width;
            public bool Equals(CellKey other) { return Text == other.Text && ReferenceEquals(Style, other.Style) && Width == other.Width; }
            public override bool Equals(object obj) { return obj is CellKey && Equals((CellKey)obj); }
            public override int GetHashCode() { unchecked { return ((Text == null ? 0 : Text.GetHashCode()) * 397 ^ Style.GetHashCode()) * 397 ^ Width.GetHashCode(); } }
        }
        sealed class CellDisplay { internal GUIContent Content; internal GUILayoutOption[] Options; }
        readonly Dictionary<CellKey, CellDisplay> cellDisplays = new Dictionary<CellKey, CellDisplay>();
        sealed class RowDisplay
        {
            internal string Hit, AB, AC, Mean, Expected;
        }
        readonly Dictionary<BetaRow, RowDisplay> rowDisplays = new Dictionary<BetaRow, RowDisplay>();
        RowDisplay PresentedRow(BetaSnapshot snapshot, BetaRow row, MitigatedResult scenario)
        {
            RowDisplay display;
            bool manual = Main.Settings.ManualScenario && Main.Settings.ScenarioConfirmed;
            if (!manual && rowDisplays.TryGetValue(row, out display)) return display;
            double? expected = scenario != null && scenario.Available ? scenario.Expected : (double?)null;
            double? mean = expected.HasValue && row.Hit > 0 ? expected.Value / row.Hit : row.MeanOnHit;
            display = new RowDisplay {
                Hit = (snapshot.Input.PairObserved && snapshot.Input.ConcealmentMiss == 0 && snapshot.Input.MirrorImages == 0 ? "" : "~") + row.Hit.ToString("P0"),
                AB = (snapshot.Input.AttackObserved ? "" : "~") + row.AB.ToString("+0;-0;0"),
                AC = (snapshot.Input.ACObserved ? "" : "~") + row.AC,
                Mean = (scenario?.Approximate == true ? "≈" : "") + PanelPresentation.Value(mean),
                Expected = (scenario?.Approximate == true ? "≈" : "") + PanelPresentation.Value(expected) + " / " + PanelPresentation.Value(row.ExpectedRaw)
            };
            if (!row.RollProbabilityAvailable) { display.Hit = "—"; display.Expected = "— / —"; }
            if (!manual) { if (rowDisplays.Count >= 256) rowDisplays.Clear(); rowDisplays[row] = display; }
            return display;
        }
        MitigatedResult Scenario(BetaSnapshot snapshot, BetaRow row)
        {
            if (!Main.Settings.ManualScenario || !Main.Settings.ScenarioConfirmed) return row.AfterDefense;
            if (!snapshot.Input.DamageKnown || !row.RollProbabilityAvailable) return null;
            return MitigationMath.Calculate(snapshot.Input.Components.Select(c => c.ToTerm()).ToArray(), snapshot.Input.Multiplier,
                row.Hit, row.Crit, DefenseReader.Scenario(Main.Settings));
        }
        bool ExpectedHealthDamage(out double expected)
        {
            expected = 0;
            bool any = false;
            if (!targetHealthKnown || targetMaximumHP <= 0 || panelAttacks.Count == 0
                || (Main.Settings.ManualScenario && Main.Settings.ScenarioConfirmed)) return false;
            foreach (var attack in panelAttacks)
            {
                if (attack.Excluded) continue;
                if (attack.Snapshot == null) return false;
                any = true;
                foreach (var row in attack.Snapshot.Rows)
                {
                    if (row.AfterDefense == null || !row.AfterDefense.Available) return false;
                    expected += row.AfterDefense.Expected;
                }
            }
            return any && !double.IsNaN(expected) && !double.IsInfinity(expected) && expected >= 0;
        }
        void CapturePanelHealthEvidence()
        {
            using (PreviewPerformance.Measure("weapon-health-evidence")) CapturePanelHealthEvidenceCore();
        }
        void CapturePanelHealthEvidenceCore()
        {
            if (Main.Settings.CompareBeta != true && Main.Settings.LocalDiagnostics != true) return;
            bool complete = ExpectedHealthDamage(out double expected);
            bool manual = Main.Settings.ManualScenario && Main.Settings.ScenarioConfirmed;
            var loss = targetHealthKnown && complete ? HealthProjectionSelection.MeanLoss(targetCurrentHP, targetMaximumHP, targetTemporaryHP, expected) : null;
            var tempLoss = targetHealthKnown && complete ? HealthProjectionSelection.MeanTemporaryLoss(targetTemporaryHP, expected) : null;
            var data = new Dictionary<string, object> {
                { "currentHp", targetHealthKnown ? (object)targetCurrentHP : null },
                { "maximumHp", targetHealthKnown ? (object)targetMaximumHP : null },
                { "temporaryHp", targetHealthKnown ? (object)targetTemporaryHP : null },
                { "expectedHpLoss", loss }, { "expectedTemporaryHpLoss", tempLoss },
                { "expectedAfterDefense", complete ? (object)expected : null },
                { "status", manual ? "ManualScenario" : !targetHealthKnown ? "HealthUnavailable" : complete ? "Computed" : "DamageUnavailable" },
                { "basis", "mean-state-hp-display" }, { "approximate", true },
                { "absorptionOrder", "target-defenses-then-temporary-hp-then-main-hp" },
                { "computedFrame", Time.frameCount }, { "nativeHealthIntegration", "Removed" },
                { "previewIds", panelAttacks.Where(a => !a.Excluded && a.Snapshot != null).Select(a => a.Snapshot.Id).ToArray() },
                { "unavailable", panelAttacks.Where(a => !a.Excluded && a.Snapshot == null).Select(a => a.Reason ?? "AttackSnapshotUnavailable")
                    .Concat(panelAttacks.Where(a => !a.Excluded && a.Snapshot != null).SelectMany(a => a.Snapshot.Rows)
                        .Where(row => row.AfterDefense?.Available != true).Select(row => row.AfterDefense?.Reason ?? "DefenseUnavailable"))
                    .Distinct().Take(32).ToArray() },
                { "excludedAttacks", panelAttacks.Where(a => a.Excluded).Select(a => a.Reason).Take(32).ToArray() },
                { "scope", "Whole displayed attack sequence, including rider/mount; not the loss of a single observed hit. Mean-state approximation, not E[capped damage]." }
            };
            var frozen = FrozenDiagnosticJson.Capture(data);
            foreach (var attack in panelAttacks) if (attack.Snapshot != null) attack.Snapshot.DisplayedHealth = frozen;
        }
        void DrawHealthProjection()
        {
            if (!targetHealthKnown) { GUILayout.Label("체력 막대: 확인 불가", mutedStyle); return; }
            double expected;
            bool complete = ExpectedHealthDamage(out expected);
            bool manual = Main.Settings.ManualScenario && Main.Settings.ScenarioConfirmed;
            bool project = complete && !manual;
            double current = Math.Min(targetMaximumHP, Math.Max(0, targetCurrentHP));
            double loss = HealthProjectionSelection.MeanLoss(targetCurrentHP, targetMaximumHP, targetTemporaryHP,
                project ? expected : (double?)null).GetValueOrDefault();
            double? temporaryLoss = HealthProjectionSelection.MeanTemporaryLoss(targetTemporaryHP, project ? expected : (double?)null);
            DrawHealthBar(targetCurrentHP, targetMaximumHP, loss, 26, targetTemporaryHP, temporaryLoss);
            string caption = panelAttacks.Count > 0 && panelAttacks.All(a => a.Excluded) ? "현재 실행 가능한 무기 공격 없음 · 제외 사유는 아래 표시"
                : project ? (targetTemporaryHP > 0 ? "≈ 임시 HP −" + temporaryLoss.GetValueOrDefault().ToString("0.##") + " · 본체 HP −" : "노랑: 기댓값 기준 감소 ")
                    + loss.ToString("0.##") + " HP · 잔여 약 " + Math.Max(0, current - loss).ToString("0.##") + " (확정 아님)"
                : targetTemporaryHP > 0 ? "파랑: 임시 HP " + targetTemporaryHP + " · 방어 후 기댓값 미확인"
                : manual ? "수동 시나리오 · 실제 대상 체력 감소 표시 보류" : "방어 후 기댓값 미확인 · 예상 감소 표시 보류";
            GUILayout.Label(caption, mutedStyle);
            if (panelAttacks.Any(attack => attack.Snapshot?.Rows.Any(row => row.AfterDefense?.Approximate == true) == true))
                GUILayout.Label("≈ 보호막 근사 · 평균 피해와 평균 잔량 기준", mutedStyle);
        }

        void DrawHealthBar(long currentHp, int maximumHp, double loss, float height, int temporaryHp = 0, double? temporaryLoss = null)
        {
            double current = Math.Min(maximumHp, Math.Max(0, currentHp));
            double temporary = Math.Max(0, temporaryHp);
            double total = Math.Max(1, (double)maximumHp + temporary);
            loss = Math.Min(current, Math.Max(0, loss));
            double tempLoss = Math.Min(temporary, Math.Max(0, temporaryLoss.GetValueOrDefault()));
            var bar = GUILayoutUtility.GetRect(10, height, GUILayout.ExpandWidth(true));
            var oldColor = GUI.color;
            try
            {
                GUI.color = new Color(.18f, .17f, .17f); GUI.DrawTexture(bar, Texture2D.whiteTexture);
                Fill(0, current, new Color(.22f, .43f, .29f));
                Fill(maximumHp, temporary, new Color(.20f, .53f, .78f));
                Forecast(current, loss);
                Forecast(maximumHp + temporary, tempLoss);
            }
            finally { GUI.color = oldColor; }
            GUI.Label(bar, "HP " + currentHp + " / " + maximumHp + (temporary > 0 ? "  + 임시 " + temporaryHp : ""), healthStyle);

            void Fill(double start, double amount, Color color)
            {
                if (amount <= 0) return;
                GUI.color = color;
                GUI.DrawTexture(new Rect(bar.x + (float)(bar.width * start / total), bar.y,
                    (float)(bar.width * amount / total), bar.height), Texture2D.whiteTexture);
            }
            void Forecast(double end, double amount)
            {
                if (amount <= 0) return;
                float endPixel = (float)(bar.width * end / total);
                // A small visible marker stays inside its own HP segment.
                float available = (float)(bar.width * (end == current ? current : temporary) / total);
                float pixels = PanelPresentation.HealthLossPixels((float)(bar.width * amount / total), available);
                GUI.color = new Color(.94f, .65f, .18f);
                GUI.DrawTexture(new Rect(bar.x + endPixel - pixels, bar.y, pixels, bar.height), Texture2D.whiteTexture);
            }
        }
        void DrawPanel(int id)
        {
            using (PreviewPerformance.Measure(Event.current.type == EventType.Layout ? "ui-content-layout" : Event.current.type == EventType.Repaint ? "ui-content-repaint" : "ui-content-input"))
                DrawPanelContent(id);
        }
        void DrawPanelContent(int id)
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
            if (panelSpellAbility != null) { DrawSpellPanel(); return; }
            var settings = Main.Settings;
            GUILayout.Label("전투 미리보기 · " + m_Target.CharacterName, titleStyle);
            DrawHealthProjection();
            GUILayout.Label((panelHeld ? "보관 중 · 대상 위로 돌아가면 갱신" : retention.Pinned ? "고정 중" : settings.PinKey + " 고정") + "  |  " + settings.DetailsKey + " 상세  |  ~ 기본 추정", mutedStyle);
            if (panelSnapshot?.SequenceScope != null) GUILayout.Label("거울상·보호막 소모: 표시된 공격 순서 기준", mutedStyle);
            if (panelSnapshot?.Rows.Any(row => row.AfterDefense?.Reason == "readonly-clustered-physical-DR") == true)
                GUILayout.Label("응집 사격: 같은 대상의 전력 공격에 DR 누적 1회", mutedStyle);
            bool manual = settings.ManualScenario && settings.ScenarioConfirmed;
            double manualTotal = 0; bool manualComplete = true;
            float available = Mathf.Max(120, panelWidth - 56);
            float actorW = available * .08f, hitW = available * .14f, abW = available * .12f, acW = available * .08f,
                diceW = available * .26f, meanW = available * .12f, expectedW = available * .20f;
            GUILayout.BeginHorizontal();
            TableCell("타격", actorW, mutedStyle);
            TableCell("명중 보정", abW, mutedStyle);
            TableCell("적 AC", acW, mutedStyle);
            TableCell("피해 주사위", diceW, mutedStyle);
            TableCell("명중 평균", meanW, mutedStyle);
            TableCell("기댓값 후/전", expectedW, mutedStyle);
            TableCell("명중률", hitW, mutedStyle);
            GUILayout.EndHorizontal();
            panelScroll = GUILayout.BeginScrollView(panelScroll);
            int displayIndex = 0;
            foreach (var attack in panelAttacks)
            {
                var snapshot = attack.Snapshot;
                string identity = attack.Actor + " · " + (attack.Weapon ?? attack.Role);
                GUILayout.Label(identity, textStyle, GUILayout.MinHeight(30));
                if (snapshot != null) GUILayout.Label((attack.DefenseText ?? "DR 미확인")
                    + (snapshot.Input.MirrorImages > 0 ? " · 거울상 " + snapshot.Input.MirrorImages + "개" : ""), mutedStyle);
                if (snapshot != null) GUILayout.Label(attack.ActionText ?? "행동 정보 미확인", mutedStyle);
                if (snapshot == null)
                { GUILayout.Label((attack.Excluded ? "합계 제외 · " : "") + attack.Reason, textStyle); if (!attack.Excluded) manualComplete = false; continue; }
                bool observedTotals = snapshot.Input.AttackObserved && snapshot.Input.PairObserved;
                foreach (var row in snapshot.Rows)
                {
                    var scenario = Scenario(snapshot, row);
                    double? expected = scenario != null && scenario.Available ? scenario.Expected : (double?)null;
                    double? mean = expected.HasValue && row.Hit > 0 ? expected.Value / row.Hit : row.MeanOnHit;
                    var presented = PresentedRow(snapshot, row, scenario);
                    GUILayout.BeginHorizontal(cardStyle);
                    TableCell("#" + (++displayIndex), actorW);
                    TableCell(presented.AB, abW, numberStyle);
                    TableCell(presented.AC, acW, numberStyle);
                    TableCell(attack.DiceText ?? "—", diceW, textStyle);
                    TableCell(presented.Mean, meanW, numberStyle);
                    TableCell(presented.Expected, expectedW, numberStyle);
                    TableCell(presented.Hit, hitW, hitStyle);
                    GUILayout.EndHorizontal();
                    if (settings.PanelDetails)
                        GUILayout.Label((snapshot.Input.ACObserved ? "게임 AC " : "추정 AC ") + row.AC + " · " + (snapshot.Input.AttackObserved ? "게임 AB " : "추정 AB ") + row.AB.ToString("+0;-0;0")
                            + " · " + AttackThreshold.Describe(row.AB, row.AC)
                            + " · 빗나감 " + SpellLethalPreview.Percent(snapshot.Input.ConcealmentMiss), rowStyle);
                    if (!attack.Excluded)
                    {
                        manualComplete &= expected.HasValue;
                        if (expected.HasValue) manualTotal += expected.Value;
                    }
                }
                if (settings.PanelDetails)
                {
                    if (snapshot.Input.AttackCountReasons.Count > 0) GUILayout.Label("추가 공격: " + string.Join(", ", snapshot.Input.AttackCountReasons.ToArray()), textStyle);
                    GUILayout.Label("계산 출처: " + snapshot.Input.Source, mutedStyle);
                    foreach (var note in snapshot.Input.ModifierNotes) GUILayout.Label(note, textStyle);
                    foreach (var note in snapshot.Input.DefenseNotes) GUILayout.Label(note, textStyle);
                    var unavailable = snapshot.Rows.FirstOrDefault(r => r.AfterDefense == null || !r.AfterDefense.Available);
                    if (unavailable != null) GUILayout.Label("방어 후 미확인: " + (unavailable.AfterDefense?.Reason ?? "피해 구성 미확인"), textStyle);
                    var approximate = snapshot.Rows.FirstOrDefault(row => row.AfterDefense?.Approximate == true);
                    if (approximate != null) GUILayout.Label("≈ " + approximate.AfterDefense.ApproximationAssumptions, textStyle);
                }
            }
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            GUILayout.Label("실행 가능 공격 합계 (후/전)", rowStyle);
            double? rawTotal = panelAttacks.Count > 0 && panelAttacks.All(a => a.Excluded) ? 0
                : PanelPresentation.TotalRaw(panelAttacks.Where(a => !a.Excluded).Select(a => a.Snapshot));
            bool approximateTotal = !manual && panelAttacks.Any(attack => attack.Snapshot?.Rows.Any(row => row.AfterDefense?.Approximate == true) == true);
            GUILayout.Label((manualComplete ? (approximateTotal ? "≈" : "") + PanelPresentation.Value(manualTotal) + (manual ? " (수동)" : "") : "—") + " / "
                + PanelPresentation.Value(rawTotal), numberStyle);
            if (GUILayout.Button(settings.PanelDetails ? "접기 " + settings.DetailsKey : "상세 " + settings.DetailsKey, GUILayout.Width(82))) settings.PanelDetails = !settings.PanelDetails;
            GUILayout.EndHorizontal();
            GUILayout.Label(manual ? "후=수동 방어 시나리오 · 실제 대상 방어 아님" : "후=지원 DR·저항·면역 반영 / 전=방어 전 · 특수 방어 미확인 — · 부분 추정", mutedStyle);
            GUI.DragWindow(new Rect(0, 0, 10000, 28));
            if (!panelHeld && Event.current.type == EventType.Repaint) foreach (var attack in panelAttacks) if (attack.Snapshot != null && !attack.Excluded) BetaRuntime.Shown(attack.Snapshot);
        }
    }
}
