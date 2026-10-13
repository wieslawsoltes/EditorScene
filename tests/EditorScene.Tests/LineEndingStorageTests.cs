using System.Runtime.CompilerServices;
using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class LineEndingStorageTests
{
    [TestCase("\n", 75000)]
    [TestCase("\r", 75000)]
    [TestCase("\r\n", 50000)]
    public void MixedPlanAllocatesOneExactEditArray(string delimiter, int changed)
    {
        using var document = new EditorDocument(string.Concat(Enumerable.Repeat("a\r\nb\rc\nd\r\n", 25000)));
        var snapshot = document.Snapshot();
        for (var i = 0; i < 3; i++) _ = LineEndingEditPlan.Create(snapshot, delimiter);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var plan = LineEndingEditPlan.Create(snapshot, delimiter);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Multiple(() =>
        {
            Assert.That(plan.ChangedDelimiterCount, Is.EqualTo(changed));
            Assert.That(plan.CrCount, Is.EqualTo(25000));
            Assert.That(plan.LfCount, Is.EqualTo(25000));
            Assert.That(plan.CrLfCount, Is.EqualTo(50000));
            Assert.That(allocated, Is.LessThanOrEqualTo((long)changed * Unsafe.SizeOf<TextEdit>() + 64000),
                "Planning must not allocate growing intermediate edit arrays or copy the final owned array.");
        });
        GC.KeepAlive(plan);
    }

    [Test]
    public void PublishedPlanStorageIsNotReusedByConcurrentPlans()
    {
        using var document = new EditorDocument(string.Concat(Enumerable.Repeat("a\r\nb\rc\nd\r\n", 2000)));
        var snapshot = document.Snapshot();
        var original = LineEndingEditPlan.Create(snapshot, "\n");
        var expected = original.Edits.ToArray();
        Parallel.For(0, 16, i =>
        {
            var target = i % 2 == 0 ? "\r" : "\r\n";
            var other = LineEndingEditPlan.Create(snapshot, target);
            Assert.That(other.ChangedDelimiterCount, Is.EqualTo(i % 2 == 0 ? 6000 : 4000));
            Assert.That(original.Edits, Is.EqualTo(expected));
        });
        Assert.That(original.Edits, Is.EqualTo(expected));
        Assert.That(document.Revision, Is.Zero);
    }
}
