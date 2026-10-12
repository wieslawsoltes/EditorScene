using System.Collections.Immutable;
using EditorScene.LanguageServices;
using EditorScene.Rendering.Skia;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace EditorScene.Controls;

/// <summary>Reusable current-document diagnostic grid with virtualized native rows,
/// stable sorting, shared resizable columns, and exact-analysis navigation.</summary>
public sealed partial class DiagnosticList : UserControl, IDisposable
{
    private readonly Grid _layout = new();
    private readonly ListView _rows = new() { SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true };
    private readonly TextBox _filter = new() { PlaceholderText = "Filter diagnostics", MinWidth = 64, MinHeight = 24, Height = 24, FontSize = 12, Padding = new Thickness(5, 1, 5, 1) };
    private readonly ToggleButton _errors = Toggle("Errors", "Show errors");
    private readonly ToggleButton _warnings = Toggle("Warnings", "Show warnings");
    private readonly ToggleButton _information = Toggle("Messages", "Show information");
    private readonly Button _clearFilter = SmallButton("Clear", "Clear diagnostic filter");
    private readonly Button _resetLayout = SmallButton("Reset", "Reset diagnostic columns and order");
    private readonly TextBlock _summary = new() { FontSize = 11, Margin = new Thickness(6, 2, 6, 2) };
    private readonly Border _heading = new() { BorderThickness = new Thickness(0, 1, 0, 1) };
    private SyntaxEditor? _editor;
    private LanguageAnalysis? _analysis;
    private DiagnosticTable? _table;
    private DiagnosticTableView? _queryView;
    private ImmutableArray<DiagnosticTableRow> _visible = [];
    private DiagnosticVisibility _lastVisibility;
    private string? _lastQuery;
    private DiagnosticSortColumn _sortColumn, _lastColumn;
    private DiagnosticSortDirection _sortDirection, _lastDirection;
    private long _publication;
    private bool _disposed;
    private EditorTheme _theme = EditorTheme.Light;

