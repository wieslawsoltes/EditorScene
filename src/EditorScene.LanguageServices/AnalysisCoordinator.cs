using EditorScene.Text;

namespace EditorScene.LanguageServices;

/// <summary>
/// Debounced, cancellable analysis. Publication is marshalled to the document's UI
/// thread and guarded by both request generation and document revision.
/// </summary>
public sealed class AnalysisCoordinator : IDisposable
{
    private readonly EditorDocument _document;
    private readonly Action<Action> _dispatch;
    private CancellationTokenSource? _pending;
    private long _generation;
    private bool _disposed;
    private ISyntaxLanguage _language;
    public AnalysisCoordinator(EditorDocument document, ISyntaxLanguage language, Action<Action> dispatch)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _language = language ?? throw new ArgumentNullException(nameof(language));
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _document.Changed += OnChanged;
    }
    public TimeSpan Debounce { get; set; } = TimeSpan.FromMilliseconds(180);
    public LanguageAnalysis? Current { get; private set; }
    public Task Pending { get; private set; } = Task.CompletedTask;
    public event EventHandler<LanguageAnalysis>? Published;
    public event EventHandler<Exception>? Failed;
    public ISyntaxLanguage Language
    {
        get => _language;
        set { ObjectDisposedException.ThrowIf(_disposed, this); _language = value ?? throw new ArgumentNullException(nameof(value)); Current = null; RequestAnalysis(true); }
    }
    private void OnChanged(object? sender, EventArgs e) => RequestAnalysis();
    public void RequestAnalysis(bool immediate = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var snapshot = _document.Snapshot();
        _pending?.Cancel(); _pending?.Dispose();
        var cancellation = _pending = new CancellationTokenSource();
        var generation = ++_generation; var language = _language;
        Pending = ExecuteAsync(snapshot, language, generation, immediate ? TimeSpan.Zero : Debounce, cancellation.Token);
    }
    private async Task ExecuteAsync(DocumentSnapshot snapshot, ISyntaxLanguage language, long generation, TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            var analysis = await language.AnalyzeAsync(snapshot, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (analysis.DocumentId != snapshot.DocumentId || analysis.Revision != snapshot.Revision)
                throw new InvalidOperationException("The provider returned analysis for a different document snapshot.");
            _dispatch(() =>
            {
                if (_disposed || generation != _generation || _document.Revision != snapshot.Revision) return;
                Current = analysis; Published?.Invoke(this, analysis);
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _dispatch(() => { if (!_disposed && generation == _generation) Failed?.Invoke(this, exception); });
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; ++_generation; _document.Changed -= OnChanged;
        _pending?.Cancel(); _pending?.Dispose(); _pending = null;
        Published = null; Failed = null;
    }
}
