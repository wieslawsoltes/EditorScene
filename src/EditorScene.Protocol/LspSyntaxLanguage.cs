using System.Collections.Immutable;
using System.Text.Json.Nodes;
using EditorScene.LanguageServices;
using EditorScene.Text;

namespace EditorScene.Protocol;

/// <summary>
/// One explicitly opened document and one owned connection. Requests hold a snapshot
/// lease until their response arrives; a newer didChange cannot race an older result.
/// Independent views may share the provider. Different documents need their own session.
/// </summary>
public sealed partial class LspSyntaxLanguage : ISyntaxLanguage, INotifyLanguageAnalysisChanged, IAsyncDisposable
{
    private readonly LanguageServerClient _client;
    private readonly SemaphoreSlim _requests = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _state = new();
    private DocumentSnapshot _snapshot;
    private int _serverVersion = 1;
    private ImmutableArray<EditorDiagnostic> _pushDiagnostics = [];
    private JsonArray? _pullItems;
    private string? _pullResultId;
    private string[] _tokenLegend = [];
    private bool _semanticDeltas;
    private SemanticTokenData? _semanticData;
    private long _semanticRevision = -1;
    private ImmutableArray<ClassificationSpan> _semanticSpans = [];
    private bool _pullDiagnostics, _disposed;
    private Exception? _syncFailure;
    private Task? _disposeTask;
    private readonly TimeSpan _timeout;
    private LspSyntaxLanguage(LanguageServerClient client, DocumentSnapshot snapshot, Uri uri, string languageId, TimeSpan timeout)
    { _client = client; _snapshot = snapshot; DocumentUri = uri; Id = languageId; _timeout = timeout; _client.Notification += OnNotification; }
    public string Id { get; }
    public string DisplayName => Id + " (LSP)";
    public IReadOnlyList<string> Extensions => [];
    public Guid DocumentId => _snapshot.DocumentId;
    public Uri DocumentUri { get; }
    public LanguageCapabilities Capabilities { get; private set; }
    public bool IsDisposed { get { lock (_state) return _disposed; } }
    public int ServerVersion { get { lock (_state) return _serverVersion; } }
    public int IgnoredDiagnosticNotifications { get; private set; }
    public string? LastProtocolWarning { get; private set; }
    public event EventHandler<LanguageAnalysisInvalidated>? AnalysisInvalidated;
    public event EventHandler<string>? ProtocolWarning;

