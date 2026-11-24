using System;
namespace CombatPreview
{
    public static class SpellPreviewMath
    {
        // Saves/attacks have natural 1/20 rules; SR caster-level checks do not.
        public static double SaveFailure(int bonus, int dc, bool persistent = false)
        {
            int pass = 0;
            for (int die = 1; die <= 20; die++)
                if (die == 20 || (die != 1 && (long)die + bonus >= dc)) pass++;
            double success = pass / 20.0;
            return 1 - (persistent ? success * success : success);
        }
        internal static bool AttackHits(int die, int bonus, int ac, bool alwaysChance)
            => die == 20 || (die > 1 || alwaysChance) && (long)die + bonus >= ac;

        internal static double RepeatedMiss(double miss, int rerolls) => Math.Pow(miss, 1 + rerolls);

        public static double AttackHit(int bonus, int ac, bool alwaysChance = false)
        {
            int hits = 0;
            for (int die = 1; die <= 20; die++)
                if (AttackHits(die, bonus, ac, alwaysChance)) hits++;
            return hits / 20.0;
        }
        public static double ResistancePass(int penetration, int sr)
        {
            int pass = 0;
            for (int die = 1; die <= 20; die++) if ((long)die + penetration >= sr) pass++;
            return pass / 20.0;
        }
    }
}

namespace CombatPreview
{
    // This only retains game-reported inclusion; it performs no geometry tests.
    public sealed class SpellTargetWindow<T>
    {
        readonly System.Collections.Generic.Dictionary<T, double> entries = new System.Collections.Generic.Dictionary<T, double>();
        object selection, aim;
        public void Observe(object selected, object point, T target, bool included, double now)
        {
            if (!Equals(selection, selected) || !Equals(aim, point)) { entries.Clear(); selection = selected; aim = point; }
            if (!included) { entries.Remove(target); return; }
            if (entries.Count < 128 || entries.ContainsKey(target)) entries[target] = now;
        }
        public T[] Read(object selected, double now)
        {
            if (!Equals(selection, selected)) return new T[0];
            var result = new System.Collections.Generic.List<T>();
            foreach (var pair in entries) if (now >= pair.Value && now - pair.Value <= .25) result.Add(pair.Key);
            return result.ToArray();
        }
        public void Clear() { entries.Clear(); selection = aim = null; }
    }
}
