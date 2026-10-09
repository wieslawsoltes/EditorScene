using EditorScene.LanguageServices;
using EditorScene.Text;
using SkiaSharp;
using UnoEdit.Rendering.Skia;

namespace EditorScene.Rendering.Skia;

/// <summary>Viewport-only editor overlays painted into a borrowed Skia canvas. Owns no GPU context or surface.</summary>
public sealed class EditorAdornments : IDisposable
{
    private readonly EditorDocument _document;
    private readonly DocumentViewport _viewport;
    private readonly IndicatorCollection _indicators;
    private readonly SKPaint _paint = new() { IsAntialias = true };
    private readonly SKPath _path = new();
    private EditorDiagnostic[] _diagnostics = [];
    private bool _disposed;
    public EditorAdornments(EditorDocument document, DocumentViewport viewport, IndicatorCollection indicators)
    {
        _document = document; _viewport = viewport; _indicators = indicators;
        viewport.RenderingLayer += Render; document.Changed += Changed; indicators.Changed += IndicatorsChanged;
    }
    public EditorTheme Theme { get; set; } = EditorTheme.Light;
    public bool ShowWhitespace { get; set; }
    public bool ShowIndentGuides { get; set; } = true;
    public int RulerColumn { get; set; } = 120;
    public bool ShowRuler { get; set; }
    public IReadOnlyList<SelectionRange> ExtraSelections { get; set; } = [];
    public IReadOnlyList<SourceRange> BracketHighlights { get; set; } = [];
    public void Update(LanguageAnalysis analysis)
    {
        if (analysis.DocumentId != _document.Id || analysis.Revision != _document.Revision) return;
        _diagnostics = analysis.Diagnostics.OrderBy(d => d.Range.Offset).ToArray(); _viewport.InvalidateDrawing();
    }
    private void Changed(object? sender, EventArgs e) { _diagnostics = []; BracketHighlights = []; }
    private void IndicatorsChanged(object? sender, EventArgs e) => _viewport.InvalidateDrawing();
    private void Render(object? sender, ViewportRenderEventArgs args)
    {
        if (_disposed) return; var canvas = args.Canvas;
        var visible = _viewport.GetVisibleDocumentLines();
        if (args.Layer == ViewportRenderLayer.Background)
        {
            _paint.Style = SKPaintStyle.Fill; _paint.Color = Theme.Chrome;
            canvas.DrawRect(0, 0, (float)_viewport.GutterWidth, (float)_viewport.ViewportHeight, _paint);
            _paint.Color = Theme.Border.WithAlpha(110); _paint.StrokeWidth = 1;
            if (ShowRuler)
            {
                var x = (float)(_viewport.GutterWidth + RulerColumn * _viewport.Style.FontSize * 0.602 - _viewport.HorizontalOffset);
                if (x >= _viewport.GutterWidth) canvas.DrawLine(x, 0, x, (float)_viewport.ViewportHeight, _paint);
            }
            if (ShowIndentGuides)
            {
                foreach (var line in visible)
                {
                    var count = 0; var column = 0;
                    while (count < line.Length)
                    {
                        var c = _document.Buffer.GetCharAt(line.Offset + count);
                        if (c == ' ') column++; else if (c == '\t') column += _viewport.Style.TabSize - column % _viewport.Style.TabSize; else break;
                        count++;
                    }
                    var y = (float)(_viewport.GetVisualTop(line) - _viewport.VerticalOffset);
                    var bottom = y + (float)_viewport.GetLineHeight(line);
                    for (var offset = 1; offset <= count; offset++)
                    {
                        var c = _document.Buffer.GetCharAt(line.Offset + offset - 1);
                        if (c != '\t' && offset % _viewport.Style.TabSize != 0) continue;
                        var rect = _viewport.GetCaretRectangle(line.Offset + offset);
                        if (rect.Left > _viewport.ViewportWidth) break;
                        if (rect.Left >= _viewport.GutterWidth) canvas.DrawLine(rect.Left, y, rect.Left, bottom, _paint);
                    }
                }
            }
            return;
        }
        if (args.Layer != ViewportRenderLayer.Caret) return;
        var saved = canvas.Save(); canvas.ClipRect(new((float)_viewport.GutterWidth, 0, (float)_viewport.ViewportWidth, (float)_viewport.ViewportHeight));
        _paint.Style = SKPaintStyle.Fill;
        foreach (var selection in ExtraSelections)
        {
            if (selection.End > _document.Buffer.TextLength) continue;
            _paint.Color = Theme.Selection;
            if (selection.Length > 0) foreach (var rect in _viewport.GetSegmentRectangles(selection.Start, selection.Length)) canvas.DrawRect(rect, _paint);
            var caret = _viewport.GetCaretRectangle(selection.Active); _paint.Color = Theme.Foreground;
            canvas.DrawRect(caret.Left, caret.Top, 1.4f, caret.Height, _paint);
        }
        _paint.Style = SKPaintStyle.Stroke; _paint.StrokeWidth = 1; _paint.Color = Theme.Accent;
        foreach (var bracket in BracketHighlights)
            if (bracket.Offset >= 0 && bracket.End <= _document.Buffer.TextLength)
                foreach (var rect in _viewport.GetSegmentRectangles(bracket.Offset, bracket.Length)) canvas.DrawRect(rect, _paint);
        if (visible.Count != 0)
        {
            var first = visible[0].Offset; var last = visible[^1].EndOffset;
            foreach (var diagnostic in _diagnostics)
            {
                if (diagnostic.Range.Offset > last) break;
                if (diagnostic.Range.End < first || diagnostic.Range.End > _document.Buffer.TextLength) continue;
                _paint.Color = diagnostic.Severity == IssueSeverity.Error ? new(225, 40, 45) : diagnostic.Severity == IssueSeverity.Warning ? new(208, 150, 15) : new(35, 150, 210);
                var rectangles = diagnostic.Range.Length > 0 ? _viewport.GetSegmentRectangles(diagnostic.Range.Offset, diagnostic.Range.Length) : [_viewport.GetCaretRectangle(diagnostic.Range.Offset)];
                foreach (var rect in rectangles)
                {
                    _path.Reset(); var left = Math.Max(rect.Left, (float)_viewport.GutterWidth); var right = Math.Min(Math.Max(rect.Right, left + 5), (float)_viewport.ViewportWidth); var y = rect.Bottom - 1;
                    _path.MoveTo(left, y); var up = true;
                    for (var x = left + 2; x <= right; x += 2) { _path.LineTo(x, up ? y - 2 : y); up = !up; }
                    canvas.DrawPath(_path, _paint);
                }
            }
        }
        if (ShowWhitespace) DrawWhitespace(canvas);
        canvas.RestoreToCount(saved);
        _paint.Style = SKPaintStyle.Fill;
        foreach (var indicator in _indicators.Items)
        {
            if (indicator.Offset > _document.Buffer.TextLength) continue;
            var line = _document.Buffer.GetLineByOffset(indicator.Offset);
            if (_viewport.IsLineCollapsed(line.LineNumber)) continue;
            var y = (float)(_viewport.GetVisualTop(line) - _viewport.VerticalOffset + _viewport.DefaultLineHeight / 2);
            if (y < -10 || y > _viewport.ViewportHeight + 10) continue;
            _paint.Color = indicator.Kind == IndicatorKind.Breakpoint ? new(190, 36, 38) : indicator.Kind == IndicatorKind.ExecutionPoint ? new(240, 192, 40) : Theme.Accent;
            if (indicator.Kind == IndicatorKind.Bookmark) canvas.DrawRoundRect(new SKRect(3, y - 6, 11, y + 6), 1, 1, _paint);
            else canvas.DrawCircle(7, y, 5.5f, _paint);
        }
    }
    private void DrawWhitespace(SKCanvas canvas)
    {
        _paint.Color = Theme.Muted.WithAlpha(140); _paint.Style = SKPaintStyle.Fill;
        var visited = new HashSet<int>();
        for (var y = 1d; y < _viewport.ViewportHeight; y += _viewport.DefaultLineHeight)
        {
            var start = _viewport.HitTest(_viewport.GutterWidth, y); var end = _viewport.HitTest(_viewport.ViewportWidth, y);
            if (end < start) (start, end) = (end, start);
            for (var offset = start; offset <= end && offset < _document.Buffer.TextLength; offset++)
            {
                if (!visited.Add(offset)) continue; var c = _document.Buffer.GetCharAt(offset);
                if (c is not (' ' or '\t' or '\r' or '\n')) continue;
                var rect = _viewport.GetCaretRectangle(offset);
                if (c == ' ') canvas.DrawCircle(rect.Left + _viewport.Style.FontSize * 0.28f, rect.MidY + 1, 0.9f, _paint);
                else if (c == '\t')
                {
                    var next = _viewport.GetCaretRectangle(offset + 1); var right = Math.Max(rect.Left + 4, next.Left - 3);
                    _paint.StrokeWidth = 1; canvas.DrawLine(rect.Left + 2, rect.MidY, right, rect.MidY, _paint); canvas.DrawLine(right - 3, rect.MidY - 3, right, rect.MidY, _paint); canvas.DrawLine(right - 3, rect.MidY + 3, right, rect.MidY, _paint);
                }
                else if (c == '\n' && offset > 0 && _document.Buffer.GetCharAt(offset - 1) == '\r') { }
                else { _paint.Style = SKPaintStyle.Stroke; canvas.DrawRect(rect.Left + 2, rect.MidY - 3, 3, 5, _paint); _paint.Style = SKPaintStyle.Fill; }
            }
        }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _viewport.RenderingLayer -= Render; _document.Changed -= Changed; _indicators.Changed -= IndicatorsChanged; _paint.Dispose(); _path.Dispose();
    }
}
