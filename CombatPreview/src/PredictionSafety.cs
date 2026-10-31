using System;

namespace CombatPreview
{
    // API presence is not evidence of state/RNG isolation. There is deliberately
    // no settings toggle or user-supplied version string that enables legacy replay.
    public static class PredictionSafety
    {
        public const string BlockReason = "Legacy event replay is blocked: game/mod state and RNG isolation are unverified.";
        public static bool CanReplayEvents { get { return false; } }
        public static void RequireReadOnlyPrediction()
        { throw new NotSupportedException(BlockReason); }
    }
}
