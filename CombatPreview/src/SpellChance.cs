using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Kingmaker.Blueprints.Root;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Items;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.RuleSystem.Rules.Abilities;
using Kingmaker.Controllers.Combat;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Parts;
using Kingmaker.Utility;
#if WOTR
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Class.Kineticist;
using Kingmaker.Blueprints;
#endif

namespace CombatPreview
{
    class SavePrediction
    {
        public SavingThrowType Type;
        public int DC;
        public int Stat;
        public int SuccessBonus;
        public bool AutoPass;
        public bool Evasion;
        public bool ImprovedEvasion;
        public List<D20Modifier> Rerolls;
        public List<Part> Parts;
        public double Pass;

        public override string ToString()
        {
            var s = string.Format("{0} save pass {1:0.#}% | {2} vs DC {3}", Type, Pass * 100, Stat, DC);
            if (SuccessBonus != 0) s += string.Format(" | if it fails by less: {0:+0;-0}", SuccessBonus);
            if (AutoPass) s += " | auto pass";
            if (Rerolls != null && Rerolls.Count > 0) s += " | d20: " + D20.Describe(Rerolls);
            return s;
        }
    }

    class SpellResistancePrediction
    {
        public int SpellResistance;
        public int Penetration;
        public bool Immune;
        public bool Ignored;
        public bool HasRoll;
        // WotR: the caster checks a skill instead of the caster level when it is higher (not counted).
        public bool SkillInstead;
        public List<D20Modifier> Rerolls;
        // The chance the spell gets through.
        public double Pass = 1;

        public override string ToString()
        {
            if (Ignored) return "SR ignored";
            if (Immune) return "immune to the spell";
            if (!HasRoll) return "no SR roll" + (SpellResistance > 0 ? " (SR " + SpellResistance + ")" : "");
            var s = string.Format("SR {0} vs d20 + {1}: through {2:0.#}%", SpellResistance, Penetration, Pass * 100);
            if (Rerolls != null && Rerolls.Count > 0) s += " | d20: " + D20.Describe(Rerolls);
            if (SkillInstead) s += " | a skill check may replace the caster level: not counted";
            return s;
        }
    }

    // What decides whether a spell works on a target: its attack roll, the target's spell resistance and saving throw.
    class SpellPrediction
    {
        public string Name;
        public AttackPrediction Attack;
        public SpellResistancePrediction SpellResistance;
        public SavePrediction Save;
        // Hit point damage of the first damage action: on a failed save (or with no save) and on a passed one; -1
        // when there is no such damage, SavedMax -1 when a passed save does not change it.
        public int DamageMin = -1;
        public int DamageMax = -1;
        public int SavedMin = -1;
        public int SavedMax = -1;
        // Projectiles the spell sends at the target (Magic Missile, Scorching Ray): each one is a whole spell on the
        // target, with its own attack roll, spell resistance and damage.
        public int Projectiles = 1;
        // A combat maneuver of the ability's effect (Trip, Bull Rush).
        public ManeuverPrediction Maneuver;
        // The damage is doubled by a confirmed critical hit of the attack that brings it (ContextActionDealDamage).
        public bool DamageCrits;

        public bool Empty => Attack == null && Save == null && DamageMax < 0 && Maneuver == null
            && (SpellResistance == null || SpellResistance.Pass >= 1);
    }

    static class SpellChance
    {
        internal static SavePrediction Save(UnitEntityData unit, SavingThrowType type, int dc, RuleReason reason)
        {
            PredictionSafety.RequireReadOnlyPrediction();
            return Save(new RuleSavingThrow(unit, type, dc) { Reason = reason });
        }

