using System;
using System.Collections.Generic;
using HarmonyLib;
using Kingmaker.PubSubSystem;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;

namespace CombatPreview
{
    // Rolls predicted at the very start of the rule, before any handler has added its bonuses to it: saving throws,
    // spell resistance and attack rolls not made by an attack with a weapon (touch and ray spells). Checked against the
    // real roll after it.
#if WOTR
    // WotR calls hooks before the handlers of every rule (RulebookEventBus.Hooks), subscribed with the event bus.
    class EarlyPredictionsHook : IRulebookEventAboutToTriggerHook
    {
        public void OnBeforeEventAboutToTrigger(RulebookEvent rule) => EarlyPredictions.Prefix(rule);
    }
#else
    [HarmonyPatch(typeof(RulebookEventBus), nameof(RulebookEventBus.OnEventAboutToTrigger))]
#endif
    static class EarlyPredictions
    {
        static readonly Dictionary<RulebookEvent, object> s_Pending = new Dictionary<RulebookEvent, object>();

        internal static void Prefix(RulebookEvent evt)
        {
            if (DryRun.Active || !Main.Settings.Logging) return;
            Cost.RuleStart();
            try
            {
                var save = evt as RuleSavingThrow;
                if (save != null)
                {
                    if (!save.Initiator.Descriptor.State.IsDead)
#if WOTR
                        // A copy of the rule as it was made (DC, success bonus, evasion and the rest).
                        s_Pending[evt] = SpellChance.Save(new RuleSavingThrow(save.Initiator, save) { Reason = save.Reason });
#else
                        s_Pending[evt] = SpellChance.Save(save.Initiator, save.Type, save.DifficultyClass, save.Reason);
#endif
                    return;
                }
                var sr = evt as RuleSpellResistanceCheck;
                if (sr != null)
                {
                    if (sr.Context != null && sr.Initiator != null) s_Pending[evt] = SpellChance.SpellResistance(sr.Context, sr.Target);
                    return;
                }
                var maneuver = evt as RuleCombatManeuver;
                if (maneuver != null)
                {
                    if (maneuver.Initiator != null && maneuver.Target != null) s_Pending[evt] = ManeuverChance.Predict(ManeuverChance.Copy(maneuver));
                    return;
                }
                var roll = evt as RuleAttackRoll;
                if (roll != null && roll.RuleAttackWithWeapon == null && roll.Weapon != null && roll.Target != null)
                    s_Pending[evt] = AttackChance.Predict(roll.Initiator, roll.Target, roll.Weapon, roll.AttackBonusPenalty, null, roll.Reason);
            }
            catch (Exception e)
            {
                Main.Error("early prediction", e);
            }
            finally
            {
                Cost.RuleEnd();
            }
        }

        internal static object Take(RulebookEvent evt)
        {
            object p;
            if (!s_Pending.TryGetValue(evt, out p)) return null;
            s_Pending.Remove(evt);
            return p;
        }
    }

