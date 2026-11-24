using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Parts;

namespace CombatPreview
{
    internal sealed class SpellDamageConditions
    {
        internal double Hit = 1, Penetration = 1, SaveFailure = 1, SavedMultiplier = .5;
        internal bool HasSave, Evasion, ImprovedEvasion;
        internal int MirrorImages;
        internal double HitBeforeImages, MirrorNearMiss;

        internal double MeanProjectileHit(int count)
        {
            if (MirrorImages <= 0 || count <= 1 || Penetration != 1) return Hit;
            var images = new MirrorImageSequence(MirrorImages);
            double total = 0;
            for (int i = 0; i < count; i++)
                total += HitBeforeImages * images.Apply(HitBeforeImages, MirrorNearMiss);
            return total / count;
        }

        internal Dictionary<string, object> Data(double failed, double passed)
        {
            return new Dictionary<string, object> {
                { "hitAfterVisibility", Hit }, { "mirrorImages", MirrorImages }, { "srPenetration", Penetration }, { "targetFailsSave", SaveFailure },
                { "damageOnFailedSave", failed }, { "damageOnPassedSave", passed },
                { "evasion", Evasion }, { "improvedEvasion", ImprovedEvasion },
                { "savedMultiplier", SavedMultiplier },
                { "scope", "partial model; independent rolls; special modifiers, crit and cover separate" }
            };
        }

        internal static SpellDamageConditions Create(AbilityData ability, UnitEntityData caster, UnitEntityData target,
            List<SpellCheckRecord> checks, BlueprintComponent[] components, bool attackRequired,
            List<SavingThrowType> saves, bool uncertainBranches, bool checksSr, out string status)
        {
            status = "UnsupportedDeliveryOrSaveBranches";
            var runs = components.OfType<AbilityEffectRunAction>().ToArray();
            if (uncertainBranches || runs.Length != 1 || saves.Distinct().Count() > 1
                || components.Any(c => c != null && c.GetType().Name.StartsWith("AbilityDeliver", StringComparison.Ordinal)
                    && !(c is AbilityDeliverProjectile) && !(c is AbilityDeliverTouch) && !(c is AbilityDeliverDelay))) return null;
            var result = new SpellDamageConditions();
            bool? immune = SpellResistanceReader.Immune(ability, caster, target);
            if (!immune.HasValue) { status = "UnresolvedSpellImmunity"; return null; }
            if (immune == true) { result.Penetration = 0; status = "RegisteredSpellImmunity"; return result; }
            if (attackRequired)
            {
                status = "AttackRollUnavailable";
                var attack = checks.Where(c => c.Kind == "attack" && c.Probability.HasValue).ToArray();
                if (attack.Length != 1) return null;
                result.Hit = attack[0].Probability.Value;
                result.MirrorImages = attack[0].MirrorImages.GetValueOrDefault();
                result.HitBeforeImages = attack[0].HitBeforeImages ?? result.Hit;
                result.MirrorNearMiss = attack[0].MirrorNearMiss.GetValueOrDefault();
            }
            if (checksSr)
            {
                status = "ConditionalResistanceOrImmunity";
                var part = target.Get<UnitPartSpellResistance>();
                if (part != null)
                {
                    var sr = checks.FirstOrDefault(c => c.Kind == "sr");
                    if (sr?.Status != "NoRegisteredSR")
                    {
                        if (sr?.Probability == null) return null;
                        result.Penetration = sr.Probability.Value;
                    }
                }
            }
            if (saves.Count != 0)
            {
                status = "SaveRollUnavailable";
                var type = runs[0].SavingThrowType;
                if (type == SavingThrowType.Unknown) return null;
                var save = checks.FirstOrDefault(c => c.Kind == "save" && c.Type == type.ToString() && c.Probability.HasValue);
                if (save == null) return null;
                result.HasSave = true;
                result.SaveFailure = save.Probability.Value;
                result.SavedMultiplier = caster.State.Features.AzataFavorableMagic ? .75 : .5;
                int budget = 4096;
                foreach (var fact in target.Facts.List)
                {
                    if (!fact.Active) continue;
                    foreach (var runtime in fact.Components)
                    {
                        if (--budget <= 0) { status = "EvasionScanBudget"; return null; }
                        var component = CombatFields.Component(runtime);
                        string name = component?.GetType().Name;
                        if (name == "EvasionAgainstDescriptor" || name == "EvasionWithTowerShield")
                        {
                            status = "ConditionalEvasion";
                            return null;
                        }
                        if (name != "Evasion" && name != "ImprovedEvasion") continue;
                        if (!Equals(CombatFields.Read(component, "SavingThrow"), type)) continue;
                        result.Evasion |= name == "Evasion";
                        result.ImprovedEvasion |= name == "ImprovedEvasion";
                    }
                }
            }
            status = "BasicRollsAvailable";
            return result;
        }

    }
}
