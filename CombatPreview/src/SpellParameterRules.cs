namespace CombatPreview
{
    internal static class SpellParameterRules
    {
        internal static bool DescriptorMatches(ulong spell, ulong required) => (spell & required) != 0;
        internal static bool InheritedParametersValid(int dc, int level, int inheritedMeta, int selectedMeta)
            => dc >= 0 && level > 0 && inheritedMeta == selectedMeta;
        // Event registration order is not certified by fact enumeration. Equal
        // replacements are safe; conflicting replacements need observed input.
        internal static bool TrySelectStat(ref string selected, string candidate)
        {
            switch (candidate)
            {
                case "Strength": case "Dexterity": case "Constitution": case "Intelligence":
                case "Wisdom": case "Charisma": case "BaseAttackBonus": break;
                default: return false;
            }
            if (selected != null && selected != candidate) return false;
            selected = candidate; return true;
        }
    }
}
