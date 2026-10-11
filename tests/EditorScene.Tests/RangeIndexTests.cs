using EditorScene.LanguageServices;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class RangeIndexTests
{
    [Test]
    public void NestedOverlappingAndPointRangesUseHalfOpenSemantics()
    {
        SourceRange[] ranges = [new(0, 100), new(5, 0), new(5, 2), new(7, 3), new(100, 0)];
        var index = new RangeIndex<SourceRange>(ranges, x => x, 100); var found = new List<SourceRange>();
        index.CopyOverlaps(new(5, 2), found); Assert.That(found, Is.EqualTo(ranges.Take(3)));
        found.Clear(); index.CopyOverlaps(new(100, 0), found); Assert.That(found, Is.EqualTo(new[] { ranges[4] }));
    }
    [Test]
    public void RandomQueriesAgreeWithLinearOracle()
    {
        var random = new Random(729); var ranges = Enumerable.Range(0, 2000).Select(_ => new SourceRange(random.Next(9000), random.Next(1000))).ToArray();
        var index = new RangeIndex<SourceRange>(ranges, x => x); var results = new List<SourceRange>();
        for (var i = 0; i < 100; i++)
        {
            var query = new SourceRange(random.Next(10000), random.Next(100)); results.Clear(); index.CopyOverlaps(query, results);
            var last = query.Offset + Math.Max(0, query.Length - 1);
            Assert.That(results, Is.EqualTo(ranges.Where(r => r.Offset <= last && r.Offset + Math.Max(0, r.Length - 1) >= query.Offset).OrderBy(r => r.Offset)));
        }
    }
    [Test]
    public void DeepViewportPrunesOffscreenDiagnostics()
    {
        var ranges = Enumerable.Range(0, 100_000).Select(i => new SourceRange(i * 10, 1)).ToArray();
        var index = new RangeIndex<SourceRange>(ranges, r => r); var output = new List<SourceRange>(10);
        var stats = index.CopyOverlaps(new(999_950, 50), output);
        Assert.That(stats.Matches, Is.EqualTo(5)); Assert.That(stats.VisitedNodes, Is.LessThan(60));
        output.Clear(); index.CopyOverlaps(new(0, 1), output);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) { output.Clear(); index.CopyOverlaps(new(999_950, 50), output); }
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
    }
    [Test]
    public void OverflowNegativeAndOutsideRangesAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RangeIndex<SourceRange>([new(-1, 2)], r => r));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RangeIndex<SourceRange>([new(5, int.MaxValue)], r => r));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RangeIndex<SourceRange>([new(0, 2)], r => r, 1));
        var index = new RangeIndex<SourceRange>([new(int.MaxValue, 0)], r => r); var output = new List<SourceRange>();
        Assert.That(index.CopyOverlaps(new(int.MaxValue, 0), output).Matches, Is.EqualTo(1));
    }
}
