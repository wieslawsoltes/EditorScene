using System.Reflection;
using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class SnapshotLineIndexTests
{
    internal static bool IsFlattened(DocumentSnapshot snapshot) => ((Lazy<string>)typeof(DocumentSnapshot).GetField("_text", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(snapshot)!).IsValueCreated;
    private static void AssertIndex(DocumentSnapshot snapshot, string text, bool everyOffset = true)
    {
        var expected = new List<(int Start, int Length, int Delimiter)>(); var start = 0;
        for (var i = 0; i < text.Length; i++)
            if (text[i] is '\r' or '\n')
            {
                var delimiter = text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
                expected.Add((start, i - start, delimiter)); i += delimiter - 1; start = i + 1;
            }
        expected.Add((start, text.Length - start, 0));
        Assert.That(snapshot.LineCount, Is.EqualTo(expected.Count));
        for (var line = 0; line < expected.Count; line++)
        {
            var actual = snapshot.GetLine(line); var wanted = expected[line];
            Assert.That((actual.Offset, actual.Length, actual.DelimiterLength), Is.EqualTo(wanted), $"Line {line}");
            Assert.That(actual.LineNumber, Is.EqualTo(line));
            Assert.That(snapshot.GetOffset(new(line, int.MaxValue)), Is.EqualTo(wanted.Start + wanted.Length));
            Assert.That(snapshot.GetLineFromOffset(actual.Offset), Is.EqualTo(actual));
        }
        if (everyOffset)
        {
            var line = 0;
            for (var i = 0; i <= text.Length; i++)
            {
                while (line + 1 < expected.Count && expected[line + 1].Start <= i) line++;
                Assert.That(snapshot.GetPosition(i), Is.EqualTo(new TextPosition(line, i - expected[line].Start)), "offset=" + i);
            }
        }
        Assert.That(IsFlattened(snapshot), Is.False, "Index operations must never materialize Text.");
    }
    [TestCase("")]
    [TestCase("x")]
    [TestCase("\r\n")]
    [TestCase("\n\r\r\n\n")]
    [TestCase("a🙂\r\nb\rc\n")]
    public void ImmutableLineApiPreservesDelimiterSemantics(string text)
    { using var document = new EditorDocument(text); AssertIndex(document.Snapshot(), text); }

    [TestCase(4095)] [TestCase(4096)] [TestCase(8191)]
    public void CrLfAcrossChunkBoundariesSurvivesBoundaryEdits(int prefix)
    {
        var text = new string('a', prefix) + "\r\nZ\r\n";
        using var document = new EditorDocument(text); var old = document.Snapshot(); AssertIndex(old, text);
        foreach (var change in new[] { new TextEdit(prefix + 1, 0, "x"), new TextEdit(prefix + 1, 1, ""), new TextEdit(prefix, 1, ""), new TextEdit(prefix, 0, "\r") })
        {
            text = text.Remove(change.Offset, change.Length).Insert(change.Offset, change.NewText);
            document.ApplyEdits([change], document.Revision); AssertIndex(document.Snapshot(), text);
        }
        AssertIndex(old, new string('a', prefix) + "\r\nZ\r\n");
    }
    [Test]
    public void RandomEditsAndUndoRedoMatchAReferenceLineScan()
    {
        var random = new Random(4792); var text = string.Concat(Enumerable.Repeat("line🙂\r\nnext\ronly\n", 800));
        using var document = new EditorDocument(text); _ = document.Snapshot().LineCount;
        var retained = new List<(DocumentSnapshot Snapshot, string Text)>();
        for (var i = 0; i < 600; i++)
        {
            var at = random.Next(text.Length + 1); var removed = random.Next(Math.Min(300, text.Length - at) + 1);
            var insert = new[] { "", "a", "\r", "\n", "\r\n", "🙂\n", new string('x', 5000), "\n\n\r\r\n" }[random.Next(8)];
            text = text.Remove(at, removed).Insert(at, insert); document.ApplyEdits([new(at, removed, insert)], document.Revision);
            AssertIndex(document.Snapshot(), text, false);
            if (i % 120 == 0) retained.Add((document.Snapshot(), text));
        }
        var last = document.Snapshot(); document.Buffer.UndoStack.Undo(); AssertIndex(document.Snapshot(), document.Buffer.Text, false);
        document.Buffer.UndoStack.Redo(); AssertIndex(document.Snapshot(), text, false);
        document.Dispose(); foreach (var saved in retained) AssertIndex(saved.Snapshot, saved.Text, false);
        AssertIndex(last, text, false);
    }
    [Test]
    public void SnapshotCaptureIsReusedAndDoesNotRetainPreviousTextOrMetadata()
    {
        using var document = new EditorDocument("one\ntwo", "a.txt"); var before = document.Snapshot();
        Assert.That(document.Snapshot(), Is.SameAs(before));
        document.FileName = "b.txt"; var renamed = document.Snapshot();
        Assert.That(renamed, Is.Not.SameAs(before)); Assert.That(before.FileName, Is.EqualTo("a.txt"));
        Assert.That(renamed.FileName, Is.EqualTo("b.txt"));
        document.Buffer.Insert(0, "new\n"); Assert.That(document.Snapshot(), Is.Not.SameAs(renamed));
        AssertIndex(document.Snapshot(), "new\none\ntwo");
    }
    [Test]
    public async Task LazyIndexAndCapturedLinesAreSafeAcrossWorkersAndDisposal()
    {
        var text = string.Concat(Enumerable.Repeat("x\r\n", 6000)); var document = new EditorDocument(text); var snapshot = document.Snapshot();
        document.Buffer.Insert(0, "changed\n"); var next = document.Snapshot(); document.Dispose();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => AssertIndex(snapshot, text, false))));
        AssertIndex(next, "changed\n" + text, false);
    }
    [Test]
    public void InvalidLineAndOffsetInputsAreRejected()
    {
        using var document = new EditorDocument("a\n"); var snapshot = document.Snapshot();
        Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.GetLine(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.GetLine(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.GetLineFromOffset(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.GetOffset(new(0, -1)));
        Assert.That(snapshot.GetLine(1).Text, Is.Empty);
    }
    [Test]
    public void WarmSmallEditDoesNotAllocateAnotherLargeLineTableOrFlattenText()
    {
        using var warm = new EditorDocument("x\n"); _ = warm.Snapshot().LineCount; warm.Buffer.Insert(0, "x"); _ = warm.Snapshot().LineCount;
        using var document = new EditorDocument(string.Concat(Enumerable.Repeat("1234567890123456789012345678901234567890\r\n", 100000)));
        var snapshot = document.Snapshot(); Assert.That(snapshot.LineCount, Is.EqualTo(100001));
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 50; i++) { document.Buffer.Insert(2_000_000, "x"); _ = document.Snapshot().GetPosition(2_000_001); }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
        Assert.That(allocated, Is.LessThan(1_000_000), $"50 edits allocated {allocated:N0} bytes");
        Assert.That(IsFlattened(snapshot) || IsFlattened(document.Snapshot()), Is.False);
        bytes = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) _ = document.Snapshot();
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - bytes, Is.EqualTo(0), "Unchanged snapshot capture should allocate nothing.");
    }
}
