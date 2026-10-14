using EditorScene.Rendering.Skia;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Windows.System;

namespace EditorScene.Controls;

/// <summary>A reusable separator for a trailing (right or bottom) tool pane.
/// The host supplies available bounds and applies EffectiveExtent to its pane.
/// Temporary viewport constraints never overwrite the user's preferred extent.</summary>
public sealed class EditorPaneSplitter : Thumb, IDisposable
{
    private double _preferred = 240, _minimum = 120, _maximum = 600, _default = 240;
    private double _publishedPreferred = double.NaN, _publishedEffective = double.NaN;
    private double _dragStart;
    private bool _dragging, _disposed;
    private Orientation _axis = Orientation.Vertical;
    private EditorTheme? _theme;
    public EditorPaneSplitter()
    {
        MinHeight = MinWidth = 0; IsTabStop = true; UseSystemFocusVisuals = true;
        Template = (ControlTemplate)XamlReader.Load("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Thumb'><Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'/></ControlTemplate>");
        AutomationProperties.SetName(this, "Resize tool pane");
        ToolTipService.SetToolTip(this, "Drag or use arrows to resize · Home/End: limits · Enter/double-click: reset · Escape: cancel drag");
        DragStarted += OnDragStarted; DragDelta += OnDragDelta; DragCompleted += OnDragCompleted;
        KeyDown += OnKeyDownEvent; DoubleTapped += OnDoubleTapped;
        AddHandler(PointerPressedEvent, new PointerEventHandler(OnPointerPressedEvent), true);
        Unloaded += OnUnloaded;
        UpdateAxis(); ApplyTheme(EditorTheme.Light); Publish();
    }
    /// <summary>Horizontal adjusts width; Vertical adjusts height.</summary>
    public Orientation Axis
    {
        get => _axis;
        set
        {
            Verify();
            if (value is not Orientation.Horizontal and not Orientation.Vertical) throw new ArgumentOutOfRangeException(nameof(value));
            if (_axis == value) return;
            CancelResize(); if (_disposed) return;
            _axis = value; UpdateAxis(); Publish(forceHelp: true);
        }
    }
    public double PreferredExtent { get => _preferred; set { VerifyNumber(value); if (_preferred == value) return; _preferred = value; Publish(); } }
    public double MinimumExtent { get => _minimum; set { VerifyNumber(value); if (_minimum == value) return; _minimum = value; Publish(); } }
    /// <summary>Host-calculated capacity, excluding reserved editor/chrome space.
    /// May be less than MinimumExtent, including zero in a very small viewport.</summary>
    public double MaximumExtent { get => _maximum; set { VerifyNumber(value); if (_maximum == value) return; _maximum = value; Publish(); } }
    public double DefaultExtent { get => _default; set { VerifyNumber(value); _default = value; } }
    public double EffectiveExtent => Math.Clamp(_preferred, Math.Min(_minimum, _maximum), _maximum);
    public bool IsResizing => _dragging;
    public event EventHandler? ExtentChanged;
    private void Verify() => ObjectDisposedException.ThrowIf(_disposed, this);
    private void VerifyNumber(double value)
    {
        Verify();
        if (!double.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value), "Use a finite, nonnegative extent.");
    }
    private void UpdateAxis()
    {
        Width = _axis == Orientation.Horizontal ? 6 : double.NaN;
        Height = _axis == Orientation.Vertical ? 6 : double.NaN;
        HorizontalAlignment = HorizontalAlignment.Stretch; VerticalAlignment = VerticalAlignment.Stretch;
        BorderThickness = _axis == Orientation.Horizontal ? new Thickness(1, 0, 1, 0) : new Thickness(0, 1, 0, 1);
    }
    /// <summary>Physical right/down movement shrinks a trailing pane. Uses its
    /// effective visible extent so an old off-screen preference cannot cause a jump.</summary>
    public void ResizeBy(double physicalDelta)
    {
        Verify();
        if (!double.IsFinite(physicalDelta)) throw new ArgumentOutOfRangeException(nameof(physicalDelta));
        if (physicalDelta == 0 || _maximum == 0) return;
        PreferredExtent = Math.Clamp(EffectiveExtent - physicalDelta, Math.Min(_minimum, _maximum), _maximum);
    }
    public void ResetExtent() { Verify(); PreferredExtent = _default; }
    public void CancelResize()
    {
        Verify(); if (!_dragging) return;
        var original = _dragStart; _dragging = false;
        CancelDrag();
        if (!_disposed) PreferredExtent = original;
    }
    private void Publish(bool forceHelp = false)
    {
        var effective = EffectiveExtent;
        if (!forceHelp && _publishedPreferred == _preferred && _publishedEffective == effective) return;
        _publishedPreferred = _preferred; _publishedEffective = effective;
        AutomationProperties.SetHelpText(this, $"Tool pane {(_axis == Orientation.Horizontal ? "width" : "height")}: {effective:0} pixels. Arrow keys resize; Home and End select limits; Enter resets; Escape cancels a drag.");
        ExtentChanged?.Invoke(this, EventArgs.Empty);
    }
    public void ApplyTheme(EditorTheme theme)
    {
        Verify(); ArgumentNullException.ThrowIfNull(theme);
        if (ReferenceEquals(theme, _theme)) return;
        _theme = theme; RequestedTheme = theme.IsDark ? ElementTheme.Dark : ElementTheme.Light;
        Background = SyntaxEditor.Brush(theme.Chrome); BorderBrush = SyntaxEditor.Brush(theme.Border);
    }
    private void OnPointerPressedEvent(object sender, PointerRoutedEventArgs args)
    { if (!_disposed && IsEnabled && args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) Focus(FocusState.Pointer); }
    private void OnDragStarted(object sender, DragStartedEventArgs args)
    { if (_disposed) return; _dragStart = _preferred; _dragging = true; }
    private void OnDragDelta(object sender, DragDeltaEventArgs args)
    { if (!_disposed && _dragging) ResizeBy(_axis == Orientation.Horizontal ? args.HorizontalChange : args.VerticalChange); }
    private void OnDragCompleted(object sender, DragCompletedEventArgs args)
    {
        if (_disposed || !_dragging) return;
        _dragging = false;
        if (args.Canceled) PreferredExtent = _dragStart;
    }
    private void OnKeyDownEvent(object sender, KeyRoutedEventArgs args)
    {
        if (_disposed || !IsEnabled || args.Handled) return;
        if (args.Key == VirtualKey.Escape && _dragging) CancelResize();
        else if (args.Key == VirtualKey.Home) PreferredExtent = Math.Min(_minimum, _maximum);
        else if (args.Key == VirtualKey.End) PreferredExtent = _maximum;
        else if (args.Key == VirtualKey.Enter) ResetExtent();
        else if (_axis == Orientation.Horizontal && args.Key == VirtualKey.Left || _axis == Orientation.Vertical && args.Key == VirtualKey.Up) ResizeBy(-10);
        else if (_axis == Orientation.Horizontal && args.Key == VirtualKey.Right || _axis == Orientation.Vertical && args.Key == VirtualKey.Down) ResizeBy(10);
        else return;
        args.Handled = true;
    }
    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    { if (_disposed || !IsEnabled) return; ResetExtent(); args.Handled = true; }
    private void OnUnloaded(object sender, RoutedEventArgs args) { if (!_disposed) CancelResize(); }
    public new void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _dragging = false; ExtentChanged = null; CancelDrag();
        DragStarted -= OnDragStarted; DragDelta -= OnDragDelta; DragCompleted -= OnDragCompleted;
        KeyDown -= OnKeyDownEvent; DoubleTapped -= OnDoubleTapped; Unloaded -= OnUnloaded;
        RemoveHandler(PointerPressedEvent, new PointerEventHandler(OnPointerPressedEvent));
        _theme = null; base.Dispose();
    }
}
