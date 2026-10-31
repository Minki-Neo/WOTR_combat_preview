using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Parts;
using Kingmaker.UnitLogic.FactLogic;

namespace CombatPreview
{
    // Inspection draft for the supplied Assembly-CSharp. Read existing fields
    // only; never initialize a missing part or evaluate a ContextValue/virtual DR
    // method. Cached values are evidence, not effective defense for a new attack.
    internal static class VanillaDefenseCapture
    {
        static readonly Type PartType = typeof(UnitPartDamageReduction);
        static readonly FieldInfo Initialized = AccessTools.Field(PartType, "m_Initialized");
        static readonly FieldInfo Chunks = AccessTools.Field(PartType, "m_Chunks");
        static readonly FieldInfo Immunities = AccessTools.Field(PartType, "m_Immunities");
        internal static DefenseObservation Capture(UnitEntityData target, string targetId)
        {
            var o = new DefenseObservation {
                Origin = SnapshotOrigin.Target, Rules = RulesImplementation.Unknown,
                GameBuild = Compatibility.GameVersion + " MVID=" + PartType.Module.ModuleVersionId,
                TargetId = targetId, Scope = "cached field evidence only",
                HasUnknownModifiers = true
            };
            o.Missing.Add("Live bypass, stacking, suppression, damage grouping and difficulty semantics are unverified");
            if (Compatibility.HasTabletopTweaks)
            { o.Missing.Add("TTT assembly loaded: vanilla capture refused; activation must be established separately"); return o; }
            // No TTT assembly is not proof that other mods leave vanilla rules intact.
            o.Missing.Add("Other rule handlers/patches and full condition revision are not certified");
            if (target == null || Initialized == null || Chunks == null || Immunities == null)
            { o.Missing.Add("Target or private defense field layout unavailable"); return o; }
            var part = target.Get<UnitPartDamageReduction>();
            if (part == null)
            { o.Missing.Add("No existing damage reduction part; absence does not certify zero defenses"); return o; }
            if (!(bool)Initialized.GetValue(part))
            { o.Missing.Add("Existing defense part is uninitialized; AllSources/TryInitialize deliberately not invoked"); return o; }
            var chunks = Chunks.GetValue(part) as IList;
            var immunities = Immunities.GetValue(part) as IList;
            if (chunks == null || immunities == null || chunks.Count > 128 || immunities.Count > 128)
            { o.Missing.Add("Defense list layout or inspection budget exceeded"); return o; }
            foreach (object chunk in chunks)
            {
                if (chunk == null) { o.Missing.Add("Null defense chunk"); continue; }
                Type type = chunk.GetType();
                object value = Field(type, "<Reduction>k__BackingField", chunk);
                object applied = Field(type, "<AppliedReduction>k__BackingField", chunk);
                object runtime = Field(type, "<DR>k__BackingField", chunk);
                object pool = runtime == null ? null : Field(runtime.GetType(), "m_RemainPool", runtime);
                var settings = runtime == null ? null : Field(runtime.GetType(), "<SourceBlueprintComponent>k__BackingField", runtime) as AddDamageResistanceBase;
                bool? usePool = settings == null ? (bool?)null : settings.UsePool;
                var poolSnapshot = new ProtectionPoolSnapshot(usePool, pool as int?);
                o.ObservedDetails.Add("UNVERIFIED cached chunk: reduction=" + (value ?? "unknown")
                    + ", applied=" + (applied ?? "unknown") + ", usePool=" + (usePool.HasValue ? usePool.Value.ToString() : "unknown")
                    + ", poolPresence=" + poolSnapshot.Presence + ", remainingPoolField=" + (pool ?? "unknown")
                    + ", component=" + (settings == null ? "unknown" : settings.GetType().FullName));
                if (poolSnapshot.Presence == ProtectionPoolPresence.Present) o.HasConsumablePool = true;
                if (poolSnapshot.Presence == ProtectionPoolPresence.Unknown) o.Missing.Add("Pool usage flag is unavailable; cached pool amount is not proof of a pool");
                // Never assign Kind/reduction/bypass from cached numbers alone.
            }
            foreach (object immunity in immunities)
            {
                object type = immunity == null ? null : Field(immunity.GetType(), "Type", immunity);
                o.ObservedDetails.Add("UNVERIFIED registered immunity type=" + (type ?? "unknown") + "; suppression unresolved");
            }
            return o;
        }
        static object Field(Type type, string name, object owner)
        { return AccessTools.Field(type, name)?.GetValue(owner); }
    }
}
