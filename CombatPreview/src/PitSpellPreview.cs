using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.RuleSystem;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Abilities.Components.AreaEffects;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Parts;

namespace CombatPreview
{
    internal static class PitSpellPreview
    {
        internal static void Append(AbilityData ability, UnitEntityData caster, UnitEntityData target,
            BlueprintComponent[] components, int? dc, int? cl, Metamagic? meta, int? rankBonus,
            Dictionary<string, object> metadata)
        {
            var runs = components.OfType<AbilityEffectRunAction>().Take(2).ToArray();
            if (runs.Length != 1 || runs[0].Actions?.Actions?.Length != 1
                || !(runs[0].Actions.Actions[0] is ContextActionSpawnAreaEffect spawn)) return;
            var area = LoadedBlueprintReader.Read(CombatFields.Read(spawn, "m_AreaEffect")) as BlueprintScriptableObject;
            var pits = area?.ComponentsArray.OfType<AreaEffectPit>().Take(2).ToArray();
            if (pits?.Length != 1) return;
            var pit = pits[0];
            var falls = pit.OnFallAction?.Actions?.OfType<ContextActionDealDamage>().Take(2).ToArray();
            if (falls?.Length != 1 || pit.OnFallAction.Actions.Length != 1) return;
            var fall = falls[0];
            if (fall.ReadPreRolledFromSharedValue || fall.HalfIfSaved
                || DefenseReader.Classify(fall.DamageType) != DamageKind.Physical) return;
            int? dice = SpellDamagePreview.Value(fall.Value?.DiceCountValue, area.ComponentsArray, cl, meta, caster, rankBonus);
            int? bonus = SpellDamagePreview.Value(fall.Value?.BonusValue, area.ComponentsArray, cl, meta, caster, rankBonus);
            int sides = fall.Value == null ? 0 : DiceTypeExtension.Sides(fall.Value.DiceType);
            if (!dice.HasValue || !bonus.HasValue || sides < 1 || !meta.HasValue) return;
            string formula = dice + "D" + sides + (bonus == 0 ? "" : bonus > 0 ? "+" + bonus : bonus.ToString());
            double? failure = null;
            var refs = CombatFields.Read(pit, "m_EffectsImmunityFacts") as BlueprintUnitFactReference[];
            var forced = CombatFields.Read(pit, "m_EvadingImmunityFacts") as BlueprintUnitFactReference[];
            bool immune = refs?.Any(reference => CombatFields.HasFact(target, reference)) == true
                || target.State.Size > pit.MaxUnitSize;
            bool unavoidable = forced?.Any(reference => CombatFields.HasFact(target, reference)) == true;
            if (immune && !unavoidable) failure = 0;
            else if (!unavoidable && !caster.IsEnemy(target) && !string.Equals(caster.GroupId, target.GroupId)
                && !target.Descriptor.Faction.Neutral) failure = 0;
            else if (unavoidable || target.State.IsHelpless || target.State.HasCondition(UnitCondition.ForceMove)) failure = 1;
            else if (dc.HasValue && CombatFields.Read(target.Stats.SaveReflex, "<ModifiedValue>k__BackingField") is int save)
                failure = ActiveD20Reader.SaveFailure(target, caster, ability, Kingmaker.EntitySystem.Stats.SavingThrowType.Reflex,
                    save, dc.Value, false, out _);
            bool maximize = (meta.Value & Metamagic.Maximize) != 0;
            bool empower = (meta.Value & Metamagic.Empower) != 0;
            var mean = SpellDamageMath.MeanHitPoints(dice.Value, sides, bonus.Value, maximize, empower,
                fall.Half && !fall.AlreadyHalved);
            double? expected = SpellDamageMath.Through(failure, mean);
            double? hpLoss = null, temporaryLoss = null;
            var traits = new WeaponTraits { Form = CombatFields.Int(fall.DamageType.Physical, "Form"), Known = true };
            var defense = AutomaticDefenseReader.Read(target, null, traits, out var defenseNotes, singleApplication: true, incomingKind: DamageKind.Physical);
            bool defenseKnown = defense.Complete;
            defense.Reduction.TryGetValue(DamageKind.Physical, out int reduction);
            bool damageImmune = defense.Immunities.Contains(DamageKind.Physical);
            double? defended = !defenseKnown ? (double?)null : damageImmune ? 0
                : SpellDamageMath.MeanHitPoints(dice.Value, sides, bonus.Value, maximize, empower,
                    fall.Half && !fall.AlreadyHalved, reduction);
            if (failure == 0) { hpLoss = 0; temporaryLoss = 0; }
            else if (defenseKnown && BetaReader.ReadHealth(target, out long current, out _, out int temporary))
            {
                hpLoss = SpellDamageMath.Through(failure, SpellDamageMath.MeanCappedHpLoss(dice.Value, sides,
                    bonus.Value, maximize, empower, fall.Half && !fall.AlreadyHalved, current, Math.Max(0, temporary), reduction));
                temporaryLoss = SpellDamageMath.Through(failure, SpellDamageMath.MeanCappedHpLoss(dice.Value, sides,
                    bonus.Value, maximize, empower, fall.Half && !fall.AlreadyHalved, Math.Max(0, temporary), 0, reduction));
            }
            if (damageImmune) { hpLoss = 0; temporaryLoss = 0; }
            var lines = SpellOutcome.Describe("구덩이 중앙 · 반사 실패 " + SpellLethalPreview.Percent(failure), metadata,
                new List<SpellOutcome> {
                    new SpellOutcome("낙하", failure, formula + " 낙하 피해", "구덩이에 갇힘"),
                    new SpellOutcome("낙하 회피", 1 - failure, "낙하 피해 없음")
                });
            lines.Add("낙하 기댓값 ~" + (expected.HasValue ? expected.Value.ToString("0.##") : "—") + " HP (피해 방어 전)");
            if (defenseKnown) lines.Add("방어 후 낙하 ~" + (SpellDamageMath.Through(failure, defended)?.ToString("0.##") ?? "—")
                + " HP · 적용 DR " + reduction + (damageImmune ? " · 물리 면역" : ""));
            if (metadata.TryGetValue("spellDamage", out var raw) && raw is Dictionary<string, object> damage
                && damage["terms"] is List<Dictionary<string, object>> terms)
                foreach (var term in terms.Where(term => ((string)term["path"]).Contains(".EveryRoundAction")).Take(2))
                    lines.Add("갇힌 동안 매 라운드 " + term["formula"]
                        + (Equals(term["halfIfSaved"], true) ? " · 내성 성공 시 반감" : ""));
            lines.Add("가장자리 반사 +2 · 탈출 DC " + (pit.ClimbDC?.ValueType == Kingmaker.UnitLogic.Mechanics.ContextValueType.Simple
                ? pit.ClimbDC.Value.ToString() : "—"));
            metadata["pitPreview"] = new Dictionary<string, object> {
                { "summary", lines.ToArray() }, { "fallChance", failure }, { "fallExpectedBeforeDefense", expected },
                { "expectedHpLoss", hpLoss }, { "expectedTemporaryHpLoss", temporaryLoss }, { "scope", "initial center fall only; later rounds require continued occupancy" }
            };
            ((Dictionary<string, object>)metadata["pitPreview"])["defense"] = defenseNotes.ToArray();
        }
    }
}
