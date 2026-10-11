using System.Collections.Immutable;
using EditorScene.LanguageServices;
using EditorScene.Rendering.Skia;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
using Windows.System;

namespace EditorScene.Controls;

/// <summary>Reusable current-document error list. Rows are projected once per analysis and virtualized by ListView.</summary>
public sealed class DiagnosticList : UserControl, IDisposable
{
    private readonly Grid _layout = new();
    private readonly ListView _rows = new() { SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true };
    private readonly TextBox _filter = new() { PlaceholderText = "Filter diagnostics", MinWidth = 64, MinHeight = 24, Height = 24, FontSize = 12, Padding = new Thickness(5, 1, 5, 1) };
    private readonly ToggleButton _errors = Toggle("Errors", "Show errors");
    private readonly ToggleButton _warnings = Toggle("Warnings", "Show warnings");
    private readonly ToggleButton _information = Toggle("Messages", "Show information");
    private readonly TextBlock _summary = new() { FontSize = 11, Margin = new Thickness(6, 2, 6, 2) };
    private readonly Border _heading = new() { BorderThickness = new Thickness(0, 1, 0, 1) };
    private SyntaxEditor? _editor;
    private LanguageAnalysis? _analysis;
    private DiagnosticTable? _table;
    private ImmutableArray<DiagnosticTableRow> _visible = [];
    private DiagnosticVisibility _lastVisibility;
    private string? _lastQuery;
    private bool _disposed;
    public DiagnosticList()
    {
        IsTabStop = false;
        _layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _layout.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        _layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var tools = new Grid { ColumnSpacing = 2, Padding = new Thickness(3) };
        for (var i = 0; i < 3; i++) tools.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        tools.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var controls = new Control[] { _errors, _warnings, _information, _filter };
        for (var i = 0; i < controls.Length; i++) { Grid.SetColumn(controls[i], i); tools.Children.Add(controls[i]); }
        _layout.Children.Add(tools);
        _heading.Child = (Grid)XamlReader.Load("<Grid xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Height='24'><Grid.ColumnDefinitions><ColumnDefinition Width='28'/><ColumnDefinition Width='82'/><ColumnDefinition Width='46'/><ColumnDefinition Width='46'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions><TextBlock Grid.Column='1' Text='Code' FontSize='11' VerticalAlignment='Center'/><TextBlock Grid.Column='2' Text='Line' FontSize='11' VerticalAlignment='Center'/><TextBlock Grid.Column='3' Text='Char' FontSize='11' VerticalAlignment='Center'/><TextBlock Grid.Column='4' Text='Description' FontSize='11' VerticalAlignment='Center'/></Grid>");
        Grid.SetRow(_heading, 1); _layout.Children.Add(_heading);
        _rows.ItemTemplate = (DataTemplate)XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Grid MinHeight='24' ToolTipService.ToolTip='{Binding Message}'><Grid.ColumnDefinitions><ColumnDefinition Width='28'/><ColumnDefinition Width='82'/><ColumnDefinition Width='46'/><ColumnDefinition Width='46'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions><TextBlock Text='{Binding Glyph}' FontWeight='Bold' FontSize='15' HorizontalAlignment='Center' VerticalAlignment='Center'/><TextBlock Grid.Column='1' Text='{Binding Code}' FontSize='12' VerticalAlignment='Center' TextTrimming='CharacterEllipsis'/><TextBlock Grid.Column='2' Text='{Binding Line}' FontSize='12' VerticalAlignment='Center'/><TextBlock Grid.Column='3' Text='{Binding Character}' FontSize='12' VerticalAlignment='Center'/><TextBlock Grid.Column='4' Text='{Binding Message}' FontSize='12' VerticalAlignment='Center' TextTrimming='CharacterEllipsis' Margin='0,0,6,0'/></Grid></DataTemplate>");
        _rows.ItemContainerStyle = (Style)XamlReader.Load("<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ListViewItem'><Setter Property='MinHeight' Value='24'/><Setter Property='Padding' Value='0'/><Setter Property='HorizontalContentAlignment' Value='Stretch'/></Style>");
        Grid.SetRow(_rows, 2); _layout.Children.Add(_rows); Grid.SetRow(_summary, 3); _layout.Children.Add(_summary); Content = _layout;
        AutomationProperties.SetName(_rows, "Diagnostic rows"); AutomationProperties.SetName(_filter, "Filter diagnostics");
        _rows.ItemClick += (_, e) => { if (e.ClickedItem is DiagnosticTableRow row) Navigate(row); };
        _rows.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter && _rows.SelectedItem is DiagnosticTableRow row) { Navigate(row); e.Handled = true; } };
        foreach (var toggle in new[] { _errors, _warnings, _information }) toggle.Click += (_, _) => ApplyFilter();
        _filter.TextChanged += (_, _) => ApplyFilter();
        ApplyTheme(EditorTheme.Light); ApplyFilter(true);
    }
    public SyntaxEditor? Editor
    {
        get => _editor;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (ReferenceEquals(value, _editor)) return;
            Detach(); _editor = value;
            if (_editor is not null) { _editor.AnalysisUpdated += AnalysisUpdated; _editor.StateChanged += StateChanged; }
            Refresh();
        }
    }
    public ListView RowsView => _rows;
    public TextBox FilterBox => _filter;
    public ToggleButton ErrorsToggle => _errors;
    public ToggleButton WarningsToggle => _warnings;
    public ToggleButton InformationToggle => _information;
    public DiagnosticTable? Table => _table;
    public int VisibleCount => _visible.Length;
    public long ProjectionBuildCount { get; private set; }
    public long FilterBuildCount { get; private set; }
    public event EventHandler? RowsChanged;
    public void ApplyTheme(EditorTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        RequestedTheme = theme.IsDark ? ElementTheme.Dark : ElementTheme.Light;
        _layout.Background = SyntaxEditor.Brush(theme.Background);
        _heading.Background = SyntaxEditor.Brush(theme.Chrome); _heading.BorderBrush = SyntaxEditor.Brush(theme.Border);
        _rows.Foreground = _filter.Foreground = SyntaxEditor.Brush(theme.Foreground);
        _summary.Foreground = SyntaxEditor.Brush(theme.Muted);
        if (_heading.Child is Grid grid) foreach (var child in grid.Children.OfType<TextBlock>()) child.Foreground = SyntaxEditor.Brush(theme.Foreground);
    }
    private static ToggleButton Toggle(string label, string name)
    {
        var toggle = new ToggleButton { Content = label, IsChecked = true, MinHeight = 24, Height = 24, FontSize = 11, Padding = new Thickness(5, 1, 5, 1) };
        AutomationProperties.SetName(toggle, name); return toggle;
    }
    private void AnalysisUpdated(object? sender, LanguageAnalysis analysis) => Refresh();
    private void StateChanged(object? sender, EventArgs args) => Refresh();
    private void Refresh()
    {
        if (_disposed) return;
        var analysis = _editor?.Analysis;
        if (ReferenceEquals(analysis, _analysis) && (_table is null || _table.FileName == _editor?.Document.FileName)) return;
        _analysis = analysis; _table = null;
        if (analysis is not null && _editor is not null)
        {
            try { _table = new DiagnosticTable(_editor.Document.Snapshot(), analysis); ProjectionBuildCount++; }
            catch (ArgumentException) { _analysis = null; }
        }
        ApplyFilter(true);
    }
    private void ApplyFilter(bool force = false)
    {
        if (_disposed) return;
        var visibility = (_errors.IsChecked == true ? DiagnosticVisibility.Error : 0) | (_warnings.IsChecked == true ? DiagnosticVisibility.Warning : 0) | (_information.IsChecked == true ? DiagnosticVisibility.Information : 0);
        var query = _filter.Text?.Trim() ?? "";
        if (!force && visibility == _lastVisibility && query == _lastQuery) return;
        _lastVisibility = visibility; _lastQuery = query;
        _visible = _table?.Filter(visibility, query) ?? []; _rows.ItemsSource = _visible; FilterBuildCount++;
        _errors.Content = $"{_table?.ErrorCount ?? 0} Errors"; _warnings.Content = $"{_table?.WarningCount ?? 0} Warnings"; _information.Content = $"{_table?.InformationCount ?? 0} Messages";
        _summary.Text = _table is null ? "Waiting for current document analysis" : $"{_visible.Length:N0} of {_table.Rows.Length:N0} diagnostics · current document";
        RowsChanged?.Invoke(this, EventArgs.Empty);
    }
    private void Navigate(DiagnosticTableRow row)
    {
        var editor = _editor;
        if (editor is null || _table is null || !ReferenceEquals(_analysis, editor.Analysis) || !_table.IsCurrent(editor.Document.Snapshot())) { Refresh(); return; }
        var range = row.Diagnostic.Range; editor.Select(range.Offset, range.Length); editor.FocusEditor();
    }
    private void Detach()
    { if (_editor is not null) { _editor.AnalysisUpdated -= AnalysisUpdated; _editor.StateChanged -= StateChanged; } }
    public new void Dispose()
    {
        if (_disposed) return;
        Detach(); _editor = null; _analysis = null; _table = null; _visible = []; _rows.ItemsSource = null; _disposed = true; base.Dispose();
    }
}
