using System;

namespace CombatPreview
{
    internal static class BlueprintReferenceIdentity
    {
        internal static Guid? Read(object reference, Func<object, string, object> field) => Select(
            field(field(reference, "deserializedGuid"), "m_Guid") as Guid?, field(reference, "guid") as string);
        // Runtime Guid is authoritative, including Empty. Serialized text is
        // cleared by OnAfterDeserialize and may be stale after a runtime change.
        internal static Guid? Select(Guid? runtimeGuid, string serializedGuid)
        {
            if (runtimeGuid.HasValue) return runtimeGuid.Value == Guid.Empty ? null : runtimeGuid;
            return Guid.TryParse(serializedGuid, out var parsed) && parsed != Guid.Empty ? (Guid?)parsed : null;
        }
    }
}
