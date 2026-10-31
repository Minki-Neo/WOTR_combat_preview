using System;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules.Damage;

namespace CombatPreview
{
    internal static class DefenseReader
    {
        internal static DamageKind Classify(DamageTypeDescription description)
        {
            // Parse exact enum names; unrecognised/new damage types never become physical.
            if (description == null) return DamageKind.Unknown;
            string type = description.Type.ToString();
            if (type == "Physical") return DamageKind.Physical;
            if (type == "Direct") return DamageKind.Direct;
            DamageKind kind;
            if (type == "Energy" && Enum.TryParse(description.Energy.ToString(), out kind)
                && Enum.IsDefined(typeof(DamageKind), kind) && kind != DamageKind.Physical && kind != DamageKind.Direct) return kind;
            return DamageKind.Unknown;
        }
        internal static DefenseSnapshot Read(Kingmaker.EntitySystem.Entities.UnitEntityData target = null, string targetId = null)
        {
            var observation = VanillaDefenseCapture.Capture(target, targetId);
            return Compatibility.HasTabletopTweaks
                ? new TabletopTweaksDefenseSnapshotProvider().Read(observation, null)
                : new VanillaDefenseSnapshotProvider().Read(observation, null);
        }
        internal static DefenseProfile Scenario(Settings s)
        {
            var p = new DefenseProfile { Complete = true, Source = "MANUAL SCENARIO (not target defenses)" };
            p.Reduction[DamageKind.Physical] = s.ScenarioBypassDR ? 0 : s.ScenarioDR;
            p.Reduction[DamageKind.Fire] = s.ScenarioFire;
            p.Reduction[DamageKind.Cold] = s.ScenarioCold;
            p.Reduction[DamageKind.Electricity] = s.ScenarioElectricity;
            p.Reduction[DamageKind.Acid] = s.ScenarioAcid;
            p.Reduction[DamageKind.Sonic] = s.ScenarioSonic;
            if (s.ScenarioPhysicalImmune) p.Immunities.Add(DamageKind.Physical);
            if (s.ScenarioFireImmune) p.Immunities.Add(DamageKind.Fire);
            return p;
        }
    }
}
