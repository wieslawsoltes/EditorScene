using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using EditorScene.Protocol;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class ProtocolTests
{
    [Test]
    public async Task FragmentedUtf8FramesRoundTripWithoutCharacterLengthConfusion()
    {
        using var bytes = new MemoryStream();
        await using (var writer = new StreamJsonRpcTransport(Stream.Null, bytes, leaveOpen: true))
            await writer.WriteAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "test", ["params"] = "🙂 Żółć 日本語" });
        bytes.Position = 0;
        await using var reader = new StreamJsonRpcTransport(new FragmentedStream(bytes), Stream.Null, leaveOpen: true);
        var message = await reader.ReadAsync();
        Assert.That(message?["params"]?.GetValue<string>(), Is.EqualTo("🙂 Żółć 日本語"));
        Assert.That(await reader.ReadAsync(), Is.Null);
    }
    [TestCase("Content-Length: 5\r\nContent-Length: 5\r\n\r\n{}")]
    [TestCase("Content-Length: -1\r\n\r\n")]
    [TestCase("Content-Length: 999999999\r\n\r\n")]
    [TestCase("Missing: 2\r\n\r\n{}")]
    [TestCase("Content-Length: 2\r\nContent-Type: application/vscode-jsonrpc; charset=utf-16\r\n\r\n{}")]
    public void InvalidFramingIsRejected(string input)
    {
        using var source = new MemoryStream(Encoding.ASCII.GetBytes(input));
        var reader = new StreamJsonRpcTransport(source, Stream.Null, true);
        Assert.ThrowsAsync<InvalidDataException>(async () => await reader.ReadAsync());
    }
    [Test]
    public void TruncatedBodiesAreRejected()
    {
        using var source = new MemoryStream(Encoding.ASCII.GetBytes("Content-Length: 10\r\n\r\n{}"));
        var reader = new StreamJsonRpcTransport(source, Stream.Null, true);
        Assert.ThrowsAsync<EndOfStreamException>(async () => await reader.ReadAsync());
    }
    [Test]
    public async Task CancellationAfterHeaderDoesNotCorruptNextFrame()
    {
        using var cancellation = new CancellationTokenSource();
        using var output = new CancelAfterFirstWrite(cancellation);
        await using var transport = new StreamJsonRpcTransport(Stream.Null, output, true);
        await transport.WriteAsync(new JsonObject { ["value"] = "first" }, cancellation.Token);
        await transport.WriteAsync(new JsonObject { ["value"] = "second" });
        output.Position = 0;
        await using var reader = new StreamJsonRpcTransport(output, Stream.Null, true);
        Assert.That((await reader.ReadAsync())?["value"]?.GetValue<string>(), Is.EqualTo("first"));
        Assert.That((await reader.ReadAsync())?["value"]?.GetValue<string>(), Is.EqualTo("second"));
    }
    [Test]
    public async Task RequestsCorrelateOutOfOrderAndDoNotConflateNumericAndStringIds()
    {
        var transport = new QueuedTransport(); await using var rpc = new JsonRpcConnection(transport);
        var first = rpc.RequestAsync("first"); var second = rpc.RequestAsync("second");
        var a = await transport.Sent.Reader.ReadAsync(); var b = await transport.Sent.Reader.ReadAsync();
        await transport.Incoming.Writer.WriteAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = long.Parse(a["id"]!.GetValue<string>()), ["result"] = "wrong-type" });
        await transport.Incoming.Writer.WriteAsync(Reply(b, "second-result"));
        Assert.That((await second.WaitAsync(TimeSpan.FromSeconds(5)))?.GetValue<string>(), Is.EqualTo("second-result"));
        Assert.That(first.IsCompleted, Is.False);
        await transport.Incoming.Writer.WriteAsync(Reply(a, "first-result"));
        Assert.That((await first.WaitAsync(TimeSpan.FromSeconds(5)))?.GetValue<string>(), Is.EqualTo("first-result"));
    }
    [Test]
    public async Task CancellationNotifiesPeerAndDisconnectFailsPendingRequests()
    {
        var transport = new QueuedTransport(); await using var rpc = new JsonRpcConnection(transport);
        using var cancellation = new CancellationTokenSource();
        var request = rpc.RequestAsync("slow", cancellationToken: cancellation.Token);
        var outgoing = await transport.Sent.Reader.ReadAsync(); cancellation.Cancel();
        Assert.ThrowsAsync<TaskCanceledException>(async () => await request);
        var notification = await transport.Sent.Reader.ReadAsync();
        Assert.That(notification["method"]?.GetValue<string>(), Is.EqualTo("$/cancelRequest"));
        Assert.That(notification["params"]?["id"]?.ToJsonString(), Is.EqualTo(outgoing["id"]?.ToJsonString()));
        var next = rpc.RequestAsync("lost"); await transport.Sent.Reader.ReadAsync(); transport.Incoming.Writer.TryComplete();
        Assert.ThrowsAsync<EndOfStreamException>(async () => await next);
    }
    [Test]
    public async Task BidirectionalRequestsReturnResultsAndMethodNotFound()
    {
        var transport = new QueuedTransport(); await using var rpc = new JsonRpcConnection(transport);
        rpc.RequestHandler = (method, parameters, _) => method == "echo" ? Task.FromResult(parameters?.DeepClone()) : throw new JsonRpcException(-32601, "Unknown method");
        await transport.Incoming.Writer.WriteAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 7, ["method"] = "echo", ["params"] = "hello" });
        var result = await transport.Sent.Reader.ReadAsync(); Assert.That(result["result"]?.GetValue<string>(), Is.EqualTo("hello"));
        await transport.Incoming.Writer.WriteAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = "7", ["method"] = "missing" });
        var error = await transport.Sent.Reader.ReadAsync(); Assert.That(error["error"]?["code"]?.GetValue<int>(), Is.EqualTo(-32601));
    }
    private static JsonObject Reply(JsonObject request, string value) => new() { ["jsonrpc"] = "2.0", ["id"] = request["id"]!.DeepClone(), ["result"] = value };
    private sealed class FragmentedStream(Stream inner) : Stream
    {
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => inner.Length; public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, Math.Min(1, count));
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);
        public override void Flush() => throw new NotSupportedException(); public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private sealed class CancelAfterFirstWrite(CancellationTokenSource cancellation) : MemoryStream
    {
        private int _writes;
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        { await base.WriteAsync(buffer, cancellationToken); if (++_writes == 1) cancellation.Cancel(); }
    }
    internal sealed class QueuedTransport : IJsonRpcTransport
    {
        internal Channel<JsonObject> Incoming { get; } = Channel.CreateUnbounded<JsonObject>();
        internal Channel<JsonObject> Sent { get; } = Channel.CreateUnbounded<JsonObject>();
        public async ValueTask<JsonObject?> ReadAsync(CancellationToken cancellationToken = default)
        { while (await Incoming.Reader.WaitToReadAsync(cancellationToken)) if (Incoming.Reader.TryRead(out var value)) return value; return null; }
        public ValueTask WriteAsync(JsonObject message, CancellationToken cancellationToken = default) => Sent.Writer.WriteAsync((JsonObject)message.DeepClone(), cancellationToken);
        public ValueTask DisposeAsync() { Incoming.Writer.TryComplete(); Sent.Writer.TryComplete(); return ValueTask.CompletedTask; }
    }
}
