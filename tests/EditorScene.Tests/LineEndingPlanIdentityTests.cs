using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class LineEndingPlanIdentityTests
{
    [Test]
    public void EmptyPlansStillRejectForeignAndStaleDocuments()
    {
        using var document = new EditorDocument("a\nb"); using var foreign = new EditorDocument("a\nb");
        var plan = LineEndingEditPlan.Create(document.Snapshot(), "\n"); Assert.That(plan.Edits, Is.Empty);
        Assert.Throws<InvalidOperationException>(() => plan.Apply(foreign));
        document.Buffer.Insert(0, "x"); var current = document.Buffer.Text;
        Assert.Throws<InvalidOperationException>(() => plan.Apply(document));
        Assert.That(document.Buffer.Text, Is.EqualTo(current));
    }
    [Test]
    public void AChangedPlanCannotBeAppliedTwiceAgainstItsOldCoordinates()
    {
        using var document = new EditorDocument("a\r\nb\n");
        var plan = LineEndingEditPlan.Create(document.Snapshot(), "\r");
        Assert.That(plan.Apply(document), Is.True); var text = document.Buffer.Text; var revision = document.Revision;
        Assert.Throws<InvalidOperationException>(() => plan.Apply(document));
        Assert.That(document.Buffer.Text, Is.EqualTo(text)); Assert.That(document.Revision, Is.EqualTo(revision));
    }
    [Test]
    public void OtherUnicodeSeparatorsAndMalformedUtf16ArePreservedVerbatim()
    {
        const string source = "a\u0085b\u2028c\u2029d\uD800\r\ne\uDC00\n";
        using var document = new EditorDocument(source);
        var plan = LineEndingEditPlan.Create(document.Snapshot(), "\r"); plan.Apply(document);
        Assert.That(document.Buffer.Text, Is.EqualTo("a\u0085b\u2028c\u2029d\uD800\re\uDC00\r"));
        Assert.That(plan.DelimiterCount, Is.EqualTo(2));
    }
    [TestCase(4095)]
    [TestCase(4096)]
    [TestCase(8191)]
    [TestCase(8192)]
    public void AdjacentCrRunsAndCrLfPairsRemainDistinctAcrossChunkEdges(int prefixLength)
    {
        var prefix = new string('x', prefixLength);
        using var document = new EditorDocument(prefix + "\r\r\n\n\r\n\r");
        var plan = LineEndingEditPlan.Create(document.Snapshot(), "\n");
        Assert.That(plan.CrCount, Is.EqualTo(2)); Assert.That(plan.CrLfCount, Is.EqualTo(2)); Assert.That(plan.LfCount, Is.EqualTo(1));
        plan.Apply(document); Assert.That(document.Buffer.Text, Is.EqualTo(prefix + new string('\n', 5)));
    }
}
