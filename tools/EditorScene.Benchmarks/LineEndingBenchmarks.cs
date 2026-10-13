using System.Diagnostics;
using EditorScene.Text;

internal static class LineEndingBenchmarks
{
    internal static void Add(List<object> rows)
    {
        Compare("canonical_lf_100000_lines", string.Concat(Enumerable.Repeat("content\n", 100000)), "\n");
        Compare("mixed_100000_lines", string.Concat(Enumerable.Repeat("a\r\nb\nc\rd\n", 25000)), "\r\n");
        void Compare(string scenario, string text, string delimiter)
        {
            using var document = new EditorDocument(text); var snapshot = document.Snapshot();
            var expected = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Replace("\n", delimiter, StringComparison.Ordinal);
            var planned = LineEndingEditPlan.Create(snapshot, delimiter);
            using (var verify = new EditorDocument(text))
            {
                verify.ApplyEdits(planned.Edits, verify.Revision);
                if (verify.Buffer.Text != expected) throw new InvalidOperationException("Line-ending benchmark equivalence failed.");
            }
            Measure("previous_all_delimiters", () =>
            {
                var edits = document.Buffer.Lines.Where(line => line.DelimiterLength != 0)
                    .Select(line => new TextEdit(line.EndOffset, line.DelimiterLength, delimiter)).ToArray();
                if (edits.Length != planned.DelimiterCount) throw new InvalidOperationException("Previous line-ending plan mismatch.");
                GC.KeepAlive(edits);
            }, "Reproduces preceding EditorDocument conversion planning: enumerate all live-buffer lines and allocate a replacement for every delimiter, including unchanged ones. Existing live line metadata is warm. Application/undo excluded.");
            Measure("minimal_snapshot_plan", () =>
            {
                var plan = LineEndingEditPlan.Create(snapshot, delimiter);
                if (plan.DelimiterCount != planned.DelimiterCount || plan.Edits.Length != planned.Edits.Length) throw new InvalidOperationException("Minimal line-ending plan mismatch.");
                GC.KeepAlive(plan);
            }, "Scan the immutable rope with a warm bounded reader pool and emit only differing UTF-16 units. Includes counts and immutable plan construction; source setup and initial correctness verification excluded. Does not build a snapshot line index.");
            void Measure(string implementation, Action run, string note)
            {
                for (var i = 0; i < 3; i++) run();
                var times = new double[11]; var bytes = new long[11];
                for (var i = 0; i < 11; i++)
                {
                    var before = GC.GetAllocatedBytesForCurrentThread(); var started = Stopwatch.GetTimestamp();
                    run(); times[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds; bytes[i] = GC.GetAllocatedBytesForCurrentThread() - before;
                }
                Array.Sort(times); Array.Sort(bytes);
                rows.Add(new { scenario, implementation, p50Milliseconds = times[5], p95Milliseconds = times[10], medianAllocatedBytes = bytes[5],
                    sortedElapsedMilliseconds = times, sortedAllocatedBytes = bytes,
                    delimiterCount = planned.DelimiterCount, changedDelimiterCount = planned.ChangedDelimiterCount,
                    note = note + " Three warmups, eleven sequential samples, current-thread managed allocation only. No actual edit application, permission callbacks, native UI, layout, rendering, GPU or vendor/end-to-end latency comparison. Unfavorable timings are retained." });
                Console.WriteLine($"LINE_ENDING_BENCH {scenario} {implementation} p50_ms={times[5]:F4} p95_ms={times[10]:F4} bytes={bytes[5]}");
            }
        }
    }
}
