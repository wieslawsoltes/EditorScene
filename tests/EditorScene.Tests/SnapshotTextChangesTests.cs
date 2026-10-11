using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class SnapshotTextChangesTests
{
    private static void Check(DocumentSnapshot a, DocumentSnapshot b, string oldText, string newText)
    {
        var edit = SnapshotTextChanges.GetMinimalChange(a, b);
        Assert.That(oldText.Remove(edit.Offset, edit.Length).Insert(edit.Offset, edit.NewText), Is.EqualTo(newText));
        Assert.That(SnapshotLineIndexTests.IsFlattened(a) || SnapshotLineIndexTests.IsFlattened(b), Is.False);
        bool Split(string text, int at) => at > 0 && at < text.Length && ((text[at - 1] == '\r' && text[at] == '\n') || (char.IsHighSurrogate(text[at - 1]) && char.IsLowSurrogate(text[at])));
        Assert.That(Split(oldText, edit.Offset) || Split(oldText, edit.End) || Split(newText, edit.Offset) || Split(newText, edit.Offset + edit.NewText.Length), Is.False);
    }
    [Test]
    public void RandomBatchedHistoryMatchesTheFinalDocumentWithoutFlattening()
    {
        var random = new Random(412991); var text = string.Concat(Enumerable.Repeat("Alpha🙂\r\nBeta\rGamma\n", 30));
        using var doc = new EditorDocument(text); var old = doc.Snapshot(); var initial = text;
        for (var i = 0; i < 400; i++)
        {
            var offset = random.Next(text.Length + 1); var count = random.Next(Math.Min(40, text.Length - offset) + 1);
            var added = new[] { "X", "", "\r", "\n", "\r\n", "🙂", "new\nline" }[random.Next(7)];
            text = text.Remove(offset, count).Insert(offset, added); doc.ApplyEdits([new(offset, count, added)], doc.Revision);
            Check(old, doc.Snapshot(), initial, text);
            if (i % 7 == 0) { old = doc.Snapshot(); initial = text; }
        }
    }
    [Test]
    public void HistoryWorksAcrossUndoAndRedoAndHasABoundedFallback()
    {
        using var doc = new EditorDocument("A\r\n🙂Z"); var old = doc.Snapshot();
        doc.Buffer.Insert(1, "new"); var changed = doc.Snapshot();
        Check(old, changed, "A\r\n🙂Z", "Anew\r\n🙂Z");
        doc.Buffer.UndoStack.Undo(); Check(changed, doc.Snapshot(), "Anew\r\n🙂Z", "A\r\n🙂Z");
        for (var i = 0; i < 2100; i++) doc.Buffer.Insert(0, "x");
        Check(old, doc.Snapshot(), "A\r\n🙂Z", new string('x', 2100) + "A\r\n🙂Z");
    }
    [Test]
    public void LargeDocumentSmallEditHasBoundedAllocationAndCorrectRange()
    {
        using var doc = new EditorDocument(new string('a', 4_000_000)); var old = doc.Snapshot();
        doc.Buffer.Insert(2_000_000, "X"); var current = doc.Snapshot();
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            var edit = SnapshotTextChanges.GetMinimalChange(old, current);
            if (edit.Offset != 2_000_000 || edit.Length != 0 || edit.NewText != "X") throw new InvalidOperationException();
        }
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - bytes, Is.LessThan(100_000));
        Check(old, current, new string('a', 4_000_000), new string('a', 2_000_000) + "X" + new string('a', 2_000_000));
    }
    [Test]
    public void AdjacentCrLfAndSurrogateFragmentsAreExpandedSafely()
    {
        foreach (var input in new[] { "a\r\nb", "A🙂B", "a\nb", "a\rb" })
        {
            using var doc = new EditorDocument(input); var old = doc.Snapshot(); doc.Buffer.Remove(1, 1);
            Check(old, doc.Snapshot(), input, input.Remove(1, 1));
        }
    }
    [Test]
    public void IdentityForeignSnapshotsReverseRevisionsAndCancellation()
    {
        using var doc = new EditorDocument("abc"); using var other = new EditorDocument("abc"); var old = doc.Snapshot();
        Assert.That(SnapshotTextChanges.GetMinimalChange(old, old), Is.EqualTo(new TextEdit(3, 0, "")));
        Assert.Throws<ArgumentException>(() => SnapshotTextChanges.GetMinimalChange(old, other.Snapshot()));
        doc.Buffer.Insert(0, "X"); Assert.Throws<ArgumentException>(() => SnapshotTextChanges.GetMinimalChange(doc.Snapshot(), old));
        Assert.Throws<OperationCanceledException>(() => SnapshotTextChanges.GetMinimalChange(old, doc.Snapshot(), new CancellationToken(true)));
    }
}
