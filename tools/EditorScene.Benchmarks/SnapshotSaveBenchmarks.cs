using System.Diagnostics;
using System.Text;
using EditorScene.Text;

internal static class SnapshotSaveBenchmarks
{
    internal static void Add(List<object> rows)
    {
        Compare("snapshot_save_ascii_4000000", new string('x', 4_000_000));
        Compare("snapshot_save_unicode_1000000", string.Concat(Enumerable.Repeat("日本語🙂\r\n", 142858)));
        void Compare(string scenario, string text)
        {
            using var document = new EditorDocument(text); var snapshot = document.Snapshot(); var encoding = new UTF8Encoding(true, true);
            var expected = encoding.GetPreamble().Concat(encoding.GetBytes(snapshot.Text)).ToArray();
            var prepared = SnapshotSaveContent.Create(snapshot, encoding);
            using (var output = new MemoryStream())
            {
                prepared.WriteToAsync(output).GetAwaiter().GetResult();
                if (!output.ToArray().AsSpan().SequenceEqual(expected)) throw new InvalidOperationException("Save benchmark output mismatch.");
            }
            Measure(scenario, "previous_whole_text_bytes", () =>
            {
                var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(snapshot.Text)).ToArray();
                if (bytes.LongLength != expected.LongLength) throw new InvalidOperationException("Incorrect previous encoded length.");
                Stream.Null.WriteAsync(bytes).GetAwaiter().GetResult(); Stream.Null.FlushAsync().GetAwaiter().GetResult();
                GC.KeepAlive(bytes);
            }, "Reproduces app 3eb2433e's preamble + GetBytes(snapshot.Text) + Concat.ToArray. Snapshot.Text is already warm, so its initial full-string allocation is excluded, conservatively favoring the old path. Includes copying output to Stream.Null and flush.");
            Measure(scenario, "preflight_and_stream", () =>
            {
                var content = SnapshotSaveContent.Create(snapshot, encoding);
                content.WriteToAsync(Stream.Null).GetAwaiter().GetResult();
                if (content.ByteLength != expected.LongLength) throw new InvalidOperationException("Incorrect streamed length.");
            }, "Includes both full encoding preflight and streaming encode/write/flush to Stream.Null, plus encoding clones and bounded pooled buffers. No whole-text or whole-output allocation in this path. Pooled buffers are warm; preparation is not excluded.");
        }
        void Measure(string scenario, string implementation, Action action, string note)
        {
            for (var i = 0; i < 3; i++) action();
            var times = new double[11]; var bytes = new long[11];
            for (var i = 0; i < 11; i++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
                action(); times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds; bytes[i] = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            Array.Sort(times); Array.Sort(bytes);
            rows.Add(new { scenario, implementation, p50Milliseconds = times[5], p95Milliseconds = times[10], medianAllocatedBytes = bytes[5], sortedElapsedMilliseconds = times, sortedAllocatedBytes = bytes,
                note = note + " Three warmups and eleven sequential samples, current-thread managed bytes. Setup/equivalence verification, real file I/O/provider buffering, native UI, physical GPU and whole-editor latency excluded. Stream.Null completes writes synchronously; asynchronous-provider allocation is not represented." });
            Console.WriteLine($"SAVE_BENCH {scenario} {implementation} p50_ms={times[5]:F4} p95_ms={times[10]:F4} bytes={bytes[5]}");
        }
    }
}
