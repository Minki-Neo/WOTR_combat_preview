using System;
using CombatPreview;
static class ProjectedStatTests
{
    static int assertions;
    static ProjectedModifier Mod(int value, int descriptor = 25, bool stack = true)
        => new ProjectedModifier { Value = value, Descriptor = descriptor, Stacks = stack };
    static void Near(double actual, double expected, string message)
    { assertions++; if (Math.Abs(actual - expected) > 1e-9) throw new Exception(message); }
    public static void Run()
    {
        int reflex = ProjectedStatMath.Project(23, new ProjectedModifier[0], new[] { Mod(-4) });
        Near(SpellPreviewMath.SaveFailure(reflex, 25), .25, "a debuff applied before a later save changes its failure chance");
        Near(ProjectedStatMath.Project(23, new[] { Mod(4, 22, false) }, new[] { Mod(2, 22, false) }), 23,
            "weaker typed buff does not stack");
        Near(ProjectedStatMath.Project(23, new[] { Mod(4, 22, false) }, new[] { Mod(6, 22, false) }), 25,
            "stronger typed buff only adds its difference");
        Near(ProjectedStatMath.Sum(new[] { Mod(-4, 4, true), Mod(-6, 41, false) }), -6,
            "armor and encumbrance penalties compete per audited engine rule");
        for (int a = -4; a <= 4; a++) for (int b = -4; b <= 4; b++)
        {
            Near(ProjectedStatMath.Sum(new[] { Mod(a, 22, false), Mod(b, 22, false) }),
                Math.Max(0, Math.Max(a, b)) + Math.Min(0, Math.Min(a, b)), "typed positive/negative selection");
            Near(ProjectedStatMath.Sum(new[] { Mod(a), Mod(b) }), a + b, "stacking arithmetic");
        }
        Console.WriteLine("PASS: " + assertions + " projected stat/next-save assertions");
    }
}
