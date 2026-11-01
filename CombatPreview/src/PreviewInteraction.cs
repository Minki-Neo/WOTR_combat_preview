using System;
using System.Collections.Generic;

namespace CombatPreview
{
    public static class AttackThreshold
    {
        public static int Required(int ab, int ac) { return (int)Math.Max(2L, Math.Min(20L, (long)ac - ab)); }
        public static string Describe(int ab, int ac)
        {
            int needed = Required(ab, ac);
            return needed == 20 ? "자연 20만 명중" : "d20 " + needed + " 이상 (자연 1 실패)";
        }
    }
    public sealed class PanelRetention
    {
        string actor, turn, target;
        long lastHover;
        public bool Pinned { get; private set; }
        public void Hover(string a, string t, string victim, long now)
        {
            if (actor != a || turn != t || target != victim) Pinned = false;
            actor = a; turn = t; target = victim; lastHover = now;
        }
        public void TogglePin() { if (target != null) Pinned = !Pinned; }
        public bool Keep(string a, string t, string victim, bool valid, bool overPanel, long now)
        { return valid && target != null && actor == a && turn == t && target == victim && now >= lastHover
            && (Pinned || overPanel || now - lastHover <= 1200); }
        public void Clear() { actor = turn = target = null; Pinned = false; }
    }
    // Scalars from completed existing rules, never constructed or replayed rules.
    public sealed class CompletedRuleCache
    {
        sealed class Entry { internal int Value; internal long Time; }
        readonly Dictionary<string, Entry> values = new Dictionary<string, Entry>();
        public void Put(string key, int value, long now)
        {
            if (values.Count >= 128 && !values.ContainsKey(key)) values.Clear();
            values[key] = new Entry { Value = value, Time = now };
        }
        public int? Read(string key, long now)
        {
            Entry entry;
            return values.TryGetValue(key, out entry) && now >= entry.Time && now - entry.Time <= 1500 ? entry.Value : (int?)null;
        }
        public void Clear() { values.Clear(); }
    }
}
