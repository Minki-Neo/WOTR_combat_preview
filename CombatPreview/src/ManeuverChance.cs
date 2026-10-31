using System;
using System.Collections.Generic;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Enums;
using Kingmaker.Items;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Class.Kineticist;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Parts;

namespace CombatPreview
{
    // The chance of a combat maneuver: the attacker's CMB and a d20 against the target's CMD, as RuleCombatManeuver.OnTrigger
    // decides it, with the bonuses of a dry run.
    class ManeuverPrediction
    {
        public CombatManeuver Type;
        public bool Immune;
        public int CMB;
        public int CMD;
        public double PassConcealment = 1;
        public List<D20Modifier> Rerolls;
        public double Success;

        public override string ToString()
        {
            if (Immune) return Type + " immune";
            var s = string.Format("{0} {1:0.#}% | CMB {2} vs CMD {3}", Type, Success * 100, CMB, CMD);
            if (PassConcealment < 1) s += string.Format(" | concealment {0:0.#}% pass", PassConcealment * 100);
            if (Rerolls != null && Rerolls.Count > 0) s += " | d20: " + D20.Describe(Rerolls);
            return s;
        }
    }

    static class ManeuverChance
    {
        // A maneuver rule made as the game makes it, run dry.
        internal static ManeuverPrediction Predict(RuleCombatManeuver evt)
        {
            PredictionSafety.RequireReadOnlyPrediction();
            var initiator = evt.Initiator;
            var target = evt.Target;
            var p = new ManeuverPrediction { Type = evt.Type };
            DryRun.Run(evt, () =>
            {
                if (evt.AutoFailure || target.Descriptor.State.HasCondition(UnitCondition.ImmuneToCombatManeuvers))
                {
                    p.Immune = true;
                    return;
                }
                if (!evt.IgnoreConcealment)
                {
                    var concealment = UnitPartConcealment.Calculate(initiator, target, false);
                    if (concealment != Concealment.None)
                    {
                        var check = new RuleConcealmentCheck(initiator, target);
                        double fail = 0;
                        bool reroll;
                        DryRun.Run(check, () => fail = GameRules.ConcealmentFail(check, concealment, out reroll));
                        p.PassConcealment = 1 - fail;
                    }
                }
#if WOTR
                if (evt.OverrideBonus.HasValue) p.CMB = evt.OverrideBonus.Value;
                else
                {
                    var cmb = new RuleCalculateCMB(initiator, target, evt.Type);
                    cmb.ReplaceStrength = evt.ReplaceBaseStat ?? cmb.ReplaceStrength;
                    cmb.ReplaceBAB = evt.ReplaceAttackBonus ?? cmb.ReplaceBAB;
                    // The bonuses of the attack the maneuver comes with (flanking and the like) that are not a stat's,
                    // else of an attack with the empty hand; then the maneuver's own.
                    var attack = evt.AttackRule ?? (initiator.Body.EmptyHandWeapon != null
                        ? Rulebook.Trigger(new RuleCalculateAttackBonus(initiator, target, initiator.Body.EmptyHandWeapon, 0)) : null);
                    if (attack != null)
                        foreach (var b in attack.AllBonuses)
                            if (b.Stat == StatType.Unknown) evt.AddModifierCopy(b);
                    cmb.CopyModifiersFrom(evt);
                    p.CMB = Rulebook.Trigger(cmb).Result;
                }
#else
                var cmb = new RuleCalculateCMB(initiator, target, evt.Type);
                if (evt.ReplaceBaseStat.HasValue) cmb.ReplaceStrength = evt.ReplaceBaseStat;
                if (evt.ReplaceAttackBonus.HasValue) cmb.ReplaceBAB = evt.ReplaceAttackBonus;
                foreach (var b in evt.BonusSources) cmb.AddBonus(b.Bonus, b.Source);
                p.CMB = Rulebook.Trigger(cmb).Result;
#endif
                p.CMD = Rulebook.Trigger(new RuleCalculateCMD(initiator, target, evt.Type)).Result;
                p.Rerolls = DryRun.Rerolls(evt);
            });
            if (p.Immune) return p;
            int cmbValue = p.CMB, cmd = p.CMD;
#if WOTR
            // A natural 1 fails (unless a feature lets it count), a natural 20 succeeds. "Reroll if failed" asks
            // IsSuccessRoll before the maneuver has its roll, which then always says failed.
            bool alwaysChance = GameRules.AlwaysChance(initiator);
            Func<int, bool> success = d => d == 20 || ((d > 1 || alwaysChance) && d + cmbValue >= cmd);
            Func<int, bool> failed = d => true;
#else
            // No natural 1 or 20 for a maneuver.
            Func<int, bool> success = d => d + cmbValue >= cmd;
            Func<int, bool> failed = d => !success(d);
#endif
            p.Success = p.PassConcealment * D20.P(D20.Pmf(p.Rerolls, failed), success);
            return p;
        }