        // A saving throw rule made as the game makes it, run dry.
        internal static SavePrediction Save(RuleSavingThrow evt)
        {
            PredictionSafety.RequireReadOnlyPrediction();
            var unit = evt.Initiator;
            var p = new SavePrediction { Type = evt.Type, DC = evt.DifficultyClass };
            DryRun.Run(evt, () =>
            {
                // As RuleSavingThrow.OnTrigger takes the stat.
                var stat = unit.Stats.GetStat(evt.StatType);
#if WOTR
                // A better stat may stand in for the save (UnitPartSavingThrowsReplace); the rule's own bonuses are on
                // the stat while it is read; handlers may raise the DC.
                var replace = unit.Get<UnitPartSavingThrowsReplace>();
                if (replace != null && replace.SavingThrowStat == evt.StatType)
                {
                    var other = unit.Stats.GetStat(replace.ReplaceToStat);
                    if (other.ModifiedValue > stat.ModifiedValue) stat = other;
                }
                using (stat.GetTemporaryModifiersScope(evt.AllBonuses)) p.Stat = stat.ModifiedValue;
                p.DC = evt.DifficultyClass;
#else
                p.Stat = (stat as ModifiableValueAttributeStat)?.Bonus ?? (stat as ModifiableValueSavingThrow)?.ModifiedValueWithDisabledBonuses() ?? stat.ModifiedValue;
#endif
                p.Parts = Breakdown.Save(stat, p.Stat);
                p.SuccessBonus = evt.SuccessBonus;
                p.AutoPass = evt.AutoPass;
                p.Evasion = evt.Evasion;
                p.ImprovedEvasion = evt.ImprovedEvasion;
                p.Rerolls = DryRun.Rerolls(evt);
#if WOTR
                // Rerolls handlers put on the save's own die (BuffDescriptorImmunity), outside ModifyD20.
                if (evt.D20?.Rerolls != null)
                    foreach (var r in evt.D20.Rerolls)
                        p.Rerolls.Add(new D20Modifier { Source = r.Source?.Name, Rerolls = Math.Abs(r.Amount), TakeBest = r.Amount > 0 });
#endif
            });
            int dc = p.DC;
            // "Reroll only if failed" asks RuleSavingThrow.IsSuccessRoll about the first die before OnTrigger has
            // taken the stat (0): every die but 20 below the DC counts as failed.
            Func<int, bool> failed = d => !(d == 20 || (d > 1 && d >= dc));
            int value = p.Stat, success = p.SuccessBonus;
            // A bonus that turns a failure into a success (SaveSuccessIfBonus) counts unless the die is 1.
            p.Pass = p.AutoPass ? 1 : D20.P(D20.Outcomes(p.Rerolls, failed), (d, b) => d == 20 || (d > 1 && d + value + b + success >= dc));
            return p;
        }

        internal static SpellResistancePrediction SpellResistance(MechanicsContext context, UnitEntityData target)
        {
            PredictionSafety.RequireReadOnlyPrediction();
            var evt = new RuleSpellResistanceCheck(context, target) { Reason = context };
            var p = new SpellResistancePrediction();
            DryRun.Run(evt, () =>
            {
#if WOTR
                // As RuleSpellResistanceCheck.OnTrigger and IsSpellResisted.
                var part = target.Get<UnitPartSpellResistance>();
                p.Ignored = context.SourceAbilityContext?.ForceIgnoreSpellResist == true || evt.Initiator.Descriptor.State.Features.PrimalMagicEssence
                    || evt.IgnoreSpellResistance;
                p.Immune = part?.IsImmune(context) ?? false;
                p.SpellResistance = part?.GetValue(context) ?? 0;
                p.Penetration = context.Params.CasterLevel + GameRules.SpellPenetration(evt);
                // The value is set by OnTrigger; HasResistanceRoll reads it.
                GameRules.SetSpellResistance(evt, p.SpellResistance);
                p.HasRoll = evt.HasResistanceRoll;
                if (context.MaybeCaster?.Get<UnitPartSpellResistanceCheckReplace>() != null) p.SkillInstead = true;
                if (!p.Ignored && !p.Immune && p.HasRoll) p.Rerolls = DryRun.Rerolls(evt);
#else
                // As RuleSpellResistanceCheck.OnTrigger and IsSpellResisted.
                var part = target.Get<UnitPartSpellResistance>();
                p.Ignored = evt.IgnoreSpellResistance || (evt.Initiator?.Descriptor.State.Features.PrimalMagicEssence ?? false);
                p.Immune = part?.IsImmune(context) ?? false;
                p.SpellResistance = part?.GetValue(context) ?? 0;
                p.Penetration = context.Params.CasterLevel + evt.AdditionalSpellPenetration;
                p.HasRoll = p.SpellResistance > 0 && (evt.Ability == null || evt.Ability.SpellResistance);
                if (!p.Ignored && !p.Immune && p.HasRoll) p.Rerolls = DryRun.Rerolls(evt);
#endif
            });
            if (p.Ignored) p.Pass = 1;
            else if (p.Immune) p.Pass = 0;
            else if (p.HasRoll)
            {
                int sr = p.SpellResistance, pen = p.Penetration;
                // ModifyD20 has no test of a spell resistance roll: "only if failed" always rerolls it.
                p.Pass = D20.P(D20.Pmf(p.Rerolls, d => true), d => sr <= pen + d);
            }
            return p;
        }

