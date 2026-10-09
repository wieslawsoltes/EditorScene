using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace EditorScene.Protocol;

public sealed class JsonRpcException(int code, string message, JsonNode? errorData = null) : Exception(message)
{
    public int Code { get; } = code;
    public JsonNode? ErrorData { get; } = errorData;
}
public sealed record RpcNotification(string Method, JsonNode? Parameters);

/// <summary>Concurrent JSON-RPC 2.0 requests with bidirectional cancellation and deterministic disconnect failures.</summary>
public sealed class JsonRpcConnection : IAsyncDisposable
{
    private readonly IJsonRpcTransport _transport;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonNode?>> _pending = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _inbound = new();
    private readonly ConcurrentDictionary<long, Task> _handlers = new();
    private readonly Task _reader;
    private long _nextId;
    private long _nextHandler;
    private bool _disposed;
    public JsonRpcConnection(IJsonRpcTransport transport) { _transport = transport ?? throw new ArgumentNullException(nameof(transport)); _reader = ReadLoopAsync(); }
    public event EventHandler<RpcNotification>? Notification;
    public event EventHandler<Exception>? Disconnected;
    public Func<string, JsonNode?, CancellationToken, Task<JsonNode?>>? RequestHandler { get; set; }
    public Task Completion => _reader;
    public async Task<JsonNode?> RequestAsync(string method, JsonNode? parameters = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); cancellationToken.ThrowIfCancellationRequested(); _lifetime.Token.ThrowIfCancellationRequested();
        if (_pending.Count >= 1024) throw new InvalidOperationException("The connection has too many outstanding requests.");
        var id = Interlocked.Increment(ref _nextId).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var completion = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd("s:" + id, completion)) throw new InvalidOperationException("Duplicate request ID.");
        try
        {
            await _transport.WriteAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters?.DeepClone() }, cancellationToken).ConfigureAwait(false);
            try { return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                try { await NotifyAsync("$/cancelRequest", new JsonObject { ["id"] = id }, _lifetime.Token).ConfigureAwait(false); }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
                throw;
            }
        }
        finally { _pending.TryRemove("s:" + id, out _); }
    }
    public ValueTask NotifyAsync(string method, JsonNode? parameters = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _transport.WriteAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = parameters?.DeepClone() }, cancellationToken);
    }
    private async Task ReadLoopAsync()
    {
        Exception failure = new EndOfStreamException("The language server disconnected.");
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                var message = await _transport.ReadAsync(_lifetime.Token).ConfigureAwait(false); if (message is null) break;
                if (message["jsonrpc"]?.GetValue<string>() != "2.0") throw new InvalidDataException("The peer did not send JSON-RPC 2.0.");
                var method = message["method"]?.GetValue<string>(); var id = message["id"];
                if (method is null)
                {
                    if (id is null || !_pending.TryRemove(IdKey(id), out var pending)) continue;
                    if (message["error"] is JsonObject error) pending.TrySetException(new JsonRpcException(error["code"]?.GetValue<int>() ?? -32603, error["message"]?.GetValue<string>() ?? "Remote error", error["data"]?.DeepClone()));
                    else if (message.ContainsKey("result")) pending.TrySetResult(message["result"]?.DeepClone());
                    else pending.TrySetException(new InvalidDataException("JSON-RPC response contains neither result nor error."));
                }
                else if (method == "$/cancelRequest")
                { if (message["params"]?["id"] is { } cancelled && _inbound.TryGetValue(IdKey(cancelled), out var source)) source.Cancel(); }
                else if (id is null) Notification?.Invoke(this, new(method, message["params"]?.DeepClone()));
                else
                {
                    var key = Interlocked.Increment(ref _nextHandler);
                    var task = RespondAsync(id.DeepClone(), method, message["params"]?.DeepClone()); _handlers[key] = task;
                    _ = task.ContinueWith(_ => _handlers.TryRemove(key, out var ignored), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { failure = new OperationCanceledException("The connection was closed."); }
        catch (Exception exception) { failure = exception; }
        finally
        {
            _lifetime.Cancel();
            foreach (var pair in _pending) if (_pending.TryRemove(pair.Key, out var pending)) pending.TrySetException(failure);
            foreach (var source in _inbound.Values) source.Cancel();
            if (!_disposed) Disconnected?.Invoke(this, failure);
        }
    }
    private async Task RespondAsync(JsonNode id, string method, JsonNode? parameters)
    {
        var key = IdKey(id); using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        if (!_inbound.TryAdd(key, cancellation)) { await WriteErrorAsync(id, -32600, "Duplicate active request ID.", null).ConfigureAwait(false); return; }
        try
        {
            if (RequestHandler is null) throw new JsonRpcException(-32601, $"Method '{method}' is not supported.");
            var result = await RequestHandler(method, parameters, cancellation.Token).ConfigureAwait(false);
            await _transport.WriteAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["result"] = result?.DeepClone() }, _lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { await WriteErrorAsync(id, -32800, "Request cancelled.", null).ConfigureAwait(false); }
        catch (JsonRpcException exception) { await WriteErrorAsync(id, exception.Code, exception.Message, exception.ErrorData).ConfigureAwait(false); }
        catch (Exception) { await WriteErrorAsync(id, -32603, "The request handler failed.", null).ConfigureAwait(false); }
        finally { _inbound.TryRemove(key, out _); }
    }
    private async Task WriteErrorAsync(JsonNode id, int code, string message, JsonNode? data)
    {
        if (_lifetime.IsCancellationRequested) return;
        try { await _transport.WriteAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["error"] = new JsonObject { ["code"] = code, ["message"] = message, ["data"] = data?.DeepClone() } }, _lifetime.Token).ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException) { _lifetime.Cancel(); }
    }
    private static string IdKey(JsonNode id) => id is JsonValue value && value.TryGetValue<string>(out var text) ? "s:" + text : "n:" + id.ToJsonString();
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return; _disposed = true; _lifetime.Cancel(); await _transport.DisposeAsync().ConfigureAwait(false);
        await _reader.ConfigureAwait(false); await Task.WhenAll(_handlers.Values).ConfigureAwait(false);
        Notification = null; Disconnected = null; _lifetime.Dispose();
    }
}
