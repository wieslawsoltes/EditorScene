using System.Text.Json.Nodes;
using EditorScene.Text;

namespace EditorScene.Protocol;

/// <summary>LSP lifecycle and transport surface. No connection, file, credential or document is opened implicitly.</summary>
public sealed class LanguageServerClient : IAsyncDisposable
{
    private readonly JsonRpcConnection _rpc;
    private readonly Dictionary<Uri, OpenDocument> _documents = [];
    private readonly SemaphoreSlim _writes = new(1, 1);
    private volatile bool _disposed, _initialized;
    private sealed record OpenDocument(int Version, DocumentSnapshot Snapshot);
    public LanguageServerClient(JsonRpcConnection connection)
    { _rpc = connection ?? throw new ArgumentNullException(nameof(connection)); _rpc.Notification += OnNotification; }
    public bool IsInitialized => _initialized;
    public JsonObject ServerCapabilities { get; private set; } = new();
    public LspSynchronization Synchronization { get; private set; } = new(LspTextSyncKind.None, false, false, false);
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
                    ["workspace"] = new JsonObject { ["applyEdit"] = false },
                    ["textDocument"] = new JsonObject
                    {
                        ["synchronization"] = new JsonObject { ["didSave"] = true, ["dynamicRegistration"] = false },
                        ["completion"] = new JsonObject
                        {
                            ["completionItem"] = new JsonObject { ["snippetSupport"] = false, ["insertReplaceSupport"] = true, ["documentationFormat"] = new JsonArray("plaintext") },
                            ["completionList"] = new JsonObject { ["itemDefaults"] = new JsonArray("editRange", "insertTextFormat", "insertTextMode") }
                        },
                        ["hover"] = new JsonObject { ["contentFormat"] = new JsonArray("plaintext") },
                        ["signatureHelp"] = new JsonObject { ["signatureInformation"] = new JsonObject { ["documentationFormat"] = new JsonArray("plaintext"), ["parameterInformation"] = new JsonObject { ["labelOffsetSupport"] = true }, ["activeParameterSupport"] = true } },
                        ["documentSymbol"] = new JsonObject { ["hierarchicalDocumentSymbolSupport"] = true },
                        ["foldingRange"] = new JsonObject { ["lineFoldingOnly"] = false, ["rangeLimit"] = 100000 },
                        ["publishDiagnostics"] = new JsonObject { ["versionSupport"] = true },
                        ["diagnostic"] = new JsonObject { ["dynamicRegistration"] = false, ["relatedDocumentSupport"] = false },
                        ["semanticTokens"] = new JsonObject
                        {
                            ["requests"] = new JsonObject { ["full"] = new JsonObject { ["delta"] = true } }, ["formats"] = new JsonArray("relative"),
                            ["tokenTypes"] = new JsonArray("namespace", "type", "class", "enum", "interface", "struct", "typeParameter", "parameter", "variable", "property", "enumMember", "event", "function", "method", "macro", "keyword", "modifier", "comment", "string", "number", "regexp", "operator", "decorator"),
                            ["tokenModifiers"] = new JsonArray("declaration", "definition", "readonly", "static", "deprecated", "abstract", "async", "modification", "documentation", "defaultLibrary"),
                            ["multilineTokenSupport"] = false, ["overlappingTokenSupport"] = false
                        }
                    }
                }
            };
            var result = await _rpc.RequestAsync("initialize", parameters, cancellationToken).ConfigureAwait(false);
            ServerCapabilities = result?["capabilities"]?.DeepClone() as JsonObject ?? throw new InvalidDataException("Missing initialize capabilities.");
            if (ServerCapabilities["positionEncoding"]?.GetValue<string>() is { } encoding && encoding != "utf-16")
                throw new NotSupportedException($"The server selected unsupported positions: {encoding}.");
            Synchronization = LspSynchronization.Read(ServerCapabilities["textDocumentSync"]);
            await _rpc.NotifyAsync("initialized", new JsonObject(), cancellationToken).ConfigureAwait(false); _initialized = true;
        }
        finally { _writes.Release(); }
    }
    public async Task OpenAsync(Uri uri, string languageId, DocumentSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ValidateUri(uri); ArgumentException.ThrowIfNullOrWhiteSpace(languageId); ArgumentNullException.ThrowIfNull(snapshot);
        await _writes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureReady(); if (_documents.ContainsKey(uri)) throw new InvalidOperationException("The document is already open.");
            if (Synchronization.OpenClose)
                await _rpc.NotifyAsync("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri.AbsoluteUri, ["languageId"] = languageId, ["version"] = 1, ["text"] = snapshot.Text } }, cancellationToken).ConfigureAwait(false);
            _documents[uri] = new(1, snapshot);
        }
        finally { _writes.Release(); }
    }
    public async Task ChangeAsync(Uri uri, DocumentSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ValidateUri(uri); ArgumentNullException.ThrowIfNull(snapshot);
        await _writes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureReady(); var current = GetDocument(uri);
            if (snapshot.DocumentId != current.Snapshot.DocumentId || snapshot.Revision < current.Snapshot.Revision)
                throw new InvalidOperationException("The snapshot does not belong to the latest open document revision.");
            if (snapshot.Revision == current.Snapshot.Revision) return;
            if (Synchronization.Change == LspTextSyncKind.None) throw new NotSupportedException("The server did not negotiate document changes.");
            var version = checked(current.Version + 1);
            var change = Synchronization.Change == LspTextSyncKind.Incremental ? LspTextChange.Create(current.Snapshot, snapshot, cancellationToken) : new JsonObject { ["text"] = snapshot.Text };
            await _rpc.NotifyAsync("textDocument/didChange", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri.AbsoluteUri, ["version"] = version }, ["contentChanges"] = new JsonArray(change) }, cancellationToken).ConfigureAwait(false);
            _documents[uri] = new(version, snapshot);
        }
        finally { _writes.Release(); }
    }
    public async Task SaveAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ValidateUri(uri); await _writes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureReady(); var current = GetDocument(uri); if (!Synchronization.Save) return;
            var parameters = DocumentParameters(uri);
            if (Synchronization.IncludeTextOnSave) parameters["text"] = current.Snapshot.Text;
            await _rpc.NotifyAsync("textDocument/didSave", parameters, cancellationToken).ConfigureAwait(false);
        }
        finally { _writes.Release(); }
    }
    public async Task CloseAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ValidateUri(uri); await _writes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureReady(); if (!_documents.ContainsKey(uri)) return;
            if (Synchronization.OpenClose) await _rpc.NotifyAsync("textDocument/didClose", DocumentParameters(uri), cancellationToken).ConfigureAwait(false);
            _documents.Remove(uri);
        }
        finally { _writes.Release(); }
    }
    private OpenDocument GetDocument(Uri uri) => _documents.TryGetValue(uri, out var document) ? document : throw new InvalidOperationException("Open the document first.");
    public Task<JsonNode?> CompletionAsync(Uri uri, TextPosition position, CancellationToken cancellationToken = default) => PositionRequest("textDocument/completion", uri, position, cancellationToken);
    public Task<JsonNode?> HoverAsync(Uri uri, TextPosition position, CancellationToken cancellationToken = default) => PositionRequest("textDocument/hover", uri, position, cancellationToken);
    public Task<JsonNode?> SignatureHelpAsync(Uri uri, TextPosition position, CancellationToken cancellationToken = default) => PositionRequest("textDocument/signatureHelp", uri, position, cancellationToken);
    public Task<JsonNode?> DefinitionAsync(Uri uri, TextPosition position, CancellationToken cancellationToken = default) => PositionRequest("textDocument/definition", uri, position, cancellationToken);
    public Task<JsonNode?> SymbolsAsync(Uri uri, CancellationToken cancellationToken = default) => Request("textDocument/documentSymbol", DocumentParameters(uri), cancellationToken);
    public Task<JsonNode?> FoldingAsync(Uri uri, CancellationToken cancellationToken = default) => Request("textDocument/foldingRange", DocumentParameters(uri), cancellationToken);
    public Task<JsonNode?> SemanticTokensAsync(Uri uri, CancellationToken cancellationToken = default) => Request("textDocument/semanticTokens/full", DocumentParameters(uri), cancellationToken);
    public Task<JsonNode?> SemanticTokensDeltaAsync(Uri uri, string previousResultId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(previousResultId);
        var parameters = DocumentParameters(uri); parameters["previousResultId"] = previousResultId;
        return Request("textDocument/semanticTokens/full/delta", parameters, cancellationToken);
    }
    public Task<JsonNode?> DiagnosticsAsync(Uri uri, string? previousResultId = null, CancellationToken cancellationToken = default)
    {
        var parameters = DocumentParameters(uri);
        if (previousResultId is not null) parameters["previousResultId"] = previousResultId;
        if (ServerCapabilities["diagnosticProvider"] is JsonObject provider && provider["identifier"] is { } identifier) parameters["identifier"] = identifier.DeepClone();
        return Request("textDocument/diagnostic", parameters, cancellationToken);
    }
    public Task<JsonNode?> FormatAsync(Uri uri, int tabSize = 4, bool insertSpaces = true, CancellationToken cancellationToken = default)
    {
        if (tabSize is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(tabSize));
        var parameters = DocumentParameters(uri); parameters["options"] = new JsonObject { ["tabSize"] = tabSize, ["insertSpaces"] = insertSpaces };
        return Request("textDocument/formatting", parameters, cancellationToken);
    }
    private Task<JsonNode?> PositionRequest(string method, Uri uri, TextPosition position, CancellationToken token)
    {
        if (position.Line < 0 || position.Character < 0) throw new ArgumentOutOfRangeException(nameof(position));
        var parameters = DocumentParameters(uri); parameters["position"] = LspTextChange.Position(position); return Request(method, parameters, token);
    }
    private Task<JsonNode?> Request(string method, JsonObject parameters, CancellationToken token) { EnsureReady(); return _rpc.RequestAsync(method, parameters, token); }
    private static JsonObject DocumentParameters(Uri uri) { ValidateUri(uri); return new() { ["textDocument"] = new JsonObject { ["uri"] = uri.AbsoluteUri } }; }
    private static void ValidateUri(Uri uri) { ArgumentNullException.ThrowIfNull(uri); if (!uri.IsAbsoluteUri) throw new ArgumentException("An absolute document URI is required.", nameof(uri)); }
    private void EnsureReady() { ObjectDisposedException.ThrowIf(_disposed, this); if (!_initialized) throw new InvalidOperationException("Initialize the LSP client first."); }
    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        await _writes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this); if (!_initialized) return;
            await _rpc.RequestAsync("shutdown", cancellationToken: cancellationToken).ConfigureAwait(false);
            await _rpc.NotifyAsync("exit", cancellationToken: cancellationToken).ConfigureAwait(false); _initialized = false; _documents.Clear();
        }
        finally { _writes.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return; _disposed = true; _rpc.Notification -= OnNotification;
        await _rpc.DisposeAsync().ConfigureAwait(false); await _writes.WaitAsync().ConfigureAwait(false);
        try { _initialized = false; _documents.Clear(); Notification = null; }
        finally { _writes.Release(); }
    }
}
