namespace CombatPreview
{
    internal static class SpellTargetEligibility
    {
        // AbilityTargetsAround: Enemy=0, Ally=1, Any=2. Unknown delivery
        // semantics remain candidates rather than silently dropping damage.
        internal static bool Allows(int? areaType, bool enemy, bool ally, bool self,
            bool pointTarget, bool enemies, bool friends, bool caster, bool uncertain)
        {
            if (uncertain) return true;
            if (areaType.HasValue)
                return areaType == 0 ? enemy : areaType == 1 ? ally : true;
            if (pointTarget) return true;
            if (self) return caster;
            if (enemy) return enemies;
            if (ally) return friends;
            return true;
        }
    }
}
