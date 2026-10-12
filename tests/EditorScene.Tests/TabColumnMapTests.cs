using System.Reflection;
using System.Runtime.CompilerServices;
using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class TabColumnMapTests
{
    [TestCase("a\tb\t", 4, new long[] { 0, 1, 4, 5, 8 })]
    [TestCase("\t\t", 8, new long[] { 0, 8, 16 })]
    [TestCase("ab\tc", 2, new long[] { 0, 1, 2, 4, 5 })]
    [TestCase("🙂\té", 4, new long[] { 0, 1, 2, 4, 5, 6 })]
    [TestCase("abc", 4, new long[] { 0, 1, 2, 3 })]
    [TestCase("", 4, new long[] { 0 })]
    public void ColumnsExpandTabsAndKeepExplicitUtf16Semantics(string text, int size, long[] expected)
    {
        using var doc = new EditorDocument(text); var snapshot = doc.Snapshot(); var map = new TabColumnMap(snapshot, 0, size);
        Assert.That(Enumerable.Range(0, text.Length + 1).Select(map.GetColumn), Is.EqualTo(expected));
        Assert.That(map.ColumnLength, Is.EqualTo(expected[^1]));
        foreach (var i in Enumerable.Range(0, text.Length + 1)) Assert.That(map.GetOffset(map.GetColumn(i)), Is.EqualTo(i));
        Assert.That(((Lazy<string>)typeof(DocumentSnapshot).GetField("_text", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(snapshot)!).IsValueCreated, Is.False);
    }
    [Test]
    public void InsideTabColumnsHaveExplicitAffinityAndOutOfLineColumnsClamp()
    {
        using var doc = new EditorDocument("a\tb"); var map = new TabColumnMap(doc.Snapshot(), 0);
        Assert.That(map.GetOffset(2), Is.EqualTo(1)); Assert.That(map.GetOffset(2, true), Is.EqualTo(2));
        Assert.That(map.GetOffset(1, true), Is.EqualTo(1)); Assert.That(map.GetOffset(4), Is.EqualTo(2));
        Assert.That(map.GetOffset(long.MaxValue), Is.EqualTo(3));
        Assert.That(map.GetIndentation(2, true), Is.EqualTo("    "));
        Assert.That(map.GetIndentation(3, true), Is.EqualTo("   "));
        Assert.That(map.GetIndentation(3, false), Is.EqualTo("\t"));
    }
    [Test]
    public void MapsOnlyTheRequestedLineWithoutIncludingCrLf()
    {
        using var doc = new EditorDocument("head\r\n\tvalue\r\nlast"); var s = doc.Snapshot(); var map = new TabColumnMap(s, 1, 8);
        Assert.That(map.LineOffset, Is.EqualTo(6)); Assert.That(map.Length, Is.EqualTo(6));
        Assert.That(map.ColumnLength, Is.EqualTo(13)); Assert.That(map.TabCount, Is.EqualTo(1));
        Assert.That(map.DocumentId, Is.EqualTo(doc.Id)); Assert.That(map.Revision, Is.EqualTo(s.Revision));
    }
    [Test]
    public void LongReaderBoundariesAndSparseStorageAreCorrect()
    {
        using var doc = new EditorDocument(new string('x', 4095) + "\t" + new string('x', 8191) + "\t");
        var map = new TabColumnMap(doc.Snapshot(), 0, 8);
        Assert.That(map.TabCount, Is.EqualTo(2)); Assert.That(map.GetColumn(4096), Is.EqualTo(4096));
        Assert.That(map.ColumnLength, Is.EqualTo(12288));
        // Keep setup, JIT warmup and the test framework outside the measured method.
        // The previous mixed test body observed 24 bytes and did not identify their
        // allocation site. This test enforces steady-state lookup, not cold startup.
        for (var warmup = 0; warmup < 3; warmup++) _ = MeasureLookups(map);
        var samples = new (long Sum, long Bytes)[11];
        for (var i = 0; i < samples.Length; i++) samples[i] = MeasureLookups(map);
        foreach (var sample in samples)
        {
            Assert.That(sample.Sum, Is.EqualTo(49_995_000));
            Assert.That(sample.Bytes, Is.Zero);
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (long Sum, long Bytes) MeasureLookups(TabColumnMap map)
    {
        var before = GC.GetAllocatedBytesForCurrentThread(); long sum = 0;
        for (var i = 0; i < 10000; i++) sum += map.GetColumn(i);
        return (sum, GC.GetAllocatedBytesForCurrentThread() - before);
    }
    [Test]
    public void RandomMapsAgreeWithDirectTabExpansion()
    {
        var random = new Random(912);
        for (var sample = 0; sample < 32; sample++)
        {
            var text = new string(Enumerable.Range(0, 201).Select(_ => random.Next(4) == 0 ? '\t' : 'x').ToArray());
            using var doc = new EditorDocument(text); var size = random.Next(1, 17); var map = new TabColumnMap(doc.Snapshot(), 0, size); long col = 0;
            for (var i = 0; i <= text.Length; i++)
            {
                Assert.That(map.GetColumn(i), Is.EqualTo(col)); Assert.That(map.GetOffset(col), Is.EqualTo(i));
                if (i < text.Length) col += text[i] == '\t' ? size - col % size : 1;
            }
        }
    }
    [Test]
    public void InvalidArgumentsAndCancellationFailBeforeUse()
    {
        using var doc = new EditorDocument("a"); var s = doc.Snapshot(); var map = new TabColumnMap(s, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => new TabColumnMap(s, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TabColumnMap(s, 0, 1001));
        Assert.That(() => new TabColumnMap(s, -1), Throws.InstanceOf<ArgumentException>());
        Assert.Throws<ArgumentOutOfRangeException>(() => map.GetColumn(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.GetOffset(-1));
        Assert.Throws<OperationCanceledException>(() => new TabColumnMap(s, 0, cancellationToken: new CancellationToken(true)));
    }
    [Test]
    public async Task DetachedImmutableMapRemainsUsableAfterDocumentDisposal()
    {
        var doc = new EditorDocument("\tword"); var map = new TabColumnMap(doc.Snapshot(), 0); doc.Dispose();
        Assert.That(await Task.Run(() => map.GetColumn(5)), Is.EqualTo(8));
    }
}
