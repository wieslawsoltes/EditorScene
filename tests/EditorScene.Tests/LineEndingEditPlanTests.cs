using System.Reflection;
using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class LineEndingEditPlanTests
{
    [TestCase("\n")]
    [TestCase("\r")]
    [TestCase("\r\n")]
    public void MixedAndBoundaryFixturesMatchScalarConversionWithOneUndo(string delimiter)
    {
        foreach (var input in new[] { "", "ordinary 🙂\u2028text", "a\r\nb\nc\rd", "\r\r\n\n\r",
            new string('x', 4095) + "\r\ny\r", new string('x', 4095) + "\r\ry\n",
            new string('x', 4094) + "\r\n\r\n\nz", "🙂\r\ne\u0301\n日本語\r" })
        {
            using var document = new EditorDocument(input); var snapshot = document.Snapshot();
            var plan = LineEndingEditPlan.Create(snapshot, delimiter); var expected = Normalize(input, delimiter);
            Assert.That(plan.Apply(document), Is.EqualTo(input != expected));
            Assert.That(document.Buffer.Text, Is.EqualTo(expected));
            Assert.That(IsFlattened(snapshot), Is.False);
            Assert.That(plan.Edits.All(e => e.Length <= 1 && e.NewText.Length <= 1), Is.True);
            if (input != expected)
            {
                Assert.That(document.IsModified, Is.True);
                document.Buffer.UndoStack.Undo(); Assert.That(document.Buffer.Text, Is.EqualTo(input));
                Assert.That(document.IsModified, Is.False);
                document.Buffer.UndoStack.Redo(); Assert.That(document.Buffer.Text, Is.EqualTo(expected));
            }
            else { Assert.That(document.Revision, Is.Zero); Assert.That(document.Buffer.UndoStack.CanUndo, Is.False); }
        }
    }
    [TestCase("\r\n", "\n", 1, 1, "")]
    [TestCase("\r\n", "\r", 2, 1, "")]
    [TestCase("\n", "\r\n", 1, 0, "\r")]
    [TestCase("\r", "\r\n", 2, 0, "\n")]
    [TestCase("\n", "\r", 1, 1, "\r")]
    [TestCase("\r", "\n", 1, 1, "\n")]
    public void SharedDelimiterUnitsAreRetained(string source, string target, int offset, int removed, string inserted)
    {
        using var document = new EditorDocument("a" + source + "b");
        var plan = LineEndingEditPlan.Create(document.Snapshot(), target);
        Assert.That(plan.Edits, Is.EqualTo(new[] { new TextEdit(offset, removed, inserted) }));
    }
    [TestCase("\n")]
    [TestCase("\r")]
    [TestCase("\r\n")]
    public void CanonicalConversionPreservesRevisionSavedMarkerAndRedoHistory(string delimiter)
    {
        using var document = new EditorDocument("a" + delimiter + "b" + delimiter);
        document.Buffer.Insert(0, "prefix"); document.Buffer.UndoStack.Undo();
        var snapshot = document.Snapshot(); var revision = document.Revision;
        var plan = LineEndingEditPlan.Create(snapshot, delimiter);
        Assert.That(plan.Edits, Is.Empty); Assert.That(plan.Apply(document), Is.False);
        Assert.That(document.Revision, Is.EqualTo(revision)); Assert.That(document.IsModified, Is.False);
        Assert.That(document.Buffer.UndoStack.CanRedo, Is.True);
        document.Buffer.UndoStack.Redo(); Assert.That(document.Buffer.Text.StartsWith("prefix", StringComparison.Ordinal), Is.True);
    }
    [Test]
    public void CountsAndEditsDistinguishLoneCrLfAndCrLfPairs()
    {
        using var document = new EditorDocument("\r\n\r\n\n\ra\n");
        var plan = LineEndingEditPlan.Create(document.Snapshot(), "\n");
        Assert.That(plan.CrLfCount, Is.EqualTo(2)); Assert.That(plan.CrCount, Is.EqualTo(1)); Assert.That(plan.LfCount, Is.EqualTo(2));
        Assert.That(plan.DelimiterCount, Is.EqualTo(5)); Assert.That(plan.ChangedDelimiterCount, Is.EqualTo(3));
    }
    [Test]
    public void RandomMixturesPreserveAllNonDelimiterText()
    {
        var random = new Random(194807); var alphabet = new[] { "a", "\r", "\n", "🙂", "\r\n", "\t", "\u0085", "\u2028" };
        for (var sample = 0; sample < 150; sample++)
        {
            var input = string.Concat(Enumerable.Range(0, random.Next(1, 250)).Select(_ => alphabet[random.Next(alphabet.Length)]));
            foreach (var delimiter in new[] { "\n", "\r", "\r\n" })
            {
                using var document = new EditorDocument(input); var plan = LineEndingEditPlan.Create(document.Snapshot(), delimiter);
                plan.Apply(document); Assert.That(document.Buffer.Text, Is.EqualTo(Normalize(input, delimiter)));
            }
        }
    }
    [Test]
    public async Task FrozenSourcesMayBePlannedOffThreadAndDoNotRetainTheLiveDocument()
    {
        var document = new EditorDocument("a\r\nb\r"); var snapshot = document.Snapshot(); document.Dispose();
        var plan = await Task.Run(() => LineEndingEditPlan.Create(snapshot, "\n"));
        Assert.That(plan.Edits.Length, Is.EqualTo(2)); Assert.That(IsFlattened(snapshot), Is.False);
    }
    [Test]
    public void StaleForeignAndCancelledPlansNeverPartiallyApply()
    {
        using var document = new EditorDocument("a\r\nb"); using var foreign = new EditorDocument("a\r\nb");
        var snapshot = document.Snapshot(); var plan = LineEndingEditPlan.Create(snapshot, "\n");
        Assert.Throws<InvalidOperationException>(() => plan.Apply(foreign));
        document.Buffer.Insert(0, "x"); var text = document.Buffer.Text;
        Assert.Throws<InvalidOperationException>(() => plan.Apply(document)); Assert.That(document.Buffer.Text, Is.EqualTo(text));
        Assert.Throws<OperationCanceledException>(() => LineEndingEditPlan.Create(snapshot, "\n", new CancellationToken(true)));
        Assert.Throws<ArgumentException>(() => LineEndingEditPlan.Create(snapshot, ""));
        Assert.Throws<ArgumentException>(() => LineEndingEditPlan.Create(snapshot, "\u2028"));
        Assert.Throws<ArgumentNullException>(() => LineEndingEditPlan.Create(null!, "\n"));
    }
    [Test]
    public void WarmCanonicalLargeDocumentDoesNotAllocatePerLineOrBuildAFullLineIndex()
    {
        using var document = new EditorDocument(string.Concat(Enumerable.Repeat("content\n", 100000))); var snapshot = document.Snapshot();
        for (var i = 0; i < 3; i++) LineEndingEditPlan.Create(snapshot, "\n");
        var before = GC.GetAllocatedBytesForCurrentThread(); var plan = LineEndingEditPlan.Create(snapshot, "\n");
        var allocation = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocation, Is.LessThan(4096)); Assert.That(plan.Edits, Is.Empty); Assert.That(plan.LfCount, Is.EqualTo(100000));
        Assert.That(IsFlattened(snapshot), Is.False);
        var lines = typeof(DocumentSnapshot).GetField("_lines", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(snapshot)!;
        Assert.That((bool)lines.GetType().GetProperty("IsValueCreated")!.GetValue(lines)!, Is.False);
    }
    private static string Normalize(string text, string delimiter) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Replace("\n", delimiter, StringComparison.Ordinal);
    private static bool IsFlattened(DocumentSnapshot snapshot) => ((Lazy<string>)typeof(DocumentSnapshot).GetField("_text", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(snapshot)!).IsValueCreated;
}