        // A touch or a ray weapon, made new for each attack as the game does.
        static ItemEntityWeapon Weapon(Kingmaker.Blueprints.Items.Weapons.BlueprintItemWeapon blueprint)
        {
            return blueprint.CreateEntity<ItemEntityWeapon>();
        }

        // The ability cast on the target: the parameters (DC, caster level) as the cast would calculate them, then the
        // attack roll of a touch or a ray, the spell resistance and the first saving throw of its effect. A spell brought
        // by an attack with a weapon (Spellstrike) has no attack roll of its own: it goes by the weapon's.
        internal static SpellPrediction Predict(AbilityData ability, UnitEntityData target, bool byWeapon = false)
        {
            PredictionSafety.RequireReadOnlyPrediction();
            var p = new SpellPrediction { Name = ability.Name };
            if (ability.Blueprint.HasVariants) return p;
            // A spell held as a charge (Inflict Wounds, Shocking Grasp) does its work by the touch it gives the
            // caster: that touch, made from the spell as UnitUseAbility makes it for a reach spell.
            var sticky = ability.Blueprint.StickyTouch;
            if (sticky != null && sticky.TouchDeliveryAbility != null) ability = new AbilityData(ability, sticky.TouchDeliveryAbility);
            var blueprint = ability.Blueprint;
            var caster = ability.Caster.Unit;
            var wrapper = new TargetWrapper(target);
            var context = new AbilityExecutionContext(ability, Params(ability, caster), wrapper);

            foreach (var component in blueprint.ComponentsArray)
            {
                // A touch with Reach goes as a ray (AbilityDeliverTouch).
                var touch = component as AbilityDeliverTouch;
                var touchWeapon = touch == null ? null
                    : context.HasMetamagic(Kingmaker.UnitLogic.Abilities.Metamagic.Reach) ? BlueprintRoot.Instance.SystemMechanics.RayWeapon : touch.TouchWeapon;
                if (touchWeapon != null && p.Attack == null && !byWeapon)
                    p.Attack = AttackChance.Predict(caster, target, Weapon(touchWeapon), 0, null, context);
                var projectile = component as AbilityDeliverProjectile;
                if (projectile != null && projectile.Type != AbilityProjectileType.Line && projectile.Type != AbilityProjectileType.Cone)
                {
                    // As AbilityDeliverProjectile: the count from the ability's rank, at most one per projectile it
                    // has; a hacked spell of a magus sends one.
                    context.RecalculateRanks();
                    int count = projectile.UseMaxProjectilesCount ? context[projectile.MaxProjectilesCountRank] : projectile.Projectiles.Length;
                    p.Projectiles = Math.Max(1, Math.Min(count, projectile.Projectiles.Length));
#if WOTR
                    if (ability.MagicHackData != null) p.Projectiles = 1;
#endif
                }
                if (projectile != null && projectile.NeedAttackRoll && projectile.Type != AbilityProjectileType.Line && projectile.Type != AbilityProjectileType.Cone && p.Attack == null
                    && !byWeapon)
                {
#if WOTR
                    // As AbilityDeliverProjectile: the melee weapon for Hand of the Apprentice, the main hand for a
                    // kineticist archer blast, Intelligence for the stat with the domains-by-Intelligence feature.
                    var weapon = projectile.IsHandOfTheApprentice ? caster.GetThreatHandMelee()?.MaybeWeapon
                        : AbilityKineticist.IsArcherBlast(ability) && projectile.Type == AbilityProjectileType.Simple ? caster.Body.PrimaryHand.Weapon
                        : projectile.Weapon != null ? Weapon(projectile.Weapon) : null;
                    var stat = caster.State.Features.DomainsByIntelligenceFeature ? StatType.Intelligence : projectile.AttackRollBonusStat;
                    if (weapon != null)
                        using (projectile.ReplaceAttackRollBonusStat ? ContextData<AttackBonusStatReplacement>.Request().Setup(stat) : null)
                            p.Attack = AttackChance.Predict(caster, target, weapon, 0, null, context);
#else
                    var weapon = projectile.IsHandOfTheApprentice ? caster.GetThreatHand()?.MaybeWeapon
                        : projectile.Weapon != null ? Weapon(projectile.Weapon) : null;
                    if (weapon != null)
                        using (projectile.ReplaceAttackRollBonusStat ? new AttackBonusStatReplacement(projectile.AttackRollBonusStat) : null)
                            p.Attack = AttackChance.Predict(caster, target, weapon, 0, null, context);
#endif
                }
            }

            if (target != caster && !blueprint.StickyTouch) p.SpellResistance = SpellResistance(context, target);

            Effects(p, blueprint, context, target, wrapper);

            // A touch whose effect is another spell it casts on the target (Inflict Wounds): that spell's saving throw
            // and damage, made as ContextActionCastSpell makes it, its spell resistance checked again.
            if (p.Save == null && p.DamageMax < 0 && p.Maneuver == null)
            {
                var cast = FindCast(blueprint);
                if (cast != null)
                {
                    var spell = new AbilityData(cast.Spell, caster.Descriptor);
#if WOTR
                    if (cast.MarkAsChild) spell.IsChildSpell = true;
                    if (cast.OverrideSpellbook) spell.OverrideSpellbook = ability.OverrideSpellbook ?? ability.Spellbook;
#endif
                    if (cast.OverrideDC) spell.OverrideDC = cast.DC.Calculate(context);
                    if (cast.OverrideSpellLevel) spell.OverrideSpellLevel = cast.SpellLevel.Calculate(context);
                    var inner = new AbilityExecutionContext(spell, Params(spell, caster), wrapper);
                    if (target != caster && !cast.Spell.StickyTouch)
                    {
                        var resistance = SpellResistance(inner, target);
                        if (p.SpellResistance == null) p.SpellResistance = resistance;
                        else
                        {
                            p.SpellResistance.Pass *= resistance.Pass;
                            p.SpellResistance.Immune |= resistance.Immune;
                        }
                    }
                    Effects(p, cast.Spell, inner, target, wrapper);
                }
            }
            return p;
        }

