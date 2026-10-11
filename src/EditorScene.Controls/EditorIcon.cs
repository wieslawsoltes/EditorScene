using EditorScene.Rendering.Skia;
using Microsoft.UI.Xaml;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace EditorScene.Controls;

// Preserve the existing values. New glyphs are appended for source/binary consumers.
public enum EditorIconKind { New, Open, Save, Print, Cut, Copy, Paste, Undo, Redo, Search, Replace, Bookmark, Previous, Next, Completion, Format, Fold, Unfold, Split, Settings, Error, Warning, Information, Code, Close, Indent, Outdent, Comment, Uncomment, Snippet, Signature, Record, Play, Pause, Delete, HorizontalSplit }

/// <summary>Original vector editor icons, recorded once per palette rather than rebuilding paths on every draw.</summary>
public sealed class EditorIcon : SKCanvasElement
{
    private EditorGlyphRenderer? _renderer;
    private bool _disposed;
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(EditorIconKind), typeof(EditorIcon), new PropertyMetadata(EditorIconKind.Code, (o, _) => ((EditorIcon)o).Invalidate()));
    public static readonly DependencyProperty IsDarkProperty = DependencyProperty.Register(nameof(IsDark), typeof(bool), typeof(EditorIcon), new PropertyMetadata(false, (o, _) => ((EditorIcon)o).Invalidate()));
    public static readonly DependencyProperty IsHighContrastProperty = DependencyProperty.Register(nameof(IsHighContrast), typeof(bool), typeof(EditorIcon), new PropertyMetadata(false, (o, _) => ((EditorIcon)o).Invalidate()));
    public EditorIconKind Kind { get => (EditorIconKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public bool IsDark { get => (bool)GetValue(IsDarkProperty); set => SetValue(IsDarkProperty, value); }
    public bool IsHighContrast { get => (bool)GetValue(IsHighContrastProperty); set => SetValue(IsHighContrastProperty, value); }
    public EditorIcon()
    {
        Width = 16; Height = 16; IsHitTestVisible = false;
        Unloaded += OnUnloaded;
    }
    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        if (_disposed) return;
        (_renderer ??= new EditorGlyphRenderer()).Draw(canvas, area.Width, area.Height, (EditorGlyph)Kind, IsDark, IsHighContrast);
    }
    private void OnUnloaded(object sender, RoutedEventArgs args) { _renderer?.Dispose(); _renderer = null; }
    public new void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Unloaded -= OnUnloaded; _renderer?.Dispose(); _renderer = null; base.Dispose();
    }
}
