using System.Text.Json.Nodes;
using EditorScene.LanguageServices;
using EditorScene.Protocol;
using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class LspResultConverterTests
{
    private static JsonNode J(string json) => JsonNode.Parse(json)!;
    private static JsonObject R(int start, int end) => LspTextChange.Range(new(0, start), new(0, end));
    [Test]
    public void CompletionListsSupportDefaultsInsertReplaceAndAdditionalImports()
    {
        using var document = new EditorDocument("prefix LisSuffix"); var snapshot = document.Snapshot();
        var list = new JsonObject
        {
            ["isIncomplete"] = false, ["itemDefaults"] = new JsonObject { ["editRange"] = R(7, 16), ["insertTextFormat"] = 1 },
            ["items"] = new JsonArray(new JsonObject
            {
                ["label"] = "List", ["textEditText"] = "List<int>", ["kind"] = 7, ["sortText"] = "0",
                ["documentation"] = new JsonObject { ["kind"] = "plaintext", ["value"] = "<script>not executable</script>" },
                ["additionalTextEdits"] = new JsonArray(new JsonObject { ["range"] = R(0, 0), ["newText"] = "import\n" })
            })
        };
        var item = LspResultConverter.Completions(snapshot, 10, list).Single();
        Assert.That(item.InsertText, Is.EqualTo("List<int>")); Assert.That(item.ReplaceRange, Is.EqualTo(new SourceRange(7, 9)));
        Assert.That(item.AdditionalTextEdits.Single(), Is.EqualTo(new TextEdit(0, 0, "import\n")));
        Assert.That(item.Description, Does.Contain("<script>"));
        var insertReplace = new JsonArray(new JsonObject { ["label"] = "List", ["textEdit"] = new JsonObject { ["insert"] = R(7, 10), ["replace"] = R(7, 16), ["newText"] = "List" } });
        Assert.That(LspResultConverter.Completions(snapshot, 10, insertReplace).Single().ReplaceRange!.Value.Length, Is.EqualTo(9));
    }
    [Test]
    public void UnadvertisedSnippetsAndExecutableCompletionCommandsAreNotRunOrSilentlyFlattened()
    {
        using var document = new EditorDocument("x"); var warnings = new List<string>();
        var items = LspResultConverter.Completions(document.Snapshot(), 1,
            J("[{\"label\":\"snippet\",\"insertTextFormat\":2},{\"label\":\"command\",\"command\":{\"command\":\"run.arbitrary\"}},{\"label\":\"plain\"}]"), warnings.Add);
        Assert.That(items.Select(i => i.Label), Is.EqualTo(new[] { "plain" })); Assert.That(warnings.Count, Is.EqualTo(2));
    }
    [Test]
    public void OverlappingFormattingAndCompletionEditsAreRejected()
    {
        using var document = new EditorDocument("abcdef"); var snapshot = document.Snapshot();
        var edits = new JsonArray(new JsonObject { ["range"] = R(1, 4), ["newText"] = "X" }, new JsonObject { ["range"] = R(3, 5), ["newText"] = "Y" });
        Assert.Throws<InvalidDataException>(() => LspResultConverter.Edits(snapshot, edits));
        var items = new JsonArray(new JsonObject { ["label"] = "value", ["textEdit"] = new JsonObject { ["range"] = R(1, 4), ["newText"] = "X" }, ["additionalTextEdits"] = new JsonArray(new JsonObject { ["range"] = R(2, 2), ["newText"] = "Y" }) });
        Assert.Throws<ArgumentException>(() => LspResultConverter.Completions(snapshot, 3, items));
    }
    [TestCase(-1, 0)]
    [TestCase(1, 0)]
    [TestCase(0, 99)]
    [TestCase(0, 1)]
    public void InvalidOrSplitSurrogatePositionsAreRejected(int line, int character)
    {
        using var document = new EditorDocument("🙂x");
        Assert.Throws<InvalidDataException>(() => LspResultConverter.Offset(document.Snapshot(), LspTextChange.Position(new(line, character))));
    }
    [Test]
    public void DiagnosticsPreserveUtf16OffsetsCodesAndSeverity()
    {
        using var document = new EditorDocument("🙂 bad");
        var issues = new JsonArray(new JsonObject { ["range"] = R(3, 6), ["message"] = "invalid", ["code"] = 123, ["severity"] = 2 });
        var diagnostic = LspResultConverter.Diagnostics(document.Snapshot(), issues).Single();
        Assert.That(diagnostic.Range, Is.EqualTo(new SourceRange(3, 3))); Assert.That(diagnostic.Code, Is.EqualTo("123")); Assert.That(diagnostic.Severity, Is.EqualTo(IssueSeverity.Warning));
    }
    [Test]
    public void HoverRendersMarkedStringsAsDataAndSignaturesHandleLabelOffsets()
    {
        using var document = new EditorDocument("value");
        var hover = LspResultConverter.Hover(document.Snapshot(), 2, J("{\"contents\":[{\"language\":\"csharp\",\"value\":\"int value\"},\"description\"]}"));
        Assert.That(hover!.Signature, Is.EqualTo("int value\n\ndescription"));
        var help = LspResultConverter.Signatures(J("{\"signatures\":[{\"label\":\"M(int x)\",\"parameters\":[{\"label\":[2,7]}],\"activeParameter\":0}],\"activeSignature\":99,\"activeParameter\":99}"));
        Assert.That(help!.ActiveSignature, Is.Zero); Assert.That(help.ActiveParameter, Is.Zero); Assert.That(help.Signatures[0].Parameters[0], Is.EqualTo("int x"));
    }
    [Test]
    public void NestedAndFlatSymbolsMapWithoutOpeningExternalFiles()
    {
        using var document = new EditorDocument("class C { int x; }"); var snapshot = document.Snapshot(); var uri = new Uri("file:///test.cs");
        var children = new JsonArray(new JsonObject { ["name"] = "x", ["kind"] = 8, ["range"] = R(10, 16), ["selectionRange"] = R(14, 15) });
        var tree = new JsonArray(new JsonObject { ["name"] = "C", ["kind"] = 5, ["range"] = R(0, 18), ["selectionRange"] = R(6, 7), ["children"] = children });
        Assert.That(LspResultConverter.Symbols(snapshot, uri, tree).Single().Children.Single().Name, Is.EqualTo("x"));
        var flat = new JsonArray(new JsonObject { ["name"] = "foreign", ["kind"] = 5, ["location"] = new JsonObject { ["uri"] = "file:///other.cs", ["range"] = R(0, 1) } });
        Assert.That(LspResultConverter.Symbols(snapshot, uri, flat), Is.Empty);
    }
    [Test]
    public void SemanticTokensUseTheServersLegendAndRejectOverlap()
    {
        using var document = new EditorDocument("class C\n{ int value; }"); var snapshot = document.Snapshot();
        var spans = LspResultConverter.SemanticTokens(snapshot, new[] { "class", "keyword" }, J("{\"data\":[0,0,5,1,0,0,6,1,0,0,1,2,3,1,0]}"));
        Assert.That(spans.Select(s => s.Kind), Is.EqualTo(new[] { "keyword", "type", "keyword" }));
        Assert.That(spans[2].Range.Offset, Is.EqualTo(10));
        Assert.Throws<InvalidDataException>(() => LspResultConverter.SemanticTokens(snapshot, new[] { "keyword" }, J("{\"data\":[0,0,5,0,0,0,2,3,0,0]}")));
        Assert.Throws<InvalidDataException>(() => LspResultConverter.SemanticTokens(snapshot, new[] { "keyword" }, J("{\"data\":[0,0,5]}")));
    }
    [Test]
    public void LineOnlyFoldRangesUseTextEndpointsAndRejectCrossingRegions()
    {
        using var document = new EditorDocument("class C\n{\n x;\n}\n"); var snapshot = document.Snapshot();
        var fold = LspResultConverter.Foldings(snapshot, J("[{\"startLine\":0,\"endLine\":3}]")).Single();
        Assert.That(fold.Range.Offset, Is.EqualTo(7)); Assert.That(fold.Range.End, Is.EqualTo(15));
        Assert.Throws<InvalidDataException>(() => LspResultConverter.Foldings(snapshot, J("[{\"startLine\":0,\"endLine\":2},{\"startLine\":1,\"endLine\":3}]")));
    }
}
