using Microsoft.UI.Xaml;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace EditorScene.Controls;

public enum EditorIconKind { New, Open, Save, Print, Cut, Copy, Paste, Undo, Redo, Search, Replace, Bookmark, Previous, Next, Completion, Format, Fold, Unfold, Split, Settings, Error, Warning, Information, Code, Close }

/// <summary>Original, resolution-independent editor glyphs drawn into the same GPU-backed canvas as the host.</summary>
public sealed class EditorIcon : SKCanvasElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(EditorIconKind), typeof(EditorIcon), new PropertyMetadata(EditorIconKind.Code, (o, _) => ((EditorIcon)o).Invalidate()));
    public static readonly DependencyProperty IsDarkProperty = DependencyProperty.Register(nameof(IsDark), typeof(bool), typeof(EditorIcon), new PropertyMetadata(false, (o, _) => ((EditorIcon)o).Invalidate()));
    public EditorIconKind Kind { get => (EditorIconKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public bool IsDark { get => (bool)GetValue(IsDarkProperty); set => SetValue(IsDarkProperty, value); }
    public EditorIcon() { Width = 16; Height = 16; IsHitTestVisible = false; }
    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        using var stroke = new SKPaint { Color = IsDark ? SKColor.Parse("#D7DFE6") : SKColor.Parse("#405B70"), Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, IsAntialias = true, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        using var fill = new SKPaint { Color = SKColor.Parse("#23AFA2"), IsAntialias = true };
        canvas.Save(); canvas.Scale((float)area.Width / 16, (float)area.Height / 16);
        void Lines(params float[] points) { using var p = new SKPath(); p.MoveTo(points[0], points[1]); for (var i = 2; i < points.Length; i += 2) p.LineTo(points[i], points[i + 1]); canvas.DrawPath(p, stroke); }
        switch (Kind)
        {
            case EditorIconKind.New: Lines(3, 1, 10, 1, 13, 4, 13, 15, 3, 15, 3, 1); Lines(10, 1, 10, 4, 13, 4); break;
            case EditorIconKind.Open: fill.Color = SKColor.Parse("#D7AC4C"); canvas.DrawRoundRect(new SKRect(1, 5, 15, 14), 1, 1, fill); Lines(2, 6, 2, 3, 6, 3, 8, 5, 14, 5); Lines(1, 9, 3, 6, 15, 6, 13, 14, 1, 14, 1, 9); break;
            case EditorIconKind.Save: canvas.DrawRect(new SKRect(2, 1, 14, 15), fill); canvas.DrawRect(new SKRect(4, 1, 11, 6), stroke); canvas.DrawRect(new SKRect(4, 9, 12, 15), stroke); Lines(10, 2, 10, 5); break;
            case EditorIconKind.Print: Lines(4, 5, 4, 1, 12, 1, 12, 5); canvas.DrawRoundRect(new SKRect(1, 5, 15, 12), 1, 1, stroke); canvas.DrawRect(new SKRect(4, 9, 12, 15), stroke); break;
            case EditorIconKind.Cut: canvas.DrawCircle(4, 12, 2, stroke); canvas.DrawCircle(12, 12, 2, stroke); Lines(5, 10, 12, 1); Lines(11, 10, 4, 1); break;
            case EditorIconKind.Copy: canvas.DrawRect(new SKRect(1, 1, 10, 12), stroke); canvas.DrawRect(new SKRect(5, 4, 14, 15), fill); canvas.DrawRect(new SKRect(5, 4, 14, 15), stroke); break;
            case EditorIconKind.Paste: canvas.DrawRoundRect(new SKRect(3, 3, 14, 15), 1, 1, stroke); canvas.DrawRoundRect(new SKRect(6, 1, 11, 5), 1, 1, fill); Lines(6, 8, 11, 8); Lines(6, 11, 11, 11); break;
            case EditorIconKind.Undo: Lines(6, 2, 2, 6, 6, 10); Lines(2, 6, 10, 6, 13, 8, 13, 13); break;
            case EditorIconKind.Redo: Lines(10, 2, 14, 6, 10, 10); Lines(14, 6, 6, 6, 3, 8, 3, 13); break;
            case EditorIconKind.Search: case EditorIconKind.Replace: canvas.DrawCircle(6, 6, 4, stroke); Lines(9, 9, 14, 14); if (Kind == EditorIconKind.Replace) { fill.Color = SKColors.Orange; canvas.DrawRect(new SKRect(1, 12, 7, 14), fill); } break;
            case EditorIconKind.Bookmark: using (var path = new SKPath()) { path.MoveTo(4, 1); path.LineTo(12, 1); path.LineTo(12, 15); path.LineTo(8, 11); path.LineTo(4, 15); path.Close(); canvas.DrawPath(path, fill); } break;
            case EditorIconKind.Previous: Lines(10, 3, 5, 8, 10, 13); break;
            case EditorIconKind.Next: Lines(6, 3, 11, 8, 6, 13); break;
            case EditorIconKind.Completion: fill.Color = SKColor.Parse("#9B68B7"); canvas.DrawRect(new SKRect(1, 2, 6, 7), fill); canvas.DrawRect(new SKRect(1, 10, 6, 15), fill); Lines(8, 4, 15, 4); Lines(8, 12, 15, 12); break;
            case EditorIconKind.Format: Lines(1, 3, 14, 3); Lines(5, 7, 14, 7); Lines(5, 11, 11, 11); Lines(1, 15, 14, 15); canvas.DrawRect(new SKRect(1, 6, 3, 12), fill); break;
            case EditorIconKind.Fold: case EditorIconKind.Unfold: canvas.DrawRect(new SKRect(2, 2, 14, 14), stroke); Lines(5, 8, 11, 8); if (Kind == EditorIconKind.Unfold) Lines(8, 5, 8, 11); break;
            case EditorIconKind.Split: canvas.DrawRect(new SKRect(1, 2, 15, 14), stroke); Lines(8, 2, 8, 14); break;
            case EditorIconKind.Settings: canvas.DrawCircle(8, 8, 5, stroke); canvas.DrawCircle(8, 8, 2, stroke); for (var i = 0; i < 8; i++) { var a = i * MathF.PI / 4; Lines(8 + MathF.Cos(a) * 5, 8 + MathF.Sin(a) * 5, 8 + MathF.Cos(a) * 7, 8 + MathF.Sin(a) * 7); } break;
            case EditorIconKind.Error: fill.Color = SKColor.Parse("#E14B4B"); canvas.DrawCircle(8, 8, 7, fill); stroke.Color = SKColors.White; Lines(5, 5, 11, 11); Lines(11, 5, 5, 11); break;
            case EditorIconKind.Warning: fill.Color = SKColors.Goldenrod; using (var p = new SKPath()) { p.MoveTo(8, 1); p.LineTo(15, 14); p.LineTo(1, 14); p.Close(); canvas.DrawPath(p, fill); } Lines(8, 5, 8, 9); canvas.DrawCircle(8, 12, .7f, stroke); break;
            case EditorIconKind.Information: canvas.DrawCircle(8, 8, 7, fill); stroke.Color = SKColors.White; Lines(8, 7, 8, 12); canvas.DrawCircle(8, 4, .6f, stroke); break;
            case EditorIconKind.Close: Lines(4, 4, 12, 12); Lines(4, 12, 12, 4); break;
            default: Lines(5, 3, 1, 8, 5, 13); Lines(11, 3, 15, 8, 11, 13); Lines(9, 2, 7, 14); break;
        }
        canvas.Restore();
    }
}
