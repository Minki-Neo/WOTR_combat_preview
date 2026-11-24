using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Enums;
using Kingmaker.Items;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Parts;

namespace CombatPreview
{
    // Mirrors the supported game's target-specific rule arithmetic. The predicted
    // engager set is local: never add engagements or dispatch preview rule events.
    internal static class TargetCombatReader
    {
        internal static int Attack(UnitEntityData actor, UnitEntityData target, ItemEntityWeapon weapon,
            bool ranged, List<string> notes)
        {
            int bonus = 0;
            if (UnitPartConcealment.Calculate(target, actor, false) == Concealment.Total
                || actor.State.Features.Blinking && !target.Descriptor.IsSeeInvisibility)
            { bonus += 2; notes.Add("보이지 않는 공격자: AB +2"); }
            var engagers = new HashSet<UnitEntityData>(target.CombatState.EngagedBy);
            bool flanked = target.CombatState.IsFlanked;
            if (!ranged && weapon != null && target.IsInCombat && !target.State.Features.CannotBeFlanked)
            {
                engagers.Add(actor);
                var partner = BetaReader.Mount(actor) ?? BetaReader.Rider(actor);
                var partnerWeapon = partner == null ? null : BetaReader.Primary(partner);
                if (partner != null && partnerWeapon?.Blueprint.IsMelee == true && !partner.State.IsDead
                    && partner.State.CanAct && partner.IsEnemy(target))
                {
                    float approach = Math.Min(actor.DistanceTo(target), actor.Corpulence + target.Corpulence
                        + (weapon?.AttackRange.Meters ?? 0)) - .1f;
                    if (approach < partner.Corpulence + target.Corpulence + partnerWeapon.AttackRange.Meters)
                        engagers.Add(partner);
                }
                flanked |= engagers.Count > 1;
            }
            if (!ranged && flanked) { bonus += 2; notes.Add("협공 (접근 후 포함): AB +2"); }
            bool precise = false;
            foreach (var fact in actor.Facts.List)
            {
                if (!fact.Active) continue;
                foreach (var runtime in fact.Components)
                {
                    if (!CombatFields.Bool(runtime, "<IsSubscribedOnEventBus>k__BackingField")) continue;
                    var component = CombatFields.Component(runtime);
                    precise |= component?.GetType() == typeof(PreciseShot);
                    if (ranged || !flanked || component?.GetType() != typeof(OutflankAttackBonus)) continue;
                    var outflank = (OutflankAttackBonus)component;
                    if (outflank.Descriptor != ModifierDescriptor.UntypedStackable) continue;
                    if (!actor.State.Features.SoloTactics && !engagers.Any(other => other != actor
                        && CombatFields.HasFact(other, CombatFields.Read(component, "m_OutflankFact")))) continue;
                    int amount = checked(outflank.AttackBonus * CombatFields.Rank(fact));
                    bonus += amount; notes.Add(CombatFields.Label(fact) + ": 대상 AB " + amount.ToString("+0;-0;0"));
                }
            }
            if (ranged && !precise && target.CombatState.EngagedBy.Any(other => other != actor && !other.IsEnemy(actor)))
            { bonus -= 4; notes.Add("근접전 중인 대상 원거리 페널티: AB -4"); }
            return bonus;
        }

        internal static int Armor(UnitEntityData actor, UnitEntityData target, bool ranged, bool touch, List<string> notes)
        {
            bool ignoreVisibility = false, ignoreConcealment = false;
            foreach (var fact in target.Facts.List.Where(fact => fact.Active))
                foreach (var runtime in fact.Components)
                {
                    if (!CombatFields.Bool(runtime, "<IsSubscribedOnEventBus>k__BackingField")) continue;
                    if (!(CombatFields.Component(runtime) is FlatFootedIgnore ignore)) continue;
                    switch (ignore.Type)
                    {
                        case FlatFootedIgnoreType.UncannyDodge: ignoreVisibility = ignoreConcealment = true; break;
                        case FlatFootedIgnoreType.BlindFight: ignoreConcealment |= !ranged; break;
                        case FlatFootedIgnoreType.ImprovedBlindFight: ignoreConcealment |= actor.DistanceTo(target) <= 9.144f; break;
                        case FlatFootedIgnoreType.GreaterBlindFight: ignoreConcealment = true; break;
                    }
                }
            bool flat = !ignoreVisibility && (!target.CombatState.CanActInCombat || !target.Memory.Contains(actor))
                || target.State.IsHelpless || target.State.HasCondition(UnitCondition.Stunned)
                || !ignoreConcealment && UnitPartConcealment.Calculate(target, actor, false) == Concealment.Total
                || target.State.HasCondition(UnitCondition.LoseDexterityToAC)
                || actor.State.Features.ShatterDefenses && (target.State.HasCondition(UnitCondition.Shaken)
                    || target.State.HasCondition(UnitCondition.Frightened));
            string field = flat ? touch ? "<FlatFootedTouch>k__BackingField" : "<FlatFooted>k__BackingField"
                : touch ? "<Touch>k__BackingField" : "<ModifiedValue>k__BackingField";
            int ac = CombatFields.Read(target.Stats.AC, field) is int cached ? cached
                : throw new NotSupportedException("대상 AC 캐시 미확인");
            if (flat) notes.Add("무방비 AC 반영");
            if (target.State.Prone.Active || target.View != null && target.View.IsGetUp)
            { ac += ranged ? 4 : -4; notes.Add("넘어짐: AC " + (ranged ? "+4" : "-4")); }
            if (!ranged && target.State.IsHelpless) { ac -= 4; notes.Add("무력화 근접: AC -4"); }
            if (target.State.HasCondition(UnitCondition.Blindness)) { ac -= 2; notes.Add("실명: AC -2"); }
            return ac;
        }
    }
}
