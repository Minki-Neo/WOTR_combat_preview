using System;
using System.Collections.Generic;
using CombatPreview;
class Program
{
    static int assertions;
    static void Equal(double actual, double expected, string name)
    { assertions++; if (double.IsNaN(actual) || double.IsInfinity(actual) || Math.Abs(actual - expected) > 1e-9) throw new Exception(name + ": " + actual + " != " + expected); }
    static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--diagnostic-fixture")
        { ContractTests.WriteDiagnosticFixture(args[1]); return; }
        if (args.Length == 2 && args[0] == "--beta-fixture")
        { BetaTests.WriteExample(args[1]); return; }
        if (args.Length != 0) throw new ArgumentException("Usage: [--diagnostic-fixture NEW_OUTPUT_PATH | --beta-fixture NEW_OUTPUT_PATH]");
        var sword = new List<DamageTerm> { new DamageTerm(1, 8, 15, true) };
        var n = DamageMath.Summarize(sword, 1); var c = DamageMath.Summarize(sword, 2);
        Equal(n.Min, 16, "min"); Equal(n.Mean, 19.5, "mean"); Equal(n.Max, 23, "max");
        Equal(c.Mean, 39, "crit mean"); Equal(c.Min, 32, "crit min"); Equal(c.Max, 46, "crit max");
        Equal(DamageMath.Expected(.75, .075, n.Mean, c.Mean), 16.0875, "unconditional critical");
        Equal(DamageMath.Expected(0, 0, n.Mean, c.Mean), 0, "miss");
        Equal(DamageMath.Expected(1, 1, n.Mean, c.Mean), 39, "all critical");
        sword.Add(new DamageTerm(1, 6, 0, false));
        Equal(DamageMath.Summarize(sword, 1).Mean, 23, "flaming normal");
        Equal(DamageMath.Summarize(sword, 2).Mean, 42.5, "flaming not multiplied");
        Equal(DamageMath.Summarize(new[] {new DamageTerm(1, 6, -3, true)}, 1).Mean, 1.5, "clipping exact mean");
        Equal(DamageMath.Summarize(new[] {new DamageTerm(0, 1, 5, true)}, 2).Mean, 10, "constant damage");
        // Enumerate every two-dice outcome independently and compare exact moments.
        for (int a = 2; a <= 12; a++) for (int b = 2; b <= 12; b++) for (int bonus = -8; bonus <= 8; bonus++)
        {
            double sum = 0; int min = int.MaxValue, max = 0;
            for (int i=1; i<=a; i++) for (int j=1; j<=b; j++)
            { int v=Math.Max(1,i+j+bonus); sum+=v; min=Math.Min(min,v); max=Math.Max(max,v); }
            var actual = DamageMath.Summarize(new[] {new DamageTerm(1,a,bonus,true),new DamageTerm(1,b,0,false)},1);
            Equal(actual.Mean,sum/(a*b),"enumerated mean"); Equal(actual.Min,min,"enumerated min"); Equal(actual.Max,max,"enumerated max");
        }
        var fair = D20.Pmf(null, d => d < 6);
        Equal(D20.P(fair, d => d >= 6), .75, "+22 vs AC28");
        Equal(D20.P(fair, d => d >= 11), .50, "iterative -5");
        Equal(D20.P(fair, d => d >= 16), .25, "iterative -10");
        Equal(D20.P(fair, d => d == 20), .05, "natural 20");
        Equal(D20.P(fair, d => d > 1), .95, "natural 1");
        var advantage = D20.Pmf(new List<D20Modifier>{new D20Modifier{Rerolls=1,TakeBest=true}},d=>d<6);
        Equal(D20.P(advantage,d=>d>=6), .9375, "best of two");
        bool rejected = false;
        try { DamageMath.Expected(.2,.3,1,2); } catch (ArgumentOutOfRangeException) { rejected=true; }
        Equal(rejected ? 1 : 0,1,"invalid probability rejected");
        var fire = new DamageTerm(1, 6, 0, false) { Kind = DamageKind.Fire };
        var physical = new DamageTerm(1, 8, 15, true) { Kind = DamageKind.Physical };
        var defense = new DefenseProfile { Complete = true, Source = "test snapshot" };
        defense.Reduction[DamageKind.Fire] = 5;
        defense.Reduction[DamageKind.Physical] = 10;
        var mixed = MitigationMath.Calculate(new[] {physical, fire},2,1,0,defense);
        Equal(mixed.Available ? 1 : 0,1,"mitigation available");
        Equal(mixed.Normal.Mean,9.5+1.0/6,"typed resistance distribution");
        Equal(mixed.Critical.Mean,29+1.0/6,"critical physical and unmultiplied fire");
        Equal(mixed.Normal.Min,6,"mitigated min"); Equal(mixed.Normal.Max,14,"mitigated max");
        var critical = MitigationMath.Calculate(new[] {physical,fire},2,.75,.075,defense);
        Equal(critical.Expected,.675*(9.5+1.0/6)+.075*(29+1.0/6),"mitigated unconditional crit");
        defense.Immunities.Add(DamageKind.Fire);
        Equal(MitigationMath.Calculate(new[] {physical,fire},2,1,0,defense).Normal.Mean,9.5,"energy immunity");
        defense.Immunities.Add(DamageKind.Physical);
        Equal(MitigationMath.Calculate(new[] {physical,fire},2,1,0,defense).Expected,0,"total immunity");
        defense.HasConsumablePool=true;
        Equal(MitigationMath.Calculate(new[] {physical},2,1,0,defense).Available?1:0,0,"pool unsupported not zero");
        defense.HasConsumablePool=false; defense.Complete=false;
        Equal(MitigationMath.Calculate(new[] {physical},2,1,0,defense).Available?1:0,0,"unknown defense not zero");
        defense.Complete=true;
        Equal(MitigationMath.Calculate(new[] {new DamageTerm(1,6,0,true)},2,1,0,defense).Available?1:0,0,"unknown damage type");
        defense.Immunities.Clear();
        Equal(defense.Reduction[DamageKind.Physical],10,"snapshot not mutated");
        // The reduction applies once to the same-type damage group, not per die component.
        defense.Reduction[DamageKind.Fire]=5;
        var twoFire = MitigationMath.Calculate(new[] {fire,fire},2,1,0,defense);
        double twoFireOracle=0;
        for(int a=1;a<=6;a++) for(int b=1;b<=6;b++) twoFireOracle+=Math.Max(0,a+b-5)/36.0;
        Equal(twoFire.Normal.Mean,twoFireOracle,"same-type group once");
        // Sweep all single-die thresholds against a separate enumeration oracle.
        for(int sides=2;sides<=20;sides++) for(int resistance=0;resistance<=25;resistance++)
        {
            defense.Reduction[DamageKind.Fire]=resistance;
            double expected=0;
            for(int d=1;d<=sides;d++) expected+=Math.Max(0,d-resistance)/(double)sides;
            Equal(MitigationMath.Calculate(new[] {new DamageTerm(1,sides,0,false){Kind=DamageKind.Fire}},2,1,0,defense).Expected,
                expected,"resistance sweep");
        }
        bool invalidMean=false;
        try { DamageMath.Expected(.5,.1,double.NaN,2); } catch(ArgumentOutOfRangeException) { invalidMean=true; }
        Equal(invalidMean?1:0,1,"NaN mean rejected");
        Console.WriteLine("PASS: " + assertions + " original math assertions");
        ContractTests.Run();
        BetaTests.Run();
        NativeDefenseTests.Run();
        SpellPreviewTests.Run();
    }
}
