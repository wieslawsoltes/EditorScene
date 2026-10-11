using System.Text;
using System.Xml;
using EditorScene.Snippets;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class SnippetTests
{
    [Test]
    public void RepeatedFieldsSelectedTextEndAndEscapedDelimiterAreTokenized()
    {
        var snippet = new CodeSnippet("Loop", "for", "C#", "$$ $i$ $i$ $selected$$end$", [new("i", "i")]);
        Assert.That(snippet.Language, Is.EqualTo("csharp"));
        Assert.That(snippet.Parts.Count(p => p.Kind == SnippetPartKind.Field), Is.EqualTo(2));
        Assert.That(snippet.Parts[0].Text, Is.EqualTo("$ "));
        Assert.That(snippet.Parts.Any(p => p.Kind == SnippetPartKind.SelectedText), Is.True);
        Assert.That(snippet.Parts[^1].Kind, Is.EqualTo(SnippetPartKind.End));
    }
    [TestCase("$missing$")]
    [TestCase("$unfinished")]
    [TestCase("$end$$end$")]
    public void InvalidSnippetCodeIsRejectedBeforeInsertion(string code) => Assert.Throws<FormatException>(() => new CodeSnippet("Invalid", "x", "csharp", code));
    [Test]
    public void DuplicateAndReservedDeclarationsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new CodeSnippet("X", "x", "csharp", "$a$", [new("a", "1"), new("a", "2")]));
        Assert.Throws<ArgumentException>(() => new CodeSnippet("X", "x", "csharp", "", [new("end", "x")]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CodeSnippet("X", "x", "csharp", new string('x', 1_048_577)));
    }
    [Test]
    public void CustomDelimiterAndUnicodeArePreserved()
    {
        var snippet = new CodeSnippet("日本語", "s", "VB", "%% %name% 🙂 %end%", [new("name", "Śoltés")], delimiter: '%');
        Assert.That(snippet.Language, Is.EqualTo("vb")); Assert.That(snippet.Parts[0].Text, Is.EqualTo("% "));
        Assert.That(snippet.Declarations[0].DefaultText, Is.EqualTo("Śoltés"));
    }
    [Test]
    public void VisualStudioXmlRoundTripsAllRetainedMetadataWithoutOwningStream()
    {
        var original = new CodeSnippet("A < B", "test", "CSharp", "$object$ $selected$ $$ $end$ ]]> 🙂", [new("object", "value", false, "tip", "HostFunction()", true, "System.String")],
            SnippetTypes.Expansion | SnippetTypes.SurroundsWith, description: "desc & more", author: "Author", helpUrl: "https://example.invalid/help", codeKind: "method body",
            imports: ["System"], references: [new("System.Runtime", "https://example.invalid/ref")], keywords: ["one", "two"]);
        using var stream = new MemoryStream(); CodeSnippetSerializer.Save(stream, [original]); Assert.That(stream.CanWrite, Is.True); stream.Position = 0;
        var copy = CodeSnippetSerializer.Load(stream).Single(); Assert.That(stream.CanRead, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(copy.Title, Is.EqualTo(original.Title)); Assert.That(copy.Shortcut, Is.EqualTo(original.Shortcut));
            Assert.That(copy.Code, Is.EqualTo(original.Code)); Assert.That(copy.Types, Is.EqualTo(original.Types));
            Assert.That(copy.Declarations, Is.EqualTo(original.Declarations)); Assert.That(copy.Parts, Is.EqualTo(original.Parts));
            Assert.That(copy.Imports, Is.EqualTo(original.Imports)); Assert.That(copy.References, Is.EqualTo(original.References));
            Assert.That(copy.Keywords, Is.EqualTo(original.Keywords)); Assert.That(copy.CodeKind, Is.EqualTo(original.CodeKind));
            Assert.That(copy.Description, Is.EqualTo(original.Description)); Assert.That(copy.HelpUrl, Is.EqualTo(original.HelpUrl));
        });
    }
    [Test]
    public void VisualStudioCdataAndObjectLiteralDeclarationsAreSupported()
    {
        const string xml = """
        <CodeSnippets xmlns="http://schemas.microsoft.com/VisualStudio/2005/CodeSnippet">
          <CodeSnippet Format="1.0.0"><Header><Title>Loop</Title><Shortcut>for</Shortcut></Header><Snippet>
            <Declarations><Literal Editable="true"><ID>i</ID><Default>index</Default></Literal></Declarations>
            <Code Language="CSharp"><![CDATA[for ($i$) { $selected$ }$end$]]></Code>
          </Snippet></CodeSnippet>
        </CodeSnippets>
        """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml)); var snippet = CodeSnippetSerializer.Load(stream).Single();
        Assert.That(snippet.Code, Is.EqualTo("for ($i$) { $selected$ }$end$")); Assert.That(snippet.Declarations[0].Editable, Is.True);
    }
    [TestCase("<!DOCTYPE x SYSTEM 'file:///etc/passwd'><CodeSnippets/>")]
    [TestCase("<!DOCTYPE x [<!ENTITY a 'expand'>]><CodeSnippets>&a;</CodeSnippets>")]
    public void DtdAndExternalEntitiesAreProhibited(string xml)
    { using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml)); Assert.Throws<XmlException>(() => CodeSnippetSerializer.Load(stream)); }
    [Test]
    public void FolderFilteringAndLanguageSpecificShortcutComparisonWork()
    {
        var catalog = SnippetCatalog.CreateDefault(); Assert.That(catalog.Entries.Count, Is.EqualTo(16));
        Assert.That(catalog.FindShortcut("FOR", "VB"), Is.Not.Null); Assert.That(catalog.FindShortcut("FOR", "C#"), Is.Null);
        var flow = catalog.Find("csharp", SnippetTypes.SurroundsWith, folder: "Control flow").ToArray();
        Assert.That(flow.Length, Is.EqualTo(5)); Assert.That(flow.All(e => e.Snippet.Types.HasFlag(SnippetTypes.SurroundsWith)), Is.True);
        Assert.That(catalog.Find("csharp", SnippetTypes.Expansion, "property").Single().Snippet.Shortcut, Is.EqualTo("prop"));
        Assert.Throws<ArgumentException>(() => catalog.Register(new("X", "x", "csharp", "x"), "../outside"));
    }
    [Test]
    public void AllBuiltInSnippetsSurviveXmlRoundTrip()
    {
        var catalog = SnippetCatalog.CreateDefault(); using var stream = new MemoryStream();
        CodeSnippetSerializer.Save(stream, catalog.Entries.Select(e => e.Snippet)); stream.Position = 0;
        var loaded = CodeSnippetSerializer.Load(stream); Assert.That(loaded.Length, Is.EqualTo(catalog.Entries.Count));
        Assert.That(loaded.Select(s => s.Code), Is.EqualTo(catalog.Entries.Select(e => e.Snippet.Code)));
    }
}
