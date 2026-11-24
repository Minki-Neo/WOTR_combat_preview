using System.Linq;
namespace CombatPreview
{
    internal static class SpellProtectionReader
    {
        internal static SpellProtection[] Read(DefenseProfile profile, DamageKind kind)
        {
            var sources = kind == DamageKind.Physical ? profile.PhysicalSequenceSources
                : profile.EnergySequenceSources.TryGetValue(kind, out var energy) ? energy : null;
            if (sources == null || sources.Count == 0) return null;
            // With just one source, registration order cannot change selection.
            return sources.Select(source => new SpellProtection { Order = sources.Count == 1 && source.Order < 0 ? 1000000 + (int)kind : source.Order,
                Remaining = source.Remaining, PerHit = source.PerHit }).ToArray();
        }
    }
}
