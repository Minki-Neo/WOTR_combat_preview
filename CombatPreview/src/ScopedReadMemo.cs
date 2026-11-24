using System;
using System.Collections.Generic;

namespace CombatPreview
{
    // Reuse reads only inside one synchronous observer/capture. Never across
    // callbacks or frames, where game state may have changed.
    public static class ScopedReadMemo<TKey, TValue>
    {
        [ThreadStatic] static Dictionary<TKey, TValue> current;
        public struct Scope : IDisposable
        {
            readonly Dictionary<TKey, TValue> previous;
            internal Scope(Dictionary<TKey, TValue> previous) { this.previous = previous; }
            public void Dispose() { current = previous; }
        }
        public static Scope Begin()
        {
            var scope = new Scope(current);
            current = new Dictionary<TKey, TValue>();
            return scope;
        }
        public static Scope BeginShared()
        {
            var scope = new Scope(current);
            if (current == null) current = new Dictionary<TKey, TValue>();
            return scope;
        }
        public static bool TryGet(TKey key, out TValue value)
        { value = default(TValue); return current != null && current.TryGetValue(key, out value); }
        public static void Put(TKey key, TValue value) { if (current != null) current[key] = value; }
    }
}
