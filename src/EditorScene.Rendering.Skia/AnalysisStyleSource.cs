using EditorScene.LanguageServices;
using EditorScene.Text;
using UnoEdit.Document;
using UnoEdit.Rendering.Skia;

namespace EditorScene.Rendering.Skia;

/// <summary>Indexed semantic styles over an optional incremental lexical source. Stale spans are dropped immediately on edits.</summary>
public sealed class AnalysisStyleSource : ILineStyleSource, IDisposable
{
    private readonly EditorDocument _document;
    private readonly ILineStyleSource? _fallback;
    private ClassificationSpan[] _spans = [];
    private EditorTheme _theme;
    private bool _disposed;
    public AnalysisStyleSource(EditorDocument document, EditorTheme theme, ILineStyleSource? fallback = null)
    {
        _document = document; _theme = theme; _fallback = fallback;
        if (fallback is not null && !ReferenceEquals(fallback.Document, document.Buffer)) throw new ArgumentException("The fallback belongs to a different document.", nameof(fallback));
        document.Changed += Changed;
        if (fallback is not null) fallback.StylesChanged += FallbackChanged;
    }
    public TextDocument Document => _document.Buffer;
    public event EventHandler<LineStylesChangedEventArgs>? StylesChanged;
    public EditorTheme Theme { get => _theme; set { _theme = value ?? throw new ArgumentNullException(nameof(value)); Invalidate(); } }
    public void Update(LanguageAnalysis analysis)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (analysis.DocumentId != _document.Id || analysis.Revision != _document.Revision) return;
        var spans = analysis.Classifications.Where(s => s.Range.Length > 0).OrderBy(s => s.Range.Offset).ToArray();
        var end = 0;
        foreach (var span in spans)
        {
            if (span.Range.Offset < end || span.Range.Length > Document.TextLength - span.Range.Offset) throw new ArgumentException("Classification spans must be sorted, disjoint and within the captured document.", nameof(analysis));
            end = span.Range.End;
        }
        _spans = spans; Invalidate();
    }
    public IReadOnlyList<TextStyleSpan> GetStyles(DocumentLine line)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var lexical = (_fallback?.GetStyles(line) ?? []).Select(AdaptLexical).ToArray();
        var semantic = new List<TextStyleSpan>(); var low = 0; var high = _spans.Length;
        while (low < high) { var middle = low + (high - low) / 2; if (_spans[middle].Range.End <= line.Offset) low = middle + 1; else high = middle; }
        for (var i = low; i < _spans.Length && _spans[i].Range.Offset < line.EndOffset; i++)
        {
            var span = _spans[i]; var start = Math.Max(line.Offset, span.Range.Offset); var end = Math.Min(line.EndOffset, span.Range.End);
            if (end > start) semantic.Add(new(start - line.Offset, end - start) { Foreground = Theme.ColorFor(span.Kind) });
        }
        if (semantic.Count == 0) return lexical;
        if (lexical.Length == 0) return semantic;
        var boundaries = lexical.Concat(semantic).SelectMany(s => new[] { s.Start, s.Start + s.Length }).Distinct().Order().ToArray();
        var result = new List<TextStyleSpan>(); var a = 0; var b = 0;
        for (var i = 0; i + 1 < boundaries.Length; i++)
        {
            var start = boundaries[i]; var end = boundaries[i + 1];
            while (a < lexical.Length && lexical[a].Start + lexical[a].Length <= start) a++;
            while (b < semantic.Count && semantic[b].Start + semantic[b].Length <= start) b++;
            var basis = a < lexical.Length && lexical[a].Start <= start ? lexical[a] : new TextStyleSpan(start, end - start);
            if (b < semantic.Count && semantic[b].Start <= start) basis = basis with { Foreground = semantic[b].Foreground };
            result.Add(basis with { Start = start, Length = end - start });
        }
        return result;
    }
    private TextStyleSpan AdaptLexical(TextStyleSpan span)
    {
        if (!Theme.IsDark || span.Foreground is not { } color) return span;
        var kind = color.Red == 0 && color.Green == 0 && color.Blue > 100 ? "keyword" : color.Green > color.Red && color.Green > color.Blue ? "comment" : color.Red > color.Green * 2 && color.Red > color.Blue * 2 ? "string" : null;
        return span with { Foreground = kind is not null ? Theme.ColorFor(kind) : color.Red < 70 && color.Green < 70 && color.Blue < 70 ? Theme.Foreground : color };
    }
    private void FallbackChanged(object? sender, LineStylesChangedEventArgs e) => StylesChanged?.Invoke(this, e);
    private void Changed(object? sender, EventArgs e) { _spans = []; Invalidate(); }
    private void Invalidate() { if (!_disposed) StylesChanged?.Invoke(this, new(1, Document.LineCount)); }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _document.Changed -= Changed;
        if (_fallback is not null) _fallback.StylesChanged -= FallbackChanged; _spans = []; StylesChanged = null;
    }
}
