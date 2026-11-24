using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace CombatPreview
{
    internal static class PreviewPerformance
    {
        internal static double NowSeconds => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        sealed class Metric { internal long Count, Ticks, Maximum, MaximumWithoutGC, GCIntervals, Over4ms, Over16ms, AllocationSamples, Allocated, MaximumAllocated; internal int MaxFrame, MaxGC0; }
        static readonly Dictionary<string, Metric> metrics = new Dictionary<string, Metric>();
        static readonly Func<long> allocatedBytes = AllocationCounter();
        static Func<long> AllocationCounter()
        {
            try
            {
                var method = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", Type.EmptyTypes);
                if (method == null) return null;
                var counter = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), method);
                counter(); return counter;
            }
            catch (Exception) { return null; }
        }
        internal struct Sample : IDisposable
        {
            readonly string name;
            readonly long start;
            readonly int gc0;
            readonly long allocationStart;
            internal Sample(string name) { this.name = name; gc0 = GC.CollectionCount(0); allocationStart = allocatedBytes?.Invoke() ?? -1; start = Stopwatch.GetTimestamp(); }
            public void Dispose()
            {
                long elapsed = Stopwatch.GetTimestamp() - start;
                int collections = GC.CollectionCount(0) - gc0;
                long allocated = allocationStart < 0 ? -1 : Math.Max(0, (allocatedBytes?.Invoke() ?? allocationStart) - allocationStart);
                lock (metrics)
                {
                    Metric metric;
                    if (!metrics.TryGetValue(name, out metric)) metrics[name] = metric = new Metric();
                    metric.Count++; metric.Ticks += elapsed;
                    if (allocated >= 0) { metric.AllocationSamples++; metric.Allocated += allocated; metric.MaximumAllocated = Math.Max(metric.MaximumAllocated, allocated); }
                    if (elapsed * 1000.0 / Stopwatch.Frequency > 4) metric.Over4ms++;
                    if (elapsed * 1000.0 / Stopwatch.Frequency > 16) metric.Over16ms++;
                    if (collections == 0) metric.MaximumWithoutGC = Math.Max(metric.MaximumWithoutGC, elapsed);
                    else metric.GCIntervals++;
                    if (elapsed > metric.Maximum)
                    { metric.Maximum = elapsed; metric.MaxFrame = UnityEngine.Time.frameCount; metric.MaxGC0 = collections; }
                }
            }
        }
        static float nextReport;
        static long frames, slowFrames, verySlowFrames;
        static double frameSeconds;
        static float maxFrameSeconds;
        internal static void Tick()
        {
            float delta = UnityEngine.Time.unscaledDeltaTime;
            if (delta > 0) { frames++; frameSeconds += delta; maxFrameSeconds = Math.Max(maxFrameSeconds, delta); if (delta > .033333f) slowFrames++; if (delta > .1f) verySlowFrames++; }
            float now = UnityEngine.Time.unscaledTime;
            if (nextReport == 0) { nextReport = now + 15; return; }
            if (now < nextReport) return;
            nextReport = now + 15;
            Flush("automatic-15s");
        }
        internal static void Flush(string reason)
        {
            if (frames == 0) return;
            Dictionary<string, object>[] stages;
            lock (metrics) stages = metrics.Select(p => new Dictionary<string, object> {
                { "stage", p.Key }, { "calls", p.Value.Count },
                { "averageMs", p.Value.Ticks * 1000.0 / Stopwatch.Frequency / p.Value.Count },
                { "maximumMs", p.Value.Maximum * 1000.0 / Stopwatch.Frequency },
                { "maximumFrame", p.Value.MaxFrame }, { "gc0AtMaximum", p.Value.MaxGC0 },
                { "maximumWithoutGcMs", p.Value.MaximumWithoutGC * 1000.0 / Stopwatch.Frequency },
                { "gcIntervals", p.Value.GCIntervals }, { "over4ms", p.Value.Over4ms }, { "over16ms", p.Value.Over16ms },
                { "allocationSamples", p.Value.AllocationSamples },
                { "allocatedAverageBytes", p.Value.AllocationSamples > 0 ? (object)(p.Value.Allocated / (double)p.Value.AllocationSamples) : null },
                { "allocatedMaximumBytes", p.Value.AllocationSamples > 0 ? (object)p.Value.MaximumAllocated : null }
            }).ToArray();
            Main.Diagnostics?.PerformanceWindow(new Dictionary<string, object> {
                { "version", 1 }, { "reason", reason }, { "endFrame", UnityEngine.Time.frameCount },
                { "frames", frames }, { "frameSeconds", frameSeconds },
                { "averageFps", frameSeconds > 0 ? (object)(frames / frameSeconds) : null },
                { "maximumFrameMs", maxFrameSeconds * 1000.0 }, { "over33ms", slowFrames }, { "over100ms", verySlowFrames },
                { "stages", stages }, { "nativeHealthIntegration", "Removed" },
                { "settings", new Dictionary<string, object> {
                    { "compareBeta", Main.Settings?.CompareBeta }, { "localDiagnostics", Main.Settings?.LocalDiagnostics },
                    { "showOnHover", Main.Settings?.ShowOnHover },
                    { "manualScenario", Main.Settings != null && Main.Settings.ManualScenario && Main.Settings.ScenarioConfirmed }
                } },
                { "scope", "All game frames while this preview component updates; nested scopes overlap. GC counts are process-wide and frame spikes alone do not prove mod causation." }
            });
            Main.Log?.Log("Performance window (" + reason + "; all game frames; scopes overlap): frames=" + frames +
                " avgFPS=" + (frameSeconds > 0 ? frames / frameSeconds : 0).ToString("0.0", CultureInfo.InvariantCulture) +
                " maxFrameMs=" + (maxFrameSeconds * 1000).ToString("0.0", CultureInfo.InvariantCulture) +
                " over33ms=" + slowFrames + " over100ms=" + verySlowFrames + "\n" + Summary());
            frames = slowFrames = verySlowFrames = 0; frameSeconds = 0; maxFrameSeconds = 0;
            lock (metrics) metrics.Clear();
        }
        internal static Sample Measure(string name) { return new Sample(name); }
        internal static void Reset() { Flush("manual-reset"); lock (metrics) metrics.Clear(); nextReport = 0; frames = slowFrames = verySlowFrames = 0; frameSeconds = 0; maxFrameSeconds = 0; }
        internal static string Summary()
        {
            lock (metrics) return string.Join("\n", metrics.Select(p => string.Format(CultureInfo.InvariantCulture,
                "{0}: {1} calls · avg {2:0.000} ms · max {3:0.000} ms · maxFrame {4} · GC0 at max {5} · maxNoGC {6:0.000} ms · GC intervals {7} · over4ms {8} · over16ms {9} · allocatedAvgKB {10} · allocatedMaxKB {11}", p.Key, p.Value.Count,
                p.Value.Ticks * 1000.0 / Stopwatch.Frequency / p.Value.Count,
                p.Value.Maximum * 1000.0 / Stopwatch.Frequency, p.Value.MaxFrame, p.Value.MaxGC0,
                p.Value.MaximumWithoutGC * 1000.0 / Stopwatch.Frequency, p.Value.GCIntervals, p.Value.Over4ms, p.Value.Over16ms,
                p.Value.AllocationSamples > 0 ? (p.Value.Allocated / 1024.0 / p.Value.AllocationSamples).ToString("0.0", CultureInfo.InvariantCulture) : "unavailable",
                p.Value.AllocationSamples > 0 ? (p.Value.MaximumAllocated / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) : "unavailable")).ToArray());
        }
    }
}
