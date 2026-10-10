using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class SnapshotTrackingTests
{
    [TestCase(PointTrackingMode.Negative, 1)]
    [TestCase(PointTrackingMode.Positive, 4)]
    public void PointInsertionAffinityIsExplicit(PointTrackingMode mode, int expected)
    {
        using var doc = new EditorDocument("ab"); var original = doc.Snapshot(); var point = original.CreateTrackingPoint(1, mode);
        doc.ApplyEdits([new(1, 0, "XYZ")], doc.Revision);
        Assert.That(point.GetOffset(doc.Snapshot()), Is.EqualTo(expected)); Assert.That(original.Text, Is.EqualTo("ab"));
    }
    [TestCase(SpanTrackingMode.EdgeExclusive, "bc")]
    [TestCase(SpanTrackingMode.EdgeInclusive, "XbcY")]
    [TestCase(SpanTrackingMode.EdgeNegative, "Xbc")]
    [TestCase(SpanTrackingMode.EdgePositive, "bcY")]
    public void SpanEdgeInsertionsRespectTrackingMode(SpanTrackingMode mode, string expected)
    {
        using var doc = new EditorDocument("abcd"); var span = doc.Snapshot().GetSpan(1, 2).Track(mode);
        doc.ApplyEdits([new(1, 0, "X"), new(3, 0, "Y")], doc.Revision);
        Assert.That(span.GetSpan(doc.Snapshot()).Text, Is.EqualTo(expected));
    }
    [TestCase(SpanTrackingMode.EdgeExclusive, 2, 0)]
    [TestCase(SpanTrackingMode.EdgeInclusive, 1, 1)]
    [TestCase(SpanTrackingMode.EdgeNegative, 1, 0)]
    [TestCase(SpanTrackingMode.EdgePositive, 2, 0)]
    public void EmptyRangesHaveDefinedBoundaryBehavior(SpanTrackingMode mode, int offset, int length)
    {
        using var doc = new EditorDocument("ab"); var span = doc.Snapshot().GetSpan(1, 0).Track(mode);
        doc.ApplyEdits([new(1, 0, "X")], doc.Revision); var mapped = span.GetSpan(doc.Snapshot());
        Assert.That(mapped.Offset, Is.EqualTo(offset)); Assert.That(mapped.Length, Is.EqualTo(length));
    }
    [Test]
    public void InteriorInsertionsAreIncludedAndEntireDeletionCollapsesRange()
    {
        using var doc = new EditorDocument("abcd"); var span = doc.Snapshot().GetSpan(1, 2).Track();
        doc.ApplyEdits([new(2, 0, "🙂")], doc.Revision); Assert.That(span.GetSpan(doc.Snapshot()).Text, Is.EqualTo("b🙂c"));
        doc.ApplyEdits([new(1, 4, "")], doc.Revision); var removed = span.GetSpan(doc.Snapshot());
        Assert.That(removed.Offset, Is.EqualTo(1)); Assert.That(removed.IsEmpty, Is.True);
    }
    [Test]
    public void TranslationWorksBackwardsAndAcrossUndoRedo()
    {
        using var doc = new EditorDocument("first\nsecond"); var original = doc.Snapshot();
        doc.ApplyEdits([new(0, 0, "🙂\r\n")], doc.Revision); var next = doc.Snapshot();
        Assert.That(next.TranslateOffsetTo(original, 10), Is.EqualTo(6));
        var point = original.CreateTrackingPoint(6); Assert.That(point.GetPosition(next), Is.EqualTo(new TextPosition(2, 0)));
        doc.Buffer.UndoStack.Undo(); Assert.That(point.GetOffset(doc.Snapshot()), Is.EqualTo(6));
        doc.Buffer.UndoStack.Redo(); Assert.That(point.GetOffset(doc.Snapshot()), Is.EqualTo(10));
    }
    [Test]
    public void InvalidRangesModesAndDifferentDocumentsAreRejected()
    {
        using var first = new EditorDocument("abc"); using var second = new EditorDocument("abc"); var snapshot = first.Snapshot();
        Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.GetSpan(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.GetSpan(1, int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.CreateTrackingPoint(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.CreateTrackingPoint(0, (PointTrackingMode)9));
        Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.GetSpan(0, 1).Track((SpanTrackingMode)9));
        Assert.Throws<ArgumentException>(() => snapshot.GetSpan(0, 1).TranslateTo(second.Snapshot()));
        Assert.That(snapshot.GetSpan(1, 1).Contains(2), Is.False);
    }
    [Test]
    public async Task VersionTokensTranslateOnWorkersAfterLiveDocumentDisposal()
    {
        var doc = new EditorDocument("alpha beta"); var origin = doc.Snapshot(); var tracked = origin.GetSpan(6, 4).Track();
        doc.ApplyEdits([new(0, 0, "prefix ")], doc.Revision); var target = doc.Snapshot(); doc.Dispose();
        var text = await Task.Run(() => tracked.GetSpan(target).Text);
        Assert.That(text, Is.EqualTo("beta"));
    }
}
