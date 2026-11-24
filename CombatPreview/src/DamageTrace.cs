using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CombatPreview
{
    internal sealed class FrozenDiagnosticJson
    {
        internal readonly string Value;
        FrozenDiagnosticJson(string value) { Value = value; }
        internal static FrozenDiagnosticJson Capture(object value) => new FrozenDiagnosticJson(DamageTrace.Json(value));
    }
    // Data-only diagnostic helpers. A sampled damage roll is never an expected value.
    public static class DamageTrace
    {
        public static int ReconstructFinal(int beforeReduction, int reduction, int? maximum)
        { return Math.Max(0, Math.Min(maximum ?? int.MaxValue, checked(beforeReduction - reduction))); }

        public static int ReplayStaticDefense(int observedBeforeReduction, DamageKind kind, DefenseProfile profile)
        {
            if (profile == null || !profile.Complete || profile.HasConsumablePool || kind == DamageKind.Unknown)
                throw new NotSupportedException("No complete static defense model");
            int reduction; profile.Reduction.TryGetValue(kind, out reduction);
            if (observedBeforeReduction < 0 || reduction < 0) throw new ArgumentOutOfRangeException();
            return profile.Immunities.Contains(kind) ? 0 : Math.Max(0, observedBeforeReduction - reduction);
        }

        // Only known DTO values; never reflect on or serialize a live game object.
        public static string Json(object value)
        {
            if (value == null) return "null";
            if (value is FrozenDiagnosticJson frozen) return frozen.Value;
            if (value is string) return DiagnosticRecorder.Quote((string)value);
            if (value is bool) return (bool)value ? "true" : "false";
            if (value is Enum) return DiagnosticRecorder.Quote(value.ToString());
            if (value is IDictionary<string, object>) return "{" + string.Join(",", ((IDictionary<string, object>)value)
                .Select(p => DiagnosticRecorder.Quote(p.Key) + ":" + Json(p.Value)).ToArray()) + "}";
            if (value is IEnumerable) return "[" + string.Join(",", ((IEnumerable)value).Cast<object>().Select(Json).ToArray()) + "]";
            if (value is int || value is long) return Convert.ToString(value, CultureInfo.InvariantCulture);
            if (value is float || value is double)
            {
                double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return double.IsNaN(number) || double.IsInfinity(number) ? "null" : number.ToString("R", CultureInfo.InvariantCulture);
            }
            throw new ArgumentException("Unsupported trace value type");
        }
    }
}
