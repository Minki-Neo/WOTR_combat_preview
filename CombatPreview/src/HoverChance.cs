using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.GameModes;
using Kingmaker.PubSubSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.View;
using TurnBased.Controllers;
using UnityEngine;

namespace CombatPreview
{
    // Reuses the upstream event/API selection and EnumerateFullAttack approach.
    // UI refresh is throttled; unsupported selected abilities hide the weapon panel.
    partial class HoverChance : MonoBehaviour, IUnitDirectHoverUIHandler
    {
        static HoverChance instance;
        UnitEntityData hovered, m_Target, attacker;
        bool m_Full;
        int panelFrame = -1;
        internal static void Create()
        {
            var go = new GameObject("CombatPreviewPanel");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<HoverChance>();
            EventBus.Subscribe(instance);
        }
        public void HandleHoverChange(UnitEntityView view, bool isHover)
        {
            if (view == null) return;
            if (isHover) hovered = view.EntityData;
            else if (hovered == view.EntityData) hovered = null;
        }
        [HarmonyPatch(typeof(UnitEntityView), nameof(UnitEntityView.MouseHighlighted), MethodType.Setter)]
        static class Highlighted
        {
            static void Postfix(UnitEntityView __instance)
            { if (instance != null) instance.HandleHoverChange(__instance, __instance.MouseHighlighted); }
        }
        void Update()
        {
            try
            {
                var game = Game.Instance;
                var target = hovered;
                if (!Compatibility.CanPreview || !Main.Settings.Enabled || !Main.Settings.ShowOnHover || game == null
                    || !CombatController.IsInTurnBasedCombat()
                    || (game.CurrentMode != GameModeType.Default && game.CurrentMode != GameModeType.Pause)
                    || target == null || target.View == null || !target.IsPlayersEnemy
                    || !target.IsVisibleForPlayer || target.Descriptor.State.IsDead
                    || game.SelectedAbilityHandler?.SelectedAbility != null)
                { Hide(); return; }
                var turn = game.TurnBasedCombatController.CurrentTurn;
                var unit = CombatController.SelectedUnit;
                var states = turn != null && unit != null ? turn.GetActionsStates(unit) : null;
                if (unit == null || !unit.IsDirectlyControllable || states == null || states.IsOutOfRange || !states.IsAttack())
                { Hide(); return; }
                bool full = turn.GetEnabledFullAttack(unit);
                // Without a complete condition revision, never reuse a cached row.
                if (panelFrame != Time.frameCount)
                {
                    panelRows = null;
                    attacker = unit; m_Target = target; m_Full = full;
                    panelRows = Series(unit, target, full);
                    panelFrame = Time.frameCount;
                }
            }
            catch (Exception e)
            {
                Hide(); Main.Settings.ShowOnHover = false;
                Main.Error("Preview disabled after failure", e);
            }
        }
        void Hide() { panelRows = null; m_Target = null; attacker = null; panelFrame = -1; }
        internal static void Invalidate() { if (instance != null) { instance.Hide(); instance.hovered = null; } }
        void OnDestroy() { EventBus.Unsubscribe(this); if (instance == this) instance = null; }

        internal static List<AttackPrediction> Series(UnitEntityData unit, UnitEntityData target, bool full)
        {
            PredictionSafety.RequireReadOnlyPrediction();
            var results = new List<AttackPrediction>();
            // The game supplies haste, off-hand, iterative and natural attack ordering.
            foreach (var entry in UnitAttack.EnumerateFullAttack(unit))
            {
                var weapon = entry.Hand?.MaybeWeapon;
                if (weapon == null) throw new NotSupportedException("Unresolved weapon in attack enumeration; entire series unavailable");
                var attack = new RuleAttackWithWeapon(unit, target, weapon, entry.AttackBonusPenalty)
                { IsFullAttack = full, IsFirstAttack = results.Count == 0, AttackNumber = results.Count };
                var prediction = AttackChance.Predict(unit, target, weapon, entry.AttackBonusPenalty, attack);
                string[] omissions = { "Sneak attack and conditional on-hit effects excluded", "Mount attacks excluded",
                    "Frozen state: mirror images, buffs and target death do not evolve" };
                prediction.RawReport = new PredictionReport(PredictionQuality.PartialEstimate, prediction.Expected, "RAW before defense", omissions);
                var defense = DefenseReader.Read(target);
                prediction.TargetMitigated = MitigationMath.Calculate(prediction.DamageTerms, prediction.CriticalMultiplier,
                    prediction.Hit, prediction.Crit, defense.ToProfile());
                prediction.TargetReport = new PredictionReport(prediction.TargetMitigated.Available ? PredictionQuality.PartialEstimate : PredictionQuality.Unavailable,
                    prediction.TargetMitigated.Available ? (double?)prediction.TargetMitigated.Expected : null,
                    "TARGET | " + defense.Source, defense.Missing.Concat(omissions).ToArray());
                if (Main.Settings.ManualScenario && Main.Settings.ScenarioConfirmed)
                {
                    prediction.ScenarioMitigated = MitigationMath.Calculate(prediction.DamageTerms, prediction.CriticalMultiplier,
                        prediction.Hit, prediction.Crit, DefenseReader.Scenario(Main.Settings));
                    prediction.ScenarioReport = new PredictionReport(prediction.ScenarioMitigated.Available ? PredictionQuality.PartialEstimate : PredictionQuality.Unavailable,
                        prediction.ScenarioMitigated.Available ? (double?)prediction.ScenarioMitigated.Expected : null,
                        "MANUAL SCENARIO (not target defenses)", omissions.Concat(new[] { prediction.ScenarioMitigated.Reason }).ToArray());
                }
                results.Add(prediction);
                if (!full) break;
            }
            return results;
        }
    }
}
