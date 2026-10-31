using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Enums;
using Kingmaker.Items;
using Kingmaker.RuleSystem;
using Kingmaker.PubSubSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UI.SettingsUI;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Parts;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Components;
using Kingmaker.Designers.Mechanics.Buffs;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Mechanics.Actions;
#if WOTR
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.Utility;
#else
using Kingmaker.Controllers.Combat;
#endif
#if WOTR
using DamageGracePart = Kingmaker.UnitLogic.Parts.UnitPartDamageGrace;
#endif

namespace CombatPreview
{
    // The chance of one attack with a weapon, as RuleAttackRoll.OnTrigger decides it, with the bonuses of a dry run.
    class AttackPrediction
    {
        public string Weapon;
        public int Penalty;
        public int AttackBonus;
        public int TargetAC;
        public bool FlatFooted;
        public bool AutoHit;
        public bool AutoMiss;
        public Concealment Concealment;
        public int MissChance;
        public bool ConcealmentReroll;
        public double PassConcealment = 1;
        public double RollHits;
        public int CriticalEdge;
        public bool CanCrit;
        public int CriticalAC;
        public int ConfirmationBonus;
        public double Confirm;
        public int MirrorImages;
        public int ParryBonus = int.MinValue;
        public int Fortification;
        public List<D20Modifier> Rerolls;
        public int OnAttackBonus;
        public List<Part> AttackBonusParts;
        public List<Part> ArmorClassParts;
        public List<string> Notes = new List<string>();
        // Terms of abilities that turn on at the attack, named by their buff.
        public List<Part> PendingParts = new List<Part>();
        // Damage those abilities add to the weapon's (Power Attack, Deadly Aim).
        public int PendingDamage;
        // A combat maneuver the attack makes on a hit (a wolf's trip with its bite), its chance counted with the hit.
        public ManeuverPrediction Maneuver;
        // A spell the hit brings (Spellstrike, a held charge, a ray of an eldritch archer), by this attack's roll.
        public SpellPrediction Charge;
        public double Hit;
        public double Crit;
        // Damage of a hit that is not a critical one: the weapon's dice and bonuses (no sneak attack and other damage
        // added when the damage is dealt).
        public int DamageMin;
        public readonly List<DamageTerm> DamageTerms = new List<DamageTerm>();
        public DamageSummary NormalDamage, CriticalDamage;
        public MitigatedResult TargetMitigated, ScenarioMitigated;
        public PredictionReport RawReport;
        public PredictionReport TargetReport, ScenarioReport;
        public double Expected => DamageMath.Expected(Hit, Crit, NormalDamage.Mean, CriticalDamage.Mean);
        public int DamageMax;
        // Damage of a confirmed critical hit: as RuleAttackWithWeapon.CreateDamage, the critical multiplier goes on the
        // weapon's own damage (its dice rolled that many times, its bonus multiplied), the rest (energy dice) once.
        public int CriticalMultiplier;
        public int CritDamageMin;
        public int CritDamageMax;
        // Time the prediction took.
        public double Milliseconds;

        public override string ToString()
        {
            var s = string.Format("hit {0:0.#}% crit {1:0.#}% | AB {2} vs AC {3}{4} | damage {5}-{6} | {7:0.0} ms", Hit * 100, Crit * 100,
                AttackBonus, TargetAC, FlatFooted ? " (flat-footed)" : "", DamageMin, DamageMax, Milliseconds);
            if (AutoHit) s += " | auto hit";
            if (AutoMiss) s += " | auto miss";
            if (PassConcealment < 1)
                s += string.Format(" | {0} {1:0.#}% pass", MissChance > 0 ? "miss chance " + MissChance + "%" : "concealment " + Concealment,
                    PassConcealment * 100) + (ConcealmentReroll ? " (reroll)" : "");
            if (CanCrit) s += string.Format(" | crit {0}-20 confirm {1:0.#}% vs AC {2}", CriticalEdge, Confirm * 100, CriticalAC);
            if (MirrorImages > 0) s += " | mirror images " + MirrorImages;
            if (ParryBonus != int.MinValue) s += " | parry +" + ParryBonus;
            if (Fortification > 0) s += " | fortification " + Fortification + "%";
            if (Rerolls != null && Rerolls.Count > 0) s += " | d20: " + D20.Describe(Rerolls);
            if (PendingDamage != 0) s += string.Format(" | damage {0:+0;-0} at the attack", PendingDamage);
            if (Maneuver != null) s += " | on hit " + Maneuver;
            if (Notes.Count > 0) s += " | " + string.Join("; ", Notes.ToArray());
            return s;
        }
    }

    static class AttackChance
    {
        static bool? s_SuccessRollFixed;

