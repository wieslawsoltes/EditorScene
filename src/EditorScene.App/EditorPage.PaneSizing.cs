using EditorScene.Controls;
using EditorScene.Rendering.Skia;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EditorScene.App;

public sealed partial class EditorPage
{
    private readonly EditorPaneSplitter _outlineSplitter = new() { Axis = Orientation.Horizontal };
    private readonly EditorPaneSplitter _toolSplitter = new() { DefaultExtent = 200, PreferredExtent = 200 };
    private readonly List<TextBlock> _paneCaptions = [];

    private void InitializePaneSizing()
    {
        _outlineSplitter.ExtentChanged += (_, _) => ApplyPaneExtents();
        _toolSplitter.ExtentChanged += (_, _) => ApplyPaneExtents();
        SizeChanged += (_, _) => RefreshPaneBounds();
        Loaded += (_, _) => RefreshPaneBounds();
        Register("Outline pane", _outlinePane); Register("Bottom tool pane", _toolPane);
        ApplyPaneExtents();
    }
    private void RefreshPaneBounds()
    {
        // Preserve the editor and the 24-pixel outline rail. Preferred sizes are
        // retained separately, so restoring a large viewport restores the layout.
        var width = Math.Max(0, ActualWidth - 24);
        _outlineSplitter.MaximumExtent = Math.Max(0, Math.Min(width * .6, width - 140));
        double chrome = 0;
        for (var i = 0; i < _root.RowDefinitions.Count; i++)
            if (i is not 5 and not 6) chrome += _root.RowDefinitions[i].ActualHeight;
        _toolSplitter.MaximumExtent = Math.Max(0, Math.Min(ActualHeight * .6, ActualHeight - chrome - 80));
        ApplyPaneExtents();
    }
    private void ApplyPaneExtents()
    {
        var width = _outlineSplitter.EffectiveExtent; var height = _toolSplitter.EffectiveExtent;
        var changed = false;
        if (_outlinePane.Width != width) { _outlinePane.Width = width; changed = true; }
        if (_toolPane.Height != height) { _toolPane.Height = height; changed = true; }
        if (changed) QueueDiagnostics();
    }
    private void ApplyPaneSizingTheme(EditorTheme theme)
    {
        _outlineSplitter.ApplyTheme(theme); _toolSplitter.ApplyTheme(theme);
        var foreground = SyntaxEditor.Brush(theme.Foreground);
        foreach (var caption in _paneCaptions) caption.Foreground = foreground;
    }
}
