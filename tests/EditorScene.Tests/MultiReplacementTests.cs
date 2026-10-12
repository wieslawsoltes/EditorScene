using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class MultiReplacementTests
{
    [Test]
    public void DifferentReplacementsPreservePrimaryCaretAndUndoTogether()
    {
        using var doc = new EditorDocument("aa bb cc"); var multi = new MultiSelection(doc);
        multi.Set([new(6, 8), new(0, 2)]); multi.ReplaceEach(["first", "last"]);
        Assert.That(doc.Buffer.Text, Is.EqualTo("first bb last"));
        Assert.That(multi.PrimaryIndex, Is.Zero); Assert.That(multi.PrimaryRange, Is.EqualTo(new SelectionRange(5, 5)));
        Assert.That(multi.Ranges[1], Is.EqualTo(new SelectionRange(13, 13)));
        doc.Buffer.UndoStack.Undo(); Assert.That(doc.Buffer.Text, Is.EqualTo("aa bb cc"));
    }
    [Test]
    public void InvalidCountNullAndDeniedReplacementDoNotModifySelectionsOrText()
    {
        using var doc = new EditorDocument("a b"); var multi = new MultiSelection(doc); multi.Set([new(0, 1), new(2, 3)]);
        var view = multi.Ranges;
        Assert.Throws<ArgumentException>(() => multi.ReplaceEach(["x"]));
        Assert.Throws<ArgumentException>(() => multi.ReplaceEach(["x", null!]));
        Assert.Throws<InvalidOperationException>(() => multi.ReplaceEach(["x", "long"], edit => edit.Offset == 0));
        Assert.That(doc.Buffer.Text, Is.EqualTo("a b")); Assert.That(doc.Revision, Is.Zero); Assert.That(multi.Ranges, Is.SameAs(view));
    }
    [Test]
    public void ReplacementArrayIsCapturedBeforePermissionCallbacks()
    {
        using var doc = new EditorDocument("a b"); var multi = new MultiSelection(doc); multi.Set([new(0, 1), new(2, 3)]);
        string[] texts = ["A", "B"];
        multi.ReplaceEach(texts, _ => { texts[1] = "CHANGED"; return true; });
        Assert.That(doc.Buffer.Text, Is.EqualTo("A B"));
    }
    [Test]
    public void PreflightSelectionChangesAbortWithoutWritingText()
    {
        using var doc = new EditorDocument("a b"); var multi = new MultiSelection(doc); multi.Set([new(0, 1), new(2, 3)]);
        Assert.Throws<InvalidOperationException>(() => multi.ReplaceEach(["A", "B"], _ => { multi.Set([new(1, 1)]); return true; }));
        Assert.That(doc.Buffer.Text, Is.EqualTo("a b")); Assert.That(multi.PrimaryRange, Is.EqualTo(new SelectionRange(1, 1)));
    }
    [Test]
    public void EditNotificationCannotOverwriteANewerHostSelection()
    {
        using var doc = new EditorDocument("a b"); var multi = new MultiSelection(doc); multi.Set([new(0, 1), new(2, 3)]);
        doc.Changed += (_, _) => multi.Set([new(1, 1)]);
        multi.ReplaceEach(["A", "B"]);
        Assert.That(doc.Buffer.Text, Is.EqualTo("A B")); Assert.That(multi.PrimaryRange, Is.EqualTo(new SelectionRange(1, 1)));
    }
    [Test]
    public void EmptyDifferentReplacementsDeduplicateCoincidentOutputCarets()
    {
        using var doc = new EditorDocument("ab"); var multi = new MultiSelection(doc); multi.Set([new(0, 1), new(1, 2)]);
        multi.ReplaceEach(["", ""]);
        Assert.That(doc.Buffer.Text, Is.Empty); Assert.That(multi.Ranges.Count, Is.EqualTo(1));
        multi.ReplaceEach(["x"]); Assert.That(doc.Buffer.Text, Is.EqualTo("x"));
    }
}
