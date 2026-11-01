using System;
using System.Linq;
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
                // Poll the existing highlight as a fallback; no setter patch is installed.
                var target = game.State.Units.FirstOrDefault(u => u.View != null && u.View.MouseHighlighted) ?? hovered;
                if (target == null || target.View == null || !target.IsPlayersEnemy || !target.IsVisibleForPlayer || target.Descriptor.State.IsDead)
                { Hide(); return; }
                var turn = game.TurnBasedCombatController.CurrentTurn;
                var unit = CombatController.SelectedUnit;
                // These read the turn controller's existing cached action state.
                var states = turn != null && unit != null ? turn.GetActionsStates(unit) : null;
                if (unit == null || !unit.IsDirectlyControllable || states == null || states.IsOutOfRange || !states.IsAttack())
                { Hide(); return; }
                bool full = turn.GetEnabledFullAttack(unit);
                if (unit != attacker || target != m_Target || full != m_Full || Time.unscaledTime >= nextRefresh)
                {
                    panelSnapshot = null; panelReason = null;
                    attacker = unit; m_Target = target; m_Full = full;
                    targetHealth = BetaReader.HealthLabel(target);
                    try { panelSnapshot = BetaRuntime.Capture(unit, target, full); }
                    catch (NotSupportedException e) { panelReason = e.Message; }
                    nextRefresh = Time.unscaledTime + .2f;
                }
            }
            catch (Exception e)
            {
                Hide(); BetaRuntime.Invalidate(); Main.Settings.ShowOnHover = false;
                Main.Error("Read-only beta preview disabled after failure", e);
            }
        }
        void Hide() { panelSnapshot = null; panelReason = null; targetHealth = null; m_Target = null; attacker = null; nextRefresh = 0; }
        internal static void Invalidate()
        { if (instance != null) { instance.Hide(); instance.hovered = null; } BetaRuntime.Invalidate(); }
        void OnDestroy()
        { DisposePanel(); EventBus.Unsubscribe(this); BetaRuntime.Invalidate(); if (instance == this) instance = null; }
    }
}
