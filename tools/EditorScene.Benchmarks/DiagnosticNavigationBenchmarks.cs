using System.Diagnostics;
using EditorScene.LanguageServices;

internal static class DiagnosticNavigationBenchmarks
{
    internal static void Add(List<object> rows, DiagnosticTable table)
    {
        var cursor = new DiagnosticNavigationCursor(table.Rows);
        cursor.SelectIndex(0);
        void Run()
        {
            for (var i = 0; i < 10_000; i++)
            {
                if (!cursor.TryMove(out _) || !cursor.TryMove(out _, backwards: true)) throw new InvalidOperationException("Navigation fixture is empty");
            }
            if (cursor.CurrentIndex != 0) throw new InvalidOperationException("Navigation did not restore its starting index");
        }
        for (var i = 0; i < 3; i++) Run();
        var elapsed = new double[11]; var allocated = new long[11];
        for (var i = 0; i < elapsed.Length; i++)
        {
            var bytes = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
            Run(); elapsed[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds; allocated[i] = GC.GetAllocatedBytesForCurrentThread() - bytes;
        }
        Array.Sort(elapsed); Array.Sort(allocated);
        rows.Add(new { scenario = "diagnostic_cursor_cycles_10000", implementation = "current", p50Milliseconds = elapsed[5], p95Milliseconds = elapsed[10], medianAllocatedBytes = allocated[5],
            sortedElapsedMilliseconds = elapsed, sortedAllocatedBytes = allocated, diagnosticCount = table.Rows.Length,
            note = "10000 next/previous pairs on a preconstructed immutable-result cursor, with 10000 diagnostic rows. Cursor construction/validation and table projection excluded; three warmups, eleven samples. Measures headless cursor work, not ListView binding, callbacks, document navigation, scrolling, UI latency or GPU rendering." });
        Console.WriteLine($"NAVIGATION_BENCH diagnostic_cursor_cycles_10000 p50_ms={elapsed[5]:F4} p95_ms={elapsed[10]:F4} bytes={allocated[5]}");
    }
}
