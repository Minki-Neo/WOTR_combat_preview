using System;
using System.Collections.Generic;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Enums;
using Kingmaker.Items;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic.Parts;

namespace CombatPreview
{
    internal static class AttackConcealment
    {
        // The supported game's query reads concealment, sight, visibility and distance.
        // It does not create a rule event or roll dice. Outside an attack it uses the
        // equipped weapon to evaluate weapon-range-specific entries.
        internal static double? MissChance(UnitEntityData attacker, UnitEntityData target, ItemEntityWeapon weapon = null)
        {
            if (!BetaReader.Supported || attacker?.View == null || target?.View == null) return null;
            try
            {
                var concealment = UnitPartConcealment.Calculate(attacker, target, true);
                if (concealment == Concealment.None) return 0;
                if (concealment != Concealment.Partial && concealment != Concealment.Total) return null;
                int rerolls = 0;
                foreach (var fact in attacker.Facts.List)
                {
                    if (!fact.Active) continue;
                    foreach (var runtime in fact.Components)
                    {
                        var component = CombatFields.Component(runtime);
                        if (component?.GetType() != typeof(RerollConcealment)
                            || !CombatFields.Bool(runtime, "<IsSubscribedOnEventBus>k__BackingField")) continue;
                        string coverage = CombatFields.Read(component, "m_WeaponCoverage")?.ToString();
                        if (coverage == "All" || coverage == "Melee" && (weapon == null || weapon.Blueprint.IsMelee)
                            || coverage == "Ranged" && (weapon == null || weapon.Blueprint.IsRanged)) rerolls++;
                    }
                }
                return SpellPreviewMath.RepeatedMiss(concealment == Concealment.Partial ? .2 : .5, rerolls);
            }
            catch (Exception) { return null; }
        }

        internal static Dictionary<string, object> Observed(RuleAttackRoll attack)
        {
            var concealment = attack.ConcealmentCheck;
            var miss = attack.MissChanceRoll;
            return new Dictionary<string, object> {
                { "d20", D20Observation.Capture(attack.D20) },
                { "ignoreConcealment", attack.IgnoreConcealment },
                { "concealment", concealment?.Concealment.ToString() },
                { "concealmentPercent", concealment?.ConcealmentValue },
                { "concealmentRoll", concealment?.Roll?.Result },
                { "concealmentPassed", concealment == null ? (bool?)null : concealment.Success },
                { "otherMissPercent", miss == null ? (int?)null : attack.MissChanceValue },
                { "otherMissRoll", miss?.Result },
                { "attackResult", attack.Result.ToString() },
                { "hitMirrorImageIndex", attack.HitMirrorImageIndex }
            };
        }

        internal static int MirrorImages(UnitEntityData attacker, UnitEntityData target)
        {
            if (!BetaReader.Supported || attacker == null || target == null || attacker.Descriptor.IsImmuneToVisualEffects)
                return 0;
            return target.Get<UnitPartMirrorImage>()?.MechanicsImages.Count ?? 0;
        }
    }
}
