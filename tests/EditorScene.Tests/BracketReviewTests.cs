using System.Reflection;
using EditorScene.LanguageServices;
using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class BracketReviewTests
{
    [Test]
    public void BracketsCrossReaderWindowsWithoutFlattening()
    {
        using var doc = new EditorDocument("(" + new string('x', 9000) + ")"); var s = doc.Snapshot();
        var map = new BracketMap(s, []);
        Assert.That(map.TryGetPair(0, out var first, out var second), Is.True);
        Assert.That(first.Offset, Is.Zero); Assert.That(second.Offset, Is.EqualTo(9001)); Assert.That(map.PairCount, Is.EqualTo(1));
        Assert.That(((Lazy<string>)typeof(DocumentSnapshot).GetField("_text", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!).IsValueCreated, Is.False);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) map.TryGetPair(0, out _, out _);
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
    }
    [Test]
    public void ExcludedStringsCommentsAndMismatchesDoNotCreatePairs()
    {
        using var doc = new EditorDocument("( [x] ) { ] } []"); var map = new BracketMap(doc.Snapshot(), [new(new(2, 3), "string")]);
        Assert.That(map.AtCaret(3), Is.Empty); Assert.That(map.AtCaret(8), Is.Empty); Assert.That(map.PairCount, Is.EqualTo(2));
    }
    [Test]
    public void InvalidExclusionsAndCancellationFailEarly()
    {
        using var doc = new EditorDocument("[]"); var s = doc.Snapshot();
        Assert.Throws<ArgumentOutOfRangeException>(() => new BracketMap(s, [new(new(-1, 1), "comment")]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BracketMap(s, [new(new(1, int.MaxValue), "string")]));
        Assert.Throws<OperationCanceledException>(() => new BracketMap(s, [], new CancellationToken(true)));
    }
}
