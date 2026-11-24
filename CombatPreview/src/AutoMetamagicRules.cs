namespace CombatPreview
{
    internal static class AutoMetamagicRules
    {
        internal static bool? Suitable(int allowed, int abilityType, int itemKind, bool kinetic)
        {
            if (itemKind >= 0 && allowed != 5 && allowed != 6) return false;
            switch (allowed)
            {
                case -1: return abilityType == 0 || abilityType == 1;
                case 0: return abilityType == 0;
                case 1: return abilityType == 1;
                case 2: return kinetic;
                case 3: return true;
                case 4: return abilityType != 6;
                case 5: return itemKind == 3;
                case 6: return itemKind == 2;
                default: return null;
            }
        }
        internal static bool? And(params bool?[] filters)
        {
            bool unknown = false;
            foreach (var filter in filters) { if (filter == false) return false; unknown |= !filter.HasValue; }
            return unknown ? (bool?)null : true;
        }
        internal static bool? Or(bool? a, bool? b) => a == true || b == true ? true : a == false && b == false ? (bool?)false : null;
    }
}
