using EditorScene.Controls;
using EditorScene.Rendering.Skia;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;

namespace EditorScene.App;

internal static class NativeDocumentTabPaletteChecks
{
    internal static void Run(List<string> checks)
    {
        void Check(bool condition, string name)
        { if (!condition) throw new InvalidOperationException("Native tab palette regression: " + name); checks.Add(name); }
        static bool Matches(Brush brush, SKColor expected) => brush is SolidColorBrush solid
            && solid.Color.A == expected.Alpha && solid.Color.R == expected.Red
            && solid.Color.G == expected.Green && solid.Color.B == expected.Blue;
        var caption = new TextBlock { Text = "Document 🙂 *", Foreground = new SolidColorBrush(Microsoft.UI.Colors.Magenta) };
        var panel = new StackPanel(); panel.Children.Add(caption);
        var tab = new Button { Content = panel };
        EditorDocumentTabPalette palette = null!;
        foreach (var theme in new[] { EditorTheme.Light, EditorTheme.Dark, EditorTheme.HighContrast })
        {
            palette = new(theme);
            foreach (var active in new[] { false, true })
            {
                palette.Apply(tab, caption, active);
                Check(Matches(caption.Foreground, theme.Foreground) && Matches(tab.Foreground, theme.Foreground)
                    && Matches(tab.Background, active ? theme.Background : theme.Chrome)
                    && Matches(tab.BorderBrush, active ? theme.Accent : theme.Chrome)
                    && caption.Text == "Document 🙂 *" && ReferenceEquals(tab.Content, panel),
                    "Tab palette " + theme.Name + " / " + (active ? "active" : "inactive") + " explicitly colors retained caption and chrome");
            }
        }
        var otherCaption = new TextBlock(); var other = new Button { Content = otherCaption };
        palette.Apply(other, otherCaption, true);
        Check(ReferenceEquals(tab.Foreground, other.Foreground) && ReferenceEquals(tab.Background, other.Background)
            && ReferenceEquals(tab.BorderBrush, other.BorderBrush) && ReferenceEquals(caption.Foreground, otherCaption.Foreground),
            "Tab palette shares existing brushes across native tabs");
        caption.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Magenta);
        palette.Apply(tab, caption, true);
        Check(ReferenceEquals(caption.Foreground, tab.Foreground), "Tab palette repairs a stale explicit caption foreground");
        var writes = 0;
        var foregroundToken = tab.RegisterPropertyChangedCallback(Control.ForegroundProperty, (_, _) => writes++);
        var backgroundToken = tab.RegisterPropertyChangedCallback(Control.BackgroundProperty, (_, _) => writes++);
        var borderToken = tab.RegisterPropertyChangedCallback(Control.BorderBrushProperty, (_, _) => writes++);
        var captionToken = caption.RegisterPropertyChangedCallback(TextBlock.ForegroundProperty, (_, _) => writes++);
        try
        {
            for (var i = 0; i < 32; i++) palette.Apply(tab, caption, true);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 10_000; i++) palette.Apply(tab, caption, true);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(writes == 0 && allocated == 0, "Ten thousand unchanged native tab palette applications allocate no managed bytes and write no properties");
        }
        finally
        {
            tab.UnregisterPropertyChangedCallback(Control.ForegroundProperty, foregroundToken);
            tab.UnregisterPropertyChangedCallback(Control.BackgroundProperty, backgroundToken);
            tab.UnregisterPropertyChangedCallback(Control.BorderBrushProperty, borderToken);
            caption.UnregisterPropertyChangedCallback(TextBlock.ForegroundProperty, captionToken);
        }
    }
}
