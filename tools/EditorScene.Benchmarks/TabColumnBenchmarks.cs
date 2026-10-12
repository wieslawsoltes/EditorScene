using System.Diagnostics;
using EditorScene.Text;

internal static class TabColumnBenchmarks
{
    internal static void Add(List<object> rows)
    {
        using var document = new EditorDocument(new string('x', 100000) + "\tX\t"); var snapshot = document.Snapshot();
        var cached = new TabColumnMap(snapshot, 0); var offset = snapshot.Length;
        Measure("tab_column_queries_10000", () =>
        {
            long sum = 0; for (var i = 0; i < 10000; i++) sum += cached.GetColumn(offset);
            if (sum != cached.ColumnLength * 10000) throw new InvalidOperationException("Incorrect column query");
        }, "10000 lookups on a prebuilt 100003-code-unit line with two tabs. Construction excluded. This is a new sparse mapping operation, not a previous-version throughput/GPU/input-latency comparison.");
        Measure("tab_column_build_100003", () =>
        {
            var map = new TabColumnMap(snapshot, 0);
            if (map.TabCount != 2 || map.ColumnLength != cached.ColumnLength) throw new InvalidOperationException("Incorrect column map construction");
        }, "Construct a sparse map for one 100003-code-unit line with two tabs. Includes reader and sparse-array construction with a warm shared pool; scans the line but never flattens the document. Documents/snapshots excluded.");
        void Measure(string scenario, Action run, string note)
        {
            for (var i = 0; i < 3; i++) run();
            var times = new double[11]; var bytes = new long[11];
            for (var i = 0; i < 11; i++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread(); var started = Stopwatch.GetTimestamp();
                run(); times[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds; bytes[i] = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            Array.Sort(times); Array.Sort(bytes);
            rows.Add(new { scenario, implementation = "current", p50Milliseconds = times[5], p95Milliseconds = times[10], medianAllocatedBytes = bytes[5], sortedElapsedMilliseconds = times, sortedAllocatedBytes = bytes,
                note = note + " Three warmups, eleven samples, current-thread managed allocations. No native UI/layout, complete frame or physical-GPU measurements." });
            Console.WriteLine($"TAB_BENCH {scenario} p50_ms={times[5]:F4} p95_ms={times[10]:F4} bytes={bytes[5]}");
        }
    }
}