    public DiagnosticList()
    {
        IsTabStop = false;
        _layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _layout.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        _layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var tools = new Grid { ColumnSpacing = 2, Padding = new Thickness(3) };
        for (var i = 0; i < 5; i++) tools.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        tools.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        tools.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        tools.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var controls = new Control[] { _previousDiagnostic, _nextDiagnostic, _errors, _warnings, _information, _filter, _clearFilter, _resetLayout };
        for (var i = 0; i < controls.Length; i++) { Grid.SetColumn(controls[i], i); tools.Children.Add(controls[i]); }
        _layout.Children.Add(tools);
        var templates = new DiagnosticTemplates();
        _rows.ItemTemplate = (DataTemplate)templates["DiagnosticRowTemplate"];
        _rows.ItemContainerStyle = (Style)templates["DiagnosticRowContainerStyle"];
        InitializeColumns(); InitializeDiagnosticNavigation();
        Grid.SetRow(_summary, 2); _layout.Children.Add(_summary); Content = _layout;
        AutomationProperties.SetName(_rows, "Diagnostic rows"); AutomationProperties.SetName(_filter, "Filter diagnostics");
        _rows.ItemClick += OnItemClick; _rows.KeyDown += OnRowsKeyDown;
        foreach (var toggle in new[] { _errors, _warnings, _information }) toggle.Click += OnFilterToggle;
        _filter.TextChanged += OnFilterChanged;
        _clearFilter.Click += OnClearFilter; _resetLayout.Click += OnResetLayout;
        ApplyTheme(EditorTheme.Light); ApplyFilter(true);
    }
    public SyntaxEditor? Editor
    {
        get => _editor;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (value?.IsDisposed == true) throw new ObjectDisposedException(nameof(value));
            if (ReferenceEquals(value, _editor)) return;
            Detach(); _editor = value;
            if (_editor is not null) { _editor.AnalysisUpdated += AnalysisUpdated; _editor.StateChanged += StateChanged; _editor.KeyDown += OnDiagnosticNavigationKey; }
            Refresh();
        }
    }
    public ListView RowsView => _rows;
    public TextBox FilterBox => _filter;
    public ToggleButton ErrorsToggle => _errors;
    public ToggleButton WarningsToggle => _warnings;
    public ToggleButton InformationToggle => _information;
    public Button ClearFilterButton => _clearFilter;
    public Button ResetLayoutButton => _resetLayout;
    public DiagnosticTable? Table => _table;
    public ImmutableArray<DiagnosticTableRow> VisibleRows => _visible;
    public int VisibleCount => _visible.Length;
    public DiagnosticSortColumn SortColumn => _sortColumn;
    public DiagnosticSortDirection SortDirection => _sortDirection;
    public long ProjectionBuildCount { get; private set; }
    public long FilterBuildCount { get; private set; }
    public long SortBuildCount => _queryView?.SortBuildCount ?? 0;
    public event EventHandler? RowsChanged;

    public void SortBy(DiagnosticSortColumn column, DiagnosticSortDirection direction = DiagnosticSortDirection.Ascending)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Enum.IsDefined(column)) throw new ArgumentOutOfRangeException(nameof(column));
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        if (column == DiagnosticSortColumn.ProviderOrder && direction != DiagnosticSortDirection.Ascending)
            throw new ArgumentException("Provider order does not have a descending variant.", nameof(direction));
        if (_sortColumn == column && _sortDirection == direction) return;
        _sortColumn = column; _sortDirection = direction; UpdateSortHeaders(); ApplyFilter();
    }
    public void ApplyTheme(EditorTheme theme)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(theme); _theme = theme;
        RequestedTheme = theme.IsDark ? ElementTheme.Dark : ElementTheme.Light;
        _layout.Background = SyntaxEditor.Brush(theme.Background);
        _heading.Background = SyntaxEditor.Brush(theme.Chrome); _heading.BorderBrush = SyntaxEditor.Brush(theme.Border);
        _rows.Foreground = _filter.Foreground = SyntaxEditor.Brush(theme.Foreground);
        _summary.Foreground = SyntaxEditor.Brush(theme.Muted);
        ApplyColumnTheme(); if (!_disposed) ApplyNavigationTheme();
    }
    private static ToggleButton Toggle(string label, string name)
    {
        var toggle = new ToggleButton { Content = label, IsChecked = true, MinHeight = 24, Height = 24, FontSize = 11, Padding = new Thickness(5, 1, 5, 1) };
        AutomationProperties.SetName(toggle, name); return toggle;
    }
    private static Button SmallButton(string label, string name)
    {
        var button = new Button { Content = label, MinHeight = 24, Height = 24, FontSize = 11, Padding = new Thickness(5, 1, 5, 1) };
        AutomationProperties.SetName(button, name); ToolTipService.SetToolTip(button, name); return button;
    }
    private void OnItemClick(object sender, ItemClickEventArgs e) { if (e.ClickedItem is DiagnosticTableRow row) Navigate(row); }
    private void OnRowsKeyDown(object sender, KeyRoutedEventArgs e)
    { if (e.Key == VirtualKey.Enter && _rows.SelectedItem is DiagnosticTableRow row) { Navigate(row); e.Handled = true; } }
    private void OnFilterToggle(object sender, RoutedEventArgs args) => ApplyFilter();
    private void OnFilterChanged(object sender, TextChangedEventArgs args) => ApplyFilter();
    private void OnClearFilter(object sender, RoutedEventArgs args)
    { if (_disposed) return; _filter.Text = string.Empty; if (!_disposed) _filter.Focus(FocusState.Programmatic); }
    private void OnResetLayout(object sender, RoutedEventArgs args) { if (!_disposed) { ResetColumnWidths(); if (!_disposed) SortBy(DiagnosticSortColumn.ProviderOrder); } }
    private void AnalysisUpdated(object? sender, LanguageAnalysis analysis) => Refresh();
    private void StateChanged(object? sender, EventArgs args) => Refresh();
    private void Refresh()
    {
        if (_disposed) return;
        if (_editor?.IsDisposed == true) { Detach(); _editor = null; }
        UpdateNavigationAvailability(); if (_disposed) return;
        var analysis = _editor?.Analysis;
        if (ReferenceEquals(analysis, _analysis) && (_table is null || _table.FileName == _editor?.Document.FileName)) return;
        _analysis = analysis; _table = null; _queryView = null;
        if (analysis is not null && _editor is not null)
        {
            try { _table = new DiagnosticTable(_editor.Document.Snapshot(), analysis); _queryView = new(_table); ProjectionBuildCount++; }
            catch (Exception error) when (error is ArgumentException or ObjectDisposedException) { /* Reject invalid/released results once. */ }
        }
        ApplyFilter(true);
    }
    private void ApplyFilter(bool force = false)
    {
        if (_disposed) return;
        var rawQuery = _filter.Text ?? string.Empty;
        // Raw text controls Clear even when its normalized query is unchanged.
        if (_clearFilter.IsEnabled != (rawQuery.Length != 0)) _clearFilter.IsEnabled = rawQuery.Length != 0;
        if (_disposed) return;
        var visibility = (_errors.IsChecked == true ? DiagnosticVisibility.Error : 0) | (_warnings.IsChecked == true ? DiagnosticVisibility.Warning : 0) | (_information.IsChecked == true ? DiagnosticVisibility.Information : 0);
        var query = rawQuery.Trim();
        if (!force && visibility == _lastVisibility && query == _lastQuery && _sortColumn == _lastColumn && _sortDirection == _lastDirection) return;
        var generation = ++_publication;
        var result = _queryView?.Query(visibility, query, _sortColumn, _sortDirection) ?? [];
        var previousSelection = _rows.SelectedItem as DiagnosticTableRow;
        _lastVisibility = visibility; _lastQuery = query; _lastColumn = _sortColumn; _lastDirection = _sortDirection;
        var changed = _visible != result; _visible = result; FilterBuildCount++;
        if (changed) { _navigation = new(result); _rows.ItemsSource = result; }
        // ItemsSource/selection changes can synchronously rebind or dispose the grid.
        if (_disposed || generation != _publication) return;
        if (previousSelection is not null && result.Any(r => ReferenceEquals(r, previousSelection))) _rows.SelectedItem = previousSelection;
        if (_disposed || generation != _publication) return;
        _errors.Content = $"{_table?.ErrorCount ?? 0} Errors"; _warnings.Content = $"{_table?.WarningCount ?? 0} Warnings"; _information.Content = $"{_table?.InformationCount ?? 0} Messages";
        _summary.Text = _table is null ? "Waiting for current document analysis" : $"{_visible.Length:N0} of {_table.Rows.Length:N0} diagnostics · current document";
        UpdateNavigationAvailability(); if (!_disposed && generation == _publication) RowsChanged?.Invoke(this, EventArgs.Empty);
    }
    private void Detach()
    { if (_editor is not null) { _editor.AnalysisUpdated -= AnalysisUpdated; _editor.StateChanged -= StateChanged; _editor.KeyDown -= OnDiagnosticNavigationKey; } }
    public new void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _publication++; Detach(); RowsChanged = null;
        _rows.ItemClick -= OnItemClick; _rows.KeyDown -= OnRowsKeyDown;
        foreach (var toggle in new[] { _errors, _warnings, _information }) toggle.Click -= OnFilterToggle;
        _filter.TextChanged -= OnFilterChanged; _clearFilter.Click -= OnClearFilter; _resetLayout.Click -= OnResetLayout;
        DisposeDiagnosticNavigation(); DisposeColumns(); _editor = null; _analysis = null; _table = null; _queryView = null; _visible = []; _rows.ItemsSource = null; base.Dispose();
    }
}
