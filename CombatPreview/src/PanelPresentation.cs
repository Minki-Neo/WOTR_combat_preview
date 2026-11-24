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
        public static double? TotalRaw(System.Collections.Generic.IEnumerable<BetaSnapshot> snapshots)
        {
            bool any = false; double total = 0;
            foreach (var snapshot in snapshots)
            {
                if (snapshot == null) return null;
                var value = TotalRaw(snapshot);
                if (!value.HasValue) return null;
                total += value.Value; any = true;
            }
            return any ? total : (double?)null;
        }
        public static void Place(float screenWidth, float screenHeight, float width, float height,
            float actionBarTop, out float x, out float y)
        {
            x = Math.Max(0, (screenWidth - width) / 2);
            y = Math.Max(0, Math.Min(screenHeight - height, actionBarTop - height - 12));
        }
        public static void PlaceLeft(float screenWidth, float screenHeight, float width, float height, out float x, out float y)
        { PlaceLeft(screenWidth, screenHeight, width, height, screenHeight * .78f, out x, out y); }
        public static void PlaceLeft(float screenWidth, float screenHeight, float width, float height,
            float portraitTop, out float x, out float y)
        {
            x = 0;
            y = Math.Max(0, Math.Min(screenHeight - height, portraitTop - height - 16));
        }
        public static float CompactHeight(int rows, int components, bool details, float screenHeight)
        {
            float row = Math.Max(34, components * 20 + 8) + (details ? 21 : 0);
            return Math.Min(420, Math.Min(screenHeight * .6f, 145 + rows * row));
        }
        // Visual segment of mean predicted damage, not a confidence interval or kill probability.
        public static string DefenseLabel(BetaInput input)
        {
            var profile = input.Defense;
            int applied = 0;
            if (profile != null) profile.Reduction.TryGetValue(DamageKind.Physical, out applied);
            var evidence = input.DefenseNotes.Where(n => n.Contains(": 물리 DR ")).Take(3)
                .Select(n => n.Substring(n.IndexOf(": 물리 DR ", StringComparison.Ordinal) + 2)).ToArray();
            string result = profile != null && profile.Complete && !profile.HasConsumablePool
                ? "이 무기에 적용 DR " + applied : "이 무기에 적용 DR 미확인";
            if (profile != null && profile.Immunities.Contains(DamageKind.Physical)) result += " · 물리 피해 면역";
            else if (profile?.PhysicalDecline < 1) result += " · 군체 물리 반감";
            if (profile?.CriticalImmune == true) result += " · 치명타 면역";
            if (profile?.PhysicalPool is (int Remaining, int PerHit) pool)
                result += " · 물리 보호막 " + pool.Remaining + " (타격당 최대 " + pool.PerHit + ")";
            return evidence.Length == 0 ? result : string.Join(" · ", evidence) + " | " + result;
        }
        public static double HealthLoss(long current, int maximum, int temporary, double? expected)
        {
            if (maximum <= 0 || temporary > 0 || !expected.HasValue || double.IsNaN(expected.Value)
                || double.IsInfinity(expected.Value) || expected.Value < 0) return 0;
            return Math.Min(Math.Max(0, Math.Min((double)current, maximum)), expected.Value);
        }
        public static float HealthLossPixels(float lossPixels, float currentPixels)
            => lossPixels > 0 && currentPixels > 0 ? Math.Min(currentPixels, Math.Max(1.5f, lossPixels)) : 0;
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
