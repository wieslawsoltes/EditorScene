using SkiaSharp;

namespace EditorScene.Rendering.Skia;

public enum EditorGlyph { New, Open, Save, Print, Cut, Copy, Paste, Undo, Redo, Search, Replace, Bookmark, Previous, Next, Completion, Format, Fold, Unfold, Split, Settings, Error, Warning, Information, Code, Close, Indent, Outdent, Comment, Uncomment, Snippet, Signature, Record, Play, Pause, Delete, HorizontalSplit }

/// <summary>Original 16-unit vector glyphs, recorded once per kind/palette and replayed at any DPI.
/// Each instance owns one picture; use on one rendering thread and dispose with its view.</summary>
public sealed class EditorGlyphRenderer : IDisposable
{
    private SKPicture? _picture;
    private EditorGlyph _kind;
    private bool _dark, _highContrast, _disposed;
    public long BuildCount { get; private set; }
    public void Draw(SKCanvas canvas, double width, double height, EditorGlyph kind, bool dark = false, bool highContrast = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(canvas);
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 0 || height < 0 || width > float.MaxValue || height > float.MaxValue) throw new ArgumentOutOfRangeException(nameof(width));
        if (width == 0 || height == 0) return;
        if (_picture is null || _kind != kind || _dark != dark || _highContrast != highContrast)
        {
            using var recorder = new SKPictureRecorder();
            var recording = recorder.BeginRecording(new SKRect(0, 0, 16, 16));
            Record(recording, kind, dark, highContrast);
            var picture = recorder.EndRecording();
            _picture?.Dispose(); _picture = picture; _kind = kind; _dark = dark; _highContrast = highContrast; BuildCount++;
        }
        canvas.Save();
        try { canvas.Scale((float)width / 16, (float)height / 16); canvas.DrawPicture(_picture); }
        finally { canvas.Restore(); }
    }
    private static void Record(SKCanvas canvas, EditorGlyph kind, bool dark, bool highContrast)
    {
        using var stroke = new SKPaint { Color = highContrast ? SKColors.White : dark ? SKColor.Parse("#D7DFE6") : SKColor.Parse("#405B70"), Style = SKPaintStyle.Stroke, StrokeWidth = 1.1f, IsAntialias = true, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        using var fill = new SKPaint { Color = highContrast ? SKColors.Yellow : SKColor.Parse("#4289BC"), IsAntialias = true };
        using var path = new SKPath();
        // Geometry arrays are only constructed when recording, never on warm draws.
        void Lines(params float[] points) { path.Reset(); path.MoveTo(points[0], points[1]); for (var i = 2; i < points.Length; i += 2) path.LineTo(points[i], points[i + 1]); canvas.DrawPath(path, stroke); }
        void Color(SKColor color) { if (!highContrast) fill.Color = color; }
        switch (kind)
        {
            case EditorGlyph.New: Lines(3, 1, 10, 1, 13, 4, 13, 15, 3, 15, 3, 1); Lines(10, 1, 10, 4, 13, 4); break;
            case EditorGlyph.Open: Color(SKColor.Parse("#D7AC4C")); canvas.DrawRoundRect(new SKRect(1, 5, 15, 14), 1, 1, fill); Lines(2, 6, 2, 3, 6, 3, 8, 5, 14, 5); Lines(1, 9, 3, 6, 15, 6, 13, 14, 1, 14, 1, 9); break;
            case EditorGlyph.Save: canvas.DrawRect(new SKRect(2, 1, 14, 15), fill); canvas.DrawRect(new SKRect(4, 1, 11, 6), stroke); canvas.DrawRect(new SKRect(4, 9, 12, 15), stroke); Lines(10, 2, 10, 5); break;
            case EditorGlyph.Print: Lines(4, 5, 4, 1, 12, 1, 12, 5); canvas.DrawRoundRect(new SKRect(1, 5, 15, 12), 1, 1, stroke); canvas.DrawRect(new SKRect(4, 9, 12, 15), stroke); break;
            case EditorGlyph.Cut: canvas.DrawCircle(4, 12, 2, stroke); canvas.DrawCircle(12, 12, 2, stroke); Lines(5, 10, 12, 1); Lines(11, 10, 4, 1); break;
            case EditorGlyph.Copy: canvas.DrawRect(new SKRect(1, 1, 10, 12), stroke); canvas.DrawRect(new SKRect(5, 4, 14, 15), fill); canvas.DrawRect(new SKRect(5, 4, 14, 15), stroke); break;
            case EditorGlyph.Paste: canvas.DrawRoundRect(new SKRect(3, 3, 14, 15), 1, 1, stroke); canvas.DrawRoundRect(new SKRect(6, 1, 11, 5), 1, 1, fill); Lines(6, 8, 11, 8); Lines(6, 11, 11, 11); break;
            case EditorGlyph.Undo: Lines(6, 2, 2, 6, 6, 10); Lines(2, 6, 10, 6, 13, 8, 13, 13); break;
            case EditorGlyph.Redo: Lines(10, 2, 14, 6, 10, 10); Lines(14, 6, 6, 6, 3, 8, 3, 13); break;
            case EditorGlyph.Search: case EditorGlyph.Replace: canvas.DrawCircle(6, 6, 4, stroke); Lines(9, 9, 14, 14); if (kind == EditorGlyph.Replace) { Color(SKColors.Orange); canvas.DrawRect(new SKRect(1, 12, 7, 14), fill); } break;
            case EditorGlyph.Bookmark: path.MoveTo(4, 1); path.LineTo(12, 1); path.LineTo(12, 15); path.LineTo(8, 11); path.LineTo(4, 15); path.Close(); canvas.DrawPath(path, fill); break;
            case EditorGlyph.Previous: Lines(10, 3, 5, 8, 10, 13); break;
            case EditorGlyph.Next: Lines(6, 3, 11, 8, 6, 13); break;
            case EditorGlyph.Completion: Color(SKColor.Parse("#9B68B7")); canvas.DrawRect(new SKRect(1, 2, 6, 7), fill); canvas.DrawRect(new SKRect(1, 10, 6, 15), fill); Lines(8, 4, 15, 4); Lines(8, 12, 15, 12); break;
            case EditorGlyph.Format: Lines(1, 3, 14, 3); Lines(5, 7, 14, 7); Lines(5, 11, 11, 11); Lines(1, 15, 14, 15); canvas.DrawRect(new SKRect(1, 6, 3, 12), fill); break;
            case EditorGlyph.Fold: case EditorGlyph.Unfold: canvas.DrawRect(new SKRect(2, 2, 14, 14), stroke); Lines(5, 8, 11, 8); if (kind == EditorGlyph.Unfold) Lines(8, 5, 8, 11); break;
            case EditorGlyph.Split: case EditorGlyph.HorizontalSplit: canvas.DrawRect(new SKRect(1, 2, 15, 14), stroke); if (kind == EditorGlyph.Split) Lines(8, 2, 8, 14); else Lines(1, 8, 15, 8); break;
            case EditorGlyph.Settings: canvas.DrawCircle(8, 8, 5, stroke); canvas.DrawCircle(8, 8, 2, stroke); for (var i = 0; i < 8; i++) { var a = i * MathF.PI / 4; Lines(8 + MathF.Cos(a) * 5, 8 + MathF.Sin(a) * 5, 8 + MathF.Cos(a) * 7, 8 + MathF.Sin(a) * 7); } break;
            case EditorGlyph.Error: Color(SKColor.Parse("#E14B4B")); canvas.DrawCircle(8, 8, 7, fill); stroke.Color = highContrast ? SKColors.Black : SKColors.White; Lines(5, 5, 11, 11); Lines(11, 5, 5, 11); break;
            case EditorGlyph.Warning: Color(SKColors.Goldenrod); path.MoveTo(8, 1); path.LineTo(15, 14); path.LineTo(1, 14); path.Close(); canvas.DrawPath(path, fill); stroke.Color = SKColors.Black; Lines(8, 5, 8, 9); canvas.DrawCircle(8, 12, .7f, stroke); break;
            case EditorGlyph.Information: canvas.DrawCircle(8, 8, 7, fill); stroke.Color = highContrast ? SKColors.Black : SKColors.White; Lines(8, 7, 8, 12); canvas.DrawCircle(8, 4, .6f, stroke); break;
            case EditorGlyph.Close: case EditorGlyph.Delete: Lines(4, 4, 12, 12); Lines(4, 12, 12, 4); break;
            case EditorGlyph.Indent: case EditorGlyph.Outdent:
                Lines(1, 2, 15, 2); Lines(8, 6, 15, 6); Lines(8, 10, 15, 10); Lines(1, 14, 15, 14);
                if (kind == EditorGlyph.Indent) Lines(2, 5, 5, 8, 2, 11); else Lines(5, 5, 2, 8, 5, 11); break;
            case EditorGlyph.Comment: case EditorGlyph.Uncomment:
                Color(SKColor.Parse("#52904C")); canvas.DrawRect(new SKRect(5, 3, 15, 5), fill); canvas.DrawRect(new SKRect(5, 7, 15, 9), fill); canvas.DrawRect(new SKRect(5, 11, 12, 13), fill);
                if (kind == EditorGlyph.Comment) Lines(1, 6, 3, 8, 1, 10); else Lines(3, 6, 1, 8, 3, 10); break;
            case EditorGlyph.Snippet:
                Color(SKColor.Parse("#D7AC4C")); canvas.DrawRect(new SKRect(2, 2, 14, 14), fill); Lines(6, 5, 4, 8, 6, 11); Lines(10, 5, 12, 8, 10, 11); break;
            case EditorGlyph.Signature: Lines(4, 2, 1, 5, 1, 11, 4, 14); Lines(12, 2, 15, 5, 15, 11, 12, 14); canvas.DrawCircle(6, 8, 1, fill); canvas.DrawCircle(10, 8, 1, fill); break;
            case EditorGlyph.Record: Color(SKColor.Parse("#D94F45")); canvas.DrawCircle(8, 8, 5, fill); break;
            case EditorGlyph.Play: Color(SKColor.Parse("#52904C")); path.MoveTo(4, 2); path.LineTo(14, 8); path.LineTo(4, 14); path.Close(); canvas.DrawPath(path, fill); break;
            case EditorGlyph.Pause: canvas.DrawRect(new SKRect(3, 2, 6, 14), fill); canvas.DrawRect(new SKRect(10, 2, 13, 14), fill); break;
            default: Lines(5, 3, 1, 8, 5, 13); Lines(11, 3, 15, 8, 11, 13); Lines(9, 2, 7, 14); break;
        }
    }
    public void Dispose() { if (_disposed) return; _disposed = true; _picture?.Dispose(); _picture = null; }
}
