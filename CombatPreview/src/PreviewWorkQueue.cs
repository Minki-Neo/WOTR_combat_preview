using System.Collections.Generic;
using System.Linq;

namespace CombatPreview
{
    // A cycle tracks completed members separately from pending members, so
    // repeated target observations do not restart or starve a large AoE batch.
    internal sealed class PreviewWorkQueue<T> where T : class
    {
        internal const int MaximumPerFrame = 2;
        internal const double SoftBudgetSeconds = .003;
        readonly List<T> pending = new List<T>();
        readonly HashSet<T> scheduled = new HashSet<T>();
        internal int PendingCount => pending.Count;
        internal void Clear() { pending.Clear(); scheduled.Clear(); }
        internal void Begin(IEnumerable<T> targets, T priority)
        { Clear(); Reconcile(targets, priority); }
        internal void Reconcile(IEnumerable<T> targets, T priority)
        {
            var wanted = new HashSet<T>(targets.Where(target => target != null));
            pending.RemoveAll(target => !wanted.Contains(target)); scheduled.IntersectWith(wanted);
            foreach (var target in targets) if (target != null && scheduled.Add(target)) pending.Add(target);
            if (priority != null && pending.Remove(priority)) pending.Insert(0, priority);
        }
        internal bool Take(out T target)
        {
            target = null; if (pending.Count == 0) return false;
            target = pending[0]; pending.RemoveAt(0); return true;
        }
        internal void RefreshPriority(T target)
        {
            if (target == null || !scheduled.Contains(target)) return;
            pending.Remove(target); pending.Insert(0, target);
        }
        internal static bool CanContinue(int processed, double elapsedSeconds)
            => processed < 1 || processed < MaximumPerFrame && elapsedSeconds < SoftBudgetSeconds;
    }
}
