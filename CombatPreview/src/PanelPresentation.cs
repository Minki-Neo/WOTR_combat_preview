using System;
using System.Globalization;
using System.Linq;

namespace CombatPreview
{
    // Presentation math only; no Unity/game objects, assets, event replay or RNG.
    public static class PanelPresentation
    {
        public static string DiceLines(BetaInput input)
        {
            return string.Join(" +\n", input.Components.Select(c => c.ToTerm().ToString().Replace("d", "D")
                + (c.Kind == DamageKind.Physical ? "" : " (" + Kind(c.Kind) + ")")).ToArray());
        }
        static string Kind(DamageKind kind)
        {
            switch (kind)
            {
                case DamageKind.Fire: return "화염";
                case DamageKind.Cold: return "냉기";
                case DamageKind.Electricity: return "전기";
                case DamageKind.Acid: return "산성";
                case DamageKind.Sonic: return "음파";
                case DamageKind.Force: return "역장";
                case DamageKind.Unknown: return "유형 미확인";
                default: return kind.ToString();
            }
        }
        public static string Value(double? number)
        { return number.HasValue ? number.Value.ToString("0.00", CultureInfo.InvariantCulture) : "—"; }
        public static double? TotalRaw(BetaSnapshot snapshot)
        { return snapshot.Rows.All(r => r.ExpectedRaw.HasValue) ? snapshot.Rows.Sum(r => r.ExpectedRaw.Value) : (double?)null; }
        public static void Place(float screenWidth, float screenHeight, float width, float height,
            float actionBarTop, out float x, out float y)
        {
            x = Math.Max(0, (screenWidth - width) / 2);
            y = Math.Max(0, Math.Min(screenHeight - height, actionBarTop - height - 12));
        }
    }
    // Copies only scalars from an already completed GAME-created estimate.
    // No fake event is created, executed, or counted as a real attack by the mod.
    public sealed class ObservedPreviewCache
    {
        BetaInput key, value;
        long captured;
        public void Clear() { key = value = null; }
        public void Store(BetaInput original, BetaInput observed, long now)
        {
            if (original == null || observed == null || original.Actor != observed.Actor || original.Target != observed.Target
                || original.Weapon != observed.Weapon || original.Turn != observed.Turn || original.FullAttack != observed.FullAttack)
                throw new ArgumentException("Observed estimate identity differs from its inputs");
            key = original; value = observed; captured = now;
        }
        public BetaInput Read(BetaInput current, long now)
        {
            if (key == null || now < captured || now - captured > 2000 || key.Signature != current.Signature
                || key.Actor != current.Actor || key.Target != current.Target || key.Weapon != current.Weapon
                || key.Turn != current.Turn || key.FullAttack != current.FullAttack) return current;
            return value;
        }
    }
}
