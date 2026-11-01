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
        bool m_Full;
        float nextRefresh;
        BetaSnapshot panelSnapshot;
        string panelReason;
        string targetHealth;
        string panelTurn;
        bool panelHeld;
        readonly PanelRetention retention = new PanelRetention();
        readonly List<PanelAttack> panelAttacks = new List<PanelAttack>();
        Rect panelScreenRect;
        sealed class PanelAttack
        {
            internal string Actor, Weapon, Role, Reason;
            internal BetaSnapshot Snapshot;
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
        }
        void Update()
        {
            try
            {
                var game = Game.Instance;
                if (!BetaRuntime.Enabled || game == null || !CombatController.IsInTurnBasedCombat()
                    || (game.CurrentMode != GameModeType.Default && game.CurrentMode != GameModeType.Pause)
                    || game.SelectedAbilityHandler?.SelectedAbility != null)
                { Hide(); BetaRuntime.Invalidate(); return; }
                var turn = game.TurnBasedCombatController.CurrentTurn;
                var unit = CombatController.SelectedUnit;
                if (turn == null || unit == null || !unit.IsDirectlyControllable) { Hide(); BetaRuntime.Invalidate(); return; }
                if (m_Target != null && Key(Main.Settings.DetailsKey, KeyCode.F6)) Main.Settings.PanelDetails = !Main.Settings.PanelDetails;
                if (m_Target != null && Key(Main.Settings.PinKey, KeyCode.F7)) retention.TogglePin();
                long now = (long)(Time.unscaledTime * 1000);
                var hoveredTarget = game.State.Units.FirstOrDefault(u => u.View != null && u.View.MouseHighlighted) ?? hovered;
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
                    if (pairChanged) BetaRuntime.InvalidateDisplayed();
                    panelSnapshot = null; panelReason = null; panelAttacks.Clear();
                    attacker = unit; m_Target = target; m_Full = full;
                    panelTurn = BetaIdentity.Id(turn);
                    targetHealth = BetaReader.HealthLabel(target);
                    var mount = BetaReader.Mount(unit);
                    var rider = BetaReader.Rider(unit);
                    foreach (var actor in new[] { unit, mount ?? rider }.Where(a => a != null))
                    {
                        string role = actor == mount || actor == unit && rider != null ? "탈것" : mount != null || rider != null ? "기수" : "공격자";
                        var states = BetaReader.ActionState(actor);
                        if (states == null || states.IsOutOfRange || !states.IsAttack())
                        {
                            panelAttacks.Add(new PanelAttack { Actor = actor.CharacterName, Role = role, Reason = "현재 행동 상태에서 공격 불가 / 범위 미확인" });
                            continue;
                        }
                        bool actorFull = BetaReader.FullMode(actor);
                        var weapons = BetaReader.Weapons(actor, actorFull).ToArray();
                        if (weapons.Length == 0) panelAttacks.Add(new PanelAttack { Actor = actor.CharacterName, Role = role, Reason = "이미 로드된 무기 정보가 없습니다." });
                        foreach (var weapon in weapons)
                        {
                            var entry = new PanelAttack { Actor = actor.CharacterName, Role = role, Weapon = weapon.Blueprint.Name };
                            try { entry.Snapshot = BetaRuntime.Capture(actor, target, actorFull, weapon); }
                            catch (NotSupportedException e) { entry.Reason = e.Message; }
                            panelAttacks.Add(entry);
                        }
                    }
                    panelSnapshot = panelAttacks.Select(a => a.Snapshot).FirstOrDefault(snap => snap != null);
                    if (panelSnapshot == null) panelReason = "공격 정보를 읽을 수 없습니다. 각 공격자 상태를 확인하세요.";
                    nextRefresh = Time.unscaledTime + .2f;
                }
            }
            catch (Exception e)
            {
                Hide(); BetaRuntime.Invalidate(); Main.Settings.ShowOnHover = false;
                Main.Error("Read-only beta preview disabled after failure", e);
            }
        }
        void Hide() { panelSnapshot = null; panelReason = null; targetHealth = null; m_Target = null; attacker = null; nextRefresh = 0; panelAttacks.Clear(); retention.Clear(); panelHeld = false; HidePanelBlocker(); }
        internal static void Invalidate()
        { if (instance != null) { instance.Hide(); instance.hovered = null; } BetaRuntime.Invalidate(); }
        void OnDestroy()
        { DisposePanel(); EventBus.Unsubscribe(this); BetaRuntime.Invalidate(); if (instance == this) instance = null; }
    }
}
