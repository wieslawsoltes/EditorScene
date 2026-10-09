using System.Text.Json.Nodes;
using EditorScene.Text;

namespace EditorScene.Protocol;

/// <summary>
/// LSP 3.17 client lifecycle and document/request surface. Hosts explicitly connect
/// a stream or WebSocket; no credentials, files or source are transmitted implicitly.
/// </summary>
public sealed class LanguageServerClient : IAsyncDisposable
{
    private readonly JsonRpcConnection _rpc;
    private readonly Dictionary<Uri, (int Version, Guid Id, long Revision)> _openDocuments = new();
    private readonly SemaphoreSlim _writes = new(1, 1);
    private volatile bool _disposed;
    private volatile bool _initialized;
    public LanguageServerClient(JsonRpcConnection connection)
    {
        _rpc = connection ?? throw new ArgumentNullException(nameof(connection));
        _rpc.Notification += OnNotification;
    }
    public JsonObject ServerCapabilities { get; private set; } = new();
    public event EventHandler<RpcNotification>? Notification;
    private void OnNotification(object? sender, RpcNotification notification) => Notification?.Invoke(this, notification);
    public async Task InitializeAsync(Uri? rootUri = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _writes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_initialized) throw new InvalidOperationException("The client is already initialized.");
            var parameters = new JsonObject
            {
                ["processId"] = null, ["rootUri"] = rootUri?.AbsoluteUri,
                ["clientInfo"] = new JsonObject { ["name"] = "EditorScene", ["version"] = "0.1.0-alpha.1" },
                ["capabilities"] = new JsonObject
                {
                    ["general"] = new JsonObject { ["positionEncodings"] = new JsonArray("utf-16") },
                    ["textDocument"] = new JsonObject
                    {
                        ["synchronization"] = new JsonObject { ["didSave"] = true },
                        ["completion"] = new JsonObject { ["completionItem"] = new JsonObject { ["snippetSupport"] = false, ["documentationFormat"] = new JsonArray("plaintext") } },
                        ["hover"] = new JsonObject { ["contentFormat"] = new JsonArray("plaintext") },
                        ["documentSymbol"] = new JsonObject { ["hierarchicalDocumentSymbolSupport"] = true },
                        ["publishDiagnostics"] = new JsonObject { ["versionSupport"] = true }
                    }
                }
            };
            var result = await _rpc.RequestAsync("initialize", parameters, cancellationToken).ConfigureAwait(false);
            ServerCapabilities = result?["capabilities"]?.DeepClone() as JsonObject ?? new();
            if (ServerCapabilities["positionEncoding"]?.GetValue<string>() is { } encoding && encoding != "utf-16") throw new NotSupportedException($"The server selected unsupported positions: {encoding}.");
            await _rpc.NotifyAsync("initialized", new JsonObject(), cancellationToken).ConfigureAwait(false); _initialized = true;
        }
        finally { _writes.Release(); }
    }
    public async Task OpenAsync(Uri uri, string languageId, DocumentSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _writes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureReady(); if (_openDocuments.ContainsKey(uri)) throw new InvalidOperationException("The document is already open.");
            await _rpc.NotifyAsync("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri.AbsoluteUri, ["languageId"] = languageId, ["version"] = 1, ["text"] = snapshot.Text } }, cancellationToken).ConfigureAwait(false);
            _openDocuments[uri] = (1, snapshot.DocumentId, snapshot.Revision);
        }
        finally { _writes.Release(); }
    }
    public async Task ChangeAsync(Uri uri, DocumentSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _writes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureReady(); if (!_openDocuments.TryGetValue(uri, out var current)) throw new InvalidOperationException("Open the document before sending changes.");
            if (snapshot.DocumentId != current.Id || snapshot.Revision < current.Revision) throw new InvalidOperationException("The snapshot does not belong to the latest open document revision.");
            if (snapshot.Revision == current.Revision) return;
            await _rpc.NotifyAsync("textDocument/didChange", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri.AbsoluteUri, ["version"] = checked(current.Version + 1) }, ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = snapshot.Text }) }, cancellationToken).ConfigureAwait(false);
            _openDocuments[uri] = (checked(current.Version + 1), snapshot.DocumentId, snapshot.Revision);
        }
        finally { _writes.Release(); }
    }
    public async Task CloseAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _writes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureReady(); if (!_openDocuments.ContainsKey(uri)) return;
            await _rpc.NotifyAsync("textDocument/didClose", DocumentParameters(uri), cancellationToken).ConfigureAwait(false); _openDocuments.Remove(uri);
        }
        finally { _writes.Release(); }
    }
    public Task<JsonNode?> CompletionAsync(Uri uri, TextPosition position, CancellationToken cancellationToken = default) => PositionRequest("textDocument/completion", uri, position, cancellationToken);
    public Task<JsonNode?> HoverAsync(Uri uri, TextPosition position, CancellationToken cancellationToken = default) => PositionRequest("textDocument/hover", uri, position, cancellationToken);
    public Task<JsonNode?> SignatureHelpAsync(Uri uri, TextPosition position, CancellationToken cancellationToken = default) => PositionRequest("textDocument/signatureHelp", uri, position, cancellationToken);
    public Task<JsonNode?> DefinitionAsync(Uri uri, TextPosition position, CancellationToken cancellationToken = default) => PositionRequest("textDocument/definition", uri, position, cancellationToken);
    public Task<JsonNode?> SymbolsAsync(Uri uri, CancellationToken cancellationToken = default) => Request("textDocument/documentSymbol", DocumentParameters(uri), cancellationToken);
    public Task<JsonNode?> FoldingAsync(Uri uri, CancellationToken cancellationToken = default) => Request("textDocument/foldingRange", DocumentParameters(uri), cancellationToken);
    public Task<JsonNode?> FormatAsync(Uri uri, int tabSize = 4, bool insertSpaces = true, CancellationToken cancellationToken = default)
    {
        if (tabSize < 1 || tabSize > 32) throw new ArgumentOutOfRangeException(nameof(tabSize));
        var parameters = DocumentParameters(uri); parameters["options"] = new JsonObject { ["tabSize"] = tabSize, ["insertSpaces"] = insertSpaces };
        return Request("textDocument/formatting", parameters, cancellationToken);
    }
    private Task<JsonNode?> PositionRequest(string method, Uri uri, TextPosition position, CancellationToken token)
    {
        if (position.Line < 0 || position.Character < 0) throw new ArgumentOutOfRangeException(nameof(position));
        var parameters = DocumentParameters(uri); parameters["position"] = new JsonObject { ["line"] = position.Line, ["character"] = position.Character }; return Request(method, parameters, token);
    }
    private Task<JsonNode?> Request(string method, JsonObject parameters, CancellationToken token) { EnsureReady(); return _rpc.RequestAsync(method, parameters, token); }
    private static JsonObject DocumentParameters(Uri uri) => new() { ["textDocument"] = new JsonObject { ["uri"] = uri.AbsoluteUri } };
    private void EnsureReady() { ObjectDisposedException.ThrowIf(_disposed, this); if (!_initialized) throw new InvalidOperationException("Initialize the LSP client first."); }
    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _writes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_initialized) return;
            await _rpc.RequestAsync("shutdown", cancellationToken: cancellationToken).ConfigureAwait(false);
            await _rpc.NotifyAsync("exit", cancellationToken: cancellationToken).ConfigureAwait(false); _initialized = false; _openDocuments.Clear();
        }
        finally { _writes.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return; _disposed = true;
        _rpc.Notification -= OnNotification; await _rpc.DisposeAsync().ConfigureAwait(false);
        await _writes.WaitAsync().ConfigureAwait(false);
        try { _initialized = false; _openDocuments.Clear(); Notification = null; }
        finally { _writes.Release(); }
    }
}
