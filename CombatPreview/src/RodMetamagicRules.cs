namespace CombatPreview
{
    internal static class RodMetamagicRules
    {
        internal static bool Applies(int rod, int available, bool swiftSpent, bool sticky,
            bool spellbookSpell, int? spellLevel, int maximum, bool whitelisted)
        {
            if (sticky) return false;
            int required = rod;
            if ((rod & 4) != 0 && swiftSpent)
            { if (rod == 4) return false; required &= ~4; }
            if ((available & required) != required) return false;
            return spellbookSpell && spellLevel.HasValue && spellLevel.Value <= maximum || whitelisted;
        }
    }
}