        // A copy of a maneuver rule as it was made, before its handlers (for the check against the real roll).
        internal static RuleCombatManeuver Copy(RuleCombatManeuver evt)
        {
#if WOTR
            var copy = new RuleCombatManeuver(evt.Initiator, evt.Target, evt.Type, evt.AttackRule)
            {
                OverrideBonus = evt.OverrideBonus,
            };
            copy.CopyModifiersFrom(evt);
#else
            var copy = new RuleCombatManeuver(evt.Initiator, evt.Target, evt.Type);
            foreach (var b in evt.BonusSources) copy.AddBonus(b.Bonus, b.Source);
#endif
            copy.IgnoreConcealment = evt.IgnoreConcealment;
            copy.AutoFailure = evt.AutoFailure;
            copy.ReplaceBaseStat = evt.ReplaceBaseStat;
            copy.ReplaceAttackBonus = evt.ReplaceAttackBonus;
            copy.Reason = evt.Reason;
            return copy;
        }

        // The maneuver of an ability's action, made as ContextActionCombatManeuver.RunAction makes it; null when the
        // action would do nothing (tripping a prone target).
        internal static RuleCombatManeuver Rule(ContextActionCombatManeuver action, MechanicsContext context, UnitEntityData caster, UnitEntityData target)
        {
            if (action.Type == CombatManeuver.Trip && Prone(target)) return null;
#if WOTR
            if (action.Type == CombatManeuver.AwesomeBlow && Prone(target)) return null;
#endif
            var rule = new RuleCombatManeuver(caster, target, action.Type) { IgnoreConcealment = action.IgnoreConcealment, Reason = context };
            if (action.ReplaceStat)
            {
                var value = action.NewStat;
                if (action.UseKineticistMainStat) value = caster.Get<UnitPartKineticist>()?.MainStatType ?? action.NewStat;
                if (action.UseCastingStat) value = context.SourceAbilityContext?.Ability.Spellbook?.Blueprint.CastingAttribute ?? StatType.Charisma;
                if (action.UseCasterLevelAsBaseAttack) rule.ReplaceAttackBonus = context.Params.CasterLevel;
                if (action.UseBestMentalStat)
                {
                    var stats = caster.Stats;
                    int cha = stats.Charisma.ModifiedValue, wis = stats.Wisdom.ModifiedValue, intel = stats.Intelligence.ModifiedValue;
                    value = cha >= intel && cha >= wis ? StatType.Charisma : wis > intel ? StatType.Wisdom : StatType.Intelligence;
                }
                rule.ReplaceBaseStat = value;
            }
            return rule;
        }

        static bool Prone(UnitEntityData unit) => unit.Descriptor.State.Prone.Active || (unit.View != null && unit.View.IsGetUp);

        // The maneuver a hit with this weapon makes (ManeuverOnAttack of the attacker's features and buffs, as a wolf's
        // trip with its bite), with the attack bonus rule of the attack; null when there is none or it would do nothing.
        internal static ManeuverPrediction OnHit(UnitEntityData attacker, UnitEntityData target, ItemEntityWeapon weapon, RuleCalculateAttackBonus attack)
        {
            foreach (var fact in Facts(attacker))
                foreach (var component in fact.Blueprint.ComponentsArray)
                {
                    var on = component as ManeuverOnAttack;
                    if (on == null || weapon.Blueprint.Type.Category != on.Category) continue;
                    if (Prone(target) && (on.Maneuver == CombatManeuver.Trip
#if WOTR
                        || on.Maneuver == CombatManeuver.AwesomeBlow
#endif
                        )) return null;
#if WOTR
                    return Predict(new RuleCombatManeuver(attacker, target, on.Maneuver, attack));
#else
                    return Predict(new RuleCombatManeuver(attacker, target, on.Maneuver));
#endif
                }
            return null;
        }

#if WOTR
        internal static IEnumerable<Kingmaker.EntitySystem.EntityFact> Facts(UnitEntityData unit) => unit.Facts.List;
#else
        internal static IEnumerable<Kingmaker.Blueprints.Facts.Fact> Facts(UnitEntityData unit)
        {
            foreach (var f in unit.Descriptor.Progression.Features.Enumerable) yield return f;
            foreach (var b in unit.Descriptor.Buffs.Enumerable) yield return b;
        }
#endif

        internal static string Name(CombatManeuver type, bool ru)
        {
            switch (type)
            {
                case CombatManeuver.Trip: return ru ? "Сбивание" : "Trip";
                case CombatManeuver.Disarm: return ru ? "Обезоруживание" : "Disarm";
                case CombatManeuver.BullRush: return ru ? "Натиск" : "Bull rush";
                case CombatManeuver.Overrun: return ru ? "Опрокидывание" : "Overrun";
                case CombatManeuver.Grapple: return ru ? "Захват" : "Grapple";
                case CombatManeuver.SunderArmor: return ru ? "Раскол брони" : "Sunder armor";
                case CombatManeuver.DirtyTrickBlind:
                case CombatManeuver.DirtyTrickEntangle:
                case CombatManeuver.DirtyTrickSickened: return ru ? "Грязный трюк" : "Dirty trick";
                default: return ru ? "Приём" : "Maneuver";
            }
        }
    }
}
