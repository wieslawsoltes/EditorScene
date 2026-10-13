using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class DocumentSavePlanTests
{
    [Test]
    public void OnlyDirtyDocumentsAreFrozenInInputOrder()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
        DocumentSaveCandidate[] candidates = [new(a, true), new(b, false), new(c, true)];
        var plan = DocumentSavePlan.Create(candidates);
        candidates[0] = new(Guid.NewGuid(), false);
        Assert.That(plan.DocumentIds, Is.EqualTo(new[] { a, c }));
    }
    [Test]
    public void SourceIsEnumeratedOnceAndThePlanRetainsNoSourceObjects()
    {
        var visits = 0; var id = Guid.NewGuid();
        IEnumerable<DocumentSaveCandidate> Source() { visits++; yield return new(id, true); }
        var plan = DocumentSavePlan.Create(Source());
        Assert.That(visits, Is.EqualTo(1)); Assert.That(plan.DocumentIds.Single(), Is.EqualTo(id));
    }
    [Test]
    public void CleanAndEmptyWorkspacesProduceAnEmptyPlan()
    {
        Assert.That(DocumentSavePlan.Create([]).DocumentIds, Is.Empty);
        Assert.That(DocumentSavePlan.Create([new(Guid.NewGuid(), false)]).DocumentIds, Is.Empty);
    }
    [Test]
    public void DuplicateOrEmptyCleanIdentitiesAreStillRejected()
    {
        var id = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => DocumentSavePlan.Create([new(id, true), new(id, false)]));
        Assert.Throws<ArgumentException>(() => DocumentSavePlan.Create([new(id, true), new(Guid.Empty, false)]));
        Assert.Throws<ArgumentNullException>(() => DocumentSavePlan.Create(null!));
    }
    [Test]
    public void EnumerationFailureDoesNotReturnAPartialSavePlan()
    {
        IEnumerable<DocumentSaveCandidate> Broken() { yield return new(Guid.NewGuid(), true); throw new InvalidOperationException("source failed"); }
        Assert.Throws<InvalidOperationException>(() => DocumentSavePlan.Create(Broken()));
    }
}
