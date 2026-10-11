using System.Text.Json.Nodes;
using EditorScene.LanguageServices;
using EditorScene.Protocol;
using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class LspProviderTests
{
    private static readonly Uri Uri = new("file:///project/test.cs");
    private static TaskCompletionSource<T> Source<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static JsonObject Diagnostic(string code) => new() { ["range"] = LspTextChange.Range(new(0, 0), new(0, 1)), ["code"] = code, ["message"] = "issue", ["severity"] = 1 };
    [Test]
    public async Task ProviderMapsAdvertisedFeaturesAndClosesItsOwnedDocument()
    {
        using var document = new EditorDocument("value"); var snapshot = document.Snapshot();
        await using var peer = new LspTestPeer { Capabilities = new() { ["textDocumentSync"] = 2, ["completionProvider"] = new JsonObject(), ["hoverProvider"] = true } };
        peer.Request = (method, _, _) => Task.FromResult<JsonNode?>(method switch
        { "textDocument/completion" => new JsonArray(new JsonObject { ["label"] = "valueResult" }), "textDocument/hover" => new JsonObject { ["contents"] = "int value" }, _ => null });
        var language = await LspSyntaxLanguage.OpenAsync(peer.Client, snapshot, Uri, "csharp");
        await peer.InitializedAsync(); await peer.NextAsync("textDocument/didOpen");
        Assert.That(language.Capabilities.HasFlag(LanguageCapabilities.Completion | LanguageCapabilities.QuickInfo), Is.True);
        Assert.That((await language.CompleteAsync(snapshot, 5)).Single().Label, Is.EqualTo("valueResult")); await peer.NextAsync("textDocument/completion");
        Assert.That((await language.GetQuickInfoAsync(snapshot, 2))!.Signature, Is.EqualTo("int value")); await peer.NextAsync("textDocument/hover");
        Assert.That(await language.FormatAsync(snapshot), Is.Empty); // No unadvertised request.
        await language.DisposeAsync(); await peer.NextAsync("textDocument/didClose"); await peer.NextAsync("shutdown"); await peer.NextAsync("exit");
        await language.DisposeAsync(); Assert.That(language.IsDisposed, Is.True);
    }
    [Test]
    public async Task SnapshotLeasePreventsNewerChangesFromRacingOlderResponses()
    {
        using var document = new EditorDocument("old"); var before = document.Snapshot(); document.ApplyEdits([new(0, 3, "new")], document.Revision); var after = document.Snapshot();
        await using var peer = new LspTestPeer { Capabilities = new() { ["textDocumentSync"] = 2, ["completionProvider"] = new JsonObject() } };
        var release = Source<bool>(); var receivedVersion = 1;
        peer.Server.Notification += (_, notification) => { if (notification.Method == "textDocument/didChange") receivedVersion = notification.Parameters!["textDocument"]!["version"]!.GetValue<int>(); };
        peer.Request = async (_, _, token) => { await release.Task.WaitAsync(token); Assert.That(receivedVersion, Is.EqualTo(1)); return new JsonArray(); };
        await using var language = await LspSyntaxLanguage.OpenAsync(peer.Client, before, Uri, "csharp"); await peer.InitializedAsync(); await peer.NextAsync("textDocument/didOpen");
        var oldRequest = language.CompleteAsync(before, 3); await peer.NextAsync("textDocument/completion");
        var newRequest = language.AnalyzeAsync(after); Assert.That(newRequest.IsCompleted, Is.False);
        release.TrySetResult(true); await oldRequest; await newRequest; await peer.NextAsync("textDocument/didChange"); Assert.That(language.ServerVersion, Is.EqualTo(2));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await language.AnalyzeAsync(before));
    }
    [Test]
    public async Task VersionedDiagnosticsInvalidateOnlyTheirExactSnapshot()
    {
        using var document = new EditorDocument("old"); var before = document.Snapshot(); document.ApplyEdits([new(0, 3, "new")], document.Revision); var after = document.Snapshot();
        await using var peer = new LspTestPeer(); await using var language = await LspSyntaxLanguage.OpenAsync(peer.Client, before, Uri, "csharp");
        var changed = Source<LanguageAnalysisInvalidated>(); language.AnalysisInvalidated += (_, e) => changed.TrySetResult(e);
        async Task Publish(int? version, string code)
        {
            var parameters = new JsonObject { ["uri"] = Uri.AbsoluteUri, ["diagnostics"] = new JsonArray(Diagnostic(code)) };
            if (version is not null) parameters["version"] = version.Value;
            await peer.Server.NotifyAsync("textDocument/publishDiagnostics", parameters);
        }
        await Publish(null, "UNVERSIONED"); await Publish(2, "FUTURE"); await Publish(1, "CURRENT");
        Assert.That((await changed.Task.WaitAsync(TimeSpan.FromSeconds(5))).Revision, Is.EqualTo(before.Revision));
        Assert.That((await language.AnalyzeAsync(before)).Diagnostics.Single().Code, Is.EqualTo("CURRENT"));
        Assert.That(language.IgnoredDiagnosticNotifications, Is.EqualTo(2));
        Assert.That((await language.AnalyzeAsync(after)).Diagnostics, Is.Empty);
        changed = Source<LanguageAnalysisInvalidated>(); await Publish(1, "STALE"); await Publish(2, "NEW");
        Assert.That((await changed.Task.WaitAsync(TimeSpan.FromSeconds(5))).Revision, Is.EqualTo(after.Revision));
        Assert.That((await language.AnalyzeAsync(after)).Diagnostics.Single().Code, Is.EqualTo("NEW"));
        Assert.That(language.IgnoredDiagnosticNotifications, Is.EqualTo(3));
    }
    [Test]
    public async Task PullDiagnosticsReuseExplicitResultIdsAndRejectUnsolicitedUnchangedReports()
    {
        using var document = new EditorDocument("x"); var snapshot = document.Snapshot();
        await using var peer = new LspTestPeer { Capabilities = new() { ["textDocumentSync"] = 1, ["diagnosticProvider"] = new JsonObject { ["identifier"] = "test", ["interFileDependencies"] = false, ["workspaceDiagnostics"] = false } } };
        var calls = 0;
        peer.Request = (_, parameters, _) =>
        {
            Assert.That(parameters!["identifier"]!.GetValue<string>(), Is.EqualTo("test"));
            if (++calls == 1) return Task.FromResult<JsonNode?>(new JsonObject { ["kind"] = "full", ["resultId"] = "r1", ["items"] = new JsonArray(Diagnostic("PULL")) });
            Assert.That(parameters["previousResultId"]!.GetValue<string>(), Is.EqualTo("r1"));
            return Task.FromResult<JsonNode?>(new JsonObject { ["kind"] = "unchanged", ["resultId"] = "r1" });
        };
        await using var language = await LspSyntaxLanguage.OpenAsync(peer.Client, snapshot, Uri, "csharp");
        Assert.That((await language.AnalyzeAsync(snapshot)).Diagnostics.Single().Code, Is.EqualTo("PULL"));
        Assert.That((await language.AnalyzeAsync(snapshot)).Diagnostics.Single().Code, Is.EqualTo("PULL"));
        Assert.That(calls, Is.EqualTo(2));
    }
    [Test]
    public async Task CancellationReleasesSnapshotLeaseAndDoesNotPoisonTheNextRequest()
    {
        using var document = new EditorDocument("x"); var snapshot = document.Snapshot();
        await using var peer = new LspTestPeer { Capabilities = new() { ["textDocumentSync"] = 2, ["completionProvider"] = new JsonObject() } };
        peer.Request = async (_, _, token) => { await Task.Delay(Timeout.Infinite, token); return null; };
        await using var language = await LspSyntaxLanguage.OpenAsync(peer.Client, snapshot, Uri, "csharp"); await peer.InitializedAsync(); await peer.NextAsync("textDocument/didOpen");
        using var cancellation = new CancellationTokenSource(); var request = language.CompleteAsync(snapshot, 1, cancellation.Token); await peer.NextAsync("textDocument/completion");
        cancellation.Cancel(); Assert.That(async () => await request, Throws.InstanceOf<OperationCanceledException>());
        Assert.That((await language.AnalyzeAsync(snapshot)).Revision, Is.EqualTo(snapshot.Revision));
    }
    [Test]
    public async Task RefusesLiveEditingWhenServerCannotSynchronizeAndRejectsForeignDocuments()
    {
        using var document = new EditorDocument("x"); using var other = new EditorDocument("y"); var snapshot = document.Snapshot(); var foreign = other.Snapshot();
        await using var peer = new LspTestPeer { Capabilities = new() };
        Assert.ThrowsAsync<NotSupportedException>(async () => await LspSyntaxLanguage.OpenAsync(peer.Client, snapshot, Uri, "csharp"));
        await using var working = new LspTestPeer(); await using var language = await LspSyntaxLanguage.OpenAsync(working.Client, snapshot, Uri, "csharp");
        Assert.ThrowsAsync<InvalidOperationException>(async () => await language.AnalyzeAsync(foreign));
    }
}
