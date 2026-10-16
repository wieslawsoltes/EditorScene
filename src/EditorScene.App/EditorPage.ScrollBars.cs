using EditorScene.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace EditorScene.App;

public sealed partial class EditorPage
{
    // Read-only observations of the native rails and their actual template parts.
    // This is only called by the opt-in diagnostics bridge; it never changes UI.
    private Dictionary<string, EditorScrollBarState> CaptureEditorScrollBars()
    {
        var result = new Dictionary<string, EditorScrollBarState>(StringComparer.Ordinal);
        Capture("Primary", _primary);
        if (_secondary is not null) Capture("Secondary", _secondary);
        return result;

        void Capture(string owner, SyntaxEditor editor)
        {
            if (editor.NativeEditor.TextArea.Content is not Panel layout) return;
            var view = editor.NativeEditor.TextArea.TextView;
            foreach (var bar in layout.Children.OfType<ScrollBar>())
            {
                var vertical = bar.Orientation == Orientation.Vertical;
                result[owner + "." + bar.Orientation] = new()
                {
                    Name = AutomationProperties.GetName(bar), IndicatorMode = bar.IndicatorMode.ToString(),
                    Bounds = Bounds(bar), ViewBounds = Bounds(view), Enabled = bar.IsEnabled,
                    Value = bar.Value, Maximum = bar.Maximum, ViewportSize = bar.ViewportSize,
                    SmallChange = bar.SmallChange, LargeChange = bar.LargeChange,
                    Offset = vertical ? view.VerticalOffset : view.HorizontalOffset,
                    Extent = vertical ? view.ExtentHeight : view.ExtentWidth,
                    Parts = Descendants(bar).Where(e => e is Thumb or RepeatButton).Select(e => new EditorScrollPartState
                    {
                        Name = e.Name, Kind = e is Thumb ? "Thumb" : "Button", Bounds = Bounds(e),
                        Visible = Visible(e), Opacity = EffectiveOpacity(e)
                    }).ToArray()
                };
            }
        }
        double[] Bounds(FrameworkElement element)
        {
            if (element.XamlRoot is null) return [0, 0, 0, 0];
            var point = element.TransformToVisual(this).TransformPoint(new Point());
            return [point.X, point.Y, element.ActualWidth, element.ActualHeight];
        }
        static bool Visible(FrameworkElement element)
        {
            if (element.XamlRoot is null || element.ActualWidth <= 0 || element.ActualHeight <= 0) return false;
            for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
                if (current is UIElement { Visibility: not Visibility.Visible }) return false;
            return true;
        }
        static double EffectiveOpacity(FrameworkElement element)
        {
            double opacity = 1;
            for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
                if (current is UIElement visual) opacity *= visual.Opacity;
            return opacity;
        }
    }
}

internal sealed class EditorScrollBarState
{
    public string Name { get; set; } = "";
    public string IndicatorMode { get; set; } = "";
    public bool Enabled { get; set; }
    public double[] Bounds { get; set; } = [];
    public double[] ViewBounds { get; set; } = [];
    public double Value { get; set; }
    public double Maximum { get; set; }
    public double ViewportSize { get; set; }
    public double SmallChange { get; set; }
    public double LargeChange { get; set; }
    public double Offset { get; set; }
    public double Extent { get; set; }
    public EditorScrollPartState[] Parts { get; set; } = [];
}
internal sealed class EditorScrollPartState
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public double[] Bounds { get; set; } = [];
    public bool Visible { get; set; }
    public double Opacity { get; set; }
}
