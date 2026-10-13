using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class LineEndingDocumentTests
{
    [TestCase("\n")]
    [TestCase("\r")]
    [TestCase("\r\n")]
    public void PublicConversionIsANoOpForCanonicalSavedText(string delimiter)
    {
        using var document = new EditorDocument("first" + delimiter + "last" + delimiter);
        var snapshot = document.Snapshot(); var changes = 0;
        document.Changed += (_, _) => changes++;
        document.ConvertLineEndings(delimiter);
        Assert.That(document.Revision, Is.EqualTo(snapshot.Revision)); Assert.That(changes, Is.Zero);
        Assert.That(document.IsModified, Is.False); Assert.That(document.Buffer.UndoStack.CanUndo, Is.False);
        Assert.That(document.Snapshot(), Is.SameAs(snapshot));
    }
    [Test]
    public void PublicCanonicalConversionKeepsRedoAvailable()
    {
        using var document = new EditorDocument("first\nlast\n");
        document.Buffer.Insert(0, "prefix"); document.Buffer.UndoStack.Undo();
        var revision = document.Revision;
        document.ConvertLineEndings("\n");
        Assert.That(document.Revision, Is.EqualTo(revision)); Assert.That(document.Buffer.UndoStack.CanRedo, Is.True);
        document.Buffer.UndoStack.Redo(); Assert.That(document.Buffer.Text, Is.EqualTo("prefixfirst\nlast\n"));
    }
    [Test]
    public void PublicConversionRejectsInvalidDelimiterWithoutChangingHistory()
    {
        using var document = new EditorDocument("a\nb");
        Assert.Throws<ArgumentException>(() => document.ConvertLineEndings("\t"));
        Assert.Throws<ArgumentException>(() => document.ConvertLineEndings(null!));
        Assert.That(document.Revision, Is.Zero); Assert.That(document.Buffer.UndoStack.CanUndo, Is.False);
    }
}
