using EditorScene.Rendering.Skia;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace EditorScene.Controls;

/// <summary>Compact native editor status/zoom controls. Unchanged state notifications do not rebuild strings or UI.</summary>
public sealed class EditorStatusBar : UserControl, IDisposable
{
    private readonly Grid _grid = new() { MinHeight = 24, ColumnSpacing = 6, Padding = new Thickness(6, 0, 6, 0) };
    private readonly TextBlock _message = Label();
    private readonly TextBlock _position = Label();
    private readonly TextBlock _encoding = Label();
    private readonly Button _mode = SmallButton("INS", "Toggle insert or overwrite");
    private readonly Button _less = SmallButton("−", "Zoom out");
    private readonly Button _more = SmallButton("+", "Zoom in");
    private readonly Button _percent = SmallButton("100%", "Reset zoom");
    private readonly Slider _zoom = new() { Minimum = 8, Maximum = 40, Value = 13, StepFrequency = 1, Width = 90, MinHeight = 20, Height = 24 };
    private readonly StackPanel _zoomPanel = new() { Orientation = Orientation.Horizontal, Spacing = 0 };
    private SyntaxEditor? _editor;
    private StatusKey? _last;
    private string _prefix = "";
    private bool _updating, _disposed;
    private Guid _endingDocument;
    private long _endingRevision = -1;
    private string _ending = "LF";
    public EditorStatusBar()
    {
        IsTabStop = false;
        _grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 4; i++) _grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var items = new UIElement[] { _message, _encoding, _position, _mode, _zoomPanel };
        for (var i = 0; i < items.Length; i++) { Grid.SetColumn(items[i], i); _grid.Children.Add(items[i]); }
        _zoomPanel.Children.Add(_less); _zoomPanel.Children.Add(_zoom); _zoomPanel.Children.Add(_more); _zoomPanel.Children.Add(_percent);
        Content = _grid;
        AutomationProperties.SetName(_zoom, "Editor zoom");
        AutomationProperties.SetHelpText(_zoom, "Editor font size, from 8 to 40 points. The adjacent button shows the percentage and resets to 100 percent.");
        AutomationProperties.SetName(_position, "Caret position");
        _mode.Click += (_, _) => { if (_editor is { } editor) { editor.IsOverstrike = !editor.IsOverstrike; editor.FocusEditor(); } };
        _less.Click += (_, _) => { if (_editor is { } editor) _ = editor.ExecuteAsync(EditorCommand.ZoomOut); };
        _more.Click += (_, _) => { if (_editor is { } editor) _ = editor.ExecuteAsync(EditorCommand.ZoomIn); };
        _percent.Click += (_, _) => { if (_editor is { } editor) _ = editor.ExecuteAsync(EditorCommand.ResetZoom); };
        _zoom.ValueChanged += (_, e) => { if (!_updating && _editor is { } editor) editor.FontSize = e.NewValue; };
        SizeChanged += (_, _) => AdaptWidth();
        ApplyTheme(EditorTheme.Light);
    }
    public SyntaxEditor? Editor
    {
        get => _editor;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (ReferenceEquals(value, _editor)) return;
            if (_editor is not null) _editor.StateChanged -= StateChanged;
            _editor = value; _last = null;
            if (_editor is not null) _editor.StateChanged += StateChanged;
            Refresh();
        }
    }
    public string MessagePrefix { get => _prefix; set { value ??= ""; if (_prefix == value) return; _prefix = value; Refresh(); } }
    public Slider ZoomSlider => _zoom;
    public Button ResetZoomButton => _percent;
    public Button OverwriteButton => _mode;
    public string PositionText => _position.Text;
    public long RefreshCount { get; private set; }
    public void ApplyTheme(EditorTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        RequestedTheme = theme.IsDark ? ElementTheme.Dark : ElementTheme.Light;
        _grid.Background = SyntaxEditor.Brush(theme.Chrome);
        foreach (var label in new[] { _message, _position, _encoding }) label.Foreground = SyntaxEditor.Brush(theme.Foreground);
        foreach (var button in new[] { _mode, _less, _more, _percent }) button.Foreground = SyntaxEditor.Brush(theme.Foreground);
    }
    private static TextBlock Label() => new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private static Button SmallButton(string text, string name)
    {
        var button = new Button { Content = text, MinHeight = 22, Height = 22, MinWidth = 24, Padding = new Thickness(4, 0, 4, 0), BorderThickness = new Thickness(0), FontSize = 11 };
        AutomationProperties.SetName(button, name); ToolTipService.SetToolTip(button, name); return button;
    }
    private void StateChanged(object? sender, EventArgs e) => Refresh();
    public void Refresh()
    {
        if (_disposed) return;
        var editor = _editor;
        if (editor is null) { _message.Text = _position.Text = _encoding.Text = ""; _last = null; IsEnabled = false; return; }
        IsEnabled = true;
        var document = editor.Document;
        var key = new StatusKey(document.Id, document.Revision, editor.CaretOffset, editor.IsOverstrike, editor.IsReadOnly, editor.FontSize, editor.Status, document.Encoding.WebName, _prefix);
        if (_last == key) return;
        _last = key; RefreshCount++;
        if (_endingDocument != document.Id || _endingRevision != document.Revision)
        {
            _endingDocument = document.Id; _endingRevision = document.Revision;
            _ending = document.PreferredLineEnding switch { "\r\n" => "CRLF", "\r" => "CR", _ => "LF" };
        }
        var location = document.Buffer.GetLocation(editor.CaretOffset);
        // Ch is a line-local character position, not the absolute document offset.
        _position.Text = $"Ln {location.Line:N0}   Col {location.Column:N0}   Ch {location.Column:N0}";
        _message.Text = _prefix + (editor.IsReadOnly ? "Read only" : editor.Status);
        _encoding.Text = document.Encoding.WebName.ToUpperInvariant() + "  " + _ending;
        _mode.Content = editor.IsOverstrike ? "OVR" : "INS";
        _percent.Content = $"{Math.Round(editor.FontSize / 13 * 100):0}%";
        _updating = true;
        try { _zoom.Value = Math.Clamp(editor.FontSize, _zoom.Minimum, _zoom.Maximum); }
        finally { _updating = false; }
    }
    private void AdaptWidth()
    {
        _encoding.Visibility = ActualWidth >= 760 ? Visibility.Visible : Visibility.Collapsed;
        var expanded = ActualWidth >= 620 ? Visibility.Visible : Visibility.Collapsed;
        _zoom.Visibility = _less.Visibility = _more.Visibility = expanded;
    }
    public new void Dispose()
    {
        if (_disposed) return;
        if (_editor is not null) _editor.StateChanged -= StateChanged;
        _editor = null; _last = null; _disposed = true; base.Dispose();
    }
    private readonly record struct StatusKey(Guid Document, long Revision, int Offset, bool Overwrite, bool ReadOnly, double FontSize, string Status, string Encoding, string Prefix);
}
