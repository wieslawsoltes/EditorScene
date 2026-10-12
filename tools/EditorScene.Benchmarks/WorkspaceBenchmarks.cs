using System.Collections.Immutable;
using System.Diagnostics;
using EditorScene.LanguageServices;
using EditorScene.Rendering.Skia;
using EditorScene.Text;
using SkiaSharp;

internal static class WorkspaceBenchmarks
{
    internal static void Add(List<object> rows)
    {
        using var document = new EditorDocument(string.Concat(Enumerable.Repeat("line content\n", 10000)), "diagnostics.cs");
        var snapshot = document.Snapshot();
        var analysis = LanguageAnalysis.Empty(snapshot) with { Diagnostics = Enumerable.Range(0, 10000).Select(i => new EditorDiagnostic("D" + i, "Diagnostic " + i, IssueSeverity.Warning, new(i * 13, 4))).ToImmutableArray() };
        var table = new DiagnosticTable(snapshot, analysis);
        Measure("diagnostic_rows_10000", "rebuild_projection", () => GC.KeepAlive(new DiagnosticTable(snapshot, analysis)), "Constructs all 10,000 position-bound data rows. Snapshot line index is warm; rendering and collection binding are excluded.");
        Measure("diagnostic_rows_10000", "reuse_unfiltered_projection", () => { if (table.Filter() != table.Rows) throw new InvalidOperationException("Expected immutable diagnostic reuse"); }, "Returns the existing unfiltered 10,000-row immutable projection, as used on unchanged UI state. This avoids work, not a faster fresh projection; filtered queries still scan their source.");
        var orderedView = new DiagnosticTableView(table);
        var ordered = orderedView.Query(column: DiagnosticSortColumn.Code);
        Measure("diagnostic_order_10000_requests_10", "fresh_stable_sort", () =>
        {
            for (var i = 0; i < 10; i++)
            {
                var result = new DiagnosticTableView(table).Query(column: DiagnosticSortColumn.Code);
                if (result.Length != 10000 || result[0].Code != "D0") throw new InvalidOperationException("Incorrect ordered fixture");
            }
        }, "Ten fresh query views each stably sort the same preprojected 10,000 rows. Includes view, scratch and immutable-order allocation. This is a same-implementation uncached control experiment, not an older implementation or vendor benchmark. Snapshot/projection construction and UI binding are excluded.");
        Measure("diagnostic_order_10000_requests_10", "cached_order_lookup", () =>
        {
            for (var i = 0; i < 10; i++) if (orderedView.Query(column: DiagnosticSortColumn.Code) != ordered) throw new InvalidOperationException("Expected retained ordering");
            if (orderedView.SortBuildCount != 1) throw new InvalidOperationException("Unnecessary sorting");
        }, "Ten exact requests reuse the retained 10,000-row ordering. Initial sorting is excluded. Measures avoiding repeated work, not the cost of a new sort, changed filter, UI layout or painting.");
        using var bitmap = new SKBitmap(16, 16); using var canvas = new SKCanvas(bitmap); using var cached = new EditorGlyphRenderer();
        cached.Draw(canvas, 16, 16, EditorGlyph.Save);
        Measure("glyph_draws_1000", "record_each_draw", () =>
        {
            for (var i = 0; i < 1000; i++) { using var renderer = new EditorGlyphRenderer(); renderer.Draw(canvas, 16, 16, EditorGlyph.Save); }
        }, "Records and replays the same current vector geometry for each draw. An uncached control experiment, NOT an exact reproduction of the preceding EditorIcon implementation. Includes recording/disposal and CPU raster drawing.");
        Measure("glyph_draws_1000", "cached_picture_replay", () =>
        {
            for (var i = 0; i < 1000; i++) cached.Draw(canvas, 16, 16, EditorGlyph.Save);
            if (cached.BuildCount != 1) throw new InvalidOperationException("Glyph was unnecessarily recorded");
        }, "Replays a previously recorded picture 1,000 times onto the same 16×16 CPU bitmap. Includes CPU raster drawing, excludes initial recording. No physical-GPU, compositor/frame-rate or whole-editor input-latency claim.");

        void Measure(string scenario, string implementation, Action run, string note)
        {
            for (var i = 0; i < 3; i++) run();
            var elapsed = new double[11]; var bytes = new long[11];
            for (var i = 0; i < elapsed.Length; i++)
            {
                var allocated = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
                run(); elapsed[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds; bytes[i] = GC.GetAllocatedBytesForCurrentThread() - allocated;
            }
            Array.Sort(elapsed); Array.Sort(bytes);
            rows.Add(new { scenario, implementation, p50Milliseconds = elapsed[5], p95Milliseconds = elapsed[10], medianAllocatedBytes = bytes[5], sortedElapsedMilliseconds = elapsed, sortedAllocatedBytes = bytes, note = note + " Three warmups, eleven samples, current-thread managed allocations only; retained native picture memory is not measured." });
            Console.WriteLine($"WORKSPACE_BENCH {scenario} {implementation} p50_ms={elapsed[5]:F4} p95_ms={elapsed[10]:F4} bytes={bytes[5]}");
        }
    }
}