        // Whether some mod patches RuleAttackRoll.IsSuccessRoll (Kingmaker Fixes makes it test the given die).
        internal static bool AttackSuccessRollFixed
        {
            get
            {
                if (s_SuccessRollFixed == null)
                {
                    var info = Harmony.GetPatchInfo(AccessTools.Method(typeof(RuleAttackRoll), nameof(RuleAttackRoll.IsSuccessRoll)));
                    s_SuccessRollFixed = info != null && (info.Prefixes.Count > 0 || info.Transpilers.Count > 0);
                    ChanceLog.Write("RuleAttackRoll.IsSuccessRoll " + (s_SuccessRollFixed.Value
                        ? "is patched (" + string.Join(", ", info.Owners.ToArray()) + "): rerolls if failed test the die"
                        : "as shipped: rerolls if failed treat every die but 20 as failed"));
                }
                return s_SuccessRollFixed.Value;
            }
        }

        internal static AttackPrediction Predict(UnitEntityData attacker, UnitEntityData target, ItemEntityWeapon weapon, int penalty,
            RuleAttackWithWeapon attack = null, RuleReason reason = null)
        {
            PredictionSafety.RequireReadOnlyPrediction();
            var stats = new RuleCalculateWeaponStats(attacker, weapon, attack);
            var roll = new RuleAttackRoll(attacker, target, stats, penalty) { RuleAttackWithWeapon = attack, Reason = reason };
            if (attack != null)
            {
                roll.AutoHit = attack.AutoHit;
                roll.AutoCriticalThreat = attack.AutoCriticalThreat;
                roll.AutoCriticalConfirmation = attack.AutoCriticalConfirmation;
                roll.Reason = attack;
            }
            var p = new AttackPrediction { Weapon = weapon.Blueprint.Name, Penalty = penalty };
            var watch = System.Diagnostics.Stopwatch.StartNew();
            DryRun.Run(roll, () => Calculate(roll, p));
            p.Milliseconds = watch.Elapsed.TotalMilliseconds;
            return p;
        }

        // Abilities switched on that put their buff on only when an attack starts (ActivateOnUnitAction = Attack, as
        // Power Attack and Deadly Aim): before the attack the buff is not there yet, so its attack bonus is added here
        // as WeaponParametersAttackBonus would add it. Components of other kinds are named in the notes. In WotR that
        // buff (and any other buff of the attacker) may in turn put a buff on right before each attack with a weapon
        // (AddInitiatorAttackWithWeaponTrigger, TriggerBeforeAttack): Power Attack's own bonus comes so.
        static int OnAttackBonus(UnitEntityData attacker, RuleCalculateWeaponStats stats, RuleAttackWithWeapon attack, AttackPrediction p)
        {
            var pending = new List<KeyValuePair<BlueprintBuff, string>>();
            foreach (var ability in attacker.Descriptor.ActivatableAbilities.Enumerable)
            {
                var bp = ability.Blueprint;
                if (!ability.IsOn || GameRules.IsStarted(ability) || !bp.ActivateOnUnitAction
                    || bp.ActivateOnUnitActionType != AbilityActivateOnUnitActionType.Attack || bp.Buff == null)
                    continue;
                if (attacker.Descriptor.Buffs.HasFact(bp.Buff)) continue;
                pending.Add(new KeyValuePair<BlueprintBuff, string>(bp.Buff, bp.Name));
            }
#if WOTR
            var weapon = stats.Weapon;
            var sources = new List<KeyValuePair<BlueprintBuff, string>>(pending);
            foreach (var buff in attacker.Descriptor.Buffs.Enumerable)
                sources.Add(new KeyValuePair<BlueprintBuff, string>(buff.Blueprint, buff.Name));
            foreach (var source in sources)
                foreach (var applied in BeforeAttackBuffs(source.Key, attacker, weapon, attack))
                    if (!attacker.Descriptor.Buffs.HasFact(applied) && !pending.Any(x => x.Key == applied))
                        pending.Add(new KeyValuePair<BlueprintBuff, string>(applied, source.Value));
#endif
            int total = 0;
            foreach (var entry in pending)
            {
                int bonus = BuffAttackBonus(entry.Key, attacker, stats, attack, p);
                if (bonus != 0) p.Notes.Add(string.Format("{0} {1:+0;-0} (turns on at the attack)", entry.Value, bonus));
                total += bonus;
            }
            return total;
        }

#if WOTR
        // The buffs a buff puts on its owner right before an attack with this weapon, as AbstractWeaponTrigger tests
        // the attack before it (the parts about the roll's result do not apply then).
        static IEnumerable<BlueprintBuff> BeforeAttackBuffs(BlueprintBuff buff, UnitEntityData attacker, ItemEntityWeapon weapon, RuleAttackWithWeapon attack)
        {
            foreach (var component in buff.ComponentsArray)
            {
                var trigger = component as AddInitiatorAttackWithWeaponTrigger;
                if (trigger == null || !trigger.TriggerBeforeAttack || !trigger.ActionsOnInitiator || trigger.Action == null) continue;
                var bp = weapon.Blueprint;
                if (trigger.CheckWeaponBlueprint ? bp != trigger.WeaponBlueprint
                    : (trigger.WeaponType != null && trigger.WeaponType != bp.Type)
                      || (trigger.CheckWeaponCategory && trigger.Category != bp.Category)
                      || (trigger.CheckWeaponRangeType && !trigger.RangeType.IsSuitableWeapon(weapon))
                      || (trigger.CheckWeaponGroup && !bp.FighterGroup.Contains(trigger.Group))
                      || (trigger.AllNaturalAndUnarmed && !bp.Type.IsNatural && !bp.Type.IsUnarmed))
                    continue;
                if (trigger.OnlyOnFullAttack && attack != null && !attack.IsFullAttack) continue;
                if (trigger.OnlyOnFirstAttack && attack != null && !attack.IsFirstAttack) continue;
                if (trigger.Action.Actions == null) continue;
                foreach (var action in trigger.Action.Actions)
                {
                    var apply = action as ContextActionApplyBuff;
                    if (apply?.Buff != null) yield return apply.Buff;
                }
            }
        }
#endif

