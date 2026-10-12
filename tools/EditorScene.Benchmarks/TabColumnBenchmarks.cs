using System.Buffers;
using System.Diagnostics;
using EditorScene.Text;

internal static class TabColumnBenchmarks
{
    internal static void Add(List<object> rows)
    {
        using var document = new EditorDocument(new string('x', 100000) + "\tX\t"); var snapshot = document.Snapshot();
        var cached = new TabColumnMap(snapshot, 0); var offset = snapshot.Length;
        Measure("tab_column_queries_10000", "current", () =>
        {
            long sum = 0; for (var i = 0; i < 10000; i++) sum += cached.GetColumn(offset);
            if (sum != cached.ColumnLength * 10000) throw new InvalidOperationException("Incorrect column query");
        }, "10000 lookups on a prebuilt 100003-code-unit line with two tabs. Construction excluded; no previous query implementation comparison.");
        Measure("tab_column_build_100003", "current", () =>
        {
            var map = new TabColumnMap(snapshot, 0);
            if (map.TabCount != 2 || map.ColumnLength != cached.ColumnLength) throw new InvalidOperationException("Incorrect column map construction");
            GC.KeepAlive(map);
        }, "Construct a sparse map for one 100003-code-unit line with two tabs. Includes reader and sparse-array construction with a warm shared pool, excluding document/snapshot creation.");
        Compare("tab_build_sparse_100003", snapshot);
        using var noTabs = new EditorDocument(new string('x', 100000));
        Compare("tab_build_no_tabs_100000", noTabs.Snapshot());
        using var indentation = new EditorDocument(new string(' ', 100000));
        var indentationSnapshot = indentation.Snapshot();
        Compare("tab_build_indentation_100000", indentationSnapshot);
        using var dense = new EditorDocument(string.Concat(Enumerable.Repeat("   \t", 25000)));
        Compare("tab_build_dense_100000", dense.Snapshot());
        var softMap = new TabColumnMap(indentationSnapshot, 0, 4);
        Measure("soft_tab_queries_10000", "current", () =>
        {
            long sum = 0; for (var i = 1; i <= 10000; i++) sum += softMap.GetSoftTabBackspaceLength(i);
            if (sum != 25000) throw new InvalidOperationException("Incorrect soft-tab query");
        }, "10000 preceding-stop queries at distinct leading-space positions on a prebuilt 100000-unit line. Initial construction and all edits/native UI are excluded.");

        void Compare(string scenario, DocumentSnapshot source)
        {
            var oracle = new PreviousScalarMap(source, 0, 4); var expectedCount = oracle.TabCount; var expectedColumn = oracle.ColumnLength;
            // Preserve the prior constructor rather than comparing a string scan
            // with a reader scan. Both use the same snapshot, pool and tab width.
            Measure(scenario, "previous_scalar", () =>
            {
                var map = new PreviousScalarMap(source, 0, 4);
                if (map.TabCount != expectedCount || map.ColumnLength != expectedColumn) throw new InvalidOperationException("Incorrect scalar map");
                GC.KeepAlive(map);
            }, "Previous constructor reproduced from main 75a81960a1bbd6cb1c7d88179b1d639af3af4d5b, including scalar character loop, two eager lists, pooled reader and cleared buffer return. Snapshot setup excluded; result count/length validation included.");
            Measure(scenario, "current_span", () =>
            {
                var map = new TabColumnMap(source, 0, 4);
                if (map.TabCount != expectedCount || map.ColumnLength != expectedColumn) throw new InvalidOperationException("Incorrect span map");
                GC.KeepAlive(map);
            }, "Current constructor: span searches skip non-tab runs, tab lists allocate lazily, and the same scan additionally records the leading space/tab prefix. Same immutable snapshot/tab width and warm pool as scalar baseline; result validation included.");
        }
        void Measure(string scenario, string implementation, Action run, string note)
        {
            for (var i = 0; i < 3; i++) run();
            var times = new double[11]; var bytes = new long[11];
            for (var i = 0; i < 11; i++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread(); var started = Stopwatch.GetTimestamp();
                run(); times[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds; bytes[i] = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            Array.Sort(times); Array.Sort(bytes);
            rows.Add(new { scenario, implementation, p50Milliseconds = times[5], p95Milliseconds = times[10], medianAllocatedBytes = bytes[5], sortedElapsedMilliseconds = times, sortedAllocatedBytes = bytes,
                note = note + " Three warmups, eleven samples, sequential baseline/current groups, current-thread managed allocations. Dense-tab costs are retained even if slower. No native UI/layout, complete frame, input-latency or physical-GPU measurements." });
            Console.WriteLine($"TAB_BENCH {scenario} {implementation} p50_ms={times[5]:F4} p95_ms={times[10]:F4} bytes={bytes[5]}");
        }
    }
    // Exact prior constructor/field shape, excluding unrelated query methods. Keep
    // both sparse arrays and metadata alive so allocation comparisons are meaningful.
    private sealed class PreviousScalarMap
    {
        private readonly int[] _tabs;
        private readonly long[] _after;
        public Guid DocumentId { get; }
        public long Revision { get; }
        public int LineIndex { get; }
        public int LineOffset { get; }
        public int Length { get; }
        public int TabSize { get; }
        public int TabCount => _tabs.Length;
        public long ColumnLength { get; }
        public PreviousScalarMap(DocumentSnapshot snapshot, int lineIndex, int tabSize, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            if (tabSize is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(tabSize));
            cancellationToken.ThrowIfCancellationRequested();
            var line = snapshot.GetLine(lineIndex);
            DocumentId = snapshot.DocumentId; Revision = snapshot.Revision; LineIndex = lineIndex;
            LineOffset = line.Offset; Length = line.Length; TabSize = tabSize;
            var tabs = new List<int>(); var after = new List<long>(); long column = 0;
            var buffer = ArrayPool<char>.Shared.Rent(4096);
            try
            {
                using var reader = snapshot.TextSource.CreateReader(line.Offset, line.Length);
                var offset = 0;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var count = reader.Read(buffer, 0, 4096); if (count == 0) break;
                    for (var i = 0; i < count; i++, offset++)
                    {
                        if (buffer[i] == '\t') { column += tabSize - column % tabSize; tabs.Add(offset); after.Add(column); }
                        else column++;
                    }
                }
            }
            finally { ArrayPool<char>.Shared.Return(buffer, clearArray: true); }
            _tabs = tabs.ToArray(); _after = after.ToArray(); ColumnLength = column;
        }
    }
}
