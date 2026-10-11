using System.Reflection;
using EditorScene.LanguageServices;
using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class SelectionExpansionTests
{
    [Test]
    public void ExpansionAndContractionRestoreExactInitialCaret()
    {
        using var doc = new EditorDocument("call(alpha + beta)"); var s = doc.Snapshot();
        var history = new SelectionExpansion(s, new(7, 7), [new(5, 5), new(5, 12), new(0, 18), new(5, 5)]);
        Assert.That(history.TryExpand(s, new(7, 7), out var selected), Is.True); Assert.That(selected, Is.EqualTo(new SelectionRange(5, 10)));
        while (history.TryExpand(s, selected, out selected)) { }
        Assert.That(selected, Is.EqualTo(new SelectionRange(0, 18)));
        while (history.TryContract(s, selected, out selected)) { }
        Assert.That(selected, Is.EqualTo(new SelectionRange(7, 7))); Assert.That(doc.Revision, Is.Zero);
    }
    [Test]
    public void ReverseDirectionSurvivesEveryStep()
    {
        using var doc = new EditorDocument("abcdefghij"); var s = doc.Snapshot();
        var history = new SelectionExpansion(s, new(5, 3), [new(2, 4), new(1, 8)]); var current = history.Current;
        Assert.That(history.TryExpand(s, current, out current), Is.True); Assert.That(current, Is.EqualTo(new SelectionRange(6, 2)));
        history.TryExpand(s, current, out current); Assert.That(current, Is.EqualTo(new SelectionRange(9, 1)));
        history.TryContract(s, current, out current); history.TryContract(s, current, out current); Assert.That(current, Is.EqualTo(new SelectionRange(5, 3)));
    }
    [Test]
    public void CrossingAndDuplicateCandidatesDoNotProduceNonParentSteps()
    {
        using var doc = new EditorDocument("abcdefghij"); var s = doc.Snapshot();
        var history = new SelectionExpansion(s, new(3, 4), [new(2, 3), new(2, 3), new(3, 4), new(0, 9)]);
        var current = history.Current; history.TryExpand(s, current, out current); Assert.That(current, Is.EqualTo(new SelectionRange(2, 5)));
        history.TryExpand(s, current, out current); Assert.That(current, Is.EqualTo(new SelectionRange(0, 9)));
    }
    [Test]
    public void EditsUndoForeignDocumentsAndManualSelectionsInvalidateHistory()
    {
        using var doc = new EditorDocument("abc"); using var foreign = new EditorDocument("abc"); var original = doc.Snapshot();
        var history = new SelectionExpansion(original, new(1, 1), [new(0, 3)]);
        Assert.That(history.IsCurrent(foreign.Snapshot(), new(1, 1)), Is.False);
        Assert.Throws<InvalidOperationException>(() => history.TryExpand(original, new(0, 0), out _));
        doc.Buffer.Insert(0, "x"); doc.Buffer.UndoStack.Undo();
        Assert.That(history.IsCurrent(doc.Snapshot(), new(1, 1)), Is.False);
        Assert.That(history.IsCurrent(original, new(1, 1)), Is.True);
    }
    [TestCase(-1, 1)]
    [TestCase(1, int.MaxValue)]
    [TestCase(1, 1)]
    [TestCase(4, 0)]
    public void InvalidOrSplitSequencesAreRejected(int start, int length)
    {
        using var doc = new EditorDocument("A🙂\r\nB");
        Assert.That(() => new SelectionExpansion(doc.Snapshot(), new(0, 0), [new(start, length)]), Throws.InstanceOf<ArgumentException>());
    }
    [Test]
    public void EmptyDocumentHasNoExpansionAndHugeCandidateListsFailExplicitly()
    {
        using var doc = new EditorDocument(); var s = doc.Snapshot(); var history = new SelectionExpansion(s, new(0, 0), []);
        Assert.That(history.TryExpand(s, new(0, 0), out _), Is.False);
        Assert.Throws<ArgumentException>(() => new SelectionExpansion(s, new(0, 0), Enumerable.Repeat(new SourceRange(0, 0), SelectionExpansion.MaximumCandidates + 1)));
    }
    [Test]
    public void WarmHistoryNavigationAllocatesNothing()
    {
        using var doc = new EditorDocument("alpha beta"); var s = doc.Snapshot();
        var history = new SelectionExpansion(s, new(2, 2), [new(0, 5)]); var current = history.Current;
        for (var i = 0; i < 20; i++) { history.TryExpand(s, current, out current); history.TryContract(s, current, out current); }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) { history.TryExpand(s, current, out current); history.TryContract(s, current, out current); }
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
    }
    [Test]
    public void BasicCandidatesHandleScalarWordsCrLfAndNeverFlatten()
    {
        using var doc = new EditorDocument("prefix\r\n𐐀valué_tail\r\nlast"); var s = doc.Snapshot();
        var ranges = SelectionCandidates.Basic(s, 12);
        Assert.That(ranges[0], Is.EqualTo(new SourceRange(8, "𐐀valué_tail".Length)));
        Assert.That(ranges.Any(r => r.Offset == 8 && r.Length == "𐐀valué_tail\r\n".Length), Is.True);
        Assert.That(((Lazy<string>)typeof(DocumentSnapshot).GetField("_text", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!).IsValueCreated, Is.False);
    }
    [Test]
    public void CancelledFallbackDoesNotReadOrChangeDocument()
    {
        using var doc = new EditorDocument("word");
        Assert.Throws<OperationCanceledException>(() => SelectionCandidates.Basic(doc.Snapshot(), 0, new CancellationToken(true)));
        Assert.That(doc.Revision, Is.Zero);
    }
}
