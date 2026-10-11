using System.Xml.Linq;
using EditorScene.Languages.Structured;
using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class XmlIndentationSafetyTests
{
    [Test]
    public async Task MinifiedElementContainersAreIndentedAndFormattingIsIdempotent()
    {
        using var first = new EditorDocument("<root><a/><b><c/></b></root>");
        var edits = await new XmlLanguage().FormatAsync(first.Snapshot());
        Assert.That(edits.Single().NewText, Does.Contain("\n    <a"));
        using var second = new EditorDocument(edits.Single().NewText);
        Assert.That(await new XmlLanguage().FormatAsync(second.Snapshot()), Is.Empty);
    }
    [Test]
    public async Task XmlSpacePreserveAppliesToElementOnlyDescendants()
    {
        const string source = "<root xml:space=\"preserve\"><child><a /><b /></child></root>";
        using var document = new EditorDocument(source);
        var edits = await new XmlLanguage().FormatAsync(document.Snapshot());
        var formatted = edits.IsEmpty ? source : edits.Single().NewText;
        Assert.That(formatted, Does.Not.Contain('\n'));
        Assert.That(XDocument.Parse(formatted, LoadOptions.PreserveWhitespace).Root!.Value, Is.Empty);
    }
    [Test]
    public void DeepIndentationRequestsFailWithoutEditingTheSource()
    {
        var text = string.Concat(Enumerable.Repeat("<x>", 258)) + string.Concat(Enumerable.Repeat("</x>", 258));
        using var document = new EditorDocument(text); var snapshot = document.Snapshot();
        Assert.ThrowsAsync<InvalidOperationException>(() => new XmlLanguage().FormatAsync(snapshot));
        Assert.That(document.Revision, Is.Zero); Assert.That(document.Buffer.Text, Is.EqualTo(text));
    }
}
