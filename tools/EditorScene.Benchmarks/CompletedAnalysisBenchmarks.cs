using System.Diagnostics;
using EditorScene.Languages.DotNet;
using EditorScene.Text;

internal static class CompletedAnalysisBenchmarks
{
    internal static void Add(List<object> rows)
    {
        using var document = new EditorDocument("class C { int M() => 1 + 2; }");
        var snapshot = document.Snapshot(); var language = new DotNetLanguage();
        var result = language.AnalyzeAsync(snapshot).GetAwaiter().GetResult();
        var sharedTask = language.AnalyzeAsync(snapshot);
        void Run()
        {
            for (var i = 0; i < 10_000; i++)
            {
                var task = language.AnalyzeAsync(snapshot);
                if (!ReferenceEquals(task, sharedTask) || !task.IsCompletedSuccessfully || !ReferenceEquals(task.Result, result))
                    throw new InvalidOperationException("Completed analysis was not reused.");
            }
        }
        for (var i = 0; i < 3; i++) Run();
        var times = new double[11]; var bytes = new long[11];
        for (var i = 0; i < 11; i++)
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
            Run(); times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds; bytes[i] = GC.GetAllocatedBytesForCurrentThread() - allocated;
        }
        Array.Sort(times); Array.Sort(bytes);
        if (language.CacheStatistics.AnalysisBuilds != 1) throw new InvalidOperationException("Warm analysis was recomputed.");
        rows.Add(new { scenario = "completed_analysis_requests_10000", implementation = "current", p50Milliseconds = times[5], p95Milliseconds = times[10], medianAllocatedBytes = bytes[5],
            sortedElapsedMilliseconds = times, sortedAllocatedBytes = bytes, documentCharacters = snapshot.Length,
            note = "10000 exact-snapshot AnalyzeAsync calls returning the same completed task/result. Initial analysis and three warmups excluded; eleven samples. Measures cache lookup, not parsing, semantic analysis, UI or GPU." });
        Console.WriteLine($"COMPILER_BENCH completed_analysis_requests_10000 current p50_ms={times[5]:F4} p95_ms={times[10]:F4} bytes={bytes[5]}");
    }
}
