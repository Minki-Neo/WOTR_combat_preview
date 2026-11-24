namespace CombatPreview
{
    internal static class BuffTransitionPolicy
    {
        internal static bool Matches(bool byDescriptor, ulong incoming, ulong required, bool sameBuff,
            bool checkConditionImmunity, bool immuneToCondition)
            => !(checkConditionImmunity && immuneToCondition) && (byDescriptor ? (incoming & required) != 0 : sameBuff);

        internal static bool? Blocks(bool? ignore, bool? requiredFact)
        {
            if (ignore == true || requiredFact == false) return false;
            if (ignore == false && requiredFact == true) return true;
            return null;
        }
    }
}
