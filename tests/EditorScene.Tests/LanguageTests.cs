using EditorScene.LanguageServices;
using EditorScene.Languages.DotNet;
using EditorScene.Languages.Structured;
using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class LanguageTests
{
    [Test]
    public void ReferenceMetadataIsEmbeddedAndDoesNotDependOnAssemblyLocation()
    { Assert.That(ReferenceAssemblyCatalog.Default.Length, Is.GreaterThan(100)); Assert.That(ReferenceAssemblyCatalog.Default.Any(r => r.Display?.Contains("System.Runtime", StringComparison.Ordinal) == true), Is.True); }
    [Test]
    public async Task CSharpProducesRealSemanticDiagnosticsAndStructuredSymbols()
    {
        using var document = new EditorDocument("using System;\nnamespace Example\n{\n public class C\n {\n  public int Value { get; set; }\n  void M() { int x = \"wrong\"; Console.WriteLine(x); }\n }\n}", "test.cs");
        var language = new DotNetLanguage(); var analysis = await language.AnalyzeAsync(document.Snapshot());
        Assert.That(analysis.Diagnostics.Any(d => d.Code == "CS0029"), Is.True);
        Assert.That(analysis.Classifications.Any(c => c.Kind == "keyword"), Is.True);
        Assert.That(analysis.Classifications.Any(c => c.Kind == "type"), Is.True);
        Assert.That(analysis.Foldings.Length, Is.GreaterThanOrEqualTo(2));
        Assert.That(Flatten(analysis.Symbols).Any(s => s.Name.Contains("Value", StringComparison.Ordinal)), Is.True);
    }
    [Test]
    public async Task CSharpValidBclCodeCompilesWithoutMissingReferenceErrors()
    {
        using var document = new EditorDocument("using System; using System.Collections.Generic; using System.Linq; class C { public double M() => new List<int> { 1, 2 }.Average(); }", "test.cs");
        var analysis = await new DotNetLanguage().AnalyzeAsync(document.Snapshot());
        Assert.That(analysis.Diagnostics.Where(d => d.Severity == IssueSeverity.Error), Is.Empty);
    }
    [Test]
    public async Task CSharpMemberCompletionUsesReceiverAndPrefix()
    {
        const string code = "using System; class C { void M() { Console.Wri } }";
        using var document = new EditorDocument(code); var language = new DotNetLanguage(); var offset = code.IndexOf("Wri", StringComparison.Ordinal) + 3;
        var items = await language.CompleteAsync(document.Snapshot(), offset);
        Assert.That(items.Any(i => i.Label == "WriteLine"), Is.True, string.Join(",", items.Select(i => i.Label)));
        Assert.That(items.All(i => i.Label.StartsWith("Wri", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(items.First(i => i.Label == "WriteLine").ReplaceRange, Is.EqualTo(new SourceRange(offset - 3, 3)));
    }
    [Test]
    public async Task InstanceCompletionQuickInfoAndSignatureHelpUseSemanticSymbols()
    {
        const string code = "using System; class C { void M() { string value = \"x\"; value.Sub; Console.WriteLine(\"hello\", 1); } }";
        using var document = new EditorDocument(code); var snapshot = document.Snapshot(); var language = new DotNetLanguage();
        var items = await language.CompleteAsync(snapshot, code.IndexOf("value.Sub", StringComparison.Ordinal) + 9);
        Assert.That(items.Any(i => i.Label == "Substring"), Is.True);
        var info = await language.GetQuickInfoAsync(snapshot, code.IndexOf("WriteLine", StringComparison.Ordinal));
        Assert.That(info?.Signature, Does.Contain("WriteLine"));
        var signature = await language.GetSignatureHelpAsync(snapshot, code.IndexOf(", 1", StringComparison.Ordinal) + 2);
        Assert.That(signature, Is.Not.Null); Assert.That(signature!.ActiveParameter, Is.EqualTo(1)); Assert.That(signature.Signatures.Length, Is.GreaterThan(1));
    }
    [Test]
    public async Task VisualBasicUsesCompilerDiagnosticsSymbolsAndCompletion()
    {
        const string code = "Imports System\nPublic Class Sample\n Public Sub M()\n  Dim value As Integer = \"bad\"\n  Console.Wri\n End Sub\nEnd Class";
        using var document = new EditorDocument(code, "test.vb"); var language = new DotNetLanguage(true);
        var analysis = await language.AnalyzeAsync(document.Snapshot());
        Assert.That(analysis.Diagnostics.Any(d => d.Severity == IssueSeverity.Error), Is.True);
        Assert.That(analysis.Foldings.Length, Is.GreaterThanOrEqualTo(2));
        Assert.That(Flatten(analysis.Symbols).Any(s => s.Name.Contains("Sample", StringComparison.Ordinal)), Is.True);
        var items = await language.CompleteAsync(document.Snapshot(), code.IndexOf("Wri", StringComparison.Ordinal) + 3);
        Assert.That(items.Any(i => i.Label == "WriteLine"), Is.True);
    }
    [Test]
    public async Task DotNetFormattingPreservesCommentsAndUsesDocumentLineEndings()
    {
        using var document = new EditorDocument("// keep\r\nclass C{int M(){return 1;}}");
        var edits = await new DotNetLanguage().FormatAsync(document.Snapshot());
        Assert.That(edits[0].NewText, Does.Contain("// keep\r\n")); Assert.That(edits[0].NewText, Does.Contain("    int M()"));
    }
    [TestCase("{\"value\": \"🙂\", \"items\": [1,true,null]}", 0)]
    [TestCase("{\"value\": \"🙂\", nope}", 1)]
    [TestCase("", 1)]
    [TestCase("{\"x\": 1,}", 1)]
    public async Task JsonUsesStrictParsingAndUtf16Spans(string code, int errors)
    {
        using var document = new EditorDocument(code, "test.json"); var analysis = await new JsonLanguage().AnalyzeAsync(document.Snapshot());
        Assert.That(analysis.Diagnostics.Length, Is.EqualTo(errors));
        foreach (var span in analysis.Classifications) Assert.That(span.Range.End, Is.LessThanOrEqualTo(code.Length));
        if (code.Contains("nope", StringComparison.Ordinal)) Assert.That(analysis.Diagnostics[0].Range.Offset, Is.EqualTo(code.IndexOf("nope", StringComparison.Ordinal)));
    }
    [Test]
    public async Task JsonOutliningAndFormattingComeFromParsedStructure()
    {
        using var document = new EditorDocument("{\n\"x\": [\n1,2\n]\n}"); var language = new JsonLanguage();
        var analysis = await language.AnalyzeAsync(document.Snapshot()); Assert.That(analysis.Foldings.Length, Is.EqualTo(2));
        Assert.That(Flatten(analysis.Symbols).Any(s => s.Name == "x"), Is.True);
        var edits = await language.FormatAsync(document.Snapshot()); Assert.That(edits[0].NewText, Does.Contain("  \"x\""));
    }
    [TestCase("<root><child name=\"a\" /></root>", 0)]
    [TestCase("<root><child></root>", 1)]
    [TestCase("<!DOCTYPE root SYSTEM \"file:///etc/passwd\"><root/>", 1)]
    [TestCase("<!DOCTYPE root [<!ENTITY x \"oops\">]><root>&x;</root>", 1)]
    public async Task XmlParsesAndProhibitsExternalEntities(string code, int errors)
    {
        using var document = new EditorDocument(code); var analysis = await new XmlLanguage().AnalyzeAsync(document.Snapshot());
        Assert.That(analysis.Diagnostics.Length, Is.EqualTo(errors));
    }
    [Test]
    public async Task XmlProvidesHierarchyAndPreservesDeclaredEncodingDuringFormatting()
    {
        using var document = new EditorDocument("<?xml version=\"1.0\" encoding=\"utf-8\"?><root>\n<child value=\"🙂\"/>\n</root>"); var language = new XmlLanguage();
        var analysis = await language.AnalyzeAsync(document.Snapshot()); Assert.That(analysis.Symbols.Single().Children.Single().Name, Is.EqualTo("child")); Assert.That(analysis.Foldings.Length, Is.EqualTo(1));
        var edits = await language.FormatAsync(document.Snapshot()); Assert.That(edits[0].NewText, Does.Contain("encoding=\"utf-8\""));
    }
    private static IEnumerable<NavigationSymbol> Flatten(IEnumerable<NavigationSymbol> symbols)
    { foreach (var symbol in symbols) { yield return symbol; foreach (var child in Flatten(symbol.Children)) yield return child; } }
}
