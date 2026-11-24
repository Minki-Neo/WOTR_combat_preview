using System;
using System.Collections.Generic;

namespace CombatPreview
{
    internal static class HealthProjectionSelection
    {
        static Dictionary<string, object> Data(Dictionary<string, object> metadata, string key)
            => metadata.TryGetValue(key, out var value) ? value as Dictionary<string, object> : null;
        static double? Number(Dictionary<string, object> data, string key)
            => data != null && data.TryGetValue(key, out var value) && value is double number
                && !double.IsNaN(number) && !double.IsInfinity(number) && number >= 0 ? (double?)number : null;
        internal static double? MeanLoss(long hp, int maximum, int temporary, double? mean)
            => maximum > 0 && mean.HasValue && mean.Value >= 0 && !double.IsInfinity(mean.Value) && !double.IsNaN(mean.Value)
                ? (double?)Math.Min(Math.Max(0, Math.Min(hp, maximum)), Math.Max(0, mean.Value - Math.Max(0, temporary))) : null;
        internal static double? MeanTemporaryLoss(int temporary, double? mean)
            => mean.HasValue && mean.Value >= 0 && !double.IsInfinity(mean.Value) && !double.IsNaN(mean.Value)
                ? (double?)Math.Min(Math.Max(0, temporary), mean.Value) : null;
        internal static double? TemporaryLoss(Dictionary<string, object> metadata, int temporary, string basis)
        {
            if (temporary <= 0) return 0;
            if (basis == "secondary-effects-unresolved") return null;
            var pit = Data(metadata, "pitPreview"); var stage = Data(metadata, "stagedOutcome"); var branch = Data(metadata, "branchPreview");
            var source = basis == "initial-pit-fall" ? pit
                : basis == "staged-damage-and-instant-death" ? stage
                : basis == "conditional-action-tree-distribution" ? branch : null;
            if (source != null) return Number(source, "expectedTemporaryHpLoss");
            if (basis == "mean-state-hp-display")
                foreach (var candidate in pit != null ? new[] { pit } : new[] { stage, branch })
                {
                    var loss = MeanTemporaryLoss(temporary, Number(candidate, "expectedDamage"));
                    if (loss.HasValue) return loss;
                }
            return null;
        }
        internal static double? Select(Dictionary<string, object> metadata, long hp, int maximum, int temporary,
            out bool authoritative, out bool approximate, out string basis)
        {
            approximate = false; basis = null;
            var pit = Data(metadata, "pitPreview"); var stage = Data(metadata, "stagedOutcome"); var branch = Data(metadata, "branchPreview");
            authoritative = pit != null || stage != null || branch != null;
            var secondary = Data(metadata, "secondaryTargetEffects");
            if (secondary != null && secondary.TryGetValue("status", out var status) && Equals(status, "Unmodeled"))
            { authoritative = true; basis = "secondary-effects-unresolved"; return null; }
            var sources = pit != null ? new[] { pit } : new[] { stage, branch };
            var labels = pit != null ? new[] { "initial-pit-fall" }
                : new[] { "staged-damage-and-instant-death", "conditional-action-tree-distribution" };
            for (int i = 0; i < sources.Length; i++)
            {
                var loss = Number(sources[i], "expectedHpLoss");
                if (loss.HasValue) { basis = labels[i]; return Math.Min(Math.Max(0, hp), loss.Value); }
            }
            // Only a complete branch mean; never substitute one damage branch
            // for an unknown instant-death probability or collateral effects.
            foreach (var source in sources)
            {
                var loss = MeanLoss(hp, maximum, temporary, Number(source, "expectedDamage"));
                if (loss.HasValue) { approximate = true; basis = "mean-state-hp-display"; return loss; }
            }
            return null;
        }
    }
}
