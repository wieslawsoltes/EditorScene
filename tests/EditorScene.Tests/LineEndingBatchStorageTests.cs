using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class LineEndingBatchStorageTests
{
    [TestCase(1000)]
    [TestCase(4097)]
    public void MaintainingCountsDoesNotAllocateOneMetadataObjectPerBulkEdit(int count)
    {
        var text = string.Concat(Enumerable.Repeat("x\r", count));
        for (var i = 0; i < 3; i++) { _ = Measure(false); _ = Measure(true); }
        var unobserved = Measure(false); var observed = Measure(true);
        Assert.That(observed, Is.LessThanOrEqualTo(unobserved + 4096),
            "Observed delimiter maintenance must not add per-edit metadata objects or rope index cache nodes.");
        long Measure(bool observe)
        {
            using var document = new EditorDocument(text);
            var original = document.Snapshot();
            var info = observe ? original.GetLineEndings() : null;
            var before = GC.GetAllocatedBytesForCurrentThread();
            using (document.Buffer.RunUpdate())
                for (var i = 0; i < count; i++) document.Buffer.Replace(i * 2 + 1, 1, "\n");
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            var result = document.Snapshot().GetLineEndings();
            Assert.That((result.CrCount, result.LfCount, result.CrLfCount), Is.EqualTo((0, count, 0)));
            if (info is not null) Assert.That(info.CrCount, Is.EqualTo(count));
            return allocated;
        }
    }
    [Test]
    public void ObservingInsideAnUpdatePublishesIndependentCurrentRevisions()
    {
        using var document = new EditorDocument("a\rb\r");
        var first = document.Snapshot(); first.GetLineEndings();
        DocumentSnapshot middle;
        using (document.Buffer.RunUpdate())
        {
            document.Buffer.Replace(1, 1, "\n"); middle = document.Snapshot();
            Assert.That(middle.GetLineEndings().Kind, Is.EqualTo(LineEndingKind.Mixed));
            document.Buffer.Replace(3, 1, "\n");
        }
        var last = document.Snapshot();
        Assert.That(last.GetLineEndings().Kind, Is.EqualTo(LineEndingKind.Lf));
        Assert.That(first.GetLineEndings().Kind, Is.EqualTo(LineEndingKind.Cr));
        Assert.That(middle.GetLineEndings().Kind, Is.EqualTo(LineEndingKind.Mixed));
        document.Buffer.UndoStack.Undo(); Assert.That(document.Snapshot().GetLineEndings().Kind, Is.EqualTo(LineEndingKind.Cr));
        document.Buffer.UndoStack.Redo(); Assert.That(document.Snapshot().GetLineEndings().Kind, Is.EqualTo(LineEndingKind.Lf));
    }
}
