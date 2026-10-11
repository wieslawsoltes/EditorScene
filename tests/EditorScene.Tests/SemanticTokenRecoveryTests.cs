using System.Text.Json.Nodes;
using EditorScene.Protocol;
using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class SemanticTokenRecoveryTests
{
    private static JsonNode J(string value) => JsonNode.Parse(value)!;
    private static JsonObject Capabilities() => (JsonObject)J("""
        {"textDocumentSync":2,"semanticTokensProvider":{"full":{"delta":true},"legend":{"tokenTypes":["keyword"],"tokenModifiers":[]}}}
        """);
    [TestCase(-32601, 3, 1)]
    [TestCase(-32602, 2, 2)]
    public async Task UnsupportedDeltaDisablesOnlyDeltaWhileExpiredBaseCanRecover(int code, int expectedFulls, int expectedDeltas)
    {
        using var document = new EditorDocument("class"); var snapshot = document.Snapshot();
        await using var peer = new LspTestPeer { Capabilities = Capabilities() };
        var fulls = 0; var deltas = 0;
        peer.Request = (method, _, _) =>
        {
            if (method.EndsWith("/delta", StringComparison.Ordinal))
            {
                if (++deltas == 1) throw new JsonRpcException(code, "No usable delta base.");
                return Task.FromResult<JsonNode?>(J("{\"resultId\":\"good\",\"edits\":[]}"));
            }
            fulls++; return Task.FromResult<JsonNode?>(J("{\"resultId\":\"good\",\"data\":[0,0,5,0,0]}"));
        };
        await using var language = await LspSyntaxLanguage.OpenAsync(peer.Client, snapshot, new Uri("file:///test.cs"), "csharp");
        for (var i = 0; i < 3; i++) Assert.That((await language.AnalyzeAsync(snapshot)).Classifications.Length, Is.EqualTo(1));
        Assert.That((fulls, deltas), Is.EqualTo((expectedFulls, expectedDeltas)));
    }
    [Test]
    public async Task MissingResultIdStopsUsingThePreviousDeltaBase()
    {
        using var document = new EditorDocument("class"); var snapshot = document.Snapshot();
        await using var peer = new LspTestPeer { Capabilities = Capabilities() }; var methods = new List<string>();
        peer.Request = (method, _, _) =>
        {
            methods.Add(method);
            return Task.FromResult<JsonNode?>(J(method.EndsWith("/delta", StringComparison.Ordinal)
                ? "{\"edits\":[]}" : "{\"resultId\":\"good\",\"data\":[0,0,5,0,0]}"));
        };
        await using var language = await LspSyntaxLanguage.OpenAsync(peer.Client, snapshot, new Uri("file:///test.cs"), "csharp");
        for (var i = 0; i < 3; i++) await language.AnalyzeAsync(snapshot);
        Assert.That(methods, Is.EqualTo(new[] { "textDocument/semanticTokens/full", "textDocument/semanticTokens/full/delta", "textDocument/semanticTokens/full" }));
    }
    [Test]
    public async Task InvalidRecoveryFullResponseFailsWithoutRetryLoop()
    {
        using var document = new EditorDocument("class"); var snapshot = document.Snapshot();
        await using var peer = new LspTestPeer { Capabilities = Capabilities() }; var fulls = 0; var deltas = 0;
        peer.Request = (method, _, _) =>
        {
            if (method.EndsWith("/delta", StringComparison.Ordinal)) { deltas++; return Task.FromResult<JsonNode?>(J("{\"edits\":[{\"start\":9,\"deleteCount\":0}]}")); }
            return Task.FromResult<JsonNode?>(J(++fulls == 1 ? "{\"resultId\":\"good\",\"data\":[0,0,5,0,0]}" : "{\"data\":[0,0,100,0,0]}"));
        };
        await using var language = await LspSyntaxLanguage.OpenAsync(peer.Client, snapshot, new Uri("file:///test.cs"), "csharp");
        await language.AnalyzeAsync(snapshot);
        Assert.ThrowsAsync<InvalidDataException>(() => language.AnalyzeAsync(snapshot));
        Assert.That((fulls, deltas), Is.EqualTo((2, 1)));
    }
}
