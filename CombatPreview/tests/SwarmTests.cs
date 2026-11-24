using System;
using CombatPreview;
static class SwarmTests
{
    static int assertions;
    static void Near(double actual, double expected, string message)
    { assertions++; if (Math.Abs(actual - expected) > 1e-9 || double.IsNaN(actual)) throw new Exception(message + ": " + actual + " != " + expected); }
    public static void Run()
    {
        for (int form = 0; form < 8; form++) foreach (bool tiny in new[] { false, true })
        {
            Near(SwarmRules.WeaponPhysicalFactor(true, false, false, tiny, form, true), tiny ? 0 : (form & 1) != 0 ? 1 : .5,
                "swarm subtype and any bludgeoning form determine weapon physical protection");
            Near(SwarmRules.WeaponPhysicalFactor(true, true, false, tiny, form, true), 1, "kinetic blast weapon exception");
            Near(SwarmRules.WeaponPhysicalFactor(true, false, true, tiny, form, true), 1, "attacker exception fact bypasses swarm protection");
            Near(SwarmRules.WeaponPhysicalFactor(false, false, false, tiny, form, true), 1, "non-weapon physical spell damage is not weapon damage");
        }
        assertions++; if (!double.IsNaN(SwarmRules.WeaponPhysicalFactor(true, false, false, false, 0, false)))
            throw new Exception("unknown weapon form cannot certify tiny protection");
        for (int mask = 0; mask < 8; mask++)
            Near(SwarmRules.AreaVulnerable((mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0) ? 1 : 0,
                mask == 0 ? 0 : 1, "area vulnerability uses engine delivery/context predicates");
        var physical = new DamageTerm(1, 6, 0, true) { Kind = DamageKind.Physical };
        var fire = new DamageTerm(1, 6, 0, false) { Kind = DamageKind.Fire };
        var profile = new DefenseProfile { Complete = true, PhysicalDecline = .5 };
        Near(MitigationMath.Calculate(new[] { physical }, 2, 1, 0, profile).Expected, 10.0 / 6,
            "tiny half damage uses per-roll flooring and minimum one");
        profile.Reduction[DamageKind.Physical] = 1;
        Near(MitigationMath.Calculate(new[] { physical }, 2, 1, 0, profile).Expected, 4.0 / 6,
            "swarm decline precedes ordinary DR");
        profile.Immunities.Add(DamageKind.Physical);
        Near(MitigationMath.Calculate(new[] { physical, fire }, 2, 1, 0, profile).Expected, 3.5,
            "physical immunity leaves the weapon's energy damage intact");
        var absorbed = SpellDamageMath.PhysicalAbsorption(physical, 1, 3, .5);
        Near(absorbed[1], .5, "protection pools absorb declined physical damage");
        Near(absorbed[2], 1.0 / 3, "declined physical absorption distribution");
        Near(absorbed[3], 1.0 / 6, "declined physical absorption maximum");
        var term = new SpellRollPart { Dice = 1, Sides = 3, Vulnerability = 1.5, FailedFactor = 1, PassedFactor = .5 };
        Near(SpellDamageMath.MeanHitPoints(1, 3, 0, false, false, false, vulnerability: 1.5).Value, 8.0 / 3,
            "area vulnerability applies to each rolled damage value");
        Near(SpellSequenceMath.Compute(new[] { term }, 1, 1, .8, .5, 100, 0).ExpectedDamage, 1.6,
            "area vulnerability, SR, and half-on-save retain separate gates");
        for (int hp = 1; hp <= 4; hp++) for (int temp = 0; temp <= 2; temp++) for (int pool = 0; pool <= 3; pool++)
        {
            term.Protections = new[] { new SpellProtection { Order = 0, Remaining = pool, PerHit = 2 } };
            double expected = 0, expectedHp = 0;
            for (int die = 1; die <= 3; die++) foreach (double factor in new[] { 1.0, .5 })
            {
                double before = Math.Max(1, Math.Floor(Math.Max(1, die * factor) * 1.5));
                double damage = before - Math.Min(before, Math.Min(pool, 2));
                expected += .8 / 6 * damage;
                expectedHp += .8 / 6 * Math.Min(hp, Math.Max(0, damage - temp));
            }
            var result = SpellSequenceMath.Compute(new[] { term }, 1, 1, .8, .5, hp, temp);
            Near(result.ExpectedDamage, expected, "area vulnerability before protection and independent SR/save");
            Near(result.HpLoss, expectedHp, "area vulnerability projected through pool, temporary HP and current HP cap");
        }
        var copied = SupportedCombatRules.Copy(profile);
        Near(copied.PhysicalDecline, .5, "defense snapshots preserve swarm decline");
        var originalStamp = SupportedCombatRules.DefenseStamp(copied);
        copied.PhysicalDecline = 1;
        assertions++; if (SupportedCombatRules.DefenseStamp(copied) == originalStamp) throw new Exception("swarm decline invalidates cached defenses");
        var criticalProfile = new DefenseProfile { Complete = true, CriticalImmune = true };
        BetaInput Input(DefenseProfile defense) => new BetaInput("actor", "swarm", "weapon", "turn", "critical-immune", 1, 100, 1,
            1, 6, 0, 20, 2, false, true, true, defense: defense);
        var input = Input(criticalProfile);
        var row = BetaModel.Build(input, "swarm-critical").Rows[0];
        Near(row.Hit, .95, "damage and critical immunity never reduce the attack hit probability");
        Near(row.Crit, 0, "swarm critical immunity removes critical damage probability");
        Near(row.AfterDefense.Expected, .95 * 3.5, "critical immunity keeps ordinary damage and hit gates");
        assertions++; if (!SupportedCombatRules.Copy(criticalProfile).CriticalImmune) throw new Exception("defense copy lost critical immunity");
        assertions++; if (!PanelPresentation.DefenseLabel(input).Contains("치명타 면역")) throw new Exception("basic panel lost critical immunity label");
        criticalProfile.PhysicalDecline = .5;
        assertions++; if (!PanelPresentation.DefenseLabel(Input(criticalProfile)).Contains("군체 물리 반감")) throw new Exception("basic panel lost swarm decline label");
        criticalProfile.Immunities.Add(DamageKind.Physical);
        assertions++; if (!PanelPresentation.DefenseLabel(Input(criticalProfile)).Contains("물리 피해 면역")) throw new Exception("basic panel lost physical immunity label");
        Console.WriteLine("PASS: " + assertions + " swarm weapon/area/defense distribution assertions");
    }
}
