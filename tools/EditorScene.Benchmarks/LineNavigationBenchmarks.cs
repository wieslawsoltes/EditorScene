using System.Diagnostics;
using System.Globalization;
using EditorScene.Text;

internal static class LineNavigationBenchmarks
{
    internal static void Add(List<object> rows)
    {
        using var document = new EditorDocument(string.Concat(Enumerable.Repeat("content\n", 100000)));
        var snapshot = document.Snapshot(); const string query = "100000:8"; const int iterations = 10000;
        if (!LineNavigationTarget.TryParse(snapshot, query, out var target, out _)) throw new InvalidOperationException("Invalid navigation fixture");
        var expected = target.Offset * (long)iterations;
        Measure("split_string_position_queries", () =>
        {
            long sum = 0;
            for (var i = 0; i < iterations; i++)
            {
                // Prior dialog's successful-input parsing with the same immutable
                // lookup used by the new path, rather than timing unrelated UI work.
                var pieces = query.Split(':');
                if (!int.TryParse(pieces[0], out var line) || line <= 0 || line > snapshot.LineCount || pieces.Length > 2
                    || pieces.Length == 2 && !int.TryParse(pieces[1], out _)) throw new FormatException();
                var column = pieces.Length == 2 ? Math.Max(1, int.Parse(pieces[1], CultureInfo.InvariantCulture)) : 1;
                sum += snapshot.GetOffset(new(line - 1, column - 1));
            }
            if (sum != expected) throw new InvalidOperationException("Baseline navigation mismatch");
        }, "10000 valid line:character parses using the preceding dialog's Split/TryParse/Parse sequence and the same immutable line lookup as current. This isolates parsing/allocation, not prior whole-dialog or native-buffer throughput.");
        Measure("strict_span_position_queries", () =>
        {
            long sum = 0;
            for (var i = 0; i < iterations; i++)
            {
                if (!LineNavigationTarget.TryParse(snapshot, query, out var position, out _)) throw new FormatException();
                sum += position.Offset;
            }
            if (sum != expected) throw new InvalidOperationException("Strict navigation mismatch");
        }, "10000 validated value-type requests, including identity/revision capture, range and UTF-16-boundary checks. No strings split or allocated by warm queries.");
        void Measure(string implementation, Action action, string note)
        {
            for (var i = 0; i < 3; i++) action();
            var times = new double[11]; var allocated = new long[11];
            for (var i = 0; i < 11; i++)
            {
                var bytes = GC.GetAllocatedBytesForCurrentThread(); var started = Stopwatch.GetTimestamp();
                action(); times[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds; allocated[i] = GC.GetAllocatedBytesForCurrentThread() - bytes;
            }
            Array.Sort(times); Array.Sort(allocated);
            rows.Add(new { scenario = "line_navigation_10000_queries_100000_lines", implementation,
                p50Milliseconds = times[5], p95Milliseconds = times[10], medianAllocatedBytes = allocated[5], sortedElapsedMilliseconds = times, sortedAllocatedBytes = allocated,
                note = note + " Three warmups, eleven sequential samples, current-thread managed allocations. Warm line index, setup excluded. No modal/native layout, focus, scrolling, rendering, GPU, Actipro or end-to-end latency comparison." });
            Console.WriteLine($"NAVIGATION_BENCH {implementation} p50_ms={times[5]:F4} p95_ms={times[10]:F4} bytes={allocated[5]}");
        }
    }
}
