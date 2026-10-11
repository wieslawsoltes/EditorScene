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
        _widths[index] = width; ApplyColumnLayout(); ColumnLayoutChanged?.Invoke(this, EventArgs.Empty);
    }
    public void ResetColumnWidths()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_widths.SequenceEqual(DefaultWidths)) return;
        Array.Copy(DefaultWidths, _widths, 6); ApplyColumnLayout(); ColumnLayoutChanged?.Invoke(this, EventArgs.Empty);
    }
    private static double Minimum(int index) => index switch { 0 => 24, 4 => 64, 5 => 96, _ => 32 };
    private static int ColumnIndex(DiagnosticSortColumn column) => column switch
    {
        DiagnosticSortColumn.Severity => 0, DiagnosticSortColumn.Code => 1, DiagnosticSortColumn.Line => 2,
        DiagnosticSortColumn.Character => 3, DiagnosticSortColumn.File => 4, DiagnosticSortColumn.Description => 5,
        _ => throw new ArgumentOutOfRangeException(nameof(column))
    };
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
        for (var i = 0; i < 6; i++)
        {
            var index = i; var column = Columns[index];
            _headers.ColumnDefinitions.Add(new() { Width = new GridLength(_widths[index]) });
            var button = new Button { Content = Labels[index], FontSize = 11, Height = 25, MinHeight = 0, MinWidth = 0,
                Padding = new Thickness(4, 0, 8, 0), BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(button, "Sort diagnostics by " + column);
            button.Click += (_, _) => { if (!_disposed) SortBy(column, _sortColumn == column && _sortDirection == DiagnosticSortDirection.Ascending ? DiagnosticSortDirection.Descending : DiagnosticSortDirection.Ascending); };
            _sortButtons[index] = button; Grid.SetColumn(button, index); _headers.Children.Add(button);
            var grip = new Thumb { Width = 6, MinWidth = 0, MinHeight = 0, HorizontalAlignment = HorizontalAlignment.Right, IsTabStop = true, UseSystemFocusVisuals = true };
            grip.Template = (ControlTemplate)XamlReader.Load("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Thumb'><Border Background='Transparent' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='0,0,1,0'/></ControlTemplate>");
            AutomationProperties.SetName(grip, "Resize diagnostic " + column);
            AutomationProperties.SetHelpText(grip, "Drag or use Left and Right. Home or double-click restores the default width.");
            double dragWidth = _widths[index];
            grip.DragStarted += (_, _) => dragWidth = _effectiveWidths[index];
            grip.DragDelta += (_, e) => { if (!_disposed && double.IsFinite(e.HorizontalChange)) { dragWidth = Math.Clamp(dragWidth + e.HorizontalChange, Minimum(index), 2048); SetColumnWidth(column, dragWidth); } };
            grip.KeyDown += (_, e) =>
            {
                if (_disposed) return;
                if (e.Key == VirtualKey.Home) SetColumnWidth(column, DefaultWidths[index]);
                else if (e.Key == VirtualKey.Left) SetColumnWidth(column, Math.Clamp(_effectiveWidths[index] - 8, Minimum(index), 2048));
                else if (e.Key == VirtualKey.Right) SetColumnWidth(column, Math.Clamp(_effectiveWidths[index] + 8, Minimum(index), 2048));
                else return;
                e.Handled = true;
            };
            grip.DoubleTapped += (_, e) => { if (!_disposed) { SetColumnWidth(column, DefaultWidths[index]); e.Handled = true; } };
            grip.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => { if (!_disposed) grip.Focus(FocusState.Pointer); }), true);
            _columnGrips[index] = grip; Grid.SetColumn(grip, index); _headers.Children.Add(grip);
        }
        _headers.ColumnDefinitions.Add(new() { Width = new GridLength(16) });
        UpdateSortHeaders(); ApplyColumnLayout();
    }
    private void OnTableSizeChanged(object sender, SizeChangedEventArgs args) => ApplyColumnLayout();
    private void ApplyColumnLayout()
    {
        if (_disposed || _applyingColumns) return;
        _applyingColumns = true;
        try
        {
            Array.Copy(_widths, _effectiveWidths, 6);
            _effectiveWidths[5] += Math.Max(0, _tableScroll.ActualWidth - 16 - _widths.Sum());
            _tableGrid.Width = _effectiveWidths.Sum() + 16;
            for (var i = 0; i < 6; i++) _headers.ColumnDefinitions[i].Width = new GridLength(_effectiveWidths[i]);
            foreach (var row in _liveRows.ToArray()) row.ApplyColumns(_effectiveWidths);
        }
        finally { _applyingColumns = false; }
    }
    private void UpdateSortHeaders()
    {
        for (var i = 0; i < 6; i++)
        {
            var marker = _sortColumn == Columns[i] ? (_sortDirection == DiagnosticSortDirection.Ascending ? " ▲" : " ▼") : string.Empty;
            _sortButtons[i].Content = Labels[i] + marker;
            ToolTipService.SetToolTip(_sortButtons[i], "Sort by " + Columns[i] + (_sortColumn == Columns[i] ? ", currently " + _sortDirection.ToString().ToLowerInvariant() : string.Empty));
        }
    }
    private void ApplyColumnTheme()
    {
        foreach (var button in _sortButtons) { button.Background = SyntaxEditor.Brush(_theme.Chrome); button.Foreground = SyntaxEditor.Brush(_theme.Foreground); }
        foreach (var grip in _columnGrips) grip.BorderBrush = SyntaxEditor.Brush(_theme.Border);
        foreach (var row in _liveRows.ToArray()) row.ApplyPalette(_theme);
    }
    internal void AttachRow(DiagnosticRowPresenter row)
    { if (_disposed) return; _liveRows.Add(row); row.ApplyColumns(_effectiveWidths); row.ApplyPalette(_theme); }
    internal void DetachRow(DiagnosticRowPresenter row) => _liveRows.Remove(row);
    private void DisposeColumns()
    {
        _tableScroll.SizeChanged -= OnTableSizeChanged; ColumnLayoutChanged = null;
        foreach (var row in _liveRows.ToArray()) row.DetachOwner();
        _liveRows.Clear();
    }
}