    /// <summary>
    /// Takes ownership of the connection, including on failure. Initialization sends
    /// capabilities; successful open sends only the explicitly supplied snapshot/URI.
    /// No workspace files are read and incoming executable commands are unsupported.
    /// </summary>
    public static async Task<LspSyntaxLanguage> OpenAsync(JsonRpcConnection connection, DocumentSnapshot snapshot, Uri documentUri, string languageId,
        TimeSpan? requestTimeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection); ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(documentUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(languageId);
        if (!documentUri.IsAbsoluteUri) throw new ArgumentException("The document URI must be absolute.", nameof(documentUri));
        var timeout = requestTimeout ?? TimeSpan.FromSeconds(30);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(5)) throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        var provider = new LspSyntaxLanguage(new(connection), snapshot, documentUri, languageId, timeout);
        using var token = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); token.CancelAfter(timeout);
        try
        {
            await provider._client.InitializeAsync(cancellationToken: token.Token).ConfigureAwait(false);
            var synchronization = provider._client.Synchronization;
            if (!synchronization.OpenClose || synchronization.Change == LspTextSyncKind.None)
                throw new NotSupportedException("Live LSP editing requires negotiated open/close and full or incremental synchronization.");
            provider.ConfigureCapabilities();
            await provider._client.OpenAsync(documentUri, languageId, snapshot, token.Token).ConfigureAwait(false);
            return provider;
        }
        catch { await provider.DisposeAsync().ConfigureAwait(false); throw; }
    }
    private static bool Enabled(JsonNode? value) => value is JsonObject || value is JsonValue boolean && boolean.TryGetValue<bool>(out var enabled) && enabled;
    private void ConfigureCapabilities()
    {
        var caps = _client.ServerCapabilities; var result = LanguageCapabilities.Diagnostics;
        foreach (var (key, flag) in new[]
        {
            ("completionProvider", LanguageCapabilities.Completion), ("hoverProvider", LanguageCapabilities.QuickInfo),
            ("signatureHelpProvider", LanguageCapabilities.SignatureHelp), ("documentSymbolProvider", LanguageCapabilities.Symbols),
            ("foldingRangeProvider", LanguageCapabilities.Outlining), ("documentFormattingProvider", LanguageCapabilities.Formatting)
        }) if (Enabled(caps[key])) result |= flag;
        if (Enabled(caps["selectionRangeProvider"])) result |= LanguageCapabilities.SelectionRanges;
        _pullDiagnostics = Enabled(caps["diagnosticProvider"]);
        if (caps["semanticTokensProvider"] is JsonObject semantic && Enabled(semantic["full"]))
        {
            var types = semantic["legend"]?["tokenTypes"] as JsonArray ?? throw new InvalidDataException("The server omitted its semantic-token legend.");
            if (types.Count > 256) throw new InvalidDataException("Semantic-token legend exceeds limits.");
            _tokenLegend = types.Select(n => n?.GetValue<string>() ?? throw new InvalidDataException("Null semantic-token type.")).ToArray();
            _semanticDeltas = semantic["full"] is JsonObject full && Enabled(full["delta"]);
            result |= LanguageCapabilities.Classification;
        }
        Capabilities = result;
    }
    private async Task<T> WithSnapshotAsync<T>(DocumentSnapshot snapshot, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_state) { ObjectDisposedException.ThrowIf(_disposed, this); }
        using var token = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token); token.CancelAfter(_timeout);
        await _requests.WaitAsync(token.Token).ConfigureAwait(false);
        try
        {
            token.Token.ThrowIfCancellationRequested();
            lock (_state)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_syncFailure is not null) throw new IOException("Document synchronization failed; reconnect the language server.", _syncFailure);
                if (snapshot.DocumentId != _snapshot.DocumentId || snapshot.Revision < _snapshot.Revision)
                    throw new InvalidOperationException("The request is for another document or an obsolete snapshot.");
            }
            if (snapshot.Revision != _snapshot.Revision)
            {
                // Stage the target before sending: diagnostics may arrive immediately
                // on the independent reader while the notification write completes.
                lock (_state) { _snapshot = snapshot; _serverVersion = checked(_serverVersion + 1); _pushDiagnostics = []; }
                try { await _client.ChangeAsync(DocumentUri, snapshot, token.Token).ConfigureAwait(false); }
                catch (Exception error) { lock (_state) _syncFailure = error; throw; }
            }
            var result = await operation(token.Token).ConfigureAwait(false);
            token.Token.ThrowIfCancellationRequested(); return result;
        }
        finally { _requests.Release(); }
    }
    public Task<LanguageAnalysis> AnalyzeAsync(DocumentSnapshot snapshot, CancellationToken cancellationToken = default) => WithSnapshotAsync(snapshot, async token =>
    {
        Task<JsonNode?> Empty() => Task.FromResult<JsonNode?>(null);
        var symbols = Capabilities.HasFlag(LanguageCapabilities.Symbols) ? _client.SymbolsAsync(DocumentUri, token) : Empty();
        var folds = Capabilities.HasFlag(LanguageCapabilities.Outlining) ? _client.FoldingAsync(DocumentUri, token) : Empty();
        var semantic = Capabilities.HasFlag(LanguageCapabilities.Classification) ? ReadSemanticTokensAsync(snapshot, token) : Task.FromResult(ImmutableArray<ClassificationSpan>.Empty);
        var diagnostics = _pullDiagnostics ? _client.DiagnosticsAsync(DocumentUri, _pullResultId, token) : Empty();
        await Task.WhenAll(symbols, folds, semantic, diagnostics).ConfigureAwait(false);
        ImmutableArray<EditorDiagnostic> errors;
        if (_pullDiagnostics)
        {
            var report = await diagnostics.ConfigureAwait(false) as JsonObject ?? throw new InvalidDataException("Missing document diagnostic report.");
            var kind = report["kind"]?.GetValue<string>(); JsonArray items;
            if (kind == "full") items = report["items"] as JsonArray ?? throw new InvalidDataException("Missing diagnostic report items.");
            else if (kind == "unchanged" && _pullItems is not null && _pullResultId is not null) items = _pullItems;
            else throw new InvalidDataException("Invalid or unsolicited unchanged diagnostic report.");
            errors = LspResultConverter.Diagnostics(snapshot, items);
            _pullItems = (JsonArray)items.DeepClone(); _pullResultId = report["resultId"]?.GetValue<string>();
        }
        else lock (_state) errors = _pushDiagnostics;
        return new LanguageAnalysis(snapshot.DocumentId, snapshot.Revision, errors,
            await semantic.ConfigureAwait(false),
            LspResultConverter.Foldings(snapshot, await folds.ConfigureAwait(false)),
            LspResultConverter.Symbols(snapshot, DocumentUri, await symbols.ConfigureAwait(false)));
    }, cancellationToken);
    private async Task<ImmutableArray<ClassificationSpan>> ReadSemanticTokensAsync(DocumentSnapshot snapshot, CancellationToken token)
    {
        var useDelta = _semanticDeltas && _semanticData?.ResultId is not null;
        try { return await ReadAsync(useDelta).ConfigureAwait(false); }
        catch (Exception error) when (useDelta && (error is InvalidDataException || error is JsonRpcException { Code: -32601 or -32602 }))
        {
            // Recover once from an invalid/expired base. Never loop on malformed
            // server output or convert cancellation into a retry.
            token.ThrowIfCancellationRequested();
            if (error is JsonRpcException { Code: -32601 }) _semanticDeltas = false;
            _semanticData = null; _semanticRevision = -1; _semanticSpans = [];
            Warn("Semantic-token delta rejected; requesting a full replacement: " + error.Message);
            return await ReadAsync(false).ConfigureAwait(false);
        }
        async Task<ImmutableArray<ClassificationSpan>> ReadAsync(bool delta)
        {
            var result = delta
                ? await _client.SemanticTokensDeltaAsync(DocumentUri, _semanticData!.ResultId!, token).ConfigureAwait(false)
                : await _client.SemanticTokensAsync(DocumentUri, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var candidate = delta ? _semanticData!.ApplyDelta(result, token) : SemanticTokenData.FromFull(result, token);
            var spans = _semanticData is not null && candidate.Data == _semanticData.Data && _semanticRevision == snapshot.Revision
                ? _semanticSpans : LspResultConverter.DecodeSemanticTokens(snapshot, _tokenLegend, candidate.Data.AsSpan(), token);
            token.ThrowIfCancellationRequested();
            _semanticData = candidate; _semanticRevision = snapshot.Revision; _semanticSpans = spans;
            return spans;
        }
    }
    public Task<ImmutableArray<CompletionItem>> CompleteAsync(DocumentSnapshot snapshot, int offset, CancellationToken cancellationToken = default) => WithSnapshotAsync(snapshot, async token =>
    {
        if (!Capabilities.HasFlag(LanguageCapabilities.Completion)) return [];
        return LspResultConverter.Completions(snapshot, offset, await _client.CompletionAsync(DocumentUri, snapshot.GetPosition(offset), token).ConfigureAwait(false), Warn);
    }, cancellationToken);
    public Task<QuickInfo?> GetQuickInfoAsync(DocumentSnapshot snapshot, int offset, CancellationToken cancellationToken = default) => WithSnapshotAsync<QuickInfo?>(snapshot, async token =>
        !Capabilities.HasFlag(LanguageCapabilities.QuickInfo) ? null : LspResultConverter.Hover(snapshot, offset,
            await _client.HoverAsync(DocumentUri, snapshot.GetPosition(offset), token).ConfigureAwait(false)), cancellationToken);
    public Task<SignatureHelp?> GetSignatureHelpAsync(DocumentSnapshot snapshot, int offset, CancellationToken cancellationToken = default) => WithSnapshotAsync<SignatureHelp?>(snapshot, async token =>
        !Capabilities.HasFlag(LanguageCapabilities.SignatureHelp) ? null : LspResultConverter.Signatures(
            await _client.SignatureHelpAsync(DocumentUri, snapshot.GetPosition(offset), token).ConfigureAwait(false)), cancellationToken);
    public Task<ImmutableArray<TextEdit>> FormatAsync(DocumentSnapshot snapshot, CancellationToken cancellationToken = default) => WithSnapshotAsync(snapshot, async token =>
        !Capabilities.HasFlag(LanguageCapabilities.Formatting) ? ImmutableArray<TextEdit>.Empty : LspResultConverter.Edits(snapshot,
            await _client.FormatAsync(DocumentUri, cancellationToken: token).ConfigureAwait(false)), cancellationToken);
    public async Task NotifySavedAsync(DocumentSnapshot snapshot, CancellationToken cancellationToken = default) =>
        await WithSnapshotAsync(snapshot, async token => { await _client.SaveAsync(DocumentUri, token).ConfigureAwait(false); return true; }, cancellationToken).ConfigureAwait(false);
    private void OnNotification(object? sender, RpcNotification notification)
    {
        if (notification.Method != "textDocument/publishDiagnostics" || _pullDiagnostics) return;
        try
        {
            var parameters = notification.Parameters as JsonObject ?? throw new InvalidDataException("Invalid diagnostics notification.");
            if (!Uri.TryCreate(parameters["uri"]?.GetValue<string>(), UriKind.Absolute, out var uri) || uri != DocumentUri) return;
            LanguageAnalysisInvalidated? invalidated = null;
            lock (_state)
            {
                if (_disposed) return;
                // Versionless notifications cannot be proven to describe the current
                // snapshot. Prefer pull diagnostics or a versioned publishing server.
                if (parameters["version"] is null || parameters["version"]!.GetValue<int>() != _serverVersion)
                { IgnoredDiagnosticNotifications++; return; }
                var diagnostics = LspResultConverter.Diagnostics(_snapshot, parameters["diagnostics"]);
                if (!_pushDiagnostics.SequenceEqual(diagnostics)) { _pushDiagnostics = diagnostics; invalidated = new(_snapshot.DocumentId, _snapshot.Revision); }
            }
            if (invalidated is not null) AnalysisInvalidated?.Invoke(this, invalidated);
        }
        catch (Exception error) { Warn("Rejected language-server notification: " + error.Message); }
    }
    private void Warn(string message)
    {
        LastProtocolWarning = message;
        // Host diagnostic listeners must not be allowed to terminate the RPC reader.
        try { ProtocolWarning?.Invoke(this, message); } catch (Exception) { }
    }
    public ValueTask DisposeAsync() { lock (_state) return new(_disposeTask ??= DisposeCoreAsync()); }
    private async Task DisposeCoreAsync()
    {
        _disposed = true; _lifetime.Cancel(); _client.Notification -= OnNotification;
        await _requests.WaitAsync().ConfigureAwait(false);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                if (_client.IsInitialized) { await _client.CloseAsync(DocumentUri, timeout.Token).ConfigureAwait(false); await _client.ShutdownAsync(timeout.Token).ConfigureAwait(false); }
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or JsonRpcException or ObjectDisposedException) { Warn("Language-server cleanup: " + error.Message); }
            finally { await _client.DisposeAsync().ConfigureAwait(false); }
        }
        finally { _semanticData = null; _semanticSpans = []; _pullItems = null; AnalysisInvalidated = null; ProtocolWarning = null; _requests.Release(); }
    }
}
