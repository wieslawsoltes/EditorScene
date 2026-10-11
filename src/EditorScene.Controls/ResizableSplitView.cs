using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Windows.System;

namespace EditorScene.Controls;

/// <summary>Two independently hosted views with a draggable, keyboard-accessible separator.
/// Vertical stacks the views top/bottom; Horizontal arranges them side by side.</summary>
public sealed class ResizableSplitView : UserControl, IDisposable
{
    private readonly Grid _grid = new();
    private readonly ContentPresenter _first = Presenter();
    private readonly ContentPresenter _second = Presenter();
    private readonly Thumb _separator = new() { MinHeight = 0, MinWidth = 0, IsTabStop = true, UseSystemFocusVisuals = true };
    private double _ratio = .5;
    private Orientation _orientation = Orientation.Vertical;
    private bool _split, _disposed;
    public ResizableSplitView()
    {
        IsTabStop = false;
        _separator.Template = (ControlTemplate)XamlReader.Load("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Thumb'><Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' /></ControlTemplate>");
        AutomationProperties.SetName(_separator, "Resize editor split");
        AutomationProperties.SetHelpText(_separator, "Use arrow keys to resize. Home and End move to an edge. Double-click to center.");
        ToolTipService.SetToolTip(_separator, "Drag to resize · arrows adjust · double-click centers");
        _grid.Children.Add(_first); _grid.Children.Add(_separator); _grid.Children.Add(_second); Content = _grid;
        _separator.DragDelta += OnDragDelta;
        _separator.KeyDown += OnSeparatorKeyDown;
        _separator.DoubleTapped += OnDoubleTapped;
        SizeChanged += OnSizeChanged;
        RebuildLayout();
    }
    private static ContentPresenter Presenter() => new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    public UIElement? First
    {
        get => _first.Content as UIElement;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (value is not null && ReferenceEquals(value, Second)) throw new ArgumentException("A UI element cannot occupy both split panes.", nameof(value));
            if (ReferenceEquals(value, _first.Content)) return;
            _first.Content = value;
        }
    }
    public UIElement? Second
    {
        get => _second.Content as UIElement;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (value is not null && ReferenceEquals(value, First)) throw new ArgumentException("A UI element cannot occupy both split panes.", nameof(value));
            if (ReferenceEquals(value, _second.Content)) return;
            _second.Content = value; var split = value is not null;
            if (_split != split) { _split = split; RebuildLayout(); LayoutChanged?.Invoke(this, EventArgs.Empty); }
        }
    }
    public Orientation Orientation
    {
        get => _orientation;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (value is not Orientation.Vertical and not Orientation.Horizontal) throw new ArgumentOutOfRangeException(nameof(value));
            if (_orientation == value) return; _orientation = value; RebuildLayout(); LayoutChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    /// <summary>Preferred first-pane fraction. Layout clamps the effective fraction to keep both panes usable.</summary>
    public double Ratio
    {
        get => _ratio;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!double.IsFinite(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException(nameof(value));
            if (_ratio == value) return; _ratio = value; UpdateLengths(); LayoutChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public Thumb Separator => _separator;
    public bool IsSplit => _split;
    public event EventHandler? LayoutChanged;
    public void ApplyTheme(EditorScene.Rendering.Skia.EditorTheme theme)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(theme);
        _grid.Background = SyntaxEditor.Brush(theme.Background);
        _separator.Background = SyntaxEditor.Brush(theme.Chrome); _separator.BorderBrush = SyntaxEditor.Brush(theme.Border);
    }
    private void RebuildLayout()
    {
        _grid.RowDefinitions.Clear(); _grid.ColumnDefinitions.Clear();
        Grid.SetRow(_first, 0); Grid.SetColumn(_first, 0);
        Grid.SetRow(_separator, 0); Grid.SetColumn(_separator, 0);
        Grid.SetRow(_second, 0); Grid.SetColumn(_second, 0);
        _separator.Visibility = _second.Visibility = _split ? Visibility.Visible : Visibility.Collapsed;
        if (!_split) return;
        if (_orientation == Orientation.Vertical)
        {
            _grid.RowDefinitions.Add(new()); _grid.RowDefinitions.Add(new() { Height = new GridLength(6) }); _grid.RowDefinitions.Add(new());
            Grid.SetRow(_separator, 1); Grid.SetRow(_second, 2); _separator.BorderThickness = new Thickness(0, 1, 0, 1);
        }
        else
        {
            _grid.ColumnDefinitions.Add(new()); _grid.ColumnDefinitions.Add(new() { Width = new GridLength(6) }); _grid.ColumnDefinitions.Add(new());
            Grid.SetColumn(_separator, 1); Grid.SetColumn(_second, 2); _separator.BorderThickness = new Thickness(1, 0, 1, 0);
        }
        UpdateLengths();
    }
    private double Available => Math.Max(0, (_orientation == Orientation.Vertical ? ActualHeight : ActualWidth) - 6);
    private void UpdateLengths()
    {
        if (!_split || _disposed) return;
        var available = Available; var minimum = available > 0 ? Math.Min(.5, 48 / available) : 0;
        var effective = Math.Clamp(_ratio, minimum, 1 - minimum);
        var first = new GridLength(Math.Max(.0001, effective), GridUnitType.Star);
        var second = new GridLength(Math.Max(.0001, 1 - effective), GridUnitType.Star);
        if (_orientation == Orientation.Vertical) { _grid.RowDefinitions[0].Height = first; _grid.RowDefinitions[2].Height = second; }
        else { _grid.ColumnDefinitions[0].Width = first; _grid.ColumnDefinitions[2].Width = second; }
    }
    private void MoveBy(double pixels)
    {
        if (_disposed) return;
        var available = Available; if (available <= 0 || !double.IsFinite(pixels)) return;
        var length = _orientation == Orientation.Vertical ? _first.ActualHeight : _first.ActualWidth;
        var minimum = Math.Min(.5, 48 / available);
        Ratio = Math.Clamp((length + pixels) / available, minimum, 1 - minimum);
    }
    private void OnDragDelta(object sender, DragDeltaEventArgs e) => MoveBy(_orientation == Orientation.Vertical ? e.VerticalChange : e.HorizontalChange);
    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) { if (_disposed) return; Ratio = .5; e.Handled = true; }
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateLengths();
    private void OnSeparatorKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_disposed) return;
        if (e.Key == VirtualKey.Home) Ratio = 0;
        else if (e.Key == VirtualKey.End) Ratio = 1;
        else if (e.Key == VirtualKey.Up && _orientation == Orientation.Vertical || e.Key == VirtualKey.Left && _orientation == Orientation.Horizontal) MoveBy(-10);
        else if (e.Key == VirtualKey.Down && _orientation == Orientation.Vertical || e.Key == VirtualKey.Right && _orientation == Orientation.Horizontal) MoveBy(10);
        else return;
        e.Handled = true;
    }
    public new void Dispose()
    {
        if (_disposed) return;
        _disposed = true; LayoutChanged = null;
        _separator.DragDelta -= OnDragDelta; _separator.KeyDown -= OnSeparatorKeyDown; _separator.DoubleTapped -= OnDoubleTapped; SizeChanged -= OnSizeChanged;
        // Pane contents are caller-owned, so detach them before disposing this control.
        _first.Content = null; _second.Content = null; _split = false; base.Dispose();
    }
}
