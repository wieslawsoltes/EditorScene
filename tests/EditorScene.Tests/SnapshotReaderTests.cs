using System.Reflection;
using System.Text;
using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class SnapshotReaderTests
{
    [Test]
    public void ForwardBackwardPeekAndNulHaveUnambiguousBoundaries()
    {
        using var doc = new EditorDocument("a\0b"); using var reader = doc.Snapshot().CreateReader();
        Assert.That(reader.ReadPrevious(), Is.EqualTo(-1)); Assert.That(reader.Peek(), Is.EqualTo((int)'a'));
        Assert.That(reader.Read(), Is.EqualTo((int)'a')); Assert.That(reader.Read(), Is.Zero); Assert.That(reader.Read(), Is.EqualTo((int)'b'));
        Assert.That(reader.Read(), Is.EqualTo(-1)); Assert.That(reader.ReadPrevious(), Is.EqualTo((int)'b'));
        Assert.That(reader.PeekPrevious(), Is.Zero); Assert.That(reader.Position, Is.EqualTo(2));
    }
    [TestCase("")]
    [TestCase("a\r\nb\rc\n\nlast")]
    [TestCase("\r\n\r\n")]
    [TestCase("a\n")]
    public void LineReadingMatchesDotNetTextReader(string text)
    {
        using var doc = new EditorDocument(text); using var reader = doc.Snapshot().CreateReader(); using var expected = new StringReader(text);
        string? line;
        do { line = expected.ReadLine(); Assert.That(reader.ReadLine(), Is.EqualTo(line)); } while (line is not null);
        Assert.That(reader.IsAtEnd, Is.True);
    }
    [Test]
    public void BufferWindowsHandleCrLfAndSurrogatesAcrossBoundaries()
    {
        var text = new string('x', 4095) + "\r\n" + new string('y', 4093) + "🙂tail";
        using var doc = new EditorDocument(text); using var reader = doc.Snapshot().CreateReader();
        Assert.That(reader.ReadLine(), Is.EqualTo(new string('x', 4095)));
        var buffer = new char[8192]; var count = reader.Read(buffer, 7, buffer.Length - 7);
        Assert.That(new string(buffer, 7, count), Is.EqualTo(text[4097..]));
        reader.Seek(-6, SeekOrigin.End); Assert.That(reader.TryReadRune(out var rune), Is.True); Assert.That(rune.ToString(), Is.EqualTo("🙂"));
        Assert.That(reader.TryReadPreviousRune(out rune), Is.True); Assert.That(rune.ToString(), Is.EqualTo("🙂"));
    }
    [Test]
    public void BoundedReadersNeverReadOutsideTheirSpan()
    {
        using var doc = new EditorDocument("before\r\nafter"); var snapshot = doc.Snapshot(); using var reader = snapshot.GetSpan(2, 5).CreateReader();
        Assert.That(reader.Position, Is.EqualTo(2)); Assert.That(reader.ReadLine(), Is.EqualTo("fore")); Assert.That(reader.ReadLine(), Is.Null);
        Assert.That(reader.ReadPrevious(), Is.EqualTo((int)'\r')); reader.Seek(2, SeekOrigin.Begin);
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.Seek(1, SeekOrigin.Begin));
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadText(6)); Assert.That(reader.Position, Is.EqualTo(2));
    }
    [Test]
    public void UnicodeScalarReadsAndMalformedSurrogatesRespectSpanEdges()
    {
        using var doc = new EditorDocument("A🙂\ud800z\udc00"); using var reader = doc.Snapshot().CreateReader(); var values = new List<int>();
        while (reader.TryReadRune(out var rune)) values.Add(rune.Value);
        Assert.That(values, Is.EqualTo(new[] { 65, 0x1F642, 0xFFFD, 122, 0xFFFD }));
        var backwards = new List<int>(); while (reader.TryReadPreviousRune(out var rune)) backwards.Add(rune.Value);
        Assert.That(backwards, Is.EqualTo(values.AsEnumerable().Reverse()));
        using var half = doc.Snapshot().GetSpan(1, 1).CreateReader(); half.TryReadRune(out var split);
        Assert.That(split, Is.EqualTo(Rune.ReplacementChar)); Assert.That(half.IsAtEnd, Is.True);
    }
    [Test]
    public void CloneAndCheckpointSupportIndependentSpeculativeReads()
    {
        using var doc = new EditorDocument("  name: value"); var snapshot = doc.Snapshot(); using var reader = snapshot.CreateReader();
        Assert.That(reader.SkipWhitespace(), Is.EqualTo(2)); var checkpoint = reader.CapturePosition();
        Assert.That(reader.Matches("NAME", StringComparison.OrdinalIgnoreCase), Is.True);
        using var clone = reader.Clone(); Assert.That(clone.ReadText(4), Is.EqualTo("name")); Assert.That(reader.Position, Is.EqualTo(2));
        reader.ReadToEnd(); reader.RestorePosition(checkpoint); Assert.That(reader.ReadText(5), Is.EqualTo("name:"));
        using var other = new EditorDocument("same"); Assert.Throws<ArgumentException>(() => reader.RestorePosition(other.Snapshot().CreateReader().CapturePosition()));
    }
    [Test]
    public async Task ReaderRemainsValidOnWorkerAfterMutationAndDocumentDisposal()
    {
        var doc = new EditorDocument("original 🙂"); var snapshot = doc.Snapshot(); using var reader = snapshot.CreateReader();
        doc.ApplyEdits([new(0, doc.Buffer.TextLength, "replacement")], doc.Revision); doc.Dispose();
        Assert.That(await Task.Run(reader.ReadToEnd), Is.EqualTo("original 🙂"));
    }
    [Test]
    public void SequentialReadingDoesNotMaterializeTheSnapshotString()
    {
        using var doc = new EditorDocument(new string('x', 100_000)); var snapshot = doc.Snapshot(); using var reader = snapshot.CreateReader();
        var buffer = new char[1031]; var count = 0; int read;
        while ((read = reader.Read(buffer)) != 0) count += read;
        Assert.That(count, Is.EqualTo(100_000));
        var lazy = (Lazy<string>)typeof(DocumentSnapshot).GetField("_text", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(snapshot)!;
        Assert.That(lazy.IsValueCreated, Is.False, "Reader must stay rope-backed rather than calling Snapshot.Text.");
    }
    [Test]
    public void BadArgumentsAndDisposedReadersFailWithoutAdvancing()
    {
        using var doc = new EditorDocument("abc"); var reader = doc.Snapshot().CreateReader();
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.Seek(long.MaxValue, SeekOrigin.End));
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.Read(new char[1], int.MaxValue, 1)); Assert.That(reader.Position, Is.Zero);
        reader.Dispose(); Assert.Throws<ObjectDisposedException>(() => reader.Peek()); Assert.Throws<ObjectDisposedException>(() => reader.ReadPrevious());
    }
}
