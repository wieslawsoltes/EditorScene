using System.Collections.Immutable;
using EditorScene.LanguageServices;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class DiagnosticCursorIdentityTests
{
    [Test]
    public void EqualValuedDiagnosticsRetainDistinctReferencePositions()
    {
        var first = new DiagnosticTableRow(new("E", "message", IssueSeverity.Error, new(0, 1)), 1, 1, "file.cs");
        var second = first with { };
        Assert.That(first, Is.EqualTo(second)); Assert.That(ReferenceEquals(first, second), Is.False);
        var cursor = new DiagnosticNavigationCursor([first, second]);
        cursor.TryMove(out var selected); Assert.That(ReferenceEquals(selected, first), Is.True);
        cursor.TryMove(out selected); Assert.That(ReferenceEquals(selected, second), Is.True);
        Assert.That(cursor.CurrentIndex, Is.EqualTo(1));
    }
    [Test]
    public void ReplacingAnImmutableResultDoesNotMutateAnExistingCursor()
    {
        var row = new DiagnosticTableRow(new("E", "original", IssueSeverity.Error, new(0, 1)), 1, 1, "file.cs");
        var rows = ImmutableArray.Create(row); var original = new DiagnosticNavigationCursor(rows);
        rows = rows.SetItem(0, row with { Diagnostic = row.Diagnostic with { Message = "updated" } });
        var replacement = new DiagnosticNavigationCursor(rows);
        original.TryMove(out var oldRow); replacement.TryMove(out var newRow);
        Assert.That(oldRow!.Message, Is.EqualTo("original")); Assert.That(newRow!.Message, Is.EqualTo("updated"));
        Assert.That(original.Current, Is.SameAs(row)); Assert.That(ReferenceEquals(oldRow, newRow), Is.False);
    }
}
