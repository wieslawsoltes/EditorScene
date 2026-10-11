using EditorScene.LanguageServices;
using EditorScene.Rendering.Skia;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace EditorScene.Controls;

/// <summary>Recyclable native diagnostic row. Created only by realized ListView item templates;
/// keeps no document subscriptions and draws the original cached severity vector.</summary>
public sealed class DiagnosticRowPresenter : UserControl, IDisposable
{
    private readonly Grid _grid = new() { Height = 24 };
    private readonly TextBlock[] _cells = new TextBlock[5];
    private readonly EditorIcon _icon = new() { Width = 14, Height = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private DiagnosticList? _owner;
    private bool _disposed;
    public static readonly DependencyProperty RowProperty = DependencyProperty.Register(nameof(Row), typeof(DiagnosticTableRow), typeof(DiagnosticRowPresenter), new PropertyMetadata(null, (o, _) => ((DiagnosticRowPresenter)o).UpdateRow()));
    public DiagnosticTableRow? Row { get => (DiagnosticTableRow?)GetValue(RowProperty); set => SetValue(RowProperty, value); }
    public DiagnosticRowPresenter()
    {
        IsTabStop = false;
        var widths = new[] { 28d, 82d, 46d, 46d, 160d, 420d };
        foreach (var width in widths) _grid.ColumnDefinitions.Add(new() { Width = new GridLength(width) });
        _grid.Children.Add(_icon);
        for (var i = 0; i < _cells.Length; i++)
        {
            var cell = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(4, 0, 8, 0) };
            _cells[i] = cell; Grid.SetColumn(cell, i + 1); _grid.Children.Add(cell);
        }
        Content = _grid; Loaded += OnLoaded; Unloaded += OnUnloaded; UpdateRow();
    }
    private void UpdateRow()
    {
        if (_disposed) return;
        var row = Row;
        if (row is null)
        {
            foreach (var cell in _cells) cell.Text = string.Empty;
            _icon.Visibility = Visibility.Collapsed; ToolTipService.SetToolTip(this, null); AutomationProperties.SetName(this, string.Empty); return;
        }
        _icon.Visibility = Visibility.Visible;
        _icon.Kind = row.Diagnostic.Severity switch { IssueSeverity.Error => EditorIconKind.Error, IssueSeverity.Warning => EditorIconKind.Warning, _ => EditorIconKind.Information };
        _cells[0].Text = row.Code; _cells[1].Text = row.Line.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _cells[2].Text = row.Character.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _cells[3].Text = row.FileName; _cells[4].Text = row.Message;
        ToolTipService.SetToolTip(this, row.ToString() + "\n" + row.FileName);
        AutomationProperties.SetName(this, row.Severity + " " + row.Code + ", " + row.Message + ", " + row.FileName + ", line " + row.Line + ", character " + row.Character);
    }
    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_disposed) return;
        DetachOwner();
        for (DependencyObject? parent = VisualTreeHelper.GetParent(this); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is DiagnosticList owner) { _owner = owner; owner.AttachRow(this); break; }
    }
    private void OnUnloaded(object sender, RoutedEventArgs args) => DetachOwner();
    internal void DetachOwner() { var owner = _owner; _owner = null; owner?.DetachRow(this); }
    internal void ApplyColumns(double[] widths)
    {
        if (_disposed) return;
        for (var i = 0; i < 6; i++) if (_grid.ColumnDefinitions[i].Width.Value != widths[i]) _grid.ColumnDefinitions[i].Width = new GridLength(widths[i]);
        Width = widths.Sum();
    }
    internal void ApplyPalette(EditorTheme theme)
    {
        if (_disposed) return;
        var foreground = SyntaxEditor.Brush(theme.Foreground);
        foreach (var cell in _cells) cell.Foreground = foreground;
        _icon.IsDark = theme.IsDark; _icon.IsHighContrast = theme.Name == EditorTheme.HighContrast.Name;
    }
    public new void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Loaded -= OnLoaded; Unloaded -= OnUnloaded; DetachOwner(); _icon.Dispose(); base.Dispose();
    }
}
