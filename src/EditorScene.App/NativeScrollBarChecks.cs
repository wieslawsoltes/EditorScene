using EditorScene.Controls;
using EditorScene.Rendering.Skia;
using EditorScene.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace EditorScene.App;

internal static class NativeScrollBarChecks
{
    internal static void Run(List<string> checks)
    {
        void Check(bool value, string name)
        {
            if (!value) throw new InvalidOperationException("Native editor scrollbar: " + name);
            checks.Add(name);
        }
        static ScrollBar[] Bars(SyntaxEditor editor) => ((Panel)editor.NativeEditor.TextArea.Content).Children.OfType<ScrollBar>().ToArray();
        static bool VisibleIndicators(IEnumerable<ScrollBar> bars) => bars.All(b => b.IndicatorMode == ScrollingIndicatorMode.MouseIndicator);

        using var editor = new SyntaxEditor();
        var bars = Bars(editor);
        Check(bars.Length == 2 && bars.Select(b => b.Orientation).Distinct().Count() == 2,
            "Native editor retains exactly its existing two differently oriented scrollbar controls");
        Check(VisibleIndicators(bars), "Native editor sets visible indicators before its first Loaded event");
        Check(bars.All(b => b.ReadLocalValue(ScrollBar.IndicatorModeProperty) is ScrollingIndicatorMode.MouseIndicator),
            "Native editor indicator values are local rather than dependent on application resources");
        Check(bars.All(b => AutomationProperties.GetName(b) == (b.Orientation == Orientation.Vertical
            ? "Editor vertical scrollbar" : "Editor horizontal scrollbar")), "Native editor scrollbars have axis-specific accessible names");
        Check(bars.All(b => b.Orientation == Orientation.Vertical ? b.Width == 14 : b.Height == 14),
            "Native scrollbar visibility preserves the compact native rail dimensions");
        using var unrelated = new ScrollBar();
        ((Panel)editor.NativeEditor.TextArea.Content).Children.Add(unrelated);
        foreach (var theme in new[] { EditorTheme.Light, EditorTheme.Dark, EditorTheme.HighContrast })
        {
            editor.Theme = theme; editor.ApplyTheme();
            Check(VisibleIndicators(bars), "Native scrollbar indicators survive theme changes / " + theme.Name);
        }
        Check(unrelated.IndicatorMode == ScrollingIndicatorMode.None,
            "Editor scrollbar policy does not rewrite subsequently added child or application scrollbars");
        ((Panel)editor.NativeEditor.TextArea.Content).Children.Remove(unrelated);
        editor.WordWrap = true; editor.IsReadOnly = true; editor.FontSize = 22;
        Check(VisibleIndicators(bars), "Native scrollbar indicators survive wrapping, read-only and zoom changes");
        using var document = new EditorDocument("first\nsecond", "scrollbar.txt");
        editor.Document = document;
        Check(Bars(editor).SequenceEqual(bars) && VisibleIndicators(bars),
            "Native document replacement retains the existing visible scrollbar controls");
        using var second = new SyntaxEditor { Document = document };
        second.CopyViewSettingsFrom(editor);
        Check(VisibleIndicators(Bars(second)) && !Bars(second).Intersect(bars).Any(),
            "Native split views own independent persistent scrollbar indicators");
        editor.Dispose(); editor.Dispose();
        Check(VisibleIndicators(Bars(second)), "Disposing one native view leaves the other view's indicators unchanged");
    }
}
