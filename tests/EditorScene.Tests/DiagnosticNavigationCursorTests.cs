using System.Collections.Immutable;
using EditorScene.LanguageServices;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class DiagnosticNavigationCursorTests
{
    private static ImmutableArray<DiagnosticTableRow> Rows() =>
    [
        new(new("A", "first", IssueSeverity.Error, new(3, 1)), 1, 4, "a.cs"),
        new(new("B", "same position", IssueSeverity.Warning, new(3, 1)), 1, 4, "a.cs"),
        new(new("C", "last", IssueSeverity.Information, new(12, 0)), 2, 1, "a.cs")
    ];
    [TestCase(false)]
    [TestCase(true)]
    public void StartsAtTheCorrespondingEndAndWrapsInBothDirections(bool backwards)
    {
        var rows = Rows(); var cursor = new DiagnosticNavigationCursor(rows);
        Assert.That(cursor.CurrentIndex, Is.EqualTo(-1)); Assert.That(cursor.Current, Is.Null);
        var expected = backwards ? new[] { 2, 1, 0, 2 } : new[] { 0, 1, 2, 0 };
        foreach (var index in expected)
        {
            Assert.That(cursor.TryMove(out var row, backwards), Is.True);
            Assert.That(cursor.CurrentIndex, Is.EqualTo(index)); Assert.That(row, Is.SameAs(rows[index]));
        }
    }
    [TestCase(false)]
    [TestCase(true)]
    public void NonWrappingFailurePreservesTheLastSelection(bool backwards)
    {
        var rows = Rows(); var cursor = new DiagnosticNavigationCursor(rows);
        cursor.SelectIndex(backwards ? 0 : rows.Length - 1);
        var selected = cursor.Current;
        Assert.That(cursor.TryMove(out var row, backwards, wrap: false), Is.False);
        Assert.That(row, Is.Null); Assert.That(cursor.Current, Is.SameAs(selected));
    }
    [Test]
    public void EmptyAndSingleRowResultsHaveExplicitEndBehavior()
    {
        var empty = new DiagnosticNavigationCursor([]);
        Assert.That(empty.TryMove(out _), Is.False); Assert.That(empty.TryMove(out _, true), Is.False);
        empty.SelectIndex(-1); Assert.Throws<ArgumentOutOfRangeException>(() => empty.SelectIndex(0));
        var row = Rows()[0]; var single = new DiagnosticNavigationCursor([row]);
        Assert.That(single.TryMove(out var first), Is.True); Assert.That(first, Is.SameAs(row));
        Assert.That(single.TryMove(out _), Is.True); Assert.That(single.TryMove(out _, wrap: false), Is.False);
    }
    [Test]
    public void DuplicatePositionsAndRepeatedReferencesAreNotCollapsed()
    {
        var rows = Rows(); var cursor = new DiagnosticNavigationCursor([rows[0], rows[1], rows[0]]);
        for (var i = 0; i < 3; i++) { Assert.That(cursor.TryMove(out _), Is.True); Assert.That(cursor.CurrentIndex, Is.EqualTo(i)); }
        Assert.That(cursor.Current, Is.SameAs(rows[0]));
    }
    [Test]
    public void SelectionAndResetFollowTheSuppliedResultOrder()
    {
        var rows = Rows(); var cursor = new DiagnosticNavigationCursor(rows.Reverse().ToImmutableArray());
        cursor.SelectIndex(1); cursor.TryMove(out var next);
        Assert.That(next, Is.SameAs(rows[0]));
        cursor.SelectIndex(-1); cursor.TryMove(out next);
        Assert.That(next, Is.SameAs(rows[2]));
    }
    [Test]
    public void InvalidConstructionAndSelectionDoNotMutateTheCursor()
    {
        Assert.Throws<ArgumentException>(() => new DiagnosticNavigationCursor(default));
        Assert.Throws<ArgumentException>(() => new DiagnosticNavigationCursor([null!]));
        var cursor = new DiagnosticNavigationCursor(Rows()); cursor.SelectIndex(1);
        foreach (var index in new[] { -2, 3, int.MaxValue }) Assert.Throws<ArgumentOutOfRangeException>(() => cursor.SelectIndex(index));
        Assert.That(cursor.CurrentIndex, Is.EqualTo(1));
    }
    [Test]
    public void WarmMovementDoesNotAllocateOrModifyTheImmutableRows()
    {
        var rows = Rows(); var cursor = new DiagnosticNavigationCursor(rows);
        for (var i = 0; i < 20; i++) cursor.TryMove(out _);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++) { cursor.TryMove(out _); cursor.TryMove(out _, true); }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero); Assert.That(rows[0].Code, Is.EqualTo("A")); Assert.That(rows[1].Code, Is.EqualTo("B"));
    }
}
