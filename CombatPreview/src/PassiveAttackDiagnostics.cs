using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Linq;
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
        sealed class Identity { public long Value; }
        readonly ConditionalWeakTable<object, Identity> identities = new ConditionalWeakTable<object, Identity>();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly DiagnosticRecorder recorder;
        long nextId;
        bool active;
        internal PassiveAttackDiagnostics(string path)
        { var log = new LocalDiagnosticLog(path); recorder = new DiagnosticRecorder(log.Write, () => clock.ElapsedMilliseconds); }
        internal void Reset() { recorder.Enabled = false; active = false; }
        internal string Status { get { return recorder.Faulted ? "Stopped after a file write failure" : "Dropped by rate limit: " + recorder.Dropped; } }
        bool CanObserve()
        {
            bool enabled = Main.Settings != null && Main.Settings.Enabled && Main.Settings.LocalDiagnostics;
            if (!enabled) { if (active) Reset(); return false; }
            if (!active) { recorder.Enabled = true; active = true; }
            return !recorder.Faulted;
        }
        string Id(object value)
        { return value == null ? "unknown" : identities.GetValue(value, v => new Identity { Value = Interlocked.Increment(ref nextId) }).Value.ToString(System.Globalization.CultureInfo.InvariantCulture); }
        public void OnEventAboutToTrigger(RuleAttackRoll evt) { }
        public void OnEventDidTrigger(RuleAttackRoll evt)
        {
            if (!CanObserve()) return;
            try
            {
                if (evt == null || evt.Initiator == null || evt.Target == null || DryRun.Active) return;
                var context = Context(evt);
                // This observer does not know the complete condition revision or
                // applied damage. Leave them absent, never label them comparable.
                recorder.Observe(Id(evt), new ActualAttackObservation(context, evt.AttackBonus, evt.TargetAC,
                    GameRules.Die(evt) > 0, evt.IsHit, penalty: evt.AttackBonusPenalty,
                    fullAttack: evt.RuleAttackWithWeapon?.IsFullAttack, criticalConfirmed: evt.IsCriticalConfirmed));
            }
            catch (Exception e)
            {
                Reset(); Main.Settings.LocalDiagnostics = false;
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
                if (roll == null || roll.Initiator == null || roll.Target == null || evt.IsFake || DryRun.Active) return;
                var context = Context(roll, evt.Initiator, evt.Target);
                var defense = DefenseReader.Read(evt.Target, context.Target);
                string evidence = string.Join("; ", defense.Missing.Concat(defense.ObservedDetails).ToArray());
                recorder.RecordDamage(Id(evt), Id(roll), context, evt.Result, evt.RawResult, evt.DamageBeforeDifficulty, evidence);
            }
            catch (Exception e)
            {
                Reset(); Main.Settings.LocalDiagnostics = false;
                Main.Error("Passive damage diagnostics disabled", e);
            }
        }
    }
}
