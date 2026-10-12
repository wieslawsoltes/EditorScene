using System.Collections.Immutable;
using EditorScene.LanguageServices;
using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class DiagnosticQueryCacheReviewTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void WarmExactQueriesReuseTheSameArrayWithoutManagedAllocations(bool filtered)
    {
        var view = CreateView(); var text = filtered ? "item" : string.Empty;
        var expected = view.Query(query: text, column: DiagnosticSortColumn.Code);
        for (var i = 0; i < 32; i++) _ = view.Query(query: text, column: DiagnosticSortColumn.Code);
        var before = GC.GetAllocatedBytesForCurrentThread(); var same = true;
        for (var i = 0; i < 1000; i++) same &= view.Query(query: text, column: DiagnosticSortColumn.Code) == expected;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(same, Is.True); Assert.That(allocated, Is.Zero);
        Assert.That(view.SortBuildCount, Is.EqualTo(1)); Assert.That(view.QueryBuildCount, Is.EqualTo(1));
    }
    [Test]
    public void NewOrderingsCannotMutateArraysRetainedByCallers()
    {
        var view = CreateView();
        var first = view.Query(column: DiagnosticSortColumn.Code);
        var originalObjects = first.ToArray();
        var filtered = view.Query(query: "item9", column: DiagnosticSortColumn.Code);
        var filteredObjects = filtered.ToArray();
        foreach (var column in DiagnosticListColumns())
            foreach (var direction in Enum.GetValues<DiagnosticSortDirection>())
                _ = view.Query(query: "item", column: column, direction: direction);
        for (var i = 0; i < first.Length; i++) Assert.That(ReferenceEquals(first[i], originalObjects[i]), Is.True);
        for (var i = 0; i < filtered.Length; i++) Assert.That(ReferenceEquals(filtered[i], filteredObjects[i]), Is.True);
        Assert.That(view.Table.Rows[0].Code, Is.EqualTo("C099"));
    }
    private static DiagnosticSortColumn[] DiagnosticListColumns() =>
        [DiagnosticSortColumn.Severity, DiagnosticSortColumn.Code, DiagnosticSortColumn.Line, DiagnosticSortColumn.Character, DiagnosticSortColumn.Description, DiagnosticSortColumn.File];
    private static DiagnosticTableView CreateView()
    {
        using var doc = new EditorDocument(new string('x', 200)); var snapshot = doc.Snapshot();
        var diagnostics = Enumerable.Range(0, 100).Select(i => new EditorDiagnostic($"C{99 - i:000}", "item" + i, IssueSeverity.Error, new(i, 1))).ToImmutableArray();
        return new(new DiagnosticTable(snapshot, LanguageAnalysis.Empty(snapshot) with { Diagnostics = diagnostics }));
    }
}