        // The attack bonus a buff that is not on yet would give to an attack with this weapon.
        static int BuffAttackBonus(BlueprintBuff buff, UnitEntityData attacker, RuleCalculateWeaponStats stats, RuleAttackWithWeapon attack, AttackPrediction p,
            int depth = 0)
        {
            var weapon = stats.Weapon;
            int bonus = 0;
            MechanicsContext context = null;
            foreach (var component in buff.ComponentsArray)
            {
                // Damage added to the weapon's, as the component adds it to the weapon stats of the attack.
                var damageBonus = component as WeaponParametersDamageBonus;
                if (damageBonus != null)
                {
                    if (context == null)
                    {
                        context = new MechanicsContext(attacker, attacker.Descriptor, buff);
                        context.RecalculateRanks();
                    }
                    p.PendingDamage += DamageBonus(damageBonus, attacker, stats, context);
                    continue;
                }
                // A bonus to the attack stat, as Fighting Defensively has: its value comes from the buff's
                // context (ranks and properties of the unit), calculated here as the buff would on turning on.
                var statBonus = component as AddStatBonus;
                if (statBonus != null)
                {
                    if (statBonus.Stat != StatType.AdditionalAttackBonus) continue;
                    int v = statBonus.Value;
                    if (statBonus.ScaleByBasicAttackBonus) v *= 1 + attacker.Stats.BaseAttackBonus.ModifiedValue / 4;
                    bonus += v;
                    p.PendingParts.Add(new Part(buff.Name + " [" + statBonus.Descriptor + "]", v));
                    continue;
                }
                var contextBonus = component as AddContextStatBonus;
                if (contextBonus != null)
                {
                    if (contextBonus.Stat != StatType.AdditionalAttackBonus) continue;
                    if (context == null)
                    {
                        context = new MechanicsContext(attacker, attacker.Descriptor, buff);
                        context.RecalculateRanks();
                    }
                    int cv = contextBonus.Value.Calculate(context) * contextBonus.Multiplier;
                    bonus += cv;
                    p.PendingParts.Add(new Part(buff.Name + " [" + contextBonus.Descriptor + "]", cv));
                    continue;
                }
                var abilityValue = component as AddStatBonusAbilityValue;
                if (abilityValue != null)
                {
                    if (abilityValue.Stat != StatType.AdditionalAttackBonus) continue;
                    if (context == null)
                    {
                        context = new MechanicsContext(attacker, attacker.Descriptor, buff);
                        context.RecalculateRanks();
                    }
                    int av = abilityValue.Value.Calculate(context);
                    bonus += av;
                    p.PendingParts.Add(new Part(buff.Name + " [" + abilityValue.Descriptor + "]", av));
                    continue;
                }
                // Buffs the buff puts on its owner as it turns on (Fighting Defensively's dodge bonus): counted the same
                // way, a few levels deep; other actions are named in the notes.
                var actions = component as AddFactContextActions;
                if (actions != null)
                {
                    if (actions.Activated?.Actions == null) continue;
                    foreach (var action in actions.Activated.Actions)
                    {
                        var apply = action as ContextActionApplyBuff;
                        if (apply?.Buff != null && depth < 3)
                        {
                            if (apply.Buff != buff && !attacker.Descriptor.Buffs.HasFact(apply.Buff))
                                bonus += BuffAttackBonus(apply.Buff, attacker, stats, attack, p, depth + 1);
                            continue;
                        }
                        if (action == null) continue;
                        string note = buff.Name + ": " + action.GetType().Name + " on turning on not counted";
                        if (!p.Notes.Contains(note)) p.Notes.Add(note);
                    }
                    continue;
                }
                var w = component as WeaponParametersAttackBonus;
                if (w == null)
                {
                    // Components that only feed the others (ranks, recalculation, triggers before the attack, counted
                    // above) say nothing about the attack.
                    if (component != null && !(component is ContextRankConfig) && !(component is RecalculateOnFactsChange)
#if WOTR
                        && !(component is AddInitiatorAttackWithWeaponTrigger)
#endif
                        )
                    {
                        string note = buff.Name + ": " + component.GetType().Name + " not counted";
                        if (!p.Notes.Contains(note)) p.Notes.Add(note);
                    }
                    continue;
                }
                if (w.OnlyForFullAttack && attack != null && !attack.IsFullAttack) continue;
                bool finesse = !w.OnlyFinessable || weapon.Blueprint.Type.Category.HasSubCategory(WeaponSubCategory.Finessable)
                    || (attacker.Get<DamageGracePart>()?.HasEntry(weapon.Blueprint.Category) ?? false);
                bool range = w.Ranged == weapon.Blueprint.IsRanged;
                bool hands = !w.OnlyTwoHanded || weapon.Blueprint.IsTwoHanded;
#if WOTR
                if (w.SpecificWeaponBlueprint ? w.WeaponBlueprint != weapon.Blueprint : !finesse || !range || !hands) continue;
                int value = w.AttackBonus;
                if (w.UseContextIstead)
                {
                    if (context == null)
                    {
                        context = new MechanicsContext(attacker, attacker.Descriptor, buff);
                        context.RecalculateRanks();
                    }
                    value = w.ContextAttackBonus.Calculate(context);
                }
                if (w.ScaleByBasicAttackBonus) value *= 1 + attacker.Stats.BaseAttackBonus.ModifiedValue / 4;
                if (w.MultiplierFact != null && attacker.HasFact(w.MultiplierFact)) value *= w.Multiplier;
#else
                if (!finesse || !range || !hands) continue;
                int value = w.AttackBonus;
                if (w.ScaleByBasicAttackBonus) value *= 1 + attacker.Stats.BaseAttackBonus.ModifiedValue / 4;
#endif
                bonus += value;
                p.PendingParts.Add(new Part(buff.Name, value));
            }
            return bonus;
        }

#if WOTR
        static readonly System.Reflection.FieldInfo CheckGrip = AccessTools.Field(typeof(WeaponParametersDamageBonus), "m_checkGripStyle");
        static readonly System.Reflection.FieldInfo Grip = AccessTools.Field(typeof(WeaponParametersDamageBonus), "m_gripStyle");

