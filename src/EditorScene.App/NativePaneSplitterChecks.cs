using EditorScene.Controls;
using EditorScene.Rendering.Skia;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace EditorScene.App;

internal static class NativePaneSplitterChecks
{
    internal static void Run(List<string> checks)
    {
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException("Native pane splitter: " + name); checks.Add(name); }
        using var splitter = new EditorPaneSplitter(); var changes = 0;
        splitter.ExtentChanged += (_, _) => changes++;
        Check(splitter.EffectiveExtent == 240 && splitter.Height == 6 && splitter.IsTabStop && splitter.UseSystemFocusVisuals,
            "Native tool splitter has a compact keyboard-focusable default separator");
        splitter.PreferredExtent = 320; splitter.MaximumExtent = 180;
        Check(splitter.PreferredExtent == 320 && splitter.EffectiveExtent == 180,
            "Shrinking native pane capacity preserves the user preference without forcing overflow");
        splitter.MaximumExtent = 600;
        Check(splitter.EffectiveExtent == 320, "Growing native pane capacity restores the exact preferred extent");
        splitter.MaximumExtent = 70;
        Check(splitter.EffectiveExtent == 70, "A very small host relaxes the pane minimum instead of exceeding its capacity");
        splitter.MaximumExtent = 0; splitter.ResizeBy(10);
        Check(splitter.EffectiveExtent == 0 && splitter.PreferredExtent == 320, "Zero-capacity native panes ignore movement without corrupting their preference");
        splitter.MaximumExtent = 200; splitter.ResizeBy(10);
        Check(splitter.PreferredExtent == 190 && splitter.EffectiveExtent == 190, "Native splitter movement starts from the clamped visible size instead of jumping to a hidden preference");
        splitter.ResizeBy(1000); Check(splitter.EffectiveExtent == 120, "Native tool-pane movement clamps to its minimum");
        splitter.ResizeBy(-1000); Check(splitter.EffectiveExtent == 200, "Native tool-pane movement clamps to its maximum");
        splitter.DefaultExtent = 240; splitter.ResetExtent(); splitter.MaximumExtent = 600;
        Check(splitter.EffectiveExtent == 240, "Reset records the default preference even while a smaller viewport clamps it");
        splitter.Axis = Orientation.Horizontal;
        Check(splitter.Width == 6 && double.IsNaN(splitter.Height) && AutomationProperties.GetHelpText(splitter).Contains("width", StringComparison.Ordinal),
            "Native outline separator switches to a vertical grip and width-specific accessible help");
        var baseline = changes;
        for (var i = 0; i < 32; i++) { splitter.PreferredExtent = 240; splitter.MaximumExtent = 600; splitter.ResizeBy(0); }
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) { splitter.PreferredExtent = 240; splitter.MaximumExtent = 600; splitter.ResizeBy(0); }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Check(changes == baseline && allocated == 0, "Ten thousand unchanged native pane updates allocate no managed memory and publish no redundant layout events");
        var rejected = 0;
        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1.0 })
        {
            try { splitter.PreferredExtent = invalid; } catch (ArgumentOutOfRangeException) { rejected++; }
            try { splitter.MaximumExtent = invalid; } catch (ArgumentOutOfRangeException) { rejected++; }
        }
        try { splitter.Axis = (Orientation)99; } catch (ArgumentOutOfRangeException) { rejected++; }
        Check(rejected == 9 && splitter.PreferredExtent == 240 && splitter.MaximumExtent == 600 && splitter.Axis == Orientation.Horizontal,
            "Invalid native pane extents and axes are rejected before mutating state");
        foreach (var theme in new[] { EditorTheme.Light, EditorTheme.Dark, EditorTheme.HighContrast }) splitter.ApplyTheme(theme);
        var brush = splitter.Background; splitter.ApplyTheme(EditorTheme.HighContrast);
        Check(ReferenceEquals(brush, splitter.Background) && splitter.EffectiveExtent == 240,
            "Native pane theme transitions retain geometry and repeated palettes reuse their brush");
        splitter.CancelResize(); splitter.Dispose(); splitter.Dispose(); rejected = 0;
        try { splitter.ResizeBy(1); } catch (ObjectDisposedException) { rejected++; }
        try { splitter.PreferredExtent = 200; } catch (ObjectDisposedException) { rejected++; }
        Check(rejected == 2, "Disposed native pane splitters reject new mutations and tolerate repeated teardown");
    }
}
