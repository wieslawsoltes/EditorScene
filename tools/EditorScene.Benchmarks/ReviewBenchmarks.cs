using System.Diagnostics;
using EditorScene.LanguageServices;
using EditorScene.Text;

internal static class ReviewBenchmarks
{
    internal static void Add(List<object> rows)
    {
        var text = new string('x', 4_200_000) + "needle";
        var search = new SnapshotLiteralSearch("needle");
        Measure("tail_literal_search", "previous", () =>
        {
            var doc = new EditorDocument(text); var snapshot = doc.Snapshot();
            return (() => { if (snapshot.Text.IndexOf("needle", 4_200_000, StringComparison.Ordinal) != 4_200_000) throw new Exception("Baseline result mismatch"); }, doc.Dispose);
        });
        Measure("tail_literal_search", "current", () =>
        {
            var doc = new EditorDocument(text); var snapshot = doc.Snapshot();
            return (() => { if (search.FindNext(snapshot, 4_200_000)?.Offset != 4_200_000) throw new Exception("Search result mismatch"); }, doc.Dispose);
        });
        var bracketText = "(" + new string('x', 4_200_000) + ")";
        Measure("bracket_scan_sparse", "previous", () =>
        {
            var doc = new EditorDocument(bracketText); var snapshot = doc.Snapshot();
            return (() => LegacyBracketMap(snapshot), doc.Dispose);
        });
        Measure("bracket_scan_sparse", "current", () =>
        {
            var doc = new EditorDocument(bracketText); var snapshot = doc.Snapshot();
            return (() => { if (new BracketMap(snapshot, []).PairCount != 1) throw new Exception("Bracket result mismatch"); }, doc.Dispose);
        });
        var tags = Enumerable.Range(0, 100_000).Select(i => new SourceRange(i * 10, 1)).ToArray();
        var index = new RangeIndex<SourceRange>(tags, r => r); var output = new List<SourceRange>(8);
        Measure("deep_diagnostic_queries_1000", "previous", () => (() =>
        {
            var total = 0;
            for (var i = 0; i < 1000; i++)
                foreach (var tag in tags) { if (tag.Offset > 999_999) break; if (tag.End <= 999_950) continue; total++; }
            if (total != 5000) throw new Exception("Linear query result mismatch");
        }, () => { }));
        Measure("deep_diagnostic_queries_1000", "current", () => (() =>
        {
            var total = 0;
            for (var i = 0; i < 1000; i++) { output.Clear(); total += index.CopyOverlaps(new(999_950, 50), output).Matches; }
            if (total != 5000) throw new Exception("Indexed query result mismatch");
        }, () => { }));

        void Measure(string scenario, string implementation, Func<(Action Run, Action Dispose)> setup)
        {
            for (var i = 0; i < 3; i++) { var p = setup(); try { p.Run(); } finally { p.Dispose(); } }
            var times = new double[11]; var allocations = new long[11];
            for (var i = 0; i < 11; i++)
            {
                var p = setup();
                try
                {
                    var before = GC.GetAllocatedBytesForCurrentThread(); var stamp = Stopwatch.GetTimestamp();
                    p.Run(); times[i] = Stopwatch.GetElapsedTime(stamp).TotalMilliseconds;
                    allocations[i] = GC.GetAllocatedBytesForCurrentThread() - before;
                }
                finally { p.Dispose(); }
            }
            Array.Sort(times); Array.Sort(allocations);
            rows.Add(new { scenario, implementation, p50Milliseconds = times[5], p95Milliseconds = times[10], medianAllocatedBytes = allocations[5], sortedElapsedMilliseconds = times, sortedAllocatedBytes = allocations,
                note = "Previous path reproduced from dd8b565. Setup/disposal excluded. Sparse bracket scan remains linear. Diagnostic index is warm; reports 1000 queries, not frame/GPU time." });
        }
    }
    private static void LegacyBracketMap(DocumentSnapshot snapshot)
    {
        var pairs = new Dictionary<int, int>(); var stack = new Stack<(char Character, int Offset)>();
        for (var i = 0; i < snapshot.Length; i++)
        {
            var c = snapshot.Text[i];
            if (c is '(' or '[' or '{') stack.Push((c, i));
            else if (c is ')' or ']' or '}')
            {
                if (stack.TryPeek(out var open) && (open.Character, c) is ('(', ')') or ('[', ']') or ('{', '}'))
                { stack.Pop(); pairs.Add(open.Offset, i); pairs.Add(i, open.Offset); }
                else stack.Clear();
            }
        }
        if (pairs.Count != 2) throw new Exception("Baseline bracket result mismatch");
    }
}
