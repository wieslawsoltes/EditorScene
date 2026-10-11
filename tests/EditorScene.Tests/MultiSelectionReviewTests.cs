using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class MultiSelectionReviewTests
{
    [Test]
    public void AdjacentSelectionsRemainIndependentAndUndoTogether()
    {
        using var doc = new EditorDocument("aaaa"); var selection = new MultiSelection(doc);
        selection.Set([new(0, 2), new(2, 4)]); Assert.That(selection.Ranges.Count, Is.EqualTo(2));
        selection.Replace("x"); Assert.That(doc.Buffer.Text, Is.EqualTo("xx"));
        doc.Buffer.UndoStack.Undo(); Assert.That(doc.Buffer.Text, Is.EqualTo("aaaa"));
    }
    [Test]
    public void LatestSelectionRemainsPrimaryAfterWrappingToAnEarlierOffset()
    {
        using var doc = new EditorDocument("one one one"); var selection = new MultiSelection(doc);
        selection.Set([new(8, 11), new(0, 3)]);
        Assert.That(selection.PrimaryRange, Is.EqualTo(new SelectionRange(0, 3)));
        selection.Replace("x"); Assert.That(doc.Buffer.Text, Is.EqualTo("x one x"));
        Assert.That(selection.PrimaryRange, Is.EqualTo(new SelectionRange(1, 1)));
    }
    [Test]
    public void DeniedDeletionPreservesAllSelectionState()
    {
        using var doc = new EditorDocument("abc def"); var selection = new MultiSelection(doc);
        selection.Set([new(1, 1), new(5, 5)]); var view = selection.Ranges; var primary = selection.PrimaryRange;
        Assert.Throws<InvalidOperationException>(() => selection.Delete(true, e => e.Offset < 3));
        Assert.That(selection.Ranges, Is.SameAs(view)); Assert.That(selection.PrimaryRange, Is.EqualTo(primary));
        Assert.That(doc.Buffer.Text, Is.EqualTo("abc def")); Assert.That(doc.Revision, Is.Zero);
    }
    [Test]
    public void CoincidentPostDeletionCaretsAreDeduplicated()
    {
        using var doc = new EditorDocument("ab"); var selection = new MultiSelection(doc);
        selection.Set([new(0, 1), new(1, 2)]); selection.Replace("");
        Assert.That(selection.Ranges, Is.EqualTo(new[] { new SelectionRange(0, 0) }));
        selection.Replace("x"); Assert.That(doc.Buffer.Text, Is.EqualTo("x"));
    }
    [Test]
    public void ReverseSelectionDirectionAndPrimarySurviveNormalization()
    {
        using var doc = new EditorDocument("abcdef"); var selection = new MultiSelection(doc);
        selection.Set([new(0, 3), new(5, 2)]);
        Assert.That(selection.Ranges, Is.EqualTo(new[] { new SelectionRange(5, 0) }));
        Assert.That(selection.PrimaryIndex, Is.Zero);
    }
    [Test]
    public void ReentrantPreflightCannotOverwriteANewerSelection()
    {
        using var doc = new EditorDocument("abcd"); var selection = new MultiSelection(doc); selection.Set([new(1, 1)]);
        Assert.Throws<InvalidOperationException>(() => selection.Replace("x", _ => { selection.Set([new(3, 3)]); return true; }));
        Assert.That(doc.Buffer.Text, Is.EqualTo("abcd")); Assert.That(selection.PrimaryRange, Is.EqualTo(new SelectionRange(3, 3)));
    }
    [TestCase(true, "A🙂B", 2, "AB")]
    [TestCase(false, "AéB", 2, "AB")]
    [TestCase(true, "A\r\nB", 2, "AB")]
    [TestCase(false, "A\r\nB", 2, "AB")]
    [TestCase(true, "A\r\nB", 3, "AB")]
    public void DeletionNeverLeavesPartialGraphemesOrCrLf(bool backwards, string text, int caret, string expected)
    {
        using var doc = new EditorDocument(text); var selection = new MultiSelection(doc); selection.Set([new(caret, caret)]);
        selection.Delete(backwards); Assert.That(doc.Buffer.Text, Is.EqualTo(expected));
    }
    [Test]
    public void SelectionViewIsCachedAndInvalidInputIsAtomic()
    {
        using var doc = new EditorDocument("a"); var selection = new MultiSelection(doc); selection.Set([new(0, 1)]);
        var view = selection.Ranges; Assert.That(selection.Ranges, Is.SameAs(view));
        Assert.Throws<ArgumentOutOfRangeException>(() => selection.Set([new(int.MinValue, int.MaxValue)]));
        Assert.That(selection.Ranges, Is.SameAs(view));
    }
}
