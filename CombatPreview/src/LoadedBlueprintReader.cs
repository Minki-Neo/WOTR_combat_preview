using System;
using System.Collections;
using System.Threading;
using Kingmaker.Blueprints;

namespace CombatPreview
{
    internal static class LoadedBlueprintReader
    {
        // Existing reference or already-loaded dictionary entry only. Never Get,
        // TryGetBlueprint, Load or write back into a game reference.
        internal static SimpleBlueprint Read(object reference)
        {
            var cached = CombatFields.Read(reference, "<Cached>k__BackingField") as SimpleBlueprint;
            if (cached != null || reference == null) return cached;
            var guid = CombatFields.ReferenceGuid(reference);
            if (!guid.HasValue) return null;
            var cache = CombatFields.Field(typeof(ResourcesLibrary), "BlueprintsCache")?.GetValue(null);
            var entries = CombatFields.Read(cache, "m_LoadedBlueprints") as IDictionary;
            var gate = CombatFields.Read(cache, "m_Lock");
            if (entries == null || gate == null || !Monitor.TryEnter(gate)) return null;
            try
            {
                var key = new BlueprintGuid(guid.Value);
                return entries.Contains(key) ? CombatFields.Read(entries[key], "Blueprint") as SimpleBlueprint : null;
            }
            finally { Monitor.Exit(gate); }
        }
    }
}