    class SpellChecks : IGlobalRulebookHandler<RuleSavingThrow>, IGlobalRulebookHandler<RuleSpellResistanceCheck>,
        IGlobalRulebookHandler<RuleCombatManeuver>
    {
        public void OnEventAboutToTrigger(RuleCombatManeuver evt)
        {
        }

        public void OnEventDidTrigger(RuleCombatManeuver evt)
        {
            var p = EarlyPredictions.Take(evt) as ManeuverPrediction;
            if (p == null || !Main.Settings.Logging) return;
            try
            {
                int die = GameRules.Die(evt);
                bool mismatch = die > 0 && (evt.InitiatorCMB != p.CMB || evt.TargetCMD != p.CMD);
                var sb = new System.Text.StringBuilder();
                sb.AppendFormat("{0} -> {1} maneuver [{2}]: {3}\n    real: {4}", AttackCheck.Name(evt.Initiator), AttackCheck.Name(evt.Target),
                    AttackCheck.ReasonName(evt), p, evt.Success ? "success" : "failed");
                if (die > 0) sb.AppendFormat(", d20 {0} + {1} vs CMD {2}", die, evt.InitiatorCMB, evt.TargetCMD);
                if (mismatch) sb.Append("\n    <<< MISMATCH");
                ChanceLog.Write(sb.ToString());
            }
            catch (Exception e)
            {
                Main.Error("maneuver check", e);
            }
        }

        public void OnEventAboutToTrigger(RuleSavingThrow evt)
        {
        }

        public void OnEventDidTrigger(RuleSavingThrow evt)
        {
            var p = EarlyPredictions.Take(evt) as SavePrediction;
            if (p == null || !Main.Settings.Logging) return;
            try
            {
                bool rolled = evt.D20 != null;
                bool mismatch = rolled && evt.StatValue != p.Stat;
                var sb = new System.Text.StringBuilder();
                sb.AppendFormat("{0} save [{1}]: {2}\n    {3} {4} = {5}", AttackCheck.Name(evt.Initiator), AttackCheck.ReasonName(evt), p,
                    evt.StatType, p.Stat, Breakdown.Format(p.Parts));
                sb.AppendFormat("\n    real: {0}", evt.IsPassed ? "passed" : "failed");
                if (rolled) sb.AppendFormat(", d20 {0} + {1}{2} vs DC {3}", (int)evt.D20, evt.StatValue,
                    evt.RequiresSuccessBonus ? string.Format(" {0:+0;-0}", evt.SuccessBonus) : "", evt.DifficultyClass);
                if (evt.AutoPass) sb.Append(" (auto pass)");
                if (mismatch)
                {
                    var stat = evt.Initiator.Stats.GetStat(evt.StatType);
                    sb.AppendFormat("\n    <<< MISMATCH\n    real {0} {1} (after the roll, the roll's own bonuses gone) = {2}", evt.StatType, evt.StatValue,
                        Breakdown.Format(Breakdown.Save(stat, evt.StatValue)));
                }
                ChanceLog.Write(sb.ToString());
            }
            catch (Exception e)
            {
                Main.Error("save check", e);
            }
        }

        public void OnEventAboutToTrigger(RuleSpellResistanceCheck evt)
        {
        }

        public void OnEventDidTrigger(RuleSpellResistanceCheck evt)
        {
            var p = EarlyPredictions.Take(evt) as SpellResistancePrediction;
            if (p == null || !Main.Settings.Logging) return;
            try
            {
                bool mismatch = evt.TargetIsImmune != p.Immune || (!evt.TargetIsImmune && !evt.IgnoreSpellResistance
                    && (evt.SpellResistance != p.SpellResistance || evt.SpellPenetration != p.Penetration));
                var sb = new System.Text.StringBuilder();
                sb.AppendFormat("{0} -> {1} spell resistance [{2}]: {3}\n    real: {4}", AttackCheck.Name(evt.Initiator), AttackCheck.Name(evt.Target),
                    AttackCheck.ReasonName(evt), p, evt.IsSpellResisted ? "resisted" : "through");
                if (evt.HasResistanceRoll && evt.Roll != null)
                    sb.AppendFormat(", SR {0} vs d20 {1} + {2}", evt.SpellResistance, (int)evt.Roll, evt.SpellPenetration);
                if (evt.TargetIsImmune) sb.Append(", immune");
                if (mismatch) sb.Append("\n    <<< MISMATCH");
                ChanceLog.Write(sb.ToString());
            }
            catch (Exception e)
            {
                Main.Error("spell resistance check", e);
            }
        }
    }
}

namespace CombatPreview
{
    // What the mod costs the game: the time of its per-frame work and of its prefix on every rule, written to the log
    // every 30 seconds while the log is on.
    static class Cost
    {
        static readonly System.Diagnostics.Stopwatch s_Frame = new System.Diagnostics.Stopwatch();
        static readonly System.Diagnostics.Stopwatch s_Rule = new System.Diagnostics.Stopwatch();
        static double s_FrameTotal, s_FrameMax, s_RuleTotal;
        static int s_Frames, s_Rules;
        static float s_Next;

        internal static void FrameStart() => s_Frame.Restart();

        internal static void FrameEnd()
        {
            double ms = s_Frame.Elapsed.TotalMilliseconds;
            s_FrameTotal += ms;
            if (ms > s_FrameMax) s_FrameMax = ms;
            s_Frames++;
            float now = UnityEngine.Time.unscaledTime;
            if (now < s_Next) return;
            if (s_Next > 0 && Main.Settings.LogAttacks)
                ChanceLog.Write(string.Format("cost: {0} frames, mod {1:0.000} ms a frame on average, {2:0.00} ms at most; rule prefix {3} calls, {4:0.0} ms in all",
                    s_Frames, s_FrameTotal / Math.Max(1, s_Frames), s_FrameMax, s_Rules, s_RuleTotal));
            s_Next = now + 30;
            s_FrameTotal = s_FrameMax = s_RuleTotal = 0;
            s_Frames = s_Rules = 0;
        }

        internal static void RuleStart() => s_Rule.Restart();

        internal static void RuleEnd()
        {
            s_RuleTotal += s_Rule.Elapsed.TotalMilliseconds;
            s_Rules++;
        }
    }
}
