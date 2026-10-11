using System.Text.Json.Nodes;
using EditorScene.Protocol;
using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class SemanticTokenDeltaTests
{
    private static JsonNode J(string text) => JsonNode.Parse(text)!;
    private static SemanticTokenData Initial() => SemanticTokenData.FromFull(J("{\"resultId\":\"one\",\"data\":[0,0,5,0,0,1,0,3,0,0]}"));
    [Test]
    public void UnorderedIntegerEditsUseTheSameBaseAndCanSplitTokenRecords()
    {
        var initial = Initial();
        var updated = initial.ApplyDelta(J("{\"resultId\":\"two\",\"edits\":[{\"start\":7,\"deleteCount\":1,\"data\":[4]},{\"start\":0,\"deleteCount\":1,\"data\":[2]}]}"));
        Assert.That(updated.Data, Is.EqualTo(new[] { 2, 0, 5, 0, 0, 1, 0, 4, 0, 0 }));
        Assert.That(initial.Data[0], Is.Zero); Assert.That(initial.ResultId, Is.EqualTo("one")); Assert.That(updated.ResultId, Is.EqualTo("two"));
    }
    [Test]
    public void EmptyDeltaSharesImmutableStorageAndReplacementFullOrNullResetsIt()
    {
        var original = Initial(); var result = original.ApplyDelta(J("{\"resultId\":\"two\",\"edits\":[]}"));
        Assert.That(result.Data == original.Data, Is.True);
        Assert.That(result.ApplyDelta(J("{\"data\":[]}")).ResultId, Is.Null);
        Assert.That(result.ApplyDelta(null).Data, Is.Empty);
        Assert.That(result.ApplyDelta(J("{\"edits\":[]}")).ResultId, Is.Null);
    }
    [Test]
    public void RepeatedInsertionsAtTheSamePositionPreserveWireOrder()
    {
        var result = Initial().ApplyDelta(J("{\"edits\":[{\"start\":0,\"deleteCount\":0,\"data\":[0,0]},{\"start\":0,\"deleteCount\":0,\"data\":[1,0,0]}]}"));
        Assert.That(result.Data.Take(5), Is.EqualTo(new[] { 0, 0, 1, 0, 0 }));
    }
    [TestCase("{\"edits\":[{\"start\":-1,\"deleteCount\":0}]}")]
    [TestCase("{\"edits\":[{\"start\":11,\"deleteCount\":0}]}")]
    [TestCase("{\"edits\":[{\"start\":9,\"deleteCount\":2}]}")]
    [TestCase("{\"edits\":[{\"start\":0,\"deleteCount\":1}]}")]
    [TestCase("{\"edits\":[{\"start\":0,\"deleteCount\":5},{\"start\":2,\"deleteCount\":5}]}")]
    [TestCase("{\"edits\":[{\"start\":0,\"deleteCount\":1,\"data\":[-1]}]}")]
    [TestCase("{\"edits\":[{\"start\":0,\"deleteCount\":1,\"data\":[2147483648]}]}")]
    [TestCase("{\"resultId\":123,\"edits\":[]}")]
    [TestCase("{\"edits\":null}")]
    [TestCase("{\"data\":[],\"edits\":[]}")]
    public void MalformedDeltasDoNotMutateTheOldState(string json)
    {
        var state = Initial(); Assert.Throws<InvalidDataException>(() => state.ApplyDelta(J(json)));
        Assert.That(state.Data.Length, Is.EqualTo(10)); Assert.That(state.ResultId, Is.EqualTo("one"));
    }
    [Test]
    public void MissingBaseAndMalformedFullDataAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => SemanticTokenData.FromFull(J("{\"data\":[]}")).ApplyDelta(J("{\"edits\":[]}")));
        Assert.Throws<InvalidDataException>(() => SemanticTokenData.FromFull(J("{\"data\":[0,0,1,0]}")));
        Assert.Throws<InvalidDataException>(() => SemanticTokenData.FromFull(J("{\"data\":[0,0,1,0,\"bad\"]}")));
        Assert.Throws<OperationCanceledException>(() => Initial().ApplyDelta(J("{\"edits\":[]}"), new CancellationToken(true)));
    }
    [Test]
    public void FullResultDoesNotRetainMutableJsonAndDecoderUsesNoFlattenedText()
    {
        var json = J("{\"data\":[0,0,5,0,0]}"); var data = SemanticTokenData.FromFull(json); json["data"]![2] = 1;
        using var doc = new EditorDocument("class X"); var snapshot = doc.Snapshot();
        var spans = LspResultConverter.DecodeSemanticTokens(snapshot, new[] { "keyword" }, data.Data.AsSpan());
        Assert.That(spans[0].Range.Length, Is.EqualTo(5)); Assert.That(SnapshotLineIndexTests.IsFlattened(snapshot), Is.False);
    }
    [Test]
    public void TypedDecoderRejectsOverflowOverlapAndSplitSurrogatePositions()
    {
        using var doc = new EditorDocument("🙂abc"); var snapshot = doc.Snapshot(); var legend = new[] { "keyword" };
        Assert.Throws<InvalidDataException>(() => LspResultConverter.DecodeSemanticTokens(snapshot, legend, new[] { 0, 1, 1, 0, 0 }));
        Assert.Throws<InvalidDataException>(() => LspResultConverter.DecodeSemanticTokens(snapshot, legend, new[] { 0, 0, 1, 0, 0 }));
        Assert.Throws<InvalidDataException>(() => LspResultConverter.DecodeSemanticTokens(snapshot, legend, new[] { 0, 0, int.MaxValue, 0, 0 }));
        Assert.Throws<InvalidDataException>(() => LspResultConverter.DecodeSemanticTokens(snapshot, legend, new[] { int.MaxValue, 0, 1, 0, 0 }));
        Assert.Throws<InvalidDataException>(() => LspResultConverter.DecodeSemanticTokens(snapshot, legend, new[] { 0, 0, 2, 0, 0, 0, 0, 2, 0, 0 }));
        Assert.That(LspResultConverter.DecodeSemanticTokens(snapshot, new[] { "method" }, new[] { 0, 2, 3, 0, 0 })[0].Kind, Is.EqualTo("function"));
    }
    [Test]
    public void TypedDecoderAllocatesOnlyBoundedResultStorageForDenseTokens()
    {
        using var doc = new EditorDocument(string.Concat(Enumerable.Repeat("x ", 10000))); var snapshot = doc.Snapshot(); _ = snapshot.LineCount;
        var data = new int[50000]; var legend = new[] { "keyword" };
        for (var i = 0; i < data.Length; i += 5) { data[i + 1] = i == 0 ? 0 : 2; data[i + 2] = 1; }
        _ = LspResultConverter.DecodeSemanticTokens(snapshot, legend, data);
        var bytes = GC.GetAllocatedBytesForCurrentThread(); var output = LspResultConverter.DecodeSemanticTokens(snapshot, legend, data);
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - bytes, Is.LessThan(650000));
        Assert.That(output.Length, Is.EqualTo(10000)); Assert.That(SnapshotLineIndexTests.IsFlattened(snapshot), Is.False);
    }
    private static JsonObject Capabilities(bool delta = true) => new()
    {
        ["textDocumentSync"] = 2,
        ["semanticTokensProvider"] = new JsonObject { ["full"] = delta ? new JsonObject { ["delta"] = true } : JsonValue.Create(true), ["legend"] = new JsonObject { ["tokenTypes"] = new JsonArray("keyword"), ["tokenModifiers"] = new JsonArray() } }
    };
    [Test]
    public async Task ProviderNegotiatesDeltaUsesResultIdAndSharesUnchangedDecodedSpans()
    {
        using var doc = new EditorDocument("class"); var first = doc.Snapshot(); doc.Buffer.Insert(0, "\n"); var next = doc.Snapshot();
        await using var peer = new LspTestPeer { Capabilities = Capabilities() }; var fulls = 0; var deltas = 0;
        peer.Request = (method, parameters, _) =>
        {
            if (method == "textDocument/semanticTokens/full") { fulls++; return Task.FromResult<JsonNode?>(J("{\"resultId\":\"one\",\"data\":[0,0,5,0,0]}")); }
            Assert.That(method, Is.EqualTo("textDocument/semanticTokens/full/delta")); deltas++;
            Assert.That(parameters!["previousResultId"]!.GetValue<string>(), Is.EqualTo(deltas == 1 ? "one" : "two"));
            return Task.FromResult<JsonNode?>(J(deltas == 1 ? "{\"resultId\":\"two\",\"edits\":[{\"start\":0,\"deleteCount\":1,\"data\":[1]}]}" : "{\"resultId\":\"two\",\"edits\":[]}"));
        };
        await using var language = await LspSyntaxLanguage.OpenAsync(peer.Client, first, new Uri("file:///test.cs"), "csharp");
        var a = await language.AnalyzeAsync(first); var b = await language.AnalyzeAsync(next); var c = await language.AnalyzeAsync(next);
        Assert.That(a.Classifications[0].Range.Offset, Is.Zero); Assert.That(b.Classifications[0].Range.Offset, Is.EqualTo(1));
        Assert.That(b.Classifications == c.Classifications, Is.True); Assert.That((fulls, deltas), Is.EqualTo((1, 2)));
    }
    [TestCase(false)] [TestCase(true)]
    public async Task InvalidDeltaGetsOneValidatedFullReplacement(bool invalidDecodedRange)
    {
        using var doc = new EditorDocument("class"); var snapshot = doc.Snapshot();
        await using var peer = new LspTestPeer { Capabilities = Capabilities() }; var fulls = 0; var deltas = 0;
        peer.Request = (method, _, _) =>
        {
            if (method.EndsWith("/delta", StringComparison.Ordinal))
            { deltas++; return Task.FromResult<JsonNode?>(J(invalidDecodedRange ? "{\"edits\":[{\"start\":2,\"deleteCount\":1,\"data\":[500]}]}" : "{\"edits\":[{\"start\":500,\"deleteCount\":1}]}")); }
            fulls++; return Task.FromResult<JsonNode?>(J("{\"resultId\":\"good\",\"data\":[0,0,5,0,0]}"));
        };
        await using var language = await LspSyntaxLanguage.OpenAsync(peer.Client, snapshot, new Uri("file:///test.cs"), "csharp");
        await language.AnalyzeAsync(snapshot); var second = await language.AnalyzeAsync(snapshot);
        Assert.That(second.Classifications.Length, Is.EqualTo(1)); Assert.That((fulls, deltas), Is.EqualTo((2, 1)));
        Assert.That(language.LastProtocolWarning, Does.Contain("full replacement"));
    }
    [Test]
    public async Task CancellationDoesNotReplaceCacheOrFallBackAndFullOnlyServersStayFull()
    {
        using var doc = new EditorDocument("class"); var snapshot = doc.Snapshot();
        await using var peer = new LspTestPeer { Capabilities = Capabilities() }; var delay = true; var fulls = 0;
        peer.Request = async (method, parameters, token) =>
        {
            if (!method.EndsWith("/delta", StringComparison.Ordinal)) { fulls++; return J("{\"resultId\":\"good\",\"data\":[0,0,5,0,0]}"); }
            Assert.That(parameters!["previousResultId"]!.GetValue<string>(), Is.EqualTo("good"));
            if (delay) await Task.Delay(Timeout.Infinite, token);
            return J("{\"resultId\":\"good\",\"edits\":[]}");
        };
        await using var language = await LspSyntaxLanguage.OpenAsync(peer.Client, snapshot, new Uri("file:///test.cs"), "csharp");
        await peer.InitializedAsync(); await peer.NextAsync("textDocument/didOpen");
        await language.AnalyzeAsync(snapshot); await peer.NextAsync("textDocument/semanticTokens/full");
        using var cancel = new CancellationTokenSource(); var pending = language.AnalyzeAsync(snapshot, cancel.Token);
        await peer.NextAsync("textDocument/semanticTokens/full/delta"); cancel.Cancel();
        Assert.That(async () => await pending, Throws.InstanceOf<OperationCanceledException>());
        delay = false; await language.AnalyzeAsync(snapshot); Assert.That(fulls, Is.EqualTo(1));
        await using var fullPeer = new LspTestPeer { Capabilities = Capabilities(false) };
        fullPeer.Request = (method, _, _) => { Assert.That(method, Is.EqualTo("textDocument/semanticTokens/full")); return Task.FromResult<JsonNode?>(J("{\"data\":[0,0,5,0,0]}")); };
        await using var fullLanguage = await LspSyntaxLanguage.OpenAsync(fullPeer.Client, snapshot, new Uri("file:///other.cs"), "csharp");
        await fullLanguage.AnalyzeAsync(snapshot); await fullLanguage.AnalyzeAsync(snapshot);
    }
}
