using System.Text;
using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class TextTests
{
    [Test]
    public void SnapshotsStayImmutableAndCoordinatesUseUtf16AndAllNewlines()
    {
        using var document = new EditorDocument("A🙂\r\nB\rC\n", "x.cs");
        var snapshot = document.Snapshot();
        document.Buffer.Insert(0, "new");
        Assert.That(snapshot.Text, Is.EqualTo("A🙂\r\nB\rC\n"));
        Assert.That(snapshot.LineCount, Is.EqualTo(4));
        Assert.That(snapshot.GetOffset(new(0, 100)), Is.EqualTo(3));
        Assert.That(snapshot.GetOffset(new(1, 1)), Is.EqualTo(6));
        Assert.That(snapshot.GetPosition(8), Is.EqualTo(new TextPosition(2, 1)));
        Assert.That(snapshot.GetOffset(new(3, 0)), Is.EqualTo(snapshot.Length));
        Assert.That(document.Revision, Is.GreaterThan(snapshot.Revision));
    }
    [Test]
    public void EditSetsApplyAgainstOriginalCoordinatesAndUndoAsOneUnit()
    {
        using var document = new EditorDocument("abcdef");
        var events = 0; document.Changed += (_, _) => events++;
        document.ApplyEdits([new(1, 2, "X"), new(5, 1, "YZ")], document.Revision);
        Assert.That(document.Buffer.Text, Is.EqualTo("aXdeYZ"));
        Assert.That(events, Is.EqualTo(1));
        Assert.That(document.IsModified, Is.True);
        document.Buffer.UndoStack.Undo();
        Assert.That(document.Buffer.Text, Is.EqualTo("abcdef"));
        Assert.That(document.IsModified, Is.False);
        document.Buffer.UndoStack.Redo(); Assert.That(document.Buffer.Text, Is.EqualTo("aXdeYZ"));
    }
    [Test]
    public void InvalidOverlappingOrStaleEditsNeverPartiallyMutate()
    {
        using var document = new EditorDocument("abcdef");
        Assert.Throws<ArgumentException>(() => document.ApplyEdits([new(0, 3, "X"), new(2, 1, "Y")], 0));
        Assert.Throws<ArgumentException>(() => document.ApplyEdits([new(1, 0, "X"), new(1, 0, "Y")], 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.ApplyEdits([new(0, 1, "X"), new(9, 1, "Y")], 0));
        Assert.Throws<InvalidOperationException>(() => document.ApplyEdits([new(0, 1, "X")], -1));
        Assert.That(document.Buffer.Text, Is.EqualTo("abcdef"));
        Assert.That(document.Revision, Is.Zero);
    }
    [Test]
    public void MutatingEditEnumerationCannotApplyStaleCoordinates()
    {
        using var document = new EditorDocument("abc");
        IEnumerable<TextEdit> Malicious() { document.Buffer.Insert(0, "x"); yield return new(0, 1, "y"); }
        Assert.Throws<InvalidOperationException>(() => document.ApplyEdits(Malicious(), 0));
        Assert.That(document.Buffer.Text, Is.EqualTo("xabc"));
    }
    [Test]
    public void RandomEditsMatchAnIndependentStringModelAndUndoAll()
    {
        using var document = new EditorDocument(); var expected = ""; var random = new Random(3721);
        var history = new List<string>();
        for (var i = 0; i < 2000; i++)
        {
            var at = random.Next(expected.Length + 1); var remove = random.Next(Math.Min(8, expected.Length - at) + 1);
            var insert = new string(Enumerable.Range(0, random.Next(1, 7)).Select(_ => (char)random.Next(32, 126)).ToArray());
            history.Add(expected); expected = expected.Remove(at, remove).Insert(at, insert);
            document.ApplyEdits([new(at, remove, insert)], document.Revision);
            Assert.That(document.Buffer.Text, Is.EqualTo(expected));
        }
        for (var i = history.Count - 1; i >= 0; i--) { document.Buffer.UndoStack.Undo(); Assert.That(document.Buffer.Text, Is.EqualTo(history[i])); }
    }
    [TestCase("\n")]
    [TestCase("\r\n")]
    [TestCase("\r")]
    public void LineEndingConversionIsOneUndoOperation(string eol)
    {
        using var document = new EditorDocument("a\nb\r\nc\rd"); document.ConvertLineEndings(eol);
        Assert.That(document.Buffer.Text, Is.EqualTo(string.Join(eol, "a", "b", "c", "d")));
        document.Buffer.UndoStack.Undo(); Assert.That(document.Buffer.Text, Is.EqualTo("a\nb\r\nc\rd"));
    }
    [Test]
    public void LoadDetectsBomAndSaveRetainsEncodingWithoutClosingStreams()
    {
        using var document = new EditorDocument("old");
        using var source = new MemoryStream(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("hé🙂\r\n")).ToArray());
        document.Load(source); Assert.That(document.Encoding.CodePage, Is.EqualTo(1200)); Assert.That(source.CanRead, Is.True);
        using var target = new MemoryStream(); document.Save(target);
        Assert.That(target.ToArray(), Is.EqualTo(source.ToArray())); Assert.That(target.CanWrite, Is.True); Assert.That(document.IsModified, Is.False);
    }
    [Test]
    public void InvalidUtf8DoesNotReplaceExistingDocument()
    {
        using var document = new EditorDocument("keep"); using var stream = new MemoryStream([0xC3, 0x28]);
        Assert.Throws<DecoderFallbackException>(() => document.Load(stream)); Assert.That(document.Buffer.Text, Is.EqualTo("keep"));
    }
    [Test]
    public void FailedSaveNeverMarksDocumentClean()
    {
        using var document = new EditorDocument("before"); document.Buffer.Insert(0, "new");
        Assert.Throws<IOException>(() => document.Save(new FailingWriteStream())); Assert.That(document.IsModified, Is.True);
    }
    private sealed class FailingWriteStream : MemoryStream
    { public override void Write(byte[] buffer, int offset, int count) => throw new IOException("Disk full"); }
    [Test]
    public void BookmarkAnchorsMoveWithTextAndNavigationWraps()
    {
        using var document = new EditorDocument("a\nb\nc"); var indicators = new IndicatorCollection(document);
        var first = indicators.Add(2, 0, IndicatorKind.Bookmark); var last = indicators.Add(4, 0, IndicatorKind.Bookmark);
        document.Buffer.Insert(0, "new\n"); Assert.That(first.Line, Is.EqualTo(3)); Assert.That(first.Offset, Is.EqualTo(6));
        Assert.That(indicators.Next(last.Offset, IndicatorKind.Bookmark), Is.SameAs(first));
        Assert.That(indicators.Next(first.Offset, IndicatorKind.Bookmark, true), Is.SameAs(last));
        indicators.ToggleLine(3, IndicatorKind.Bookmark); Assert.That(indicators.Items.Count, Is.EqualTo(1));
    }
    [Test]
    public void MultiSelectionReplacesDisjointRangesAndUndoesTogether()
    {
        using var document = new EditorDocument("one two one"); var selections = new MultiSelection(document);
        selections.Set([new(0, 3), new(8, 11)]); selections.Replace("X");
        Assert.That(document.Buffer.Text, Is.EqualTo("X two X")); Assert.That(selections.Ranges, Is.EqualTo(new[] { new SelectionRange(1, 1), new SelectionRange(7, 7) }));
        document.Buffer.UndoStack.Undo(); Assert.That(document.Buffer.Text, Is.EqualTo("one two one"));
    }
    [TestCase("A🙂B", 3, true, "AB")]
    [TestCase("Ae\u0301B", 3, true, "AB")]
    [TestCase("A🙂B", 1, false, "AB")]
    [TestCase("A\r\nB", 3, true, "AB")]
    [TestCase("A\r\nB", 1, false, "AB")]
    public void MultiCaretDeletionRespectsGraphemesAndCrLf(string text, int offset, bool backwards, string expected)
    {
        using var document = new EditorDocument(text); var selections = new MultiSelection(document); selections.Set([new(offset, offset)]); selections.Delete(backwards);
        Assert.That(document.Buffer.Text, Is.EqualTo(expected));
    }
    [Test]
    public void ProtectedMultiSelectionFailsBeforeAnyEdit()
    {
        using var document = new EditorDocument("one two"); var selections = new MultiSelection(document); selections.Set([new(0, 3), new(4, 7)]);
        Assert.Throws<InvalidOperationException>(() => selections.Replace("X", edit => edit.Offset == 0)); Assert.That(document.Buffer.Text, Is.EqualTo("one two"));
    }
    [Test]
    public void HtmlAndRtfEscapeUntrustedContentAndPreserveUnicode()
    {
        using var document = new EditorDocument("<script>{\\🙂é}\n", "<title>"); var snapshot = document.Snapshot();
        var html = TextExport.ToHtml(snapshot, [new(0, 8, "#A31515")], true);
        Assert.That(html, Does.Not.Contain("<script>")); Assert.That(html, Does.Contain("&lt;script&gt;")); Assert.That(html, Does.Contain("&lt;title&gt;"));
        var rtf = TextExport.ToRtf(snapshot); Assert.That(rtf, Does.Contain(@"\{\\\u-10179?\u-8638?\u233?\}"));
        Assert.That(rtf, Does.Contain(@"\par "));
        Assert.Throws<ArgumentException>(() => TextExport.ToHtml(snapshot, [new(0, 1, "red\" onclick=\"x")]));
        Assert.Throws<ArgumentException>(() => TextExport.ToRtf(snapshot, [new(0, 3, "#000000"), new(2, 2, "#FFFFFF")]));
    }
}
