using EditorScene.Controls;
using EditorScene.LanguageServices;
using EditorScene.Rendering.Skia;
using EditorScene.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace EditorScene.App;

internal static class NativeWorkspaceReviewChecks
{
    internal static void Run(List<string> checks)
    {
        void Check(bool valid, string name) { if (!valid) throw new InvalidOperationException("Workspace review regression: " + name); checks.Add(name); }
        using var sourceDocument = new EditorDocument("source"); using var targetDocument = new EditorDocument("target"); using var otherDocument = new EditorDocument("other");
        using var source = new SyntaxEditor(); source.SetDocumentAndLanguage(sourceDocument, new PlainTextLanguage()); source.FontSize = 22; source.Theme = EditorTheme.Dark;
        foreach (var action in new[] { "document", "provider", "settings", "dispose" })
        {
            using var target = new SyntaxEditor(); target.SetDocumentAndLanguage(targetDocument, new PlainTextLanguage());
            var changed = false; var replacement = new PlainTextLanguage();
            target.StateChanged += (_, _) =>
            {
                if (changed || target.FontSize != 22) return;
                changed = true;
                switch (action)
                {
                    case "document": target.SetDocumentAndLanguage(otherDocument, replacement); break;
                    case "provider": target.SyntaxLanguage = replacement; break;
                    case "settings": target.Theme = EditorTheme.HighContrast; break;
                    case "dispose": target.Dispose(); break;
                }
            };
            target.CopyViewSettingsFrom(source);
            Check(changed && (action switch
            {
                "document" => ReferenceEquals(target.Document, otherDocument) && ReferenceEquals(target.Theme, EditorTheme.Light),
                "provider" => ReferenceEquals(target.SyntaxLanguage, replacement) && ReferenceEquals(target.Theme, EditorTheme.Light),
                "settings" => ReferenceEquals(target.Theme, EditorTheme.HighContrast),
                _ => target.IsDisposed
            }), "Copying split settings preserves synchronous host " + action + " changes");
        }
        using var split = new ResizableSplitView();
        var first = new Border(); var second = new Border(); split.First = first; split.Second = second;
        split.Measure(new Size(800, 400)); split.Arrange(new Rect(0, 0, 800, 400)); split.UpdateLayout();
        Check(first.ActualWidth >= 799 && second.ActualWidth >= 799 && first.ActualHeight >= 190 && second.ActualHeight >= 190,
            "Split content presenters stretch both panes to their allocated space");
        var rejectedFirst = false; try { split.First = second; } catch (ArgumentException) { rejectedFirst = true; }
        var rejectedSecond = false; try { split.Second = first; } catch (ArgumentException) { rejectedSecond = true; }
        Check(rejectedFirst && rejectedSecond && ReferenceEquals(split.First, first) && ReferenceEquals(split.Second, second),
            "Duplicate native view assignments fail before changing either split pane");
        Check(split.Separator.UseSystemFocusVisuals, "Native split separator retains visible keyboard focus");
        split.Orientation = Orientation.Horizontal; split.Measure(new Size(800, 400)); split.Arrange(new Rect(0, 0, 800, 400)); split.UpdateLayout();
        Check(first.ActualHeight >= 399 && second.ActualHeight >= 399 && first.ActualWidth >= 390 && second.ActualWidth >= 390,
            "Side-by-side orientation stretches both views after native relayout");
        Check(Enum.GetValues<EditorIconKind>().All(kind => Enum.GetName(typeof(EditorGlyph), (int)kind) == kind.ToString()),
            "Public control and reusable renderer glyph enums retain matching stable values");
        split.Second = null; split.First = null;
        using var disposed = new SyntaxEditor(); disposed.Dispose();
        using var status = new EditorStatusBar(); using var errors = new DiagnosticList();
        var statusRejected = false; var errorsRejected = false;
        try { status.Editor = disposed; } catch (ObjectDisposedException) { statusRejected = true; }
        try { errors.Editor = disposed; } catch (ObjectDisposedException) { errorsRejected = true; }
        Check(statusRejected && errorsRejected, "Workspace controls reject disposed view bindings without subscribing");
    }
}
