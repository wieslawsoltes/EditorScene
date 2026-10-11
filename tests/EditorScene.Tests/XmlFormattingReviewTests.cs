using System.Xml.Linq;
using EditorScene.Languages.Structured;
using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class XmlFormattingReviewTests
{
    private static async Task<string> Format(string text)
    {
        using var document = new EditorDocument(text, "review.xml"); var snapshot = document.Snapshot();
        var edits = await new XmlLanguage().FormatAsync(snapshot);
        return edits.IsEmpty ? text : edits.Single().NewText;
    }
    [Test]
    public async Task DeclarationRepairDoesNotReplaceUserAttributesTextCommentsOrCdata()
    {
        const string source = "<?xml version=\"1.0\" encoding=\"utf-8\"?><root encoding=\"utf-16\"><![CDATA[encoding=\"utf-16\"]]><!-- encoding=\"utf-16\" -->encoding=\"utf-16\"</root>";
        var formatted = await Format(source); var parsed = XDocument.Parse(formatted, LoadOptions.PreserveWhitespace);
        Assert.That(parsed.Declaration!.Encoding, Is.EqualTo("utf-8"));
        Assert.That(parsed.Root!.Attribute("encoding")!.Value, Is.EqualTo("utf-16"));
        Assert.That(parsed.Root.Value, Is.EqualTo(XDocument.Parse(source).Root!.Value));
        Assert.That(parsed.Root.Nodes().OfType<XComment>().Single().Value, Is.EqualTo(" encoding=\"utf-16\" "));
    }
    [TestCase("<p><b>one</b> <i>two</i></p>")]
    [TestCase("<p xml:space=\"preserve\">  <b>one</b>\n <i>two</i>  </p>")]
    [TestCase("<p>left&#xD;right</p>")]
    [TestCase("<p>🙂 é 日本語</p>")]
    public async Task TextNodesIncludingInlineWhitespaceAndCarriageReturnsArePreserved(string source)
    {
        var formatted = await Format(source);
        Assert.That(XDocument.Parse(formatted, LoadOptions.PreserveWhitespace).Root!.Value,
            Is.EqualTo(XDocument.Parse(source, LoadOptions.PreserveWhitespace).Root!.Value));
    }
    [Test]
    public async Task DeclarationWithoutEncodingDoesNotAcquireUtf16()
    {
        var formatted = await Format("<?xml version=\"1.0\" standalone=\"yes\"?><root><child /></root>");
        var declaration = XDocument.Parse(formatted).Declaration!;
        Assert.That(declaration.Encoding, Is.Null.Or.Empty); Assert.That(declaration.Standalone, Is.EqualTo("yes"));
    }
    [Test]
    public async Task ProcessingInstructionEndsAtQuestionMarkGreaterThanNotAnEmbeddedGreaterThan()
    {
        const string instruction = "<?tool value=\">\" data=\"<notAnElement>\"?>";
        using var document = new EditorDocument(instruction + "<root/>");
        var analysis = await new XmlLanguage().AnalyzeAsync(document.Snapshot());
        Assert.That(analysis.Diagnostics, Is.Empty);
        Assert.That(analysis.Classifications.First().Range.Length, Is.EqualTo(instruction.Length));
        Assert.That(analysis.Symbols.Single().Name, Is.EqualTo("root"));
    }
}
