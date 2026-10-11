using System.Text.Json.Nodes;
using EditorScene.Protocol;
using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class LspSynchronizationTests
{
    [TestCase("abc", "aXbc", 1, 1, "X")]
    [TestCase("abc", "ac", 1, 2, "")]
    [TestCase("abc", "abc", 3, 3, "")]
    [TestCase("A🙂B", "A🙃B", 1, 3, "🙃")]
    [TestCase("", "日本語", 0, 0, "日本語")]
    public void MinimalChangesHaveSafeUtf16Boundaries(string before, string after, int start, int end, string inserted)
    {
        using var document = new EditorDocument(before); var original = document.Snapshot();
        document.ApplyEdits([new(0, before.Length, after)], document.Revision); var change = LspTextChange.Create(original, document.Snapshot());
        Assert.That(change["text"]!.GetValue<string>(), Is.EqualTo(inserted));
        Assert.That(change["range"]!["start"]!["character"]!.GetValue<int>(), Is.EqualTo(start));
        Assert.That(change["range"]!["end"]!["character"]!.GetValue<int>(), Is.EqualTo(end));
    }
    [Test]
    public void CrLfChangesNeverPointIntoTheMiddleOfALineDelimiter()
    {
        using var document = new EditorDocument("a\r\nb"); var before = document.Snapshot();
        document.ApplyEdits([new(1, 2, "\n")], document.Revision); var change = LspTextChange.Create(before, document.Snapshot());
        Assert.That(change["range"]!["start"]!.ToJsonString(), Is.EqualTo("{\"line\":0,\"character\":1}"));
        Assert.That(change["range"]!["end"]!.ToJsonString(), Is.EqualTo("{\"line\":1,\"character\":0}"));
        Assert.That(change["text"]!.GetValue<string>(), Is.EqualTo("\n"));
    }
    [TestCase(1, false)]
    [TestCase(2, true)]
    public async Task ClientUsesNegotiatedSynchronizationAndIncreasingVersions(int kind, bool ranged)
    {
        using var document = new EditorDocument("abc"); var first = document.Snapshot();
        document.ApplyEdits([new(1, 0, "🙂")], document.Revision); var next = document.Snapshot();
        await using var peer = new LspTestPeer { Capabilities = new() { ["textDocumentSync"] = kind } };
        await using var client = new LanguageServerClient(peer.Client); var uri = new Uri("file:///project/Test.cs");
        await client.InitializeAsync(); await peer.InitializedAsync();
        await client.OpenAsync(uri, "csharp", first); var opened = await peer.NextAsync("textDocument/didOpen");
        Assert.That(opened.Parameters!["textDocument"]!["version"]!.GetValue<int>(), Is.EqualTo(1));
        await client.ChangeAsync(uri, next); var changed = await peer.NextAsync("textDocument/didChange");
        Assert.That(changed.Parameters!["textDocument"]!["version"]!.GetValue<int>(), Is.EqualTo(2));
        var change = (JsonObject)changed.Parameters["contentChanges"]![0]!;
        Assert.That(change.ContainsKey("range"), Is.EqualTo(ranged));
        Assert.That(change["text"]!.GetValue<string>(), Is.EqualTo(ranged ? "🙂" : "a🙂bc"));
        await client.ChangeAsync(uri, next); // Duplicate revision is not sent.
        await client.CloseAsync(uri); await peer.NextAsync("textDocument/didClose");
        await client.ShutdownAsync(); await peer.NextAsync("shutdown"); await peer.NextAsync("exit");
    }
    [Test]
    public async Task NegotiatedSaveIncludesOnlyTheOpenedDocumentsCurrentText()
    {
        using var document = new EditorDocument("value"); var snapshot = document.Snapshot();
        await using var peer = new LspTestPeer { Capabilities = new() { ["textDocumentSync"] = new JsonObject { ["openClose"] = true, ["change"] = 1, ["save"] = new JsonObject { ["includeText"] = true } } } };
        await using var client = new LanguageServerClient(peer.Client); var uri = new Uri("untitled:sample");
        await client.InitializeAsync(); await peer.InitializedAsync(); await client.OpenAsync(uri, "plaintext", snapshot); await peer.NextAsync("textDocument/didOpen");
        await client.SaveAsync(uri); var saved = await peer.NextAsync("textDocument/didSave");
        Assert.That(saved.Parameters!["text"]!.GetValue<string>(), Is.EqualTo("value"));
    }
    [Test]
    public async Task StaleSnapshotsAndForeignDocumentsCannotOverwriteServerState()
    {
        using var document = new EditorDocument("old"); using var other = new EditorDocument("other");
        var old = document.Snapshot(); document.ApplyEdits([new(0, 3, "new")], document.Revision); var current = document.Snapshot(); var foreign = other.Snapshot();
        await using var peer = new LspTestPeer(); await using var client = new LanguageServerClient(peer.Client); var uri = new Uri("file:///test");
        await client.InitializeAsync(); await client.OpenAsync(uri, "plaintext", current);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await client.ChangeAsync(uri, old));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await client.ChangeAsync(uri, foreign));
    }
    [Test]
    public async Task UnsupportedEncodingIsRejectedBeforeInitializedNotification()
    {
        await using var peer = new LspTestPeer { Capabilities = new() { ["positionEncoding"] = "utf-8" } };
        await using var client = new LanguageServerClient(peer.Client);
        Assert.ThrowsAsync<NotSupportedException>(async () => await client.InitializeAsync());
        Assert.That(client.IsInitialized, Is.False);
    }
    [TestCase(-1)]
    [TestCase(3)]
    public void InvalidSyncKindsAreRejected(int kind) => Assert.Throws<InvalidDataException>(() => LspSynchronization.Read(JsonValue.Create(kind)));
    [Test]
    public void AbsentCapabilitiesDoNotInventOpenCloseOrSaveSupport()
    {
        Assert.That(LspSynchronization.Read(null), Is.EqualTo(new LspSynchronization(LspTextSyncKind.None, false, false, false)));
        Assert.That(LspSynchronization.Read(new JsonObject { ["change"] = 2 }), Is.EqualTo(new LspSynchronization(LspTextSyncKind.Incremental, false, false, false)));
    }
}
