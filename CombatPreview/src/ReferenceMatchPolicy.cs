using System;
namespace CombatPreview
{
    public static class ReferenceMatchPolicy
    {
        public static string Status(bool available, bool sameIdentity, double ageSeconds, bool sameState, bool stableReference)
        {
            if (!available) return "NotObserved";
            if (!sameIdentity) return "IdentityMismatch";
            if (double.IsNaN(ageSeconds) || double.IsInfinity(ageSeconds) || ageSeconds < 0 || ageSeconds > .35) return "Expired";
            if (!stableReference) return "ReferenceChangedState";
            if (!sameState) return "StateChanged";
            return "ComparablePartialModel";
        }
    }
}
