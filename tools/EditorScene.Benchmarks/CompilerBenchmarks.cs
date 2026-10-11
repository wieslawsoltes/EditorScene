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
        var references = ReferenceAssemblyCatalog.Default;
        var parseOptions = new CSharpParseOptions(Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview, DocumentationMode.Parse);
        var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, allowUnsafe: true);
        foreach (var incremental in new[] { false, true })
            Measure("csharp_small_edit_model", incremental ? "current" : "previous", () =>
            {
                var doc = new EditorDocument(text); var before = doc.Snapshot();
                var oldSource = SourceText.From(before.TextSource.CreateReader(), before.Length, Encoding.UTF8);
                var oldTree = CSharpSyntaxTree.ParseText(oldSource, parseOptions, before.FileName);
                var oldCompilation = CSharpCompilation.Create("Benchmark", [oldTree], references, compilationOptions);
                GC.KeepAlive(oldCompilation.GetSemanticModel(oldTree)); GC.KeepAlive(oldTree.GetRoot());
                doc.Buffer.Replace(editOffset, 4, "4000"); var after = doc.Snapshot(); SyntaxTree? produced = null;
                return (() =>
                {
                    SourceText source;
                    if (incremental)
                    {
                        var edit = SnapshotTextChanges.GetMinimalChange(before, after);
                        source = oldSource.WithChanges(new TextChange(new TextSpan(edit.Offset, edit.Length), edit.NewText));
                    }
                    else source = SourceText.From(after.Text, Encoding.UTF8);
                    produced = oldTree.WithChangedText(source);
                    var compilation = incremental ? oldCompilation.ReplaceSyntaxTree(oldTree, produced) : CSharpCompilation.Create("Benchmark", [produced], references, compilationOptions);
                    GC.KeepAlive(compilation.GetSemanticModel(produced)); GC.KeepAlive(produced.GetRoot());
                }, () =>
                {
                    if (produced is not null && produced.GetText().ToString() != text.Remove(editOffset, 4).Insert(editOffset, "4000")) throw new InvalidOperationException("Compiler benchmark produced the wrong document.");
                    doc.Dispose();
                });
            });
        using var selectionDocument = new EditorDocument("alpha + beta"); var snapshot = selectionDocument.Snapshot();
        var history = new SelectionExpansion(snapshot, new(2, 2), [new(0, 5), new(0, snapshot.Length)]);
        Measure("structural_selection_cycles_10000", "current", () => (() =>
        {
            var current = history.Current;
            for (var i = 0; i < 10_000; i++) { history.TryExpand(snapshot, current, out current); history.TryContract(snapshot, current, out current); }
            if (current != new SelectionRange(2, 2)) throw new InvalidOperationException("Selection history did not restore the caret.");
        }, () => { }));
        void Measure(string scenario, string implementation, Func<(Action Run, Action Dispose)> setup)
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
            rows.Add(new { scenario, implementation, p50Milliseconds = times[5], p95Milliseconds = times[10], medianAllocatedBytes = allocated[5], sortedElapsedMilliseconds = times, sortedAllocatedBytes = allocated,
                documentCharacters = scenario.StartsWith("csharp", StringComparison.Ordinal) ? text.Length : snapshot.Length,
                note = "Compiler rows reproduce old/new model-construction hot paths; 4000 methods and one four-character edit. Setup, old-model warmup and final text verification excluded. No full semantic analysis, UI, Task scheduling or physical GPU measurement. Selection row measures 10000 cached expand/contract pairs." });
        }
    }
}
