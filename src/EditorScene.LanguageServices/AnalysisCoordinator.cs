using EditorScene.Text;

namespace EditorScene.LanguageServices;

/// <summary>Debounced analysis with owner-thread snapshot capture and revision-safe, reentrancy-safe publication.</summary>
public sealed class AnalysisCoordinator : IDisposable
{
    private readonly EditorDocument _document;
    private readonly Action<Action> _dispatch;
    private CancellationTokenSource? _pending;
    private long _generation;
    private bool _disposed;
    private ISyntaxLanguage _language;
    private TimeSpan _debounce = TimeSpan.FromMilliseconds(180);
    public AnalysisCoordinator(EditorDocument document, ISyntaxLanguage language, Action<Action> dispatch)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _language = language ?? throw new ArgumentNullException(nameof(language));
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _document.VerifyAccess(); _document.Changed += OnChanged; Subscribe(_language, true);
    }
    public TimeSpan Debounce
    {
        get => _debounce;
        set
        {
            VerifyAccess();
            if (value < TimeSpan.Zero || value > TimeSpan.FromMinutes(1)) throw new ArgumentOutOfRangeException(nameof(value));
            _debounce = value;
        }
    }
    public LanguageAnalysis? Current { get; private set; }
    /// <summary>The most recently scheduled provider operation; dispatching its result may complete separately.</summary>
    public Task Pending { get; private set; } = Task.CompletedTask;
    public event EventHandler<LanguageAnalysis>? Published;
    public event EventHandler<Exception>? Failed;
    public ISyntaxLanguage Language
    {
        get => _language;
        set
        {
            VerifyAccess(); ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_language, value)) return;
            Subscribe(_language, false); _language = value; Subscribe(value, true); Current = null; RequestAnalysis(true);
        }
    }
    private void VerifyAccess() { ObjectDisposedException.ThrowIf(_disposed, this); _document.VerifyAccess(); }
    private void Subscribe(ISyntaxLanguage language, bool subscribe)
    {
        if (language is not INotifyLanguageAnalysisChanged source) return;
        if (subscribe) source.AnalysisInvalidated += OnInvalidated; else source.AnalysisInvalidated -= OnInvalidated;
    }
    private void OnInvalidated(object? sender, LanguageAnalysisInvalidated invalidated) => _dispatch(() =>
    {
        if (_disposed || !ReferenceEquals(sender, _language) || invalidated.DocumentId != _document.Id || invalidated.Revision != _document.Revision) return;
        // External diagnostics or project context can change without a text edit.
        // Exact revision equality does not make explicitly invalidated data current.
        Current = null;
        RequestAnalysis(true);
    });
    private void OnChanged(object? sender, EventArgs e) => RequestAnalysis();
    public void RequestAnalysis(bool immediate = false)
    {
        VerifyAccess();
        if (Current?.Revision != _document.Revision) Current = null;
        // Cancellation callbacks are user/provider code. Detach the old source and
        // advance the generation before invoking them: a callback may edit the
        // document, change providers, dispose this coordinator or request new work.
        var generation = ++_generation; var previous = _pending; _pending = null;
        try { previous?.Cancel(); } finally { previous?.Dispose(); }
        if (_disposed || generation != _generation) return;
        var snapshot = _document.Snapshot(); var language = _language;
        var cancellation = _pending = new CancellationTokenSource();
        var work = ExecuteAsync(snapshot, language, generation, immediate ? TimeSpan.Zero : Debounce, cancellation.Token);
        // A synchronous provider/publication handler can start a newer operation
        // before ExecuteAsync returns. Never overwrite that newer Pending task.
        if (!_disposed && generation == _generation) Pending = work;
    }
    private async Task ExecuteAsync(DocumentSnapshot snapshot, ISyntaxLanguage language, long generation, TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var analysis = await language.AnalyzeAsync(snapshot, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (analysis.DocumentId != snapshot.DocumentId || analysis.Revision != snapshot.Revision)
                throw new InvalidOperationException("The provider returned analysis for a different document snapshot.");
            _dispatch(() =>
            {
                if (_disposed || generation != _generation || !ReferenceEquals(language, _language) || _document.Revision != snapshot.Revision) return;
                Current = analysis; Published?.Invoke(this, analysis);
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        { _dispatch(() => { if (!_disposed && generation == _generation) Failed?.Invoke(this, exception); }); }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; ++_generation; _document.Changed -= OnChanged; Subscribe(_language, false);
        Current = null; var previous = _pending; _pending = null; Published = null; Failed = null;
        try { previous?.Cancel(); } finally { previous?.Dispose(); }
    }
}