        // The parameters (DC, caster level) as the cast would calculate them.
        static AbilityParams Params(AbilityData ability, UnitEntityData caster)
        {
            var blueprint = ability.Blueprint;
            // The parameters rule removes some facts after it (RemoveAfterCast) and before it (metamagic for one
            // spell): it runs dry, its result calculated as its OnTrigger does.
            AbilityParams parameters = null;
#if WOTR
            // As AbilityData.CalculateParams: an item's or a fact's own parameters, or a calculator component of the
            // ability, else the rule with the ability's overrides.
            if (ability.SourceItemEquipmentBlueprint != null || ability.Fact?.OverrideParams != null
                || blueprint.GetComponent<Kingmaker.UnitLogic.Mechanics.Components.ContextAbilityParamsCalculator>() != null)
                parameters = ability.CalculateParams();
            else
            {
                var rule = new RuleCalculateAbilityParams(caster, ability)
                {
                    ReplaceDC = ability.OverrideDC,
                    ReplaceSpellLevel = ability.OverrideSpellLevel,
                    ReplaceCasterLevel = ability.OverrideCasterLevel,
                    ReplaceStatBonusModifier = ability.OverrideCasterModifier,
                    IgnoreDCBonuses = ability.IgnoreDCBonuses,
                };
#else
            if (ability.SourceItemEquipmentBlueprint != null || ability.Fact?.OverrideParams != null)
                parameters = ability.CalculateParams();
            else
            {
                var rule = new RuleCalculateAbilityParams(caster, ability) { ReplaceDC = ability.OverrideDC, ReplaceSpellLevel = ability.OverrideSpellLevel };
#endif
                DryRun.Run(rule, () => rule.OnTrigger(Kingmaker.Game.Instance.Rulebook.Context));
                parameters = rule.Result;
            }
            return parameters;
        }

        // The first spell cast by the ability's effect on the target by its caster.
        static ContextActionCastSpell FindCast(Kingmaker.UnitLogic.Abilities.Blueprints.BlueprintAbility blueprint)
        {
            foreach (var component in blueprint.ComponentsArray)
            {
                var run = component as AbilityEffectRunAction;
                var cast = run == null ? null : Find<ContextActionCastSpell>(run.Actions, 0);
#if WOTR
                if (cast != null && cast.CastByTarget) cast = null;
#endif
                if (cast != null && cast.Spell != null && !cast.Spell.HasVariants) return cast;
            }
            return null;
        }

        // The first saving throw, the damage and a combat maneuver of the ability's effect.
        static void Effects(SpellPrediction p, Kingmaker.UnitLogic.Abilities.Blueprints.BlueprintAbility blueprint, AbilityExecutionContext context,
            UnitEntityData target, TargetWrapper wrapper)
        {
            var caster = context.MaybeCaster;
            foreach (var component in blueprint.ComponentsArray)
            {
                var run = component as AbilityEffectRunAction;
                if (run == null) continue;
#if WOTR
                // The save as the effect makes it (CreateSavingThrow: DC, persistent spell and the rest); a persistent
                // spell makes the target save twice.
                if ((SavingThrowType)SaveTypeInContext.Invoke(run, new object[] { context }) != SavingThrowType.Unknown)
                {
                    p.Save = Save((RuleSavingThrow)CreateSave.Invoke(run, new object[] { target, context, false }));
                    if (context.HasMetamagic(Kingmaker.UnitLogic.Abilities.Metamagic.Persistent)) p.Save.Pass *= p.Save.Pass;
                }
#else
                if (run.HasSavingThrow)
                    p.Save = Save(target, run.SavingThrowType, context.Params.DC, context);
#endif
                else
                {
                    int bonus;
                    var action = FindSave(run.Actions, context, wrapper, out bonus);
                    if (action != null) p.Save = Save(target, action.Type, context.Params.DC + bonus, context);
                }
                if (p.Save != null) break;
            }

            foreach (var component in blueprint.ComponentsArray)
            {
                var run = component as AbilityEffectRunAction;
                var damage = run == null ? null : Find<ContextActionDealDamage>(run.Actions, 0);
                if (damage == null || DamageKind.GetValue(damage)?.ToString() != "Damage" || damage.ReadPreRolledFromSharedValue) continue;
                Damage(p, damage, context);
                break;
            }

            foreach (var component in blueprint.ComponentsArray)
            {
                var run = component as AbilityEffectRunAction;
                var maneuver = run == null ? null : Find<ContextActionCombatManeuver>(run.Actions, 0);
                if (maneuver == null) continue;
                var rule = ManeuverChance.Rule(maneuver, context, caster, target);
                if (rule != null) p.Maneuver = ManeuverChance.Predict(rule);
                break;
            }
        }

#if WOTR
        static readonly MethodInfo SaveTypeInContext = AccessTools.Method(typeof(AbilityEffectRunAction), "GetSavingThrowTypeInContext");
        static readonly MethodInfo CreateSave = AccessTools.Method(typeof(AbilityEffectRunAction), "CreateSavingThrow");
#endif

        static readonly FieldInfo DamageKind = AccessTools.Field(typeof(ContextActionDealDamage), "m_Type");

        // As ContextActionDealDamage and RuleCalculateDamage: the dice and the bonus from the context, empowered (x1.5)
        // or maximized by metamagic, halved by Half; a passed save halves it with HalfIfSaved, evasion takes all of it
        // on a passed Reflex save and improved evasion half of it on a failed one.
        static void Damage(SpellPrediction p, ContextActionDealDamage damage, AbilityExecutionContext context)
        {
            context.RecalculateRanks();
            int dice = damage.Value.DiceCountValue.Calculate(context);
            int sides = damage.Value.DiceType.Sides();
            int bonus = damage.Value.BonusValue.Calculate(context);
            if (dice <= 0 && bonus <= 0) return;
            p.DamageCrits = !damage.IgnoreCritical;
            float min = dice + bonus, max = dice * sides + bonus;
            if (context.HasMetamagic(Kingmaker.UnitLogic.Abilities.Metamagic.Maximize)) min = max;
            if (context.HasMetamagic(Kingmaker.UnitLogic.Abilities.Metamagic.Empower))
            {
                min *= 1.5f;
                max *= 1.5f;
            }
            if (damage.Half)
            {
                min /= 2;
                max /= 2;
            }
            var save = p.Save;
            bool reflex = save != null && save.Type == SavingThrowType.Reflex;
            float failMin = min, failMax = max;
            if (reflex && save.ImprovedEvasion && damage.HalfIfSaved)
            {
                failMin /= 2;
                failMax /= 2;
            }
            p.DamageMin = Math.Max(1, (int)failMin);
            p.DamageMax = Math.Max(p.DamageMin, (int)failMax);
            if (save != null && damage.HalfIfSaved)
            {
                if (reflex && (save.Evasion || save.ImprovedEvasion)) p.SavedMin = p.SavedMax = 0;
                else
                {
                    p.SavedMin = Math.Max(1, (int)(min / 2));
                    p.SavedMax = Math.Max(p.SavedMin, (int)(max / 2));
                }
            }
        }

        static readonly FieldInfo DCIncreases = AccessTools.Field(typeof(ContextActionSavingThrow), "m_ConditionalDCIncrease");

        // The first saving throw among the actions, nested ones included, with its conditional DC increases checked
        // against the target as the action does.
        static ContextActionSavingThrow FindSave(ActionList list, MechanicsContext context, TargetWrapper target, out int bonus)
        {
            bonus = 0;
            var save = Find<ContextActionSavingThrow>(list, 0);
            if (save == null) return null;
            var increases = DCIncreases?.GetValue(save) as IEnumerable;
            if (increases != null)
                using (context.GetDataScope(target))
                    foreach (var increase in increases)
                    {
                        var t = increase.GetType();
                        var condition = AccessTools.Field(t, "Condition")?.GetValue(increase) as ConditionsChecker;
                        var value = AccessTools.Field(t, "Value")?.GetValue(increase) as ContextValue;
                        if (condition != null && value != null && condition.Check()) bonus += value.Calculate(context);
                    }
            return save;
        }

        // The first action of the type among the actions, nested ones (conditions, saves) included.
        static T Find<T>(ActionList list, int depth) where T : GameAction
        {
            if (list?.Actions == null || depth > 8) return null;
            foreach (var action in list.Actions)
            {
                if (action == null) continue;
                var save = action as T;
                if (save != null) return save;
                foreach (var field in action.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (field.FieldType != typeof(ActionList)) continue;
                    var found = Find<T>(field.GetValue(action) as ActionList, depth + 1);
                    if (found != null) return found;
                }
            }
            return null;
        }
    }
}
