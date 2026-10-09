using System.Text.Json.Nodes;
using EditorScene.Protocol;
using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class LanguageServerTests
{
    [Test]
    public async Task ClientLifecyclePreservesVersionsAndUtf16Positions()
    {
        var transport = new ProtocolTests.QueuedTransport();
        await using var client = new LanguageServerClient(new JsonRpcConnection(transport));
        var init = client.InitializeAsync(); var request = await transport.Sent.Reader.ReadAsync();
        Assert.That(request["method"]?.GetValue<string>(), Is.EqualTo("initialize"));
        Assert.That(request["params"]?["capabilities"]?["general"]?["positionEncodings"]?[0]?.GetValue<string>(), Is.EqualTo("utf-16"));
        await transport.Incoming.Writer.WriteAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = request["id"]!.DeepClone(), ["result"] = new JsonObject { ["capabilities"] = new JsonObject { ["positionEncoding"] = "utf-16" } } });
        await init; Assert.That((await transport.Sent.Reader.ReadAsync())["method"]?.GetValue<string>(), Is.EqualTo("initialized"));
        using var document = new EditorDocument("🙂x"); var original = document.Snapshot();
        document.Buffer.Insert(3, "y"); var changed = document.Snapshot();
        var uri = new Uri("file:///workspace/sample.cs");
        await client.OpenAsync(uri, "csharp", original); var opened = await transport.Sent.Reader.ReadAsync();
        Assert.That(opened["params"]?["textDocument"]?["version"]?.GetValue<int>(), Is.EqualTo(1));
        await client.ChangeAsync(uri, changed); var change = await transport.Sent.Reader.ReadAsync();
        Assert.That(change["params"]?["textDocument"]?["version"]?.GetValue<int>(), Is.EqualTo(2));
        Assert.That(change["params"]?["contentChanges"]?[0]?["text"]?.GetValue<string>(), Is.EqualTo("🙂xy"));
        Assert.ThrowsAsync<InvalidOperationException>(() => client.ChangeAsync(uri, original));
        var hover = client.HoverAsync(uri, new(0, 2)); var hoverRequest = await transport.Sent.Reader.ReadAsync();
        Assert.That(hoverRequest["params"]?["position"]?["character"]?.GetValue<int>(), Is.EqualTo(2));
        await transport.Incoming.Writer.WriteAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = hoverRequest["id"]!.DeepClone(), ["result"] = null }); await hover;
        await client.CloseAsync(uri); Assert.That((await transport.Sent.Reader.ReadAsync())["method"]?.GetValue<string>(), Is.EqualTo("textDocument/didClose"));
        var shutdown = client.ShutdownAsync(); var stop = await transport.Sent.Reader.ReadAsync();
        await transport.Incoming.Writer.WriteAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = stop["id"]!.DeepClone(), ["result"] = null });
        await shutdown; Assert.That((await transport.Sent.Reader.ReadAsync())["method"]?.GetValue<string>(), Is.EqualTo("exit"));
    }
}
