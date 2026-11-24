using System;
using System.Collections.Generic;

namespace CombatPreview
{
    internal struct ContextParameterSnapshot
    { internal int Dc, Level, SpellLevel, Metamagic; }
    internal static class ContextParameterRules
    {
        internal static object Read(object context, Func<object, string, object> field)
        {
            var visited = new HashSet<object>();
            for (int depth = 0; context != null && depth < 16 && visited.Add(context); depth++)
            {
                var parameters = field(context, "m_Params");
                if (parameters != null) return parameters;
                context = field(context, "<ParentContext>k__BackingField");
            }
            return null;
        }
        internal static bool Merge(ref ContextParameterSnapshot? selected, ContextParameterSnapshot incoming, int selectedMetamagic)
        {
            if (incoming.Dc < 0 || incoming.Level < 1 || incoming.SpellLevel < 0
                || (incoming.Metamagic | selectedMetamagic) != selectedMetamagic) return false;
            if (selected.HasValue && (selected.Value.Dc != incoming.Dc || selected.Value.Level != incoming.Level
                || selected.Value.SpellLevel != incoming.SpellLevel || selected.Value.Metamagic != incoming.Metamagic)) return false;
            selected = incoming; return true;
        }
    }
}
