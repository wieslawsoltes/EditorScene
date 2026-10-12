using System.Collections.Immutable;
using EditorScene.LanguageServices;
using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class DiagnosticTableViewTests
{
    private static DiagnosticTable Fixture()
    {
        using var document = new EditorDocument(string.Join("\n", Enumerable.Repeat("abcdefghijklmnop", 15)), "Example.cs");
        var snapshot = document.Snapshot();
        return new DiagnosticTable(snapshot, LanguageAnalysis.Empty(snapshot) with
        {
            Diagnostics = [
                new("Z", "zulu", IssueSeverity.Information, new(17 * 9 + 11, 1)),
                new("b", "Bravo", IssueSeverity.Warning, new(17, 1)),
                new("B", "bravo", IssueSeverity.Error, new(17 + 3, 1)),
                new("A", "alpha", IssueSeverity.Error, new(17 * 11 + 1, 1))
            ]
        });
    }
    [Test]
    public void ProviderOrderAndExactRepeatQueriesReuseTheOriginalArray()
    {
        var table = Fixture(); var view = new DiagnosticTableView(table);
        Assert.That(view.Query(), Is.EqualTo(table.Rows));
        Assert.That(view.Query() == table.Rows, Is.True);
        Assert.That(view.Query(query: "  ") == table.Rows, Is.True);
        Assert.That(view.SortBuildCount, Is.Zero); Assert.That(view.QueryBuildCount, Is.EqualTo(1));
    }
    [TestCase(DiagnosticSortColumn.Code, false, new[] { 3, 1, 2, 0 })]
    [TestCase(DiagnosticSortColumn.Code, true, new[] { 0, 1, 2, 3 })]
    [TestCase(DiagnosticSortColumn.Line, false, new[] { 1, 2, 0, 3 })]
    [TestCase(DiagnosticSortColumn.Line, true, new[] { 3, 0, 1, 2 })]
    [TestCase(DiagnosticSortColumn.Character, false, new[] { 1, 3, 2, 0 })]
    [TestCase(DiagnosticSortColumn.Character, true, new[] { 0, 2, 3, 1 })]
    [TestCase(DiagnosticSortColumn.Description, false, new[] { 3, 1, 2, 0 })]
    [TestCase(DiagnosticSortColumn.Description, true, new[] { 0, 1, 2, 3 })]
    [TestCase(DiagnosticSortColumn.Severity, false, new[] { 2, 3, 1, 0 })]
    [TestCase(DiagnosticSortColumn.Severity, true, new[] { 0, 1, 2, 3 })]
    [TestCase(DiagnosticSortColumn.File, false, new[] { 0, 1, 2, 3 })]
    [TestCase(DiagnosticSortColumn.File, true, new[] { 0, 1, 2, 3 })]
    public void SortsUseNumericPositionsAndStableEqualKeys(DiagnosticSortColumn column, bool descending, int[] order)
    {
        var table = Fixture(); var view = new DiagnosticTableView(table);
        var result = view.Query(column: column, direction: descending ? DiagnosticSortDirection.Descending : DiagnosticSortDirection.Ascending);
        Assert.That(result.Select(r => table.Rows.IndexOf(r)), Is.EqualTo(order));
        for (var i = 0; i < order.Length; i++) Assert.That(ReferenceEquals(result[i], table.Rows[order[i]]), Is.True);
        Assert.That(table.Rows[0].Code, Is.EqualTo("Z"));
    }
    [Test]
    public void FilteringReusesTheOrderingAndOnlyTheLastQueryIsRetained()
    {
        var view = new DiagnosticTableView(Fixture());
        var all = view.Query(column: DiagnosticSortColumn.Code);
        var filtered = view.Query(query: "b", column: DiagnosticSortColumn.Code);
        Assert.That(filtered.Select(r => r.Code), Is.EqualTo(new[] { "b", "B" }));
        Assert.That(view.Query(query: " b ", column: DiagnosticSortColumn.Code) == filtered, Is.True);
        Assert.That(view.Query(query: "Example.CS", column: DiagnosticSortColumn.Code).Length, Is.EqualTo(4));
        Assert.That(view.Query(column: DiagnosticSortColumn.Code) == all, Is.True);
        Assert.That(view.SortBuildCount, Is.EqualTo(1));
        Assert.That(view.Query(DiagnosticVisibility.Warning, "bravo", DiagnosticSortColumn.Code).Single().Code, Is.EqualTo("b"));
        Assert.That(view.Query(DiagnosticVisibility.Information, "information", DiagnosticSortColumn.Code).Length, Is.EqualTo(1));
        Assert.That(view.Query(DiagnosticVisibility.None, column: DiagnosticSortColumn.Line), Is.Empty);
        Assert.That(view.SortBuildCount, Is.EqualTo(1), "Hidden results should not force a new ordering");
    }
    [Test]
    public void InvalidArgumentsAndCancelledCachedQueriesAreRejected()
    {
        var view = new DiagnosticTableView(Fixture()); view.Query();
        Assert.Throws<ArgumentNullException>(() => new DiagnosticTableView(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => view.Query((DiagnosticVisibility)8));
        Assert.Throws<ArgumentOutOfRangeException>(() => view.Query(column: (DiagnosticSortColumn)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => view.Query(direction: (DiagnosticSortDirection)99));
        Assert.Throws<ArgumentException>(() => view.Query(direction: DiagnosticSortDirection.Descending));
        Assert.Throws<OperationCanceledException>(() => view.Query(cancellationToken: new CancellationToken(true)));
        Assert.Throws<OperationCanceledException>(() => view.Query(column: DiagnosticSortColumn.Line, cancellationToken: new CancellationToken(true)));
        Assert.That(view.SortBuildCount, Is.Zero); Assert.That(view.QueryBuildCount, Is.EqualTo(1));
    }
    [Test]
    public async Task ImmutableViewsSupportConcurrentQueriesAndDoNotCrossTables()
    {
        var table = Fixture(); var view = new DiagnosticTableView(table);
        var tasks = Enumerable.Range(0, 16).Select(_ => Task.Run(() => view.Query(column: DiagnosticSortColumn.Code))).ToArray();
        await Task.WhenAll(tasks);
        Assert.That(tasks.All(t => t.Result == tasks[0].Result), Is.True);
        Assert.That(view.SortBuildCount, Is.EqualTo(1)); Assert.That(view.QueryBuildCount, Is.EqualTo(1));
        var other = new DiagnosticTableView(Fixture()); var rows = other.Query(column: DiagnosticSortColumn.Code);
        Assert.That(ReferenceEquals(rows[0], tasks[0].Result[0]), Is.False);
    }
    [Test]
    public void MergeSortMatchesStableLinqOrderingAcrossOddLengthInputs()
    {
        var random = new Random(4107);
        foreach (var count in new[] { 0, 1, 2, 3, 7, 31, 257, 1001 })
        {
            using var document = new EditorDocument(new string('x', count + 1)); var snapshot = document.Snapshot();
            var diagnostics = Enumerable.Range(0, count).Select(i => new EditorDiagnostic(random.Next(11).ToString(), "row" + i, IssueSeverity.Error, new(i, 1))).ToImmutableArray();
            var table = new DiagnosticTable(snapshot, LanguageAnalysis.Empty(snapshot) with { Diagnostics = diagnostics });
            var view = new DiagnosticTableView(table);
            foreach (var direction in Enum.GetValues<DiagnosticSortDirection>())
            {
                var expected = direction == DiagnosticSortDirection.Ascending
                    ? table.Rows.OrderBy(r => r.Code, StringComparer.OrdinalIgnoreCase)
                    : table.Rows.OrderByDescending(r => r.Code, StringComparer.OrdinalIgnoreCase);
                Assert.That(view.Query(column: DiagnosticSortColumn.Code, direction: direction), Is.EqualTo(expected), $"n={count}, {direction}");
            }
        }
    }
}
