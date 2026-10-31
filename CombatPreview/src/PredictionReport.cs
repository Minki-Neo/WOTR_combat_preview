using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatPreview
{
    public sealed class PredictionReport
    {
        public PredictionQuality Quality { get; private set; }
        public double? Expected { get; private set; }
        public string Source { get; private set; }
        public IReadOnlyList<string> Reasons { get; private set; }
        public PredictionReport(PredictionQuality quality, double? expected, string source, params string[] reasons)
        {
            if (!Enum.IsDefined(typeof(PredictionQuality), quality) || string.IsNullOrWhiteSpace(source)) throw new ArgumentException("Invalid report");
            if (quality == PredictionQuality.Unavailable) expected = null;
            if (expected.HasValue && (double.IsNaN(expected.Value) || double.IsInfinity(expected.Value) || expected < 0))
                throw new ArgumentOutOfRangeException("expected");
            if (!expected.HasValue) quality = PredictionQuality.Unavailable;
            Quality = quality; Expected = expected; Source = source;
            Reasons = Array.AsReadOnly((reasons ?? new string[0]).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToArray());
        }
        public static PredictionReport Total(IList<PredictionReport> rows, int enumeratedAttackCount)
        {
            if (rows == null || rows.Count == 0 || rows.Count != enumeratedAttackCount || rows.Any(r => r == null))
                return new PredictionReport(PredictionQuality.Unavailable, null, "SERIES", "Attack enumeration is incomplete");
            if (rows.Select(r => r.Source).Distinct().Count() != 1)
                return new PredictionReport(PredictionQuality.Unavailable, null, "SERIES", "Mixed result sources/scopes cannot be totaled");
            var reasons = rows.SelectMany(r => r.Reasons).Distinct().ToArray();
            if (rows.Any(r => !r.Expected.HasValue))
                return new PredictionReport(PredictionQuality.Unavailable, null, rows[0].Source,
                    reasons.Concat(new[] { "At least one attack is unavailable; no partial sum presented as a full attack" }).ToArray());
            var quality = rows.Any(r => r.Quality == PredictionQuality.PartialEstimate) ? PredictionQuality.PartialEstimate : PredictionQuality.SupportedModel;
            return new PredictionReport(quality, rows.Sum(r => r.Expected.Value), rows[0].Source, reasons);
        }
    }
}
