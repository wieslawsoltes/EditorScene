using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EditorScene.Protocol;

public interface IJsonRpcTransport : IAsyncDisposable
{
    ValueTask<JsonObject?> ReadAsync(CancellationToken cancellationToken = default);
    ValueTask WriteAsync(JsonObject message, CancellationToken cancellationToken = default);
}

/// <summary>LSP Content-Length framing. Lengths are UTF-8 bytes, not UTF-16 characters.</summary>
public sealed class StreamJsonRpcTransport(Stream input, Stream output, bool leaveOpen = false, int maxMessageBytes = 16 * 1024 * 1024) : IJsonRpcTransport
{
    private readonly SemaphoreSlim _writes = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    public async ValueTask<JsonObject?> ReadAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        cancellationToken = linked.Token;
        var header = new List<byte>(128); var single = new byte[1];
        while (true)
        {
            var read = await input.ReadAsync(single, cancellationToken).ConfigureAwait(false);
            if (read == 0) { if (header.Count == 0) return null; throw new EndOfStreamException("JSON-RPC header was truncated."); }
            header.Add(single[0]);
            if (header.Count > 8192) throw new InvalidDataException("JSON-RPC header exceeds 8192 bytes.");
            var n = header.Count;
            if (n >= 4 && header[n - 4] == 13 && header[n - 3] == 10 && header[n - 2] == 13 && header[n - 1] == 10) break;
        }
        int? length = null;
        foreach (var line in Encoding.ASCII.GetString(header.ToArray()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = line.IndexOf(':'); if (colon <= 0) throw new InvalidDataException("Malformed JSON-RPC header.");
            var name = line[..colon].Trim(); var value = line[(colon + 1)..].Trim();
            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                if (length is not null || !int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed) || parsed <= 0 || parsed > maxMessageBytes)
                    throw new InvalidDataException("Invalid, duplicate or oversized Content-Length.");
                length = parsed;
            }
            else if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) && value.Contains("charset=", StringComparison.OrdinalIgnoreCase) && !value.Contains("charset=utf-8", StringComparison.OrdinalIgnoreCase) && !value.Contains("charset=utf8", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("LSP messages must use UTF-8.");
        }
        if (length is null) throw new InvalidDataException("Content-Length is required.");
        var body = new byte[length.Value];
        await input.ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false);
        return JsonNode.Parse(body, documentOptions: new JsonDocumentOptions { MaxDepth = 128 }) as JsonObject ?? throw new InvalidDataException("A JSON-RPC message must be an object.");
    }
    public async ValueTask WriteAsync(JsonObject message, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(message);
        var body = Encoding.UTF8.GetBytes(message.ToJsonString());
        if (body.Length > maxMessageBytes) throw new InvalidDataException("JSON-RPC message exceeds the configured limit.");
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        await _writes.WaitAsync(cancellationToken).ConfigureAwait(false);
        var beganFrame = false;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            beganFrame = true;
            // Once a frame begins, caller cancellation must not leave a half-header/body on the stream.
            await output.WriteAsync(header, _lifetime.Token).ConfigureAwait(false);
            await output.WriteAsync(body, _lifetime.Token).ConfigureAwait(false);
            await output.FlushAsync(_lifetime.Token).ConfigureAwait(false);
        }
        catch { if (beganFrame) _lifetime.Cancel(); throw; }
        finally { _writes.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return; _disposed = true; _lifetime.Cancel();
        if (!leaveOpen) { await input.DisposeAsync().ConfigureAwait(false); if (!ReferenceEquals(input, output)) await output.DisposeAsync().ConfigureAwait(false); }
        // Do not dispose the semaphore while a cancelled writer may still release it.
    }
}

/// <summary>One JSON-RPC object per WebSocket message, including fragmented frames.</summary>
public sealed class WebSocketJsonRpcTransport(WebSocket socket, bool leaveOpen = false, int maxMessageBytes = 16 * 1024 * 1024) : IJsonRpcTransport
{
    private readonly SemaphoreSlim _writes = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    public async ValueTask<JsonObject?> ReadAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        cancellationToken = linked.Token;
        using var message = new MemoryStream(); var buffer = new byte[8192];
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType != WebSocketMessageType.Text) throw new InvalidDataException("JSON-RPC WebSocket frames must be text.");
            if (message.Length + result.Count > maxMessageBytes) throw new InvalidDataException("WebSocket message exceeds the configured limit.");
            message.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) break;
        }
        return JsonNode.Parse(message.GetBuffer().AsSpan(0, checked((int)message.Length))) as JsonObject ?? throw new InvalidDataException("A JSON-RPC message must be an object.");
    }
    public async ValueTask WriteAsync(JsonObject message, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var bytes = Encoding.UTF8.GetBytes(message.ToJsonString());
        if (bytes.Length > maxMessageBytes) throw new InvalidDataException("WebSocket message exceeds the configured limit.");
        await _writes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this); cancellationToken.ThrowIfCancellationRequested();
            await socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, _lifetime.Token).ConfigureAwait(false);
        }
        finally { _writes.Release(); }
    }
    public ValueTask DisposeAsync() { if (!_disposed) { _disposed = true; _lifetime.Cancel(); if (!leaveOpen) socket.Dispose(); } return ValueTask.CompletedTask; }
}
