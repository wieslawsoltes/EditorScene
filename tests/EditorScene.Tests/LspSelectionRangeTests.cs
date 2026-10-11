using System.Text.Json.Nodes;
using EditorScene.LanguageServices;
using EditorScene.Protocol;
using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class LspSelectionRangeTests
{
    private static JsonObject Node(int start, int end, JsonNode? parent = null) => new()
    { ["range"] = LspTextChange.Range(new(0, start), new(0, end)), ["parent"] = parent };
    [Test]
    public void ValidChainsAndEqualParentsAreDecodedWithoutMutatingInput()
    {
        using var doc = new EditorDocument("one + two"); var response = new JsonArray(Node(0, 3, Node(0, 3, Node(0, 9)))); var json = response.ToJsonString();
        var result = LspSelectionRanges.Decode(doc.Snapshot(), new[] { 2 }, response);
        Assert.That(result[0], Is.EqualTo(new[] { new SourceRange(0, 3), new SourceRange(0, 9) })); Assert.That(response.ToJsonString(), Is.EqualTo(json));
        Assert.That(LspSelectionRanges.Decode(doc.Snapshot(), new[] { 0, 5 }, null).All(r => r.IsEmpty), Is.True);
    }
    [TestCase("missing")]
    [TestCase("wrong-count")]
    [TestCase("outside")]
    [TestCase("parent")]
    [TestCase("reversed")]
    [TestCase("null-entry")]
    [TestCase("wrong-type")]
    public void MalformedResponsesAreRejectedAtomically(string mode)
    {
        using var doc = new EditorDocument("one + two");
        JsonNode response = mode switch
        {
            "missing" => new JsonArray(new JsonObject()), "wrong-count" => new JsonArray(),
            "outside" => new JsonArray(Node(5, 8)), "parent" => new JsonArray(Node(0, 3, Node(1, 9))),
            "reversed" => new JsonArray(Node(3, 0)), "null-entry" => new JsonArray((JsonNode?)null), _ => new JsonArray("bad")
        };
        Assert.Throws<InvalidDataException>(() => LspSelectionRanges.Decode(doc.Snapshot(), new[] { 1 }, response));
    }
    [Test]
    public void TooDeepParentsAndSurrogateSplitsAreRejected()
    {
        using var doc = new EditorDocument("A🙂B"); var s = doc.Snapshot();
        Assert.Throws<InvalidDataException>(() => LspSelectionRanges.Decode(s, new[] { 1 }, new JsonArray(Node(1, 2))));
        JsonNode parent = Node(0, 4); for (var i = 0; i < LspSelectionRanges.MaximumParentDepth; i++) parent = Node(0, 4, parent);
        Assert.Throws<InvalidDataException>(() => LspSelectionRanges.Decode(s, new[] { 0 }, new JsonArray(parent)));
    }
    [Test]
    public async Task NegotiatedSelectionRangesUseTheCurrentUtf16SnapshotLease()
    {
        using var doc = new EditorDocument("one + two"); var before = doc.Snapshot(); doc.Buffer.Insert(0, "x "); var after = doc.Snapshot();
        await using var peer = new LspTestPeer { Capabilities = new() { ["textDocumentSync"] = 2, ["selectionRangeProvider"] = true } };
        peer.Request = (method, parameters, _) =>
        {
            Assert.That(method, Is.EqualTo("textDocument/selectionRange"));
            Assert.That(parameters!["positions"]![0]!["character"]!.GetValue<int>(), Is.EqualTo(3));
            return Task.FromResult<JsonNode?>(new JsonArray(Node(2, 5, Node(0, 11))));
        };
        await using var language = await LspSyntaxLanguage.OpenAsync(peer.Client, before, new Uri("file:///test.cs"), "csharp");
        var initialize = await peer.NextAsync("initialize");
        Assert.That(initialize.Parameters!["capabilities"]!["textDocument"]!["selectionRange"]!["dynamicRegistration"]!.GetValue<bool>(), Is.False);
        await peer.NextAsync("initialized"); await peer.NextAsync("textDocument/didOpen");
        Assert.That(language.Capabilities.HasFlag(LanguageCapabilities.SelectionRanges), Is.True);
        var ranges = await language.GetSelectionRangesAsync(after, 3);
        await peer.NextAsync("textDocument/didChange"); await peer.NextAsync("textDocument/selectionRange");
        Assert.That(ranges[0], Is.EqualTo(new SourceRange(2, 3))); Assert.That(language.ServerVersion, Is.EqualTo(2));
        Assert.ThrowsAsync<InvalidOperationException>(() => language.GetSelectionRangesAsync(before, 1));
    }
    [Test]
    public async Task UnadvertisedSelectionDoesNotSendAnUnsupportedRequest()
    {
        using var doc = new EditorDocument("word"); var snapshot = doc.Snapshot(); await using var peer = new LspTestPeer();
        peer.Request = (_, _, _) => throw new AssertionException("Unadvertised selection request was sent");
        await using var language = await LspSyntaxLanguage.OpenAsync(peer.Client, snapshot, new Uri("file:///test.cs"), "csharp");
        Assert.That(await language.GetSelectionRangesAsync(snapshot, 0), Is.Empty);
    }
    [Test]
    public async Task CancelledSelectionReleasesTheDocumentLeaseForFollowingRequests()
    {
        using var doc = new EditorDocument("word"); var snapshot = doc.Snapshot();
        await using var peer = new LspTestPeer { Capabilities = new() { ["textDocumentSync"] = 2, ["selectionRangeProvider"] = true } };
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); var calls = 0;
        peer.Request = async (_, _, token) =>
        {
            if (++calls == 1) { started.TrySetResult(true); await Task.Delay(Timeout.Infinite, token); }
            return new JsonArray(Node(0, 4));
        };
        await using var language = await LspSyntaxLanguage.OpenAsync(peer.Client, snapshot, new Uri("file:///test.cs"), "csharp");
        using var cancellation = new CancellationTokenSource(); var pending = language.GetSelectionRangesAsync(snapshot, 1, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
        Assert.That(async () => await pending, Throws.InstanceOf<OperationCanceledException>());
        Assert.That((await language.GetSelectionRangesAsync(snapshot, 1)).Length, Is.EqualTo(1));
    }
}
