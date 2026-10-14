using System.Reflection;
using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class LineEndingInfoTests
{
    [TestCase("", 0, 0, 0, LineEndingKind.None)]
    [TestCase("plain🙂\u0085\u2028\u2029", 0, 0, 0, LineEndingKind.None)]
    [TestCase("a\r\nb\r\n", 0, 0, 2, LineEndingKind.CrLf)]
    [TestCase("\r\r", 2, 0, 0, LineEndingKind.Cr)]
    [TestCase("\n\n", 0, 2, 0, LineEndingKind.Lf)]
    [TestCase("a\rb\nc\r\n", 1, 1, 1, LineEndingKind.Mixed)]
    [TestCase("\r\n\n\r\r\n", 1, 1, 2, LineEndingKind.Mixed)]
    public void CountsKindsAndPriorityAreExact(string text, int cr, int lf, int crlf, LineEndingKind kind)
    {
        using var document = new EditorDocument(text); var snapshot = document.Snapshot(); var info = snapshot.GetLineEndings();
        Assert.That((info.CrCount, info.LfCount, info.CrLfCount), Is.EqualTo((cr, lf, crlf)));
        Assert.That(info.DelimiterCount, Is.EqualTo((long)cr + lf + crlf)); Assert.That(info.Kind, Is.EqualTo(kind));
        Assert.That(snapshot.HasUniformLineTerminators, Is.EqualTo(kind != LineEndingKind.Mixed));
        Assert.That(snapshot.InferredLineTerminator, Is.EqualTo(crlf > 0 ? "\r\n" : lf > 0 ? "\n" : cr > 0 ? "\r" : Environment.NewLine));
        Assert.That(snapshot.GetLineEndings(), Is.SameAs(info)); AssertLazySourceUntouched(snapshot);
    }
    [Test]
    public void InferenceUsesPriorityNotFirstOrMostFrequentDelimiter()
    {
        using var document = new EditorDocument("a\r" + new string('\n', 30) + "b\r\n");
        Assert.That(document.PreferredLineEnding, Is.EqualTo("\r\n"));
        // First delimiter is deliberately a standalone CR in this second fixture.
        document.Buffer.Text = "a\rb" + new string('\n', 30) + "c\r\n";
        Assert.That(document.PreferredLineEnding, Is.EqualTo("\r"));
        Assert.That(document.Snapshot().InferredLineTerminator, Is.EqualTo("\r\n"));
    }
    [Test]
    public void ReaderBoundariesAndLongOrdinaryRunsPreserveCrPairs()
    {
        foreach (var gap in new[] { 0, 1, 126, 127, 128, 4094, 4095, 4096, 8191 })
        foreach (var tail in new[] { "\n", "x\n", "\r\n", "\r", "xx", "" })
        {
            var text = new string('x', gap) + "\r" + tail + new string('z', 4096) + "\r";
            using var document = new EditorDocument(text); Verify(document.Snapshot(), text);
        }
    }
    [Test]
    public void EveryShortReplacementAndUndoMatchesAnIndependentScalarOracle()
    {
        foreach (var text in Strings(4))
        for (var start = 0; start <= text.Length; start++)
        for (var end = start; end <= text.Length; end++)
        foreach (var insertion in new[] { "", "x", "\r", "\n", "\r\n", "\n\r", "xx", "x\n" })
        {
            using var document = new EditorDocument(text); var original = document.Snapshot(); var saved = original.GetLineEndings();
            var revision = document.Revision; document.Buffer.Replace(start, end - start, insertion);
            Verify(document.Snapshot(), text[..start] + insertion + text[end..]); Verify(original, text);
            Assert.That(original.GetLineEndings(), Is.SameAs(saved));
            if (document.Revision != revision)
            {
                document.Buffer.UndoStack.Undo(); Verify(document.Snapshot(), text);
                document.Buffer.UndoStack.Redo(); Verify(document.Snapshot(), text[..start] + insertion + text[end..]);
            }
        }
    }
    [Test]
    public void RandomRepeatedEditsKeepOldSnapshotsAndUnobservedRevisionsIndependent()
    {
        var random = new Random(66271); var text = string.Concat(Enumerable.Repeat("a\r\nb\rc\nx🙂", 1000));
        using var document = new EditorDocument(text); var original = document.Snapshot(); original.GetLineEndings();
        for (var i = 0; i < 1000; i++)
        {
            var start = random.Next(text.Length + 1); var length = random.Next(Math.Min(25, text.Length - start) + 1);
            var insertion = new[] { "\r", "\n", "\r\n", "a\rb\nx", "", "🙂" }[random.Next(6)];
            document.Buffer.Replace(start, length, insertion); text = text[..start] + insertion + text[(start + length)..];
            if (i % 7 == 0) Verify(document.Snapshot(), text);
        }
        Verify(document.Snapshot(), text); Verify(original, string.Concat(Enumerable.Repeat("a\r\nb\rc\nx🙂", 1000)));
    }
    [Test]
    public void OrdinaryEditsAndRenamesReuseMetadataWithoutBuildingLineIndex()
    {
        using var document = new EditorDocument(string.Concat(Enumerable.Repeat("content\r\n", 100000)));
        var original = document.Snapshot(); var info = original.GetLineEndings();
        document.Buffer.Insert(3, "a"); var after = document.Snapshot();
        Assert.That(after.GetLineEndings(), Is.SameAs(info)); AssertLazySourceUntouched(after);
        document.FileName = "renamed.txt";
        Assert.That(document.Snapshot().GetLineEndings(), Is.SameAs(info));
        for (var i = 0; i < 32; i++) _ = after.GetLineEndings();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) _ = after.GetLineEndings();
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
        Assert.That(original.GetLineEndings(), Is.SameAs(info));
    }
    [Test]
    public void CancellationDoesNotPoisonTheSnapshotCacheIncludingCachedQueries()
    {
        using var document = new EditorDocument("a\r\nb\r"); var snapshot = document.Snapshot();
        Assert.Throws<OperationCanceledException>(() => snapshot.GetLineEndings(new CancellationToken(true)));
        Verify(snapshot, "a\r\nb\r"); var cached = snapshot.GetLineEndings();
        Assert.Throws<OperationCanceledException>(() => snapshot.GetLineEndings(new CancellationToken(true)));
        Assert.That(snapshot.GetLineEndings(), Is.SameAs(cached));
    }
    [Test]
    public async Task ConcurrentReadersPublishOneImmutableResultAfterDocumentDisposal()
    {
        var text = string.Concat(Enumerable.Repeat("a\r\nb\rc\n", 10000));
        var document = new EditorDocument(text); var snapshot = document.Snapshot(); document.Dispose();
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => snapshot.GetLineEndings())));
        foreach (var result in results) Assert.That(result, Is.SameAs(results[0]));
        Verify(snapshot, text); AssertLazySourceUntouched(snapshot);
    }
    private static void Verify(DocumentSnapshot snapshot, string text)
    {
        var cr = 0; var lf = 0; var crlf = 0;
        for (var i = 0; i < text.Length; i++)
            if (text[i] == '\r') { if (i + 1 < text.Length && text[i + 1] == '\n') { crlf++; i++; } else cr++; }
            else if (text[i] == '\n') lf++;
        var info = snapshot.GetLineEndings();
        Assert.That((info.CrCount, info.LfCount, info.CrLfCount), Is.EqualTo((cr, lf, crlf)));
    }
    private static IEnumerable<string> Strings(int maximum)
    {
        yield return "";
        for (var length = 1; length <= maximum; length++)
        {
            var count = (int)Math.Pow(3, length);
            for (var word = 0; word < count; word++)
            {
                var value = word; var text = new char[length];
                for (var i = 0; i < length; i++) { text[i] = "x\r\n"[value % 3]; value /= 3; }
                yield return new string(text);
            }
        }
    }
    private static void AssertLazySourceUntouched(DocumentSnapshot snapshot)
    {
        foreach (var name in new[] { "_text", "_lines" })
        {
            var lazy = typeof(DocumentSnapshot).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(snapshot)!;
            Assert.That(lazy.GetType().GetProperty("IsValueCreated")!.GetValue(lazy), Is.False, name);
        }
    }
}
