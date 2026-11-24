using System;
using System.Collections.Generic;

namespace CombatPreview
{
    internal static class ActionGraphScan
    {
        internal static bool Visit<T>(IEnumerable<T> roots, Func<T, IEnumerable<T>> children,
            Action<T> visit, out int visited, int maximumDepth = 12, int maximumNodes = 512) where T : class
        {
            visited = 0;
            var stack = new Stack<(T Node, int Depth)>();
            foreach (var root in roots) if (root != null) stack.Push((root, 0));
            var seen = new HashSet<T>();
            bool complete = true;
            while (stack.Count > 0)
            {
                var entry = stack.Pop();
                if (!seen.Add(entry.Node)) continue;
                if (entry.Depth > maximumDepth) { complete = false; continue; }
                if (visited >= maximumNodes) return false;
                visited++; visit(entry.Node);
                foreach (var child in children(entry.Node))
                    if (child != null) stack.Push((child, entry.Depth + 1));
            }
            return complete;
        }
    }
}
