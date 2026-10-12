using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class SoftTabBackspaceTests
{
    [TestCase("        word", 8, 4, 4)]
    [TestCase("      word", 6, 4, 2)]
    [TestCase("\t   word", 4, 8, 3)]
    [TestCase("  \t  word", 5, 4, 2)]
    [TestCase("  \tword", 3, 4, 0)]
    [TestCase("x    ", 5, 4, 0)]
    [TestCase("  x    ", 7, 4, 0)]
    [TestCase("\u00a0    ", 5, 4, 0)]
    [TestCase("    ", 0, 4, 0)]
    [TestCase("", 0, 4, 0)]
    [TestCase("   ", 3, 1, 1)]
    public void BackspaceStopsAtIndentationBoundaries(string text, int offset, int size, int expected)
    {
        using var doc = new EditorDocument(text); var map = new TabColumnMap(doc.Snapshot(), 0, size);
        Assert.That(map.GetSoftTabBackspaceLength(offset), Is.EqualTo(expected));
        Assert.That(doc.Buffer.Text, Is.EqualTo(text));
    }
    [Test]
    public void LeadingWhitespaceCrossesReaderBoundariesAndExcludesDelimiters()
    {
        using var doc = new EditorDocument(new string(' ', 8193) + "x\r\n\t  \r\n");
        var snapshot = doc.Snapshot(); var first = new TabColumnMap(snapshot, 0, 8); var second = new TabColumnMap(snapshot, 1, 8);
        Assert.That(first.LeadingWhitespaceLength, Is.EqualTo(8193));
        Assert.That(first.GetSoftTabBackspaceLength(8193), Is.EqualTo(1));
        Assert.That(first.GetSoftTabBackspaceLength(8194), Is.Zero);
        Assert.That(second.LeadingWhitespaceLength, Is.EqualTo(3));
        Assert.That(second.GetSoftTabBackspaceLength(3), Is.EqualTo(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => first.GetSoftTabBackspaceLength(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => second.GetSoftTabBackspaceLength(4));
    }
    [Test]
    public void RandomIndentationAgreesWithCharacterOracle()
    {
        var random = new Random(1942);
        for (var sample = 0; sample < 64; sample++)
        {
            var text = new string(Enumerable.Range(0, 200).Select(_ => random.Next(5) == 0 ? '\t' : ' ').ToArray()) + "x   ";
            var size = random.Next(1, 33); using var doc = new EditorDocument(text); var map = new TabColumnMap(doc.Snapshot(), 0, size);
            long column = 0;
            for (var offset = 0; offset <= text.Length; offset++)
            {
                var expected = 0;
                if (offset > 0 && offset <= 200 && text[offset - 1] == ' ')
                {
                    var distance = (int)((column - 1) % size + 1);
                    while (expected < distance && expected < offset && text[offset - expected - 1] == ' ') expected++;
                }
                Assert.That(map.GetSoftTabBackspaceLength(offset), Is.EqualTo(expected), $"sample={sample}, offset={offset}");
                Assert.That(map.GetColumn(offset), Is.EqualTo(column));
                if (offset < text.Length) column += text[offset] == '\t' ? size - column % size : 1;
            }
        }
    }
    [Test]
    public void DifferentCaretStopsShareOneUndoAndKeepPrimary()
    {
        const string text = "      a\r\n\t   b";
        using var doc = new EditorDocument(text); var selections = new MultiSelection(doc);
        selections.Set([new(6, 6), new(13, 13)]); selections.DeleteToPreviousTabStop(4);
        Assert.That(doc.Buffer.Text, Is.EqualTo("    a\r\n\tb"));
        Assert.That(selections.Ranges, Is.EqualTo(new SelectionRange[] { new(4, 4), new(8, 8) }));
        Assert.That(selections.PrimaryIndex, Is.EqualTo(1));
        doc.Buffer.UndoStack.Undo(); Assert.That(doc.Buffer.Text, Is.EqualTo(text));
    }
    [Test]
    public void OverlappingBackspacesMergeBeforePermissionAndPreservePrimary()
    {
        using var doc = new EditorDocument("        x"); var selections = new MultiSelection(doc);
        selections.Set([new(5, 5), new(7, 7)]); var calls = 0;
        selections.DeleteToPreviousTabStop(4, edit => { calls++; Assert.That(edit, Is.EqualTo(new TextEdit(4, 3, ""))); return true; });
        Assert.That(calls, Is.EqualTo(1)); Assert.That(doc.Buffer.Text, Is.EqualTo("     x"));
        Assert.That(selections.Ranges, Is.EqualTo(new SelectionRange[] { new(4, 4) }));
        Assert.That(selections.PrimaryIndex, Is.Zero);
    }
    [Test]
    public void SelectionGraphemeAndCrLfFallbacksRemainAtomic()
    {
        using var doc = new EditorDocument("    a\r\n🙂é\r\nz"); var selections = new MultiSelection(doc);
        selections.Set([new(0, 4), new(11, 11), new(13, 13)]);
        selections.DeleteToPreviousTabStop(4);
        Assert.That(doc.Buffer.Text, Is.EqualTo("a\r\n🙂z"));
        doc.Buffer.UndoStack.Undo(); Assert.That(doc.Buffer.Text, Is.EqualTo("    a\r\n🙂é\r\nz"));
    }
    [Test]
    public void LaterPermissionDenialAndSelectionInterferencePreventPlannedEdits()
    {
        const string text = "    a\n    b"; using var doc = new EditorDocument(text); var selections = new MultiSelection(doc);
        selections.Set([new(4, 4), new(10, 10)]); var calls = 0;
        Assert.Throws<InvalidOperationException>(() => selections.DeleteToPreviousTabStop(4, _ => ++calls < 2));
        Assert.That(doc.Buffer.Text, Is.EqualTo(text)); Assert.That(calls, Is.EqualTo(2));
        Assert.Throws<InvalidOperationException>(() => selections.DeleteToPreviousTabStop(4, _ => { selections.Clear(); return true; }));
        Assert.That(doc.Buffer.Text, Is.EqualTo(text)); Assert.That(selections.Ranges, Is.Empty);
        Assert.Throws<ArgumentOutOfRangeException>(() => selections.DeleteToPreviousTabStop(0));
    }
}
