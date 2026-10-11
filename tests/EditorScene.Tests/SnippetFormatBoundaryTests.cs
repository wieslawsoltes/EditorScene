using System.Text;
using EditorScene.Snippets;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class SnippetFormatBoundaryTests
{
    private static CodeSnippet Load(string xml)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        return CodeSnippetSerializer.Load(stream).Single();
    }
    [Test]
    public void UnknownFormatVersionRequiresAnExplicitMigration()
    {
        const string xml = "<CodeSnippet Format=\"2.0.0\"><Header><Title>X</Title></Header><Snippet><Code Language=\"CSharp\">value</Code></Snippet></CodeSnippet>";
        Assert.Throws<NotSupportedException>(() => Load(xml));
    }
    [Test]
    public void UnrelatedXmlNamespaceIsNotTreatedAsASnippetFile()
    {
        const string xml = "<CodeSnippet xmlns=\"urn:unrelated\"><Header><Title>X</Title></Header><Snippet><Code Language=\"CSharp\">value</Code></Snippet></CodeSnippet>";
        Assert.Throws<FormatException>(() => Load(xml));
    }
    [Test]
    public void UnknownSnippetTypesAreNotSilentlyDiscarded()
    {
        const string xml = "<CodeSnippet><Header><Title>X</Title><SnippetTypes><SnippetType>ExecuteCommand</SnippetType></SnippetTypes></Header><Snippet><Code Language=\"CSharp\">value</Code></Snippet></CodeSnippet>";
        Assert.Throws<NotSupportedException>(() => Load(xml));
    }
    [Test]
    public void NonEditableDeclarationsAndEmptyDefaultsAreRetained()
    {
        const string xml = "<CodeSnippet><Header><Title>X</Title></Header><Snippet><Declarations><Literal Editable=\"false\"><ID>empty</ID><Default></Default></Literal></Declarations><Code Language=\"CSharp\">$empty$$end$</Code></Snippet></CodeSnippet>";
        var snippet = Load(xml);
        Assert.That(snippet.Declarations.Single().Editable, Is.False);
        Assert.That(snippet.Declarations.Single().DefaultText, Is.Empty);
        Assert.That(snippet.Parts[0].Kind, Is.EqualTo(SnippetPartKind.Field));
    }
}
