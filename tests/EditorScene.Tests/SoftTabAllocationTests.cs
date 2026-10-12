using System.Runtime.CompilerServices;
using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class SoftTabAllocationTests
{
    [Test]
    public void WarmLeadingSpaceQueriesAllocateNothing()
    {
        using var doc = new EditorDocument(new string(' ', 20000));
        var map = new TabColumnMap(doc.Snapshot(), 0, 4);
        for (var i = 0; i < 3; i++) Measure(map);
        for (var i = 0; i < 11; i++)
        {
            var result = Measure(map);
            Assert.That(result.Sum, Is.EqualTo(25000));
            Assert.That(result.Bytes, Is.Zero);
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (long Sum, long Bytes) Measure(TabColumnMap map)
    {
        var before = GC.GetAllocatedBytesForCurrentThread(); long sum = 0;
        for (var i = 1; i <= 10000; i++) sum += map.GetSoftTabBackspaceLength(i);
        return (sum, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