        // As WeaponParametersDamageBonus.OnEventAboutToTrigger(RuleCalculateWeaponStats), for a buff of rank 1.
        static int DamageBonus(WeaponParametersDamageBonus c, UnitEntityData attacker, RuleCalculateWeaponStats stats, MechanicsContext context)
        {
            var weapon = stats.Weapon;
            var bp = weapon.Blueprint;
            int value = c.UseContextIstead ? c.ContextDamageBonus.Calculate(context) : c.DamageBonus;
            if (attacker.HasFact(c.MythicBlueprint)) value++;
            if (c.ScaleByBasicAttackBonus) value *= 1 + attacker.Stats.BaseAttackBonus.ModifiedValue / 4;
            bool finesse = !c.OnlyFinessable || bp.Type.Category.HasSubCategory(WeaponSubCategory.Finessable)
                || (attacker.Get<DamageGracePart>()?.HasEntry(bp.Category) ?? false)
                || (c.CanBeUsedWithFightersFinesse && attacker.HasFact(c.FightersFinesse)
                    && (attacker.Get<UnitPartWeaponTraining>()?.GetWeaponRank(weapon) ?? 0) > 0);
            bool range = c.Ranged == bp.IsRanged;
            bool hands = !c.OnlyTwoHanded || bp.IsTwoHanded;
            bool dual = !c.DualWielding || (attacker.Body.PrimaryHand.HasWeapon && attacker.Body.SecondaryHand.HasWeapon);
            bool offHand = !c.OnlyToOffHandBonus || weapon.Wielder?.Body.SecondaryHand?.MaybeWeapon == weapon;
            bool specific = !c.OnlySpecificItemBlueprint || bp == c.Weapon;
            var grip = (GripType)Grip.GetValue(c);
            bool gripOk = !(bool)CheckGrip.GetValue(c) || (grip == GripType.TwoHanded && weapon.HoldInTwoHands) || (grip == GripType.OneHanded && !weapon.HoldInTwoHands);
            if (c.PowerAttackScaling)
            {
                if (stats.IsSecondary) value /= 2;
                else if (weapon.HoldInTwoHands)
                    value = attacker.Descriptor.HasFact(c.GreaterPowerAttackBlueprint) ? value * 2
                        : attacker.HasFact(c.MythicBlueprint) ? value * 5 / 3 : value * 3 / 2;
            }
            return finesse && range && hands && dual && offHand && specific && gripOk ? value : 0;
        }
#else
        // As WeaponParametersDamageBonus.OnEventAboutToTrigger(RuleCalculateWeaponStats), for a buff of rank 1.
        static int DamageBonus(WeaponParametersDamageBonus c, UnitEntityData attacker, RuleCalculateWeaponStats stats, MechanicsContext context)
        {
            var weapon = stats.Weapon;
            var bp = weapon.Blueprint;
            int value = c.DamageBonus;
            if (c.ScaleByBasicAttackBonus) value *= 1 + attacker.Stats.BaseAttackBonus.ModifiedValue / 4;
            bool finesse = !c.OnlyFinessable || bp.Type.Category.HasSubCategory(WeaponSubCategory.Finessable)
                || (attacker.Get<DamageGracePart>()?.HasEntry(bp.Category) ?? false);
            bool range = c.Ranged == bp.IsRanged;
            bool hands = !c.OnlyTwoHanded || bp.IsTwoHanded;
            if (c.PowerAttackScaling)
            {
                if ((bp.Type.IsLight && !bp.IsUnarmed && !bp.IsNatural) || weapon.IsSecondary) value /= 2;
                else if (weapon.HoldInTwoHands)
                    value = attacker.Descriptor.HasFact(c.GreaterPowerAttackBlueprint) ? value * 2 : value * 3 / 2;
            }
            return finesse && range && hands ? value : 0;
        }
#endif

