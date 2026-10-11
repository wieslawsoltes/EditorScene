using System.Diagnostics;
using System.Text;
using EditorScene.LanguageServices;
using EditorScene.Languages.DotNet;
using EditorScene.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

internal static class CompilerBenchmarks
{
    internal static void Add(List<object> rows)
    {
        var text = "class C {\n" + string.Concat(Enumerable.Range(0, 4000).Select(i => $"    int Method{i}() => {i};\n")) + "}\n";
        var editOffset = text.LastIndexOf("3999;", StringComparison.Ordinal);
        var expected = text.Remove(editOffset, 4).Insert(editOffset, "4000");
        var references = ReferenceAssemblyCatalog.Default;
        var parseOptions = new CSharpParseOptions(Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview, DocumentationMode.Parse);
        var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, allowUnsafe: true);
        // Keep the original model-only scenario, including any regression. Add
        // a separately named semantic-warm scenario rather than replacing it.
        foreach (var semantic in new[] { false, true })
            foreach (var incremental in new[] { false, true })
                Measure(semantic ? "csharp_small_edit_bound_model" : "csharp_small_edit_model", incremental ? "current" : "previous", text.Length, () =>
                {
                    var fixture = Setup(semantic); SyntaxTree? produced = null;
                    return (() =>
                    {
                        var source = ChangedSource(fixture, incremental);
                        produced = fixture.Tree.WithChangedText(source);
                        var compilation = incremental ? fixture.Compilation.ReplaceSyntaxTree(fixture.Tree, produced) : CSharpCompilation.Create("Benchmark", [produced], references, compilationOptions);
                        GC.KeepAlive(compilation.GetSemanticModel(produced)); GC.KeepAlive(produced.GetRoot());
                        if (semantic && compilation.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error)) throw new InvalidOperationException("Invalid benchmark compilation");
                    }, () => { try { Verify(produced); } finally { fixture.Document.Dispose(); } });
                }, semantic
                    ? "Full diagnostics forced before the edit (setup excluded) and for the updated compilation (included)."
                    : "Model construction only: old semantic wrapper/root created, but diagnostics/bindings not warmed. This preserves the original regression probe.");

        foreach (var phase in new[] { "source_change", "syntax_update", "compilation_replace" })
            foreach (var incremental in new[] { false, true })
                Measure("csharp_phase_" + phase, incremental ? "current" : "previous", text.Length, () =>
                {
                    var fixture = Setup(false); SourceText? source = null; SyntaxTree? tree = null; Compilation? compilation = null;
                    if (phase != "source_change") source = ChangedSource(fixture, incremental);
                    if (phase == "compilation_replace") tree = fixture.Tree.WithChangedText(source!);
                    return (() =>
                    {
                        if (phase == "source_change") source = ChangedSource(fixture, incremental);
                        else if (phase == "syntax_update") tree = fixture.Tree.WithChangedText(source!);
                        else compilation = incremental ? fixture.Compilation.ReplaceSyntaxTree(fixture.Tree, tree!) : CSharpCompilation.Create("Benchmark", [tree!], references, compilationOptions);
                        GC.KeepAlive(compilation);
                    }, () =>
                    {
                        try
                        {
                            if (source is not null && source.ToString() != expected) throw new InvalidOperationException("Incorrect benchmark source");
                            Verify(tree);
                        }
                        finally { fixture.Document.Dispose(); }
                    });
                }, "Isolated phase; preceding phases and setup excluded. Phase measurements are explanatory, not additive to separately measured end-to-end rows.");

        using var selectionDocument = new EditorDocument("alpha + beta"); var snapshot = selectionDocument.Snapshot();
        var history = new SelectionExpansion(snapshot, new(2, 2), [new(0, 5), new(0, snapshot.Length)]);
        Measure("structural_selection_cycles_10000", "current", snapshot.Length, () => (() =>
        {
            var current = history.Current;
            for (var i = 0; i < 10_000; i++) { history.TryExpand(snapshot, current, out current); history.TryContract(snapshot, current, out current); }
            if (current != new SelectionRange(2, 2)) throw new InvalidOperationException("Selection history did not restore the caret.");
        }, () => { }), "10,000 cached headless expansion/contraction pairs; excludes native UI/events/layout/painting.");

        Fixture Setup(bool bind)
        {
            var doc = new EditorDocument(text); var before = doc.Snapshot();
            using var reader = before.TextSource.CreateReader();
            var source = SourceText.From(reader, before.Length, Encoding.UTF8);
            var tree = CSharpSyntaxTree.ParseText(source, parseOptions, before.FileName);
            var compilation = CSharpCompilation.Create("Benchmark", [tree], references, compilationOptions);
            GC.KeepAlive(compilation.GetSemanticModel(tree)); GC.KeepAlive(tree.GetRoot());
            if (bind && compilation.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error)) throw new InvalidOperationException("Invalid baseline fixture");
            doc.Buffer.Replace(editOffset, 4, "4000");
            return new(doc, before, doc.Snapshot(), source, tree, compilation);
        }
        static SourceText ChangedSource(Fixture fixture, bool incremental)
        {
            if (!incremental) return SourceText.From(fixture.After.Text, Encoding.UTF8);
            var edit = SnapshotTextChanges.GetMinimalChange(fixture.Before, fixture.After);
            return fixture.Source.WithChanges(new TextChange(new TextSpan(edit.Offset, edit.Length), edit.NewText));
        }
        void Verify(SyntaxTree? tree)
        { if (tree is not null && tree.GetText().ToString() != expected) throw new InvalidOperationException("Compiler benchmark produced incorrect text."); }
        void Measure(string scenario, string implementation, int documentCharacters, Func<(Action Run, Action Dispose)> setup, string detail)
        {
            for (var i = 0; i < 3; i++) { var p = setup(); try { p.Run(); } finally { p.Dispose(); } }
            var times = new double[11]; var allocated = new long[11];
            for (var i = 0; i < 11; i++)
            {
                var p = setup();
                try
                {
                    var bytes = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
                    p.Run(); times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds; allocated[i] = GC.GetAllocatedBytesForCurrentThread() - bytes;
                }
                finally { p.Dispose(); }
            }
            Array.Sort(times); Array.Sort(allocated);
            rows.Add(new { scenario, implementation, p50Milliseconds = times[5], p95Milliseconds = times[10], medianAllocatedBytes = allocated[5], sortedElapsedMilliseconds = times, sortedAllocatedBytes = allocated, documentCharacters,
                note = detail + " Three warmups and eleven samples; setup/disposal/output verification excluded. Four-character edit in 4000 methods for compiler fixtures. No Task scheduling, complete EditorScene classification/symbol walk, UI or physical GPU measurement." });
            Console.WriteLine($"COMPILER_BENCH {scenario} {implementation} p50_ms={times[5]:F4} p95_ms={times[10]:F4} bytes={allocated[5]}");
        }
    }
    private sealed record Fixture(EditorDocument Document, DocumentSnapshot Before, DocumentSnapshot After, SourceText Source, SyntaxTree Tree, CSharpCompilation Compilation);
}
