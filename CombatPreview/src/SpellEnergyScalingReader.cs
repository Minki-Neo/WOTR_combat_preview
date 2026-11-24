using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Enums.Damage;
using Kingmaker.RuleSystem.Rules.Damage;
using Kingmaker.UnitLogic.FactLogic;

namespace CombatPreview
{
    internal sealed class SpellEnergyScalingReader
    {
        sealed class Bonus
        {
            internal DamageEnergyTypeFlag Energy;
            internal float Multiplier;
            internal bool WeaponOnly;
            internal string Source, Name;
        }
        sealed class Vulnerability
        { internal DamageEnergyType Energy; internal string Source, Name; }
        readonly List<Bonus> bonuses = new List<Bonus>();
        readonly List<Vulnerability> vulnerabilities = new List<Vulnerability>();

        internal SpellEnergyScalingReader(UnitEntityData caster, UnitEntityData target)
        {
            if (caster != null)
                foreach (var fact in caster.Facts.List.Where(fact => fact.Active))
                foreach (var runtime in fact.Components)
                    if (CombatFields.Bool(runtime, "<IsSubscribedOnEventBus>k__BackingField")
                        && CombatFields.Component(runtime)?.GetType() == typeof(EnergyDamageBonus))
                    {
                        var component = (EnergyDamageBonus)CombatFields.Component(runtime);
                        bonuses.Add(new Bonus { Energy = component.EnergyType, Multiplier = component.Multiplier,
                            WeaponOnly = component.WeaponDamageOnly, Source = BetaIdentity.Id(fact), Name = CombatFields.Label(fact) });
                    }
            if (target != null)
                foreach (var fact in target.Facts.List.Where(fact => fact.Active))
                foreach (var runtime in fact.Components)
                    if (CombatFields.Bool(runtime, "<IsSubscribedOnEventBus>k__BackingField")
                        && CombatFields.Component(runtime)?.GetType() == typeof(AddEnergyVulnerability))
                    {
                        var component = (AddEnergyVulnerability)CombatFields.Component(runtime);
                        vulnerabilities.Add(new Vulnerability { Energy = component.Type,
                            Source = BetaIdentity.Id(fact), Name = CombatFields.Label(fact) });
                    }
        }
        internal double? Read(DamageTypeDescription damage, bool noWeaponContext, double swarm, double durability,
            out int? percent, out double energyVulnerability, out string reason, out Dictionary<string, object>[] sources)
        {
            var entries = new List<Dictionary<string, object>>();
            var contributions = new List<int>();
            energyVulnerability = 1; reason = null;
            if (damage?.Type.ToString() == "Energy")
            {
                foreach (var bonus in bonuses.Where(bonus => bonus.Energy.Contains(damage.Energy.ToFlag())))
                {
                    if (bonus.WeaponOnly && noWeaponContext) continue;
                    int? value = SpellEnergyScaling.Percent(bonus.Multiplier);
                    if (bonus.WeaponOnly) reason = "WeaponOnlyEnergyBonusContextUnavailable";
                    else if (!value.HasValue) reason = "EnergyBonusPercentInvalid";
                    else contributions.Add(value.Value);
                    entries.Add(new Dictionary<string, object> { { "source", bonus.Source }, { "name", bonus.Name },
                        { "component", "EnergyDamageBonus" }, { "percent", value }, { "weaponOnly", bonus.WeaponOnly } });
                }
                foreach (var vulnerability in vulnerabilities.Where(vulnerability => vulnerability.Energy == damage.Energy))
                {
                    energyVulnerability = Math.Max(energyVulnerability, 1.5);
                    entries.Add(new Dictionary<string, object> { { "source", vulnerability.Source }, { "name", vulnerability.Name },
                        { "component", "AddEnergyVulnerability" }, { "factor", 1.5 } });
                }
            }
            percent = SpellEnergyScaling.SumPercent(contributions);
            if (!percent.HasValue) reason = "EnergyBonusPercentOverflow";
            if (swarm > 1 && energyVulnerability > 1) reason = "VulnerabilityHandlerOrderUnavailable";
            sources = entries.ToArray();
            var scale = SpellEnergyScaling.Combined(swarm, energyVulnerability, durability);
            if (!scale.HasValue && reason == null) reason = "EnergyScalingInvalid";
            return reason == null ? scale : null;
        }
    }
}