        // Bonuses to the target's armor class that its facts give only while an attack with a weapon goes on (against
        // the attacker's alignment, against a kind of weapon, against attacks of opportunity: Protection from Evil and
        // the like). The dry run has no such attack: their "about to trigger" is called with a copy of it, and their
        // "did trigger" with the copy's end undoes them when the dry run ends. Bodyguard is left out: it spends an
        // attack of opportunity.
        static readonly HashSet<Type> AttackStartAC = new HashSet<Type>
        {
            typeof(ArmorClassBonusAgainstAlignment), typeof(ACBonusAgainstAttacks), typeof(ACBonusAgainstWeaponCategory),
            typeof(ACBonusAgainstWeaponGroup), typeof(ACBonusAgainstWeaponSubcategory), typeof(ACBonusAgainstWeaponType),
#if WOTR
            typeof(ACBonusAgainstAttackOfOpportunity), typeof(ACContextBonusAgainstWeaponSubcategory),
#endif
        };

        static void WeaponAttackStart(RuleAttackRoll roll)
        {
            var real = roll.RuleAttackWithWeapon;
            var attack = new RuleAttackWithWeapon(roll.Initiator, roll.Target, roll.Weapon, roll.AttackBonusPenalty);
            if (real != null)
            {
                attack.IsAttackOfOpportunity = real.IsAttackOfOpportunity;
                attack.IsFirstAttack = real.IsFirstAttack;
                attack.IsFullAttack = real.IsFullAttack;
                attack.IsCharge = real.IsCharge;
                attack.AttackNumber = real.AttackNumber;
            }
            var started = new List<Action>();
#if WOTR
            foreach (var fact in roll.Target.Facts.List)
                foreach (var component in fact.Active ? fact.Components : Enumerable.Empty<Kingmaker.EntitySystem.EntityFactComponent>())
                {
                    var handler = component.SourceBlueprintComponent as IRulebookHandler<RuleAttackWithWeapon>;
                    if (handler == null || !AttackStartAC.Contains(handler.GetType())) continue;
                    var runtime = component as ISubscriptionProxy;
                    if (Call(() => { using (runtime?.RequestEventContext()) handler.OnEventAboutToTrigger(attack); }))
                        started.Add(() => { using (runtime?.RequestEventContext()) handler.OnEventDidTrigger(attack); });
                }
#else
            var target = roll.Target.Descriptor;
            var facts = new List<Kingmaker.Blueprints.Facts.Fact>(target.Progression.Features.Enumerable);
            facts.AddRange(target.Buffs.Enumerable);
            foreach (var fact in facts)
            {
                if (!fact.Active) continue;
                foreach (var component in fact.Components)
                {
                    var handler = component as IRulebookHandler<RuleAttackWithWeapon>;
                    if (handler == null || !AttackStartAC.Contains(handler.GetType())) continue;
                    var f = fact;
                    if (Call(() => { using (new Kingmaker.Designers.EventConditionActionSystem.ContextData.FactData(f)) handler.OnEventAboutToTrigger(attack); }))
                        started.Add(() => { using (new Kingmaker.Designers.EventConditionActionSystem.ContextData.FactData(f)) handler.OnEventDidTrigger(attack); });
                }
            }
#endif
            DryRun.AtEnd(() =>
            {
                foreach (var end in started) Call(end);
                attack.OnDidTrigger();
            });
        }

