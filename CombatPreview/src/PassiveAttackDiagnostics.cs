using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Linq;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.PubSubSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.RuleSystem.Rules.Damage;

namespace CombatPreview
{
    // Subscribed explicitly. Observes game-created rolls only. No prediction,
    // Rulebook.Trigger, rule construction, reduction application or RNG access.
    internal sealed class PassiveAttackDiagnostics : IGlobalRulebookHandler<RuleAttackRoll>, IGlobalRulebookHandler<RuleDealDamage>
    {
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly DiagnosticRecorder recorder;
        bool active;
        string lastPreview;
        internal PassiveAttackDiagnostics(string path)
        {
            var log = new LocalDiagnosticLog(path); recorder = new DiagnosticRecorder(log.Write, () => clock.ElapsedMilliseconds);
            recorder.SetRuntimeMetadata(new Dictionary<string, string> {
                { "modVersion", typeof(Main).Assembly.GetName().Version.ToString() },
                { "gameVersion", Compatibility.GameVersion },
                { "gameAssemblyMvid", typeof(RuleAttackRoll).Module.ModuleVersionId.ToString() },
                { "ummVersion", typeof(UnityModManagerNet.UnityModManager).Assembly.GetName().Version.ToString() },
                { "harmonyVersion", typeof(HarmonyLib.Harmony).Assembly.GetName().Version.ToString() },
                { "tttAssemblyDetected", Compatibility.HasTabletopTweaks.ToString() }
            });
        }
        internal void Reset() { recorder.Enabled = false; active = false; lastPreview = null; }
        internal void PreviewShown(BetaSnapshot snapshot)
        {
            if (!CanObserve() || snapshot == null || !Main.Settings.CompareBeta) return;
            var s = Main.Settings;
            bool manual = s.ManualScenario && s.ScenarioConfirmed;
            string key = snapshot.Id + "|" + manual + "|" + s.ScenarioDR + "|" + s.ScenarioBypassDR + "|" + s.ScenarioPhysicalImmune;
            if (lastPreview == key) return;
            if (recorder.RecordBetaPreview(snapshot, manual ? DefenseReader.Scenario(s) : null)) lastPreview = key;
        }
        internal string Status { get { return recorder.Faulted ? "Stopped after a file write failure"
            : "Dropped by rate limit: " + recorder.Dropped + " | Ignored simulated events: " + recorder.IgnoredSimulated; } }
        bool CanObserve()
        {
            bool enabled = Main.Settings != null && Main.Settings.Enabled
                && (Main.Settings.LocalDiagnostics || (Main.Settings.ShowOnHover && Main.Settings.CompareBeta));
            if (!enabled) { if (active) Reset(); return false; }
            if (!active) { recorder.Enabled = true; active = true; }
            return !recorder.Faulted;
        }
        string Id(object value)
        { return BetaIdentity.Id(value); }
        public void OnEventAboutToTrigger(RuleAttackRoll evt) { }
        public void OnEventDidTrigger(RuleAttackRoll evt)
        {
            if (evt != null && evt.IsFake)
            {
                // Read an estimate the game already made, without initiating or
                // replaying it. Keep it out of all actual-attack/damage counts.
                BetaRuntime.ObserveGameEstimate(evt);
                if (CanObserve()) recorder.IgnoreSimulated();
                return;
            }
            if (!CanObserve()) return;
            try
            {
                if (evt == null || evt.Initiator == null || evt.Target == null || DryRun.Active) return;
                if (evt.IsFake) { recorder.IgnoreSimulated(); return; }
                var context = Context(evt);
                var comparison = BetaRuntime.After(evt);
                // This observer does not know the complete condition revision or
                // applied damage. Leave them absent, never label them comparable.
                recorder.Observe(Id(evt), new ActualAttackObservation(context, evt.AttackBonus, evt.TargetAC,
                    GameRules.Die(evt) > 0, evt.IsHit, penalty: evt.AttackBonusPenalty,
                    fullAttack: evt.RuleAttackWithWeapon?.IsFullAttack, criticalConfirmed: evt.IsCriticalConfirmed,
                    hasWeaponAttack: evt.RuleAttackWithWeapon != null), comparison == null ? null : comparison.Preview.Id);
                if (comparison != null) recorder.RecordBetaComparison(comparison, context, Id(evt));
            }
            catch (Exception e)
            {
                Reset(); Main.Settings.LocalDiagnostics = false; Main.Settings.CompareBeta = false; BetaRuntime.Invalidate();
                Main.Error("Passive diagnostics disabled", e);
            }
        }
        DiagnosticContext Context(RuleAttackRoll evt, Kingmaker.EntitySystem.Entities.UnitEntityData actualAttacker = null,
            Kingmaker.EntitySystem.Entities.UnitEntityData actualTarget = null)
        {
            var attack = evt.RuleAttackWithWeapon;
            var turn = Game.Instance?.TurnBasedCombatController?.CurrentTurn;
            var initiator = actualAttacker ?? evt.Initiator;
            var target = actualTarget ?? evt.Target;
            return new DiagnosticContext(Id(initiator) + ":" + initiator.CharacterName,
                Id(target) + ":" + target.CharacterName, Id(evt.Weapon) + ":" + evt.Weapon?.Blueprint.Name,
                Id(turn), "unknown", attack == null ? -1 : attack.AttackNumber, "", false);
        }
        public void OnEventAboutToTrigger(RuleDealDamage evt) { }
        public void OnEventDidTrigger(RuleDealDamage evt)
        {
            if (!CanObserve()) return;
            try
            {
                // Only already-completed weapon-associated events. No RuleDealDamage
                // is constructed or invoked here, including for a diagnostic test.
                var roll = evt?.AttackRoll;
                if (roll == null || roll.Initiator == null || roll.Target == null || DryRun.Active) return;
                if (evt.IsFake || roll.IsFake) { recorder.IgnoreSimulated(); return; }
                var context = Context(roll, evt.Initiator, evt.Target);
                var defense = DefenseReader.Read(evt.Target, context.Target);
                recorder.RecordDamage(Id(evt), Id(roll), context, evt.Result, evt.RawResult, evt.DamageBeforeDifficulty,
                    "Incomplete cached defense evidence; see defenseMissing/defenseObserved", defense.Missing, defense.ObservedDetails,
                    roll.RuleAttackWithWeapon != null);
            }
            catch (Exception e)
            {
                Reset(); Main.Settings.LocalDiagnostics = false; Main.Settings.CompareBeta = false; BetaRuntime.Invalidate();
                Main.Error("Passive damage diagnostics disabled", e);
            }
        }
    }
}
