using System.Collections.Immutable;
using EditorScene.LanguageServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Windows.System;

namespace EditorScene.Controls;

public sealed partial class DiagnosticList
{
    public static ImmutableArray<DiagnosticSortColumn> Columns { get; } = [DiagnosticSortColumn.Severity, DiagnosticSortColumn.Code, DiagnosticSortColumn.Line, DiagnosticSortColumn.Character, DiagnosticSortColumn.File, DiagnosticSortColumn.Description];
    private static readonly double[] DefaultWidths = [28, 82, 46, 46, 160, 420];
    private static readonly string[] Labels = ["!", "Code", "Line", "Char", "File", "Description"];
    private readonly double[] _widths = (double[])DefaultWidths.Clone();
    private readonly double[] _effectiveWidths = (double[])DefaultWidths.Clone();
    private readonly double[] _dragWidths = new double[6];
    private readonly Grid _tableGrid = new();
    private readonly Grid _headers = new() { Height = 26 };
    private readonly ScrollViewer _tableScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled, IsTabStop = false };
    private readonly Button[] _sortButtons = new Button[6];
    private readonly Thumb[] _columnGrips = new Thumb[6];
    private readonly HashSet<DiagnosticRowPresenter> _liveRows = [];
    private bool _applyingColumns;
    public int RealizedRowCount => _liveRows.Count;
    public ScrollViewer HorizontalScroller => _tableScroll;
    public event EventHandler? ColumnLayoutChanged;
    public Button GetSortButton(DiagnosticSortColumn column) => _sortButtons[ColumnIndex(column)];
    public Thumb GetColumnGrip(DiagnosticSortColumn column) => _columnGrips[ColumnIndex(column)];
    /// <summary>Preferred width in device-independent pixels. Description fills unused viewport width.</summary>
    public double GetColumnWidth(DiagnosticSortColumn column) => _widths[ColumnIndex(column)];
    public double GetEffectiveColumnWidth(DiagnosticSortColumn column) => _effectiveWidths[ColumnIndex(column)];
    public void SetColumnWidth(DiagnosticSortColumn column, double width)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); var index = ColumnIndex(column);
        if (!double.IsFinite(width) || width < Minimum(index) || width > 2048) throw new ArgumentOutOfRangeException(nameof(width));
        if (_widths[index] == width) return;
        _widths[index] = width; ApplyColumnLayout(); if (!_disposed) ColumnLayoutChanged?.Invoke(this, EventArgs.Empty);
    }
    public void ResetColumnWidths()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_widths.SequenceEqual(DefaultWidths)) return;
        Array.Copy(DefaultWidths, _widths, 6); ApplyColumnLayout(); if (!_disposed) ColumnLayoutChanged?.Invoke(this, EventArgs.Empty);
    }
    private static double Minimum(int index) => index switch { 0 => 24, 4 => 64, 5 => 96, _ => 32 };
    private static int ColumnIndex(DiagnosticSortColumn column) => column switch
    {
        DiagnosticSortColumn.Severity => 0, DiagnosticSortColumn.Code => 1, DiagnosticSortColumn.Line => 2,
        DiagnosticSortColumn.Character => 3, DiagnosticSortColumn.File => 4, DiagnosticSortColumn.Description => 5,
        _ => throw new ArgumentOutOfRangeException(nameof(column))
    };
    private int GripIndex(object sender) => sender is Thumb grip ? Array.IndexOf(_columnGrips, grip) : -1;
    private void InitializeColumns()
    {
        _tableGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _tableGrid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        _heading.Child = _headers; _tableGrid.Children.Add(_heading);
        Grid.SetRow(_rows, 1); _tableGrid.Children.Add(_rows);
        ScrollViewer.SetHorizontalScrollBarVisibility(_rows, ScrollBarVisibility.Disabled);
        ScrollViewer.SetHorizontalScrollMode(_rows, ScrollMode.Disabled);
        _tableScroll.Content = _tableGrid; Grid.SetRow(_tableScroll, 1); _layout.Children.Add(_tableScroll);
        _tableScroll.SizeChanged += OnTableSizeChanged;
        // Parse once per grid, not once per grip. Each Thumb instantiates its own visual tree.
        var gripTemplate = (ControlTemplate)XamlReader.Load("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Thumb'><Border Background='Transparent' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='0,0,1,0'/></ControlTemplate>");
        for (var i = 0; i < 6; i++)
        {
            var column = Columns[i];
            _headers.ColumnDefinitions.Add(new() { Width = new GridLength(_widths[i]) });
            var button = new Button { Content = Labels[i], FontSize = 11, Height = 25, MinHeight = 0, MinWidth = 0,
                Padding = new Thickness(4, 0, 8, 0), BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(button, "Sort diagnostics by " + column);
            button.Click += OnSortClick;
            _sortButtons[i] = button; Grid.SetColumn(button, i); _headers.Children.Add(button);
            var grip = new Thumb { Width = 6, MinWidth = 0, MinHeight = 0, HorizontalAlignment = HorizontalAlignment.Right, IsTabStop = true, UseSystemFocusVisuals = true, Template = gripTemplate };
            AutomationProperties.SetName(grip, "Resize diagnostic " + column);
            AutomationProperties.SetHelpText(grip, "Drag or use Left and Right. Home or double-click restores the default width.");
            grip.DragStarted += OnColumnDragStarted; grip.DragDelta += OnColumnDragDelta;
            grip.KeyDown += OnColumnKeyDown; grip.DoubleTapped += OnColumnDoubleTapped;
            grip.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnColumnPointerPressed), true);
            _columnGrips[i] = grip; Grid.SetColumn(grip, i); _headers.Children.Add(grip);
        }
        _headers.ColumnDefinitions.Add(new() { Width = new GridLength(16) });
        UpdateSortHeaders(); ApplyColumnLayout();
    }
    private void OnSortClick(object sender, RoutedEventArgs args)
    {
        if (_disposed || sender is not Button button) return;
        var index = Array.IndexOf(_sortButtons, button); if (index < 0) return;
        var column = Columns[index];
        SortBy(column, _sortColumn == column && _sortDirection == DiagnosticSortDirection.Ascending ? DiagnosticSortDirection.Descending : DiagnosticSortDirection.Ascending);
    }
    private void OnColumnDragStarted(object sender, DragStartedEventArgs args)
    { var index = GripIndex(sender); if (!_disposed && index >= 0) _dragWidths[index] = _effectiveWidths[index]; }
    private void OnColumnDragDelta(object sender, DragDeltaEventArgs args)
    {
        var index = GripIndex(sender); if (_disposed || index < 0 || !double.IsFinite(args.HorizontalChange)) return;
        _dragWidths[index] = Math.Clamp(_dragWidths[index] + args.HorizontalChange, Minimum(index), 2048);
        SetColumnWidth(Columns[index], _dragWidths[index]);
    }
    private void OnColumnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        var index = GripIndex(sender); if (_disposed || index < 0) return;
        var column = Columns[index];
        if (args.Key == VirtualKey.Home) SetColumnWidth(column, DefaultWidths[index]);
        else if (args.Key == VirtualKey.Left) SetColumnWidth(column, Math.Clamp(_effectiveWidths[index] - 8, Minimum(index), 2048));
        else if (args.Key == VirtualKey.Right) SetColumnWidth(column, Math.Clamp(_effectiveWidths[index] + 8, Minimum(index), 2048));
        else return;
        args.Handled = true;
    }
    private void OnColumnDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    { var index = GripIndex(sender); if (!_disposed && index >= 0) { SetColumnWidth(Columns[index], DefaultWidths[index]); args.Handled = true; } }
    private void OnColumnPointerPressed(object sender, PointerRoutedEventArgs args)
    { if (!_disposed && sender is Thumb grip) grip.Focus(FocusState.Pointer); }
    private void OnTableSizeChanged(object sender, SizeChangedEventArgs args) => ApplyColumnLayout();
    private void ApplyColumnLayout()
    {
        if (_disposed || _applyingColumns) return;
        _applyingColumns = true;
        try
        {
            var sum = 0d; for (var i = 0; i < 6; i++) sum += _widths[i];
            var extra = Math.Max(0, _tableScroll.ActualWidth - 16 - sum);
            var changed = _tableGrid.Width != sum + extra + 16;
            for (var i = 0; i < 6; i++)
            {
                var width = _widths[i] + (i == 5 ? extra : 0);
                changed |= _effectiveWidths[i] != width; _effectiveWidths[i] = width;
            }
            if (!changed) return;
            _tableGrid.Width = sum + extra + 16;
            for (var i = 0; i < 6 && !_disposed; i++)
                if (_headers.ColumnDefinitions[i].Width.Value != _effectiveWidths[i]) _headers.ColumnDefinitions[i].Width = new GridLength(_effectiveWidths[i]);
            if (_disposed) return;
            foreach (var row in _liveRows.ToArray()) { if (_disposed) break; row.ApplyColumns(_effectiveWidths); }
        }
        finally { _applyingColumns = false; }
    }
    private void UpdateSortHeaders()
    {
        for (var i = 0; i < 6 && !_disposed; i++)
        {
            var marker = _sortColumn == Columns[i] ? (_sortDirection == DiagnosticSortDirection.Ascending ? " ▲" : " ▼") : string.Empty;
            _sortButtons[i].Content = Labels[i] + marker;
            ToolTipService.SetToolTip(_sortButtons[i], "Sort by " + Columns[i] + (_sortColumn == Columns[i] ? ", currently " + _sortDirection.ToString().ToLowerInvariant() : string.Empty));
        }
    }
    private void ApplyColumnTheme()
    {
        var background = SyntaxEditor.Brush(_theme.Chrome); var foreground = SyntaxEditor.Brush(_theme.Foreground); var border = SyntaxEditor.Brush(_theme.Border);
        foreach (var button in _sortButtons) { button.Background = background; button.Foreground = foreground; }
        foreach (var grip in _columnGrips) grip.BorderBrush = border;
        foreach (var row in _liveRows.ToArray()) { if (_disposed) break; row.ApplyPalette(_theme); }
    }
    internal void AttachRow(DiagnosticRowPresenter row)
    { if (_disposed) return; _liveRows.Add(row); row.ApplyColumns(_effectiveWidths); row.ApplyPalette(_theme); }
    internal void DetachRow(DiagnosticRowPresenter row) => _liveRows.Remove(row);
    private void DisposeColumns()
    {
        _tableScroll.SizeChanged -= OnTableSizeChanged; ColumnLayoutChanged = null;
        for (var i = 0; i < 6; i++)
        {
            _sortButtons[i].Click -= OnSortClick;
            var grip = _columnGrips[i]; grip.DragStarted -= OnColumnDragStarted; grip.DragDelta -= OnColumnDragDelta;
            grip.KeyDown -= OnColumnKeyDown; grip.DoubleTapped -= OnColumnDoubleTapped;
            grip.RemoveHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnColumnPointerPressed));
        }
        foreach (var row in _liveRows.ToArray()) row.DetachOwner();
        _liveRows.Clear();
        // Callers may retain the publicly exposed header/grip objects. Break the
        // owned header tree and its handlers so those objects do not root the grid.
        _headers.Children.Clear(); _heading.Child = null;
    }
}