        static bool Call(Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception e)
            {
                Main.Log.Error("dry run: weapon attack handler: " + e.Message);
                return false;
            }
        }

        // The damage DistanceDamageBonus (Point Blank Shot) adds to a ranged weapon's damage, as it decides it when the
        // damage is dealt.
        static int DistanceDamage(UnitEntityData attacker, UnitEntityData target, ItemEntityWeapon weapon)
        {
            if (!weapon.Blueprint.IsRanged) return 0;
            int total = 0;
            foreach (var fact in ManeuverChance.Facts(attacker))
                foreach (var component in fact.Blueprint.ComponentsArray)
                {
                    var bonus = component as DistanceDamageBonus;
                    if (bonus == null) continue;
#if WOTR
                    if (fact.MaybeContext?.MaybeCaster?.Parts.Get<UnitPartDisableBonusForDamage>()?.DisableAdditionalDamage == true) continue;
                    if (target.DistanceTo(attacker) <= bonus.Range.Meters + target.Corpulence + attacker.Corpulence) total += bonus.DamageBonus;
#else
                    if (target.DistanceTo(attacker) < bonus.Range.Meters) total += bonus.DamageBonus;
#endif
                }
            return total;
        }

#if !WOTR
        // Enemies with Power Attack: PowerAttackWatcher turns it on or off right before their roll, and the buff is put
        // on or removed at once, so the choice applies to this roll. The watcher is silenced in a dry run; its choice is
        // made here by its own formula, reading the ability only (nothing is switched, nothing to undo). It turns on
        // whenever it is off, and off when the hit chance it costs outweighs its damage.
        static void PowerAttackAi(RuleAttackRoll roll, AttackPrediction p)
        {
            var attacker = roll.Initiator;
            if (roll.AttackType.IsTouch() || attacker.IsPlayerFaction) return;
            var hand = attacker.GetThreatHand()?.MaybeWeapon;
            if (hand == null || roll.Weapon != hand) return;
            foreach (var fact in ManeuverChance.Facts(attacker))
                foreach (var component in fact.Blueprint.ComponentsArray)
                {
                    var watcher = component as PowerAttackWatcher;
                    var ability = watcher != null ? attacker.Descriptor.GetFact(watcher.PowerAttackBlueprint) as ActivatableAbility : null;
                    var buff = ability?.Blueprint.Buff;
                    if (buff == null) continue;
                    // Running by the time of the roll: already, or put on as the attack starts (counted above).
                    bool pending = ability.IsOn && !ability.IsRunning && ability.Blueprint.ActivateOnUnitAction
                        && ability.Blueprint.ActivateOnUnitActionType == AbilityActivateOnUnitActionType.Attack;
                    bool running = ability.IsRunning || pending;
                    bool buffOn = pending || attacker.Descriptor.Buffs.HasFact(buff);
                    int n = attacker.Stats.BaseAttackBonus.ModifiedValue / 4 + 1;
                    int strength = attacker.Stats.Strength.Bonus;
                    double on = (4.5 + 2 * n + strength) * Math.Max(p.AttackBonus + 21 - p.TargetAC - (running ? 0 : n), 1) * 0.05;
                    double off = (4.5 + strength) * Math.Max(p.AttackBonus + 21 - p.TargetAC + (running ? n : 0), 1) * 0.05;
                    int sign;
                    if (ability.IsOn && off > on) sign = buffOn ? -1 : 0;
                    else if (!ability.IsOn) sign = buffOn ? 0 : 1;
                    else sign = 0;
                    if (sign == 0) continue;
                    // The buff's terms (as if it were not on yet), added or taken back under its own name.
                    var terms = new AttackPrediction();
                    int bonus = sign * BuffAttackBonus(buff, attacker, roll.WeaponStats, roll.RuleAttackWithWeapon, terms);
                    int damage = sign * terms.PendingDamage;
                    p.AttackBonus += bonus;
                    if (bonus != 0) p.AttackBonusParts.Add(new Part(buff.Name, bonus));
                    p.DamageMin = Math.Max(1, p.DamageMin + damage);
                    p.DamageMax = Math.Max(p.DamageMin, p.DamageMax + damage);
                    p.CritDamageMin = Math.Max(1, p.CritDamageMin + damage * p.CriticalMultiplier);
                    p.CritDamageMax = Math.Max(p.CritDamageMin, p.CritDamageMax + damage * p.CriticalMultiplier);
                    p.Notes.Add(string.Format("{0}: AI turns it {1} at the roll ({2:+0;-0})", ability.Blueprint.Name, sign > 0 ? "on" : "off", bonus));
                    return;
                }
        }
#endif

