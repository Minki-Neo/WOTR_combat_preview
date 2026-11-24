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
    internal sealed partial class PassiveAttackDiagnostics : IGlobalRulebookHandler<RuleAttackRoll>, IGlobalRulebookHandler<RuleDealDamage>
    {
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly DiagnosticRecorder recorder;
        bool active;
        readonly HashSet<string> loggedPreviews = new HashSet<string>();
        readonly Dictionary<string, string> loggedActionPlans = new Dictionary<string, string>();
        internal void ActionPlan(DiagnosticContext context, AttackActionPlan plan)
        {
            if (!CanObserve() || !Main.Settings.CompareBeta) return;
            string key = context.Attacker + "|" + context.Target + "|" + context.Turn;
            string stamp = plan.Input.Stamp + "|" + plan.Reason;
            string previous;
            if (loggedActionPlans.TryGetValue(key, out previous) && previous == stamp) return;
            if (loggedActionPlans.Count > 32) loggedActionPlans.Clear();
            if (recorder.RecordActionPlan(context, plan)) loggedActionPlans[key] = stamp;
        }
        sealed class LinkedPreview { internal BetaComparison Comparison; internal long Time; }
        readonly Dictionary<string, LinkedPreview> damagePreviews = new Dictionary<string, LinkedPreview>();
        internal PassiveAttackDiagnostics(string path)
        {
            var log = new LocalDiagnosticLog(path); recorder = new DiagnosticRecorder(log.Write, () => clock.ElapsedMilliseconds);
            recorder.SetRuntimeMetadata(new Dictionary<string, string> {
                { "modVersion", typeof(Main).Assembly.GetName().Version.ToString() },
                { "modAssemblyMvid", typeof(Main).Module.ModuleVersionId.ToString() },
                { "nativeHealthIntegration", "Removed" },
                { "healthProjectionModel", "target-defense-then-temporary-then-main;weapon-mean-approximation;spell-distribution-first" },
                { "diagnosticFeatures", "completed-ruling;frozen-display;health-projection;performance-window-v1;command-rule-row-link;frozen-begin-inputs;unavailable-source" },
                { "gameVersion", Compatibility.GameVersion },
                { "gameAssemblyMvid", typeof(RuleAttackRoll).Module.ModuleVersionId.ToString() },
                { "ummVersion", typeof(UnityModManagerNet.UnityModManager).Assembly.GetName().Version.ToString() },
                { "harmonyVersion", typeof(HarmonyLib.Harmony).Assembly.GetName().Version.ToString() },
                { "tttAssemblyDetected", Compatibility.HasTabletopTweaks.ToString() }
            });
        }
        internal bool PerformanceWindow(Dictionary<string, object> report)
        {
            if (!active && !CanObserve()) return false;
            return recorder.RecordPerformanceWindow(report);
        }
        internal void Reset() { recorder.Enabled = false; active = false; loggedPreviews.Clear(); spellPreviewLoggedAt.Clear(); recentSpellPreviews.Clear(); spellRefreshCount = 0; loggedActionPlans.Clear(); damagePreviews.Clear(); nextReferenceLog.Clear(); }
        sealed class LoggedSpellPreview
        {
            internal string Id, InputStamp;
            internal FrozenDiagnosticJson RawSnapshot;
            internal bool LoggedSeparately;
            internal long Time;
            internal SpellCheckRecord[] Checks;
        }
        readonly Dictionary<string, LoggedSpellPreview> recentSpellPreviews = new Dictionary<string, LoggedSpellPreview>();
        long nextSpellPreviewId;
        static string SpellPreviewKey(string caster, string target, string ability, string turn)
            => caster + "|" + target + "|" + ability + "|" + turn;
        readonly Dictionary<string, float> spellPreviewLoggedAt = new Dictionary<string, float>();
        float spellRefreshWindow;
        int spellRefreshCount;

        internal void SpellShown(DiagnosticContext context, string ability, List<string> checks, bool observedTargets,
            List<SpellCheckRecord> checkDetails = null, Dictionary<string, object> metadata = null)
        {
            if (!CanObserve() || !Main.Settings.CompareBeta) return;
            string previewId = "spell-" + (++nextSpellPreviewId).ToString(System.Globalization.CultureInfo.InvariantCulture);
            metadata = metadata ?? new Dictionary<string, object>();
            metadata["previewId"] = previewId;
            var raw = metadata.Where(pair => pair.Key != "hcoReference" && pair.Key != "outcomeTable").ToDictionary(pair => pair.Key, pair => pair.Value);
            raw["checks"] = checkDetails?.Select(check => check.Data()).ToArray();
            raw["attackerId"] = context.Attacker; raw["targetId"] = context.Target; raw["abilityInstanceId"] = context.Weapon;
            var latest = new LoggedSpellPreview { Id = previewId, Time = clock.ElapsedMilliseconds, Checks = checkDetails?.ToArray(),
                InputStamp = metadata.TryGetValue("inputStamp", out var inputStamp) ? inputStamp as string : null };
            using (PreviewPerformance.Measure("raw-displayed-snapshot")) latest.RawSnapshot = FrozenDiagnosticJson.Capture(raw);
            if (recentSpellPreviews.Count >= 128) recentSpellPreviews.Clear();
            recentSpellPreviews[SpellPreviewKey(context.Attacker, context.Target, context.Weapon, context.Turn)] = latest;
            string referenceKey = metadata != null && metadata.ContainsKey("hcoReference") ? OverlayReference.LogKey((Dictionary<string, object>)metadata["hcoReference"]) : "";
            string healthKey = metadata != null && metadata.TryGetValue("healthProjection", out var health) ? DamageTrace.Json(health) : "";
            string key = "spell|" + context.Slot + "|" + observedTargets + "|" + string.Join(";", checks) + "|" + referenceKey + "|" + healthKey;
            // Refresh damage evidence while aiming even if the displayed values stay the same.
            bool hasDamage = metadata != null && metadata.TryGetValue("spellDamage", out var payload)
                && payload is IDictionary<string, object> damage && damage.TryGetValue("terms", out var terms)
                && terms is List<Dictionary<string, object>> entries && entries.Count > 0;
            float now = UnityEngine.Time.unscaledTime;
            bool refresh = spellPreviewLoggedAt.TryGetValue(key, out float previous);
            if (refresh)
            {
                if (!hasDamage || now >= previous && now - previous < 1.5f) return;
                if (now < spellRefreshWindow || now - spellRefreshWindow >= 1)
                {
                    spellRefreshWindow = now;
                    spellRefreshCount = 0;
                }
                if (spellRefreshCount >= 4) return;
            }
            if (spellPreviewLoggedAt.Count >= 256 && !spellPreviewLoggedAt.ContainsKey(key)) spellPreviewLoggedAt.Clear();
            if (recorder.RecordSpellPreview(context, ability, checks, observedTargets, checkDetails, metadata))
            {
                latest.LoggedSeparately = true;
                spellPreviewLoggedAt[key] = now;
                if (refresh) spellRefreshCount++;
            }
        }
        readonly Dictionary<string, float> nextReferenceLog = new Dictionary<string, float>();
        internal void PreviewShown(BetaSnapshot snapshot)
        {
            if (!CanObserve() || snapshot == null || !Main.Settings.CompareBeta) return;
            float next;
            if (nextReferenceLog.TryGetValue(snapshot.Id, out next) && UnityEngine.Time.unscaledTime < next) return;
            if (nextReferenceLog.Count > 128) nextReferenceLog.Clear();
            nextReferenceLog[snapshot.Id] = UnityEngine.Time.unscaledTime + .15f;
            var reference = OverlayReference.Weapon(snapshot);
            var s = Main.Settings;
            bool manual = s.ManualScenario && s.ScenarioConfirmed;
            string key = snapshot.Id + "|" + manual + "|" + s.ScenarioDR + "|" + s.ScenarioBypassDR + "|" + s.ScenarioPhysicalImmune + "|" + OverlayReference.LogKey(reference);
            if (loggedPreviews.Contains(key)) return;
            if (loggedPreviews.Count > 256) loggedPreviews.Clear();
            if (recorder.RecordBetaPreview(snapshot, manual ? DefenseReader.Scenario(s) : null,
                PassiveRuleResults.Installed ? "Installed" : "Unavailable", BetaRuntime.ObservedAB, BetaRuntime.ObservedAC, BetaRuntime.ObservedCounts, BetaRuntime.ObservedBaseAB, BetaRuntime.ObservedWeaponStats, BetaRuntime.NativeCacheStatus, reference)) loggedPreviews.Add(key);
        }
        internal string Status { get { return recorder.Faulted ? "Stopped after a file write failure"
            : "Dropped by rate limit: " + recorder.Dropped + " | Ignored simulated events: " + recorder.IgnoredSimulated; } }
        bool CanObserve()
        {
            if (OverlayReference.Active) return false;
            bool enabled = Main.Settings != null && Main.Settings.Enabled
                && (Main.Settings.LocalDiagnostics || (Main.Settings.ShowOnHover && Main.Settings.CompareBeta));
            if (!enabled) { if (active) Reset(); return false; }
            if (!active) { recorder.Enabled = true; active = true; }
            return !recorder.Faulted;
        }
        string Id(object value)
        { return BetaIdentity.Id(value); }
        public void OnEventAboutToTrigger(RuleAttackRoll evt) { if (evt?.IsFake != true) CaptureSpellInput(evt, evt?.Target); }
        public void OnEventDidTrigger(RuleAttackRoll evt)
        {
            if (OverlayReference.Active) return;
            if (evt != null && evt.IsFake)
            {
                // Read an estimate the game already made, without initiating or
                // replaying it. Keep it out of all actual-attack/damage counts.
                SpellPreviewRuntime.ObserveAttackEstimate(evt);
                BetaRuntime.ObserveGameEstimate(evt);
                if (CanObserve()) recorder.IgnoreSimulated();
                return;
            }
            if (!CanObserve()) return;
            try
            {
                if (evt == null || evt.Initiator == null || evt.Target == null || DryRun.Active) return;
                if (evt.IsFake) { recorder.IgnoreSimulated(); return; }
                ObserveSpellAttack(evt);
                var context = Context(evt);
                var comparison = BetaRuntime.After(evt);
                var rawInputs = CompletedAttackEvidence.Capture(evt);
                rawInputs["displayedInputs"] = DisplayedCombatEvidence.Capture(comparison);
                PruneDamagePreviews();
                if (comparison != null) damagePreviews[Id(evt)] = new LinkedPreview { Comparison = comparison, Time = clock.ElapsedMilliseconds };
                // This observer does not know the complete condition revision or
                // applied damage. Leave them absent, never label them comparable.
                recorder.Observe(Id(evt), new ActualAttackObservation(context, evt.AttackBonus, evt.TargetAC,
                    GameRules.Die(evt) > 0, evt.IsHit, penalty: evt.AttackBonusPenalty,
                    fullAttack: evt.RuleAttackWithWeapon?.IsFullAttack, criticalConfirmed: evt.IsCriticalConfirmed,
                    hasWeaponAttack: evt.RuleAttackWithWeapon != null, visibility: AttackConcealment.Observed(evt), rawInputs: rawInputs),
                    comparison == null ? null : comparison.Preview.Id);
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
        public void OnEventAboutToTrigger(RuleDealDamage evt)
        {
            if (evt == null || evt.IsFake || evt.AttackRoll?.IsFake == true || DryRun.Active || OverlayReference.Active || !CanObserve()) return;
            CaptureSpellInput(evt, evt.Target);
        }
        Dictionary<string, object> CaptureDamageTrace(RuleDealDamage evt, BetaComparison comparison)
        {
            var trace = CompletedDamageTrace.Capture(evt, comparison);
            trace["displayedInputs"] = DisplayedCombatEvidence.Capture(comparison);
            trace["defenseAtCompletion"] = CompletedAttackEvidence.Defense(evt.Target);
            trace["capturePolicy"] = "Completed actual ruling only; no intermediate defense snapshots";
            return trace;
        }
        void PruneDamagePreviews()
        {
            long now = clock.ElapsedMilliseconds;
            foreach (var key in damagePreviews.Where(p => now < p.Value.Time || now - p.Value.Time > 10000).Select(p => p.Key).ToArray()) damagePreviews.Remove(key);
            if (damagePreviews.Count >= 128) damagePreviews.Remove(damagePreviews.OrderBy(p => p.Value.Time).First().Key);
        }
        public void OnEventDidTrigger(RuleDealDamage evt)
        { using (PreviewPerformance.Measure("damage-diagnostics")) ObserveDamage(evt); }
        void ObserveDamage(RuleDealDamage evt)
        {
            if (!CanObserve()) return;
            try
            {
                if (evt == null || DryRun.Active) return;
                var roll = evt.AttackRoll;
                if (evt.IsFake || roll?.IsFake == true) { recorder.IgnoreSimulated(); return; }
                if (roll?.RuleAttackWithWeapon == null) ObserveSpellDamage(evt);
                if (roll?.Initiator == null || roll.Target == null) return;
                var context = Context(roll, evt.Initiator, evt.Target);
                var defense = DefenseReader.Read(evt.Target, context.Target);
                PruneDamagePreviews();
                LinkedPreview linked;
                damagePreviews.TryGetValue(Id(roll), out linked);
                var comparison = evt.Target == roll.Target && evt.Initiator == roll.Initiator ? linked?.Comparison : null;
                Dictionary<string, object> trace;
                try { trace = CaptureDamageTrace(evt, comparison); }
                catch (Exception traceError)
                {
                    // Optional evidence must not take down existing AB/AC diagnostics.
                    trace = new Dictionary<string, object> { { "traceVersion", 1 }, { "fullReplayVerified", false },
                        { "missing", new[] { "Damage trace capture failed: " + traceError.GetType().Name } } };
                }
                recorder.RecordDamage(Id(evt), Id(roll), context, evt.Result, evt.RawResult, evt.DamageBeforeDifficulty,
                    "Incomplete cached defense evidence; see defenseMissing/defenseObserved", defense.Missing, defense.ObservedDetails,
                    roll.RuleAttackWithWeapon != null, trace);
            }
            catch (Exception e)
            {
                Reset(); Main.Settings.LocalDiagnostics = false; Main.Settings.CompareBeta = false; BetaRuntime.Invalidate();
                Main.Error("Passive damage diagnostics disabled", e);
            }
        }
    }
}
