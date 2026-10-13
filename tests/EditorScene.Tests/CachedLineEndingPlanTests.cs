using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class CachedLineEndingPlanTests
{
    [TestCase("\n")]
    [TestCase("\r")]
    [TestCase("\r\n")]
    public void ObservedMetadataAndUnobservedSourceProduceIdenticalMinimalPlans(string delimiter)
    {
        foreach (var text in new[] { "", "ordinary🙂", "a\r\nb\rc\n", "\r\n\r\r\n\n",
            new string('x', 4095) + "\r\n" + new string('y', 4095) + "\r" })
        {
            using var document = new EditorDocument(text); var snapshot = document.Snapshot();
            var uncached = LineEndingEditPlan.Create(snapshot, delimiter); var info = snapshot.GetLineEndings();
            var cached = LineEndingEditPlan.Create(snapshot, delimiter);
            Assert.That(cached.Edits, Is.EqualTo(uncached.Edits));
            Assert.That((cached.CrCount, cached.LfCount, cached.CrLfCount), Is.EqualTo((info.CrCount, info.LfCount, info.CrLfCount)));
            var expected = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Replace("\n", delimiter, StringComparison.Ordinal);
            cached.Apply(document); Assert.That(document.Buffer.Text, Is.EqualTo(expected));
        }
    }
    [TestCase("\n")]
    [TestCase("\r")]
    [TestCase("\r\n")]
    public void CachedCanonicalPlansNeedNoReaderAllocationAndKeepRedo(string delimiter)
    {
        using var document = new EditorDocument(string.Concat(Enumerable.Repeat("line" + delimiter, 100000)));
        document.Buffer.Insert(0, "x"); document.Buffer.UndoStack.Undo();
        var snapshot = document.Snapshot(); snapshot.GetLineEndings();
        for (var i = 0; i < 32; i++) _ = LineEndingEditPlan.Create(snapshot, delimiter);
        var before = GC.GetAllocatedBytesForCurrentThread(); var plan = LineEndingEditPlan.Create(snapshot, delimiter);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.LessThan(256), "Only the result plan should be allocated, not a source reader.");
        Assert.That(plan.Edits, Is.Empty); Assert.That(plan.Apply(document), Is.False);
        Assert.That(document.Buffer.UndoStack.CanRedo, Is.True);
        Assert.Throws<OperationCanceledException>(() => LineEndingEditPlan.Create(snapshot, delimiter, new CancellationToken(true)));
    }
    [Test]
    public void IncrementalCountsDriveCurrentPlansWithoutChangingFrozenPlans()
    {
        using var document = new EditorDocument("a\r\nb\nc\r"); var original = document.Snapshot(); original.GetLineEndings();
        var oldPlan = LineEndingEditPlan.Create(original, "\n");
        document.Buffer.Remove(2, 1); var current = document.Snapshot();
        var newPlan = LineEndingEditPlan.Create(current, "\r\n");
        Assert.That((newPlan.CrCount, newPlan.LfCount, newPlan.CrLfCount), Is.EqualTo((2, 1, 0)));
        Assert.That(oldPlan.CrLfCount, Is.EqualTo(1)); Assert.Throws<InvalidOperationException>(() => oldPlan.Apply(document));
        newPlan.Apply(document); Assert.That(document.Buffer.Text, Is.EqualTo("a\r\nb\r\nc\r\n"));
        Assert.That(document.Snapshot().HasUniformLineTerminators, Is.True);
    }
}