        static void Calculate(RuleAttackRoll roll, AttackPrediction p)
        {
            var attacker = roll.Initiator;
            var target = roll.Target;
            if (!GameRules.IsTriggered(roll.WeaponStats)) Rulebook.Trigger(roll.WeaponStats);
            p.CriticalEdge = roll.WeaponStats.CriticalEdge;
            int multiplier = p.CriticalMultiplier = GameRules.CriticalMultiplier(roll.WeaponStats);
            for (int i = 0; i < roll.WeaponStats.DamageDescription.Count; i++)
            {
                var damage = roll.WeaponStats.DamageDescription[i];
                var dice = GameRules.Dice(damage);
                p.DamageTerms.Add(new DamageTerm(dice.Rolls, dice.Rolls == 0 ? 1 : dice.Dice.Sides(), damage.Bonus, i == 0) { Kind = DefenseReader.Classify(damage.TypeDescription) });
                int min = dice.Rolls + damage.Bonus, max = dice.Rolls * dice.Dice.Sides() + damage.Bonus;
                p.DamageMin += min;
                p.DamageMax += max;
                p.CritDamageMin += i == 0 ? min * multiplier : min;
                p.CritDamageMax += i == 0 ? max * multiplier : max;
            }
            // Abilities that turn on at the attack: their attack bonus is added below, their damage goes with the
            // weapon's own bonus, multiplied on a critical hit.
            p.OnAttackBonus = OnAttackBonus(attacker, roll.WeaponStats, roll.RuleAttackWithWeapon, p);
            // Point Blank Shot adds its damage only when the damage is dealt, multiplied on a critical hit too.
            int dealt = p.PendingDamage + DistanceDamage(attacker, target, roll.Weapon);
            if (p.DamageTerms.Count > 0) p.DamageTerms[0].Bonus += dealt;
            else p.DamageTerms.Add(new DamageTerm(0, 1, dealt, true));
            p.NormalDamage = DamageMath.Summarize(p.DamageTerms, 1);
            p.CriticalDamage = DamageMath.Summarize(p.DamageTerms, multiplier);
            p.DamageMin += dealt;
            p.DamageMax += dealt;
            p.CritDamageMin += dealt * multiplier;
            p.CritDamageMax += dealt * multiplier;
            p.DamageMin = Math.Max(1, p.DamageMin);
            p.DamageMax = Math.Max(p.DamageMin, p.DamageMax);
            p.CritDamageMin = Math.Max(1, p.CritDamageMin);
            p.CritDamageMax = Math.Max(p.CritDamageMin, p.CritDamageMax);

            bool confused = target.Descriptor.State.HasCondition(UnitCondition.Confusion);
            bool friend = !target.IsEnemy(attacker) && !target.Faction.Neutral && !confused;
            p.AutoHit = roll.AutoHit || attacker == target || (roll.AttackType.IsTouch() && friend);
            p.AutoMiss = !p.AutoHit && roll.AutoMiss;
            p.Fortification = target.Get<UnitPartFortification>()?.Value ?? 0;
            double fortificationPassed = p.Fortification > 0 ? Math.Max(0, 100 - p.Fortification) / 100.0 : 1;
            if (p.AutoHit)
            {
                p.Hit = 1;
                p.Crit = roll.AutoCriticalThreat && roll.AutoCriticalConfirmation ? fortificationPassed : 0;
                return;
            }
            if (p.AutoMiss) return;

            // Concealment or a miss chance (d100 over the value), as TryOvercomeTargetConcealmentAndMissChance.
            p.Concealment = roll.IgnoreConcealment ? Concealment.None : UnitPartConcealment.Calculate(attacker, target, true);
            p.MissChance = GameRules.MissChance(roll);
            if (p.MissChance > 0 && (p.Concealment != Concealment.Total || p.MissChance > 50) && (p.Concealment != Concealment.Partial || p.MissChance > 20))
                p.PassConcealment = Math.Max(0, 100 - p.MissChance) / 100.0;
            else
            {
                p.MissChance = 0;
                if (!roll.IgnoreConcealment && p.Concealment != Concealment.None)
                {
                    var check = new RuleConcealmentCheck(attacker, target, true);
                    double fail = 0;
                    DryRun.Run(check, () => fail = GameRules.ConcealmentFail(check, p.Concealment, out p.ConcealmentReroll));
                    p.PassConcealment = 1 - fail;
                }
            }

            WeaponAttackStart(roll);
#if WOTR
            // As RuleAttackRoll.OnTrigger: flat-footed forced by the roll, the roll's own bonuses copied to the attack
            // bonus, with the weapon stats of the roll.
            var ac =Rulebook.Trigger(new RuleCalculateAC(attacker, target, roll.AttackType) { ForceFlatFooted = roll.ForceFlatFooted });
            var abRule = new RuleCalculateAttackBonus(attacker, target, roll.WeaponStats, roll.AttackBonusPenalty) { TargetIsFlanked = roll.TargetIsFlanked };
            abRule.CopyModifiersFrom(roll);
            Rulebook.Trigger(abRule);
#else
            var ac = Rulebook.Trigger(new RuleCalculateAC(attacker, target, roll.AttackType));
            var abRule = Rulebook.Trigger(new RuleCalculateAttackBonus(attacker, target, roll.Weapon, roll.AttackBonusPenalty));
#endif
            p.TargetAC = GameRules.Result(ac);
            p.FlatFooted = ac.IsTargetFlatFooted;
            p.ArmorClassParts = Breakdown.ArmorClass(ac, target);
            p.AttackBonus = abRule.Result;
            p.AttackBonusParts = Breakdown.AttackBonus(abRule, attacker);
            p.AttackBonus += p.OnAttackBonus;
            p.AttackBonusParts.AddRange(p.PendingParts);
#if !WOTR
            PowerAttackAi(roll, p);
#endif
            int ab = p.AttackBonus, acValue = p.TargetAC;
            // WotR: a feature lets a natural 1 hit if the total does (State.Features.AlwaysChance).
            bool alwaysChance = GameRules.AlwaysChance(attacker);
            Func<int, bool> hits = d => d == 20 || ((d > 1 || alwaysChance) && d + ab >= acValue);
            // ModifyD20 tests "failed" with RuleAttackRoll.IsSuccessRoll. In the game as shipped it adds the not yet
            // rolled Roll (0) instead of the die: every die but a natural 20 counts as failed unless AB >= AC. Kingmaker
            // Fixes (or any mod that patches the method) makes it test the die.
            Func<int, bool> failedForReroll = GameRules.SuccessRollTestsDie || AttackSuccessRollFixed
                ? (Func<int, bool>)(d => !hits(d))
                : d => !(d > 1 && (d == 20 || ab >= acValue));
            p.Rerolls = DryRun.Rerolls(roll);
            var pmf = D20.Pmf(p.Rerolls, failedForReroll);
            p.RollHits = D20.P(pmf, hits);

            p.CanCrit = GameRules.CanCrit(roll, target);
            int edge = p.CriticalEdge;
            bool autoThreat = roll.AutoCriticalThreat;
            Func<int, bool> threat = d => p.CanCrit && hits(d) && (d >= edge || autoThreat);
            if (p.CanCrit && D20.P(pmf, threat) > 0)
            {
                p.CriticalAC = GameRules.Result(Rulebook.Trigger(new RuleCalculateAC(attacker, target, roll.AttackType) { IsCritical = true }));
                p.ConfirmationBonus = roll.CriticalConfirmationBonus;
                int need = p.CriticalAC - ab - p.ConfirmationBonus;
                // The confirmation die goes through the same d20 modifiers, no natural 1 or 20 rule here. By then the
                // attack die has hit, so for "reroll only if failed" only a natural 1 counts as failed.
                p.Confirm = roll.AutoCriticalConfirmation ? 1 : D20.P(D20.Pmf(p.Rerolls, d => d == 1), d => d >= need);
            }

            // Mirror images take a hit with chance images/(images+1); a parry beats the attack roll.
            if (!attacker.Descriptor.IsImmuneToVisualEffects)
                p.MirrorImages = target.Get<UnitPartMirrorImage>()?.MechanicsImages.Count ?? 0;
            double image = p.MirrorImages > 0 ? p.MirrorImages / (p.MirrorImages + 1.0) : 0;
            Func<int, double> parried = d => 0;
            if (roll.Parry != null)
            {
                p.ParryBonus = Rulebook.Trigger(roll.Parry.AttackBonusRule).Result;
                int parry = p.ParryBonus;
                parried = d =>
                {
                    int beat = 0;
                    for (int q = 1; q <= 20; q++) if (q + parry > d + ab) beat++;
                    return beat / 20.0;
                };
            }

            double hit = 0, crit = 0;
            for (int d = 1; d <= 20; d++)
            {
                if (pmf[d] <= 0 || !hits(d)) continue;
                double stays = pmf[d] * (1 - image) * (1 - parried(d));
                hit += stays;
                if (threat(d)) crit += stays * p.Confirm * fortificationPassed;
            }
            p.Hit = p.PassConcealment * hit;
            p.Crit = p.PassConcealment * crit;
            p.Maneuver = ManeuverChance.OnHit(attacker, target, roll.Weapon, abRule);
        }
    }
}
