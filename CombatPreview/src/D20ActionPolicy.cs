using System;

namespace CombatPreview
{
    internal static class D20ActionPolicy
    {
        static readonly Guid misfortuneCooldown = new Guid("9661368281d2417e884223bcc74c7f9b");
        static readonly Guid fortuneCooldown = new Guid("82eaed601b044a55a7b2851c5451f386");
        internal static bool PassiveMarker(Guid? identity, bool supportedBuild, bool loaded,
            bool loadedBuff, int componentCount) => loaded ? loadedBuff && componentCount == 0
                : supportedBuild && identity.HasValue
                    && (identity.Value == misfortuneCooldown || identity.Value == fortuneCooldown);
    }
}
