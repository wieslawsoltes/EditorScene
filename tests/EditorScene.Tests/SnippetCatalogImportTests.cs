using EditorScene.Snippets;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class SnippetCatalogImportTests
{
    [Test]
    public void DuplicateInBatchDoesNotPartiallyImportEarlierEntries()
    {
        var catalog = SnippetCatalog.CreateDefault(); var count = catalog.Entries.Count;
        var first = new CodeSnippet("Imported", "mine", "csharp", "value");
        Assert.Throws<ArgumentException>(() => catalog.RegisterRange([new("User", first), new("User", first)]));
        Assert.That(catalog.Entries.Count, Is.EqualTo(count)); Assert.That(catalog.FindShortcut("mine", "csharp"), Is.Null);
    }
    [Test]
    public void ConflictWithExistingCatalogDoesNotImportTheRestOfTheBatch()
    {
        var catalog = SnippetCatalog.CreateDefault(); var existing = catalog.Entries[0]; var count = catalog.Entries.Count;
        Assert.Throws<ArgumentException>(() => catalog.RegisterRange([new("User", new("New", "new", "csharp", "")), existing]));
        Assert.That(catalog.Entries.Count, Is.EqualTo(count));
    }
    [Test]
    public void SeparateEditorHostsCanOwnIndependentCatalogs()
    {
        var left = SnippetCatalog.CreateDefault(); var right = SnippetCatalog.CreateDefault();
        left.RegisterRange([new("User", new("Custom", "custom", "csharp", "value"))]);
        Assert.That(left.FindShortcut("custom", "csharp"), Is.Not.Null);
        Assert.That(right.FindShortcut("custom", "csharp"), Is.Null);
    }
}
