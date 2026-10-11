using System.Text.Json.Nodes;
using System.Threading.Channels;
using EditorScene.Protocol;

namespace EditorScene.Tests;

internal sealed class LspTestPeer : IAsyncDisposable
{
    private readonly Channel<RpcNotification> _received = Channel.CreateUnbounded<RpcNotification>();
    internal JsonRpcConnection Client { get; }
    internal JsonRpcConnection Server { get; }
    internal JsonObject Capabilities { get; set; } = new() { ["textDocumentSync"] = 2 };
    internal Func<string, JsonNode?, CancellationToken, Task<JsonNode?>>? Request { get; set; }
    internal LspTestPeer()
    {
        var outgoing = Channel.CreateUnbounded<JsonObject>(); var incoming = Channel.CreateUnbounded<JsonObject>();
        Client = new(new Transport(incoming, outgoing)); Server = new(new Transport(outgoing, incoming));
        Server.Notification += (_, notification) => _received.Writer.TryWrite(notification);
        Server.RequestHandler = (method, parameters, token) =>
        {
            _received.Writer.TryWrite(new(method, parameters?.DeepClone()));
            if (method == "initialize") return Task.FromResult<JsonNode?>(new JsonObject { ["capabilities"] = Capabilities.DeepClone() });
            if (method == "shutdown") return Task.FromResult<JsonNode?>(null);
            return Request?.Invoke(method, parameters, token) ?? Task.FromResult<JsonNode?>(null);
        };
    }
    internal async Task<RpcNotification> NextAsync(string expected)
    {
        var message = await _received.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        NUnit.Framework.Assert.That(message.Method, NUnit.Framework.Is.EqualTo(expected)); return message;
    }
    internal async Task InitializedAsync()
    { await NextAsync("initialize"); await NextAsync("initialized"); }
    public async ValueTask DisposeAsync() { await Client.DisposeAsync(); await Server.DisposeAsync(); }
    private sealed class Transport(Channel<JsonObject> input, Channel<JsonObject> output) : IJsonRpcTransport
    {
        public async ValueTask<JsonObject?> ReadAsync(CancellationToken cancellationToken = default)
        { while (await input.Reader.WaitToReadAsync(cancellationToken)) if (input.Reader.TryRead(out var message)) return message; return null; }
        public ValueTask WriteAsync(JsonObject message, CancellationToken cancellationToken = default) => output.Writer.WriteAsync((JsonObject)message.DeepClone(), cancellationToken);
        public ValueTask DisposeAsync() { input.Writer.TryComplete(); output.Writer.TryComplete(); return ValueTask.CompletedTask; }
    }
}
