using System;
using System.Linq;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.GameModes;
using Kingmaker.PubSubSystem;
using Kingmaker.View;
using TurnBased.Controllers;
using UnityEngine;

namespace CombatPreview
{
    partial class HoverChance : MonoBehaviour, IUnitDirectHoverUIHandler
    {
        static HoverChance instance;
        UnitEntityData hovered, m_Target, attacker;
        UnitEntityData highlightedFallback;
        float nextHoverScan;
        bool m_Full;
        float nextRefresh;
        BetaSnapshot panelSnapshot;
        string panelReason;
        string targetHealth;
        bool targetHealthKnown;
        long targetCurrentHP;
        int targetMaximumHP, targetTemporaryHP;
        string panelTurn;
        bool panelHeld;
        readonly PanelRetention retention = new PanelRetention();
        readonly List<PanelAttack> panelAttacks = new List<PanelAttack>();
        Rect panelScreenRect;
        sealed class PanelAttack
        {
            internal string Actor, Weapon, Role, Reason;
            internal UnitEntityData ActorUnit;
            internal string ActionText;
            internal bool Excluded;
            internal BetaSnapshot Snapshot;
            internal string DiceText, DefenseText;
        }
        static bool Key(string name, KeyCode fallback)
        {
            KeyCode code;
            if (!Enum.TryParse(name, true, out code) || code == KeyCode.None) code = fallback;
            return Input.GetKeyDown(code);
        }
        bool OverPanel()
        {
            var p = Input.mousePosition;
            return m_Target != null && panelScreenRect.Contains(new Vector2(p.x, Screen.height - p.y));
        }
        internal static void Create()
        {
            if (instance != null) return;
            var go = new GameObject("CombatPreviewPanel");
            try
            {
                DontDestroyOnLoad(go); instance = go.AddComponent<HoverChance>(); EventBus.Subscribe(instance);
            }
            catch
            {
                if (instance != null) EventBus.Unsubscribe(instance);
                Destroy(go); instance = null; throw;
            }
        }
        internal static void DestroyOwned()
        { if (instance != null) Destroy(instance.gameObject); }
        public void HandleHoverChange(UnitEntityView view, bool isHover)
        {
            if (view == null) return;
            if (isHover) hovered = view.EntityData;
            else if (hovered == view.EntityData) hovered = null;
            nextHoverScan = 0;
        }
        UnitEntityData HoveredTarget()
        {
            var highlighted = CombatFields.Read(Game.Instance?.TurnBasedCombatController?.CurrentTurn, "m_HighlightedUnit") as UnitEntityData;
            if (highlighted != null && highlighted.IsInGame && highlighted.IsVisibleForPlayer && !highlighted.State.IsDead)
                return highlighted;
            if (hovered?.View != null && hovered.View.MouseHighlighted) return hovered;
            if (Time.unscaledTime >= nextHoverScan)
            {
                highlightedFallback = Game.Instance.State.Units.FirstOrDefault(u => u.View != null && u.View.MouseHighlighted);
                nextHoverScan = Time.unscaledTime + .1f;
            }
            return highlightedFallback?.View != null && highlightedFallback.View.MouseHighlighted ? highlightedFallback : hovered;
        }
        void Update()
        { PreviewPerformance.Tick(); OverlayReference.Tick(); using (PreviewPerformance.Measure("preview-update")) UpdatePanel(); }
        void UpdatePanel()
        {
            try
            {
                var game = Game.Instance;
                if (!BetaRuntime.Enabled || game == null || !CombatController.IsInTurnBasedCombat()
                    || (game.CurrentMode != GameModeType.Default && game.CurrentMode != GameModeType.Pause))
                { Hide(); BetaRuntime.Invalidate(); return; }
                var turn = game.TurnBasedCombatController.CurrentTurn;
                var unit = CombatController.SelectedUnit;
                if (turn == null || unit == null || !unit.IsDirectlyControllable) { Hide(); BetaRuntime.Invalidate(); return; }
                // Warm/cache HUD bounds while the preview is active, before the
                // first enemy hover. OnGUI normally only reads the cached bounds.
                if (Main.Settings.AnchorLeft) PortraitTop();
                if (m_Target != null && Key(Main.Settings.DetailsKey, KeyCode.F6))
                {
                    Main.Settings.PanelDetails = !Main.Settings.PanelDetails;
                    if (panelSpellAbility != null) panelScroll = Vector2.zero;
                }
                if (SpellPreviewRuntime.SelectedAbility != null)
                {
                    UpdateSpellPanel(SpellPreviewRuntime.SelectedAbility, unit);
                    return;
                }
                if (panelSpellAbility != null) { Hide(); panelSpellAbility = null; }
                if (m_Target != null && Key(Main.Settings.PinKey, KeyCode.F7)) retention.TogglePin();
                long now = (long)(Time.unscaledTime * 1000);
                var hoveredTarget = HoveredTarget();
                bool oldValid = m_Target != null && m_Target.View != null && m_Target.IsVisibleForPlayer
                    && !m_Target.Descriptor.State.IsDead && game.State.Units.Contains(m_Target);
                bool keep = retention.Keep(BetaIdentity.Id(unit), BetaIdentity.Id(turn), BetaIdentity.Id(m_Target), oldValid, OverPanel(), now);
                bool worldEnemy = hoveredTarget != null && hoveredTarget.IsPlayersEnemy && hoveredTarget.IsVisibleForPlayer && !hoveredTarget.Descriptor.State.IsDead;
                var target = keep && (retention.Pinned || OverPanel() || !worldEnemy) ? m_Target : hoveredTarget;
                if (target == null || target.View == null || !target.IsPlayersEnemy || !target.IsVisibleForPlayer || target.Descriptor.State.IsDead)
                { Hide(); return; }
                bool held = keep && target == m_Target && (OverPanel() || hoveredTarget != m_Target);
                if (held && panelTurn == BetaIdentity.Id(turn) && attacker == unit)
                { panelHeld = true; return; }
                if (panelHeld) nextRefresh = 0;
                panelHeld = false;
                if (target == hoveredTarget && !OverPanel()) retention.Hover(BetaIdentity.Id(unit), BetaIdentity.Id(turn), BetaIdentity.Id(target), now);
                bool full = BetaReader.FullMode(unit);
                if (unit != attacker || target != m_Target || panelTurn != BetaIdentity.Id(turn) || full != m_Full || Time.unscaledTime >= nextRefresh)
                {
                    bool pairChanged = unit != attacker || target != m_Target || panelTurn != BetaIdentity.Id(turn);
                    using (PreviewPerformance.Measure(pairChanged ? "weapon-first-target" : "weapon-refresh"))
                    using (ScopedReadMemo<UnitEntityData, string>.Begin())
                    using (ScopedReadMemo<UnitEntityData, ActiveD20Reader.PartyRollSources>.Begin())
                    using (ScopedReadMemo<UnitEntityData, ActorWeaponEffects.SourceSnapshot>.Begin())
                    {
                    if (pairChanged) BetaRuntime.InvalidateDisplayed();
                    panelSnapshot = null; panelReason = null; panelAttacks.Clear();
                    attacker = unit; m_Target = target; m_Full = full;
                    panelTurn = BetaIdentity.Id(turn);
                    targetHealth = BetaReader.HealthLabel(target);
                    targetHealthKnown = BetaReader.ReadHealth(target, out targetCurrentHP, out targetMaximumHP, out targetTemporaryHP);
                    var mount = BetaReader.Mount(unit);
                    var rider = BetaReader.Rider(unit);
                    foreach (var actor in new[] { unit, mount ?? rider }.Where(a => a != null))
                    {
                        string role = actor == mount || actor == unit && rider != null ? "탈것" : mount != null || rider != null ? "기수" : "공격자";
                        var plan = BetaReader.AttackPlan(actor, target);
                        Main.Diagnostics?.ActionPlan(new DiagnosticContext(BetaIdentity.Id(actor), BetaIdentity.Id(target), "action-plan",
                            BetaIdentity.Id(turn), "unknown", -1, "", false), plan);
                        if (!plan.Available)
                        {
                            panelAttacks.Add(new PanelAttack { Actor = actor.CharacterName, Role = role, Reason = plan.Reason, Excluded = plan.Excluded });
                            continue;
                        }
                        bool actorFull = plan.FullAttack;
                        var weapons = BetaReader.Weapons(actor, actorFull).ToArray();
                        if (weapons.Length == 0) panelAttacks.Add(new PanelAttack { Actor = actor.CharacterName, Role = role, Reason = "이미 로드된 무기 정보가 없습니다." });
                        foreach (var weapon in weapons)
                        {
                            var entry = new PanelAttack { Actor = actor.CharacterName, ActorUnit = actor, Role = role, Weapon = weapon.Blueprint.Name, Excluded = plan.Excluded, Reason = plan.Excluded ? plan.Reason : null,
                                ActionText = plan.Reason };
                            try { entry.Snapshot = BetaRuntime.Capture(actor, target, actorFull, weapon); }
                            catch (NotSupportedException e) { entry.Reason = e.Message; }
                            if (entry.Snapshot != null) { entry.DiceText = PanelPresentation.DiceLines(entry.Snapshot.Input); entry.DefenseText = PanelPresentation.DefenseLabel(entry.Snapshot.Input); }
                            panelAttacks.Add(entry);
                        }
                    }
                    var availableAttacks = panelAttacks.Where(attack => attack.Snapshot != null && !attack.Excluded).ToArray();
                    var sequenced = BetaModel.BuildSequence(availableAttacks.Select(attack => attack.Snapshot).ToArray());
                    for (int index = 0; index < availableAttacks.Length; index++)
                    {
                        availableAttacks[index].Snapshot = sequenced[index];
                        OverlayReference.Bind(sequenced[index], availableAttacks[index].ActorUnit, target);
                    }
                    panelSnapshot = panelAttacks.Select(a => a.Snapshot).FirstOrDefault(snap => snap != null);
                    if (panelSnapshot == null) panelReason = "공격 정보를 읽을 수 없습니다. 각 공격자 상태를 확인하세요.";
                    CapturePanelHealthEvidence();
                    nextRefresh = Time.unscaledTime + .2f;
                    }
                }
            }
            catch (Exception e)
            {
                var ability = SpellPreviewRuntime.SelectedAbility;
                if (ability != null)
                {
                    BetaRuntime.InvalidateDisplayed();
                    ShowSpellWaiting(ability, "주문 정보 판독 오류 · " + e.GetType().Name);
                    nextRefresh = Time.unscaledTime + 2;
                    Main.Error("Spell panel waiting after failure", e);
                    return;
                }
                Hide(); BetaRuntime.Invalidate(); Main.Settings.ShowOnHover = false;
                Main.Error("Read-only beta preview disabled after failure", e);
            }
        }
        void Hide() { panelSpellAbility = null; ClearSpellWork(); spellRows.Clear(); panelSnapshot = null; panelReason = null; targetHealth = null; m_Target = null; attacker = null; nextRefresh = 0; panelAttacks.Clear(); retention.Clear(); panelHeld = false; HidePanelBlocker(); }
        internal static void Invalidate()
        { if (instance != null) { instance.Hide(); instance.hovered = null; } BetaRuntime.Invalidate(); }
        void OnDestroy()
        { PreviewPerformance.Flush("preview-destroyed"); DisposePanel(); EventBus.Unsubscribe(this); BetaRuntime.Invalidate(); if (instance == this) instance = null; }
    }
}
