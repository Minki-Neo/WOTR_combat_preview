namespace CombatPreview
{
    internal static class SwarmRules
    {
        // Values copied from SwarmDamageResistance. Kinetic blasts and bundles
        // without a weapon return before the physical decline is installed.
        internal static double WeaponPhysicalFactor(bool hasWeapon, bool kineticBlast, bool exception,
            bool diminutiveOrLower, int form, bool formKnown)
        {
            if (!hasWeapon || kineticBlast || exception) return 1;
            if (diminutiveOrLower) return 0;
            if (!formKnown) return double.NaN;
            return (form & 1) != 0 ? 1 : .5; // Any bludgeoning form bypasses Tiny decline.
        }
        internal static bool AreaVulnerable(bool nonSimpleProjectile, bool targetsAround, bool blueprintAreaContext)
            => nonSimpleProjectile || targetsAround || blueprintAreaContext;
    }
}
