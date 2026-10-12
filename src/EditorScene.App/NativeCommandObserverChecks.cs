using EditorScene.Controls;
using EditorScene.LanguageServices;
using EditorScene.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EditorScene.App;

internal static class NativeCommandObserverChecks
{
    internal static void Run(List<string> checks)
    {
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException("Command observers: " + name); checks.Add(name); }
        using var document = new EditorDocument("first"); using var replacement = new EditorDocument("second");
        using var editor = new SyntaxEditor(); editor.SetDocumentAndLanguage(document, new PlainTextLanguage());
        using var bindings = new EditorCommandBindings { Editor = editor };
        var next = new Button(); var clear = new MenuFlyoutItem();
        bindings.Bind(next, EditorCommand.NextBookmark); bindings.Bind(clear, EditorCommand.ClearBookmarks);
        var marker = document.Indicators.Add(0, 0, IndicatorKind.Bookmark);
        var added = next.IsEnabled && clear.IsEnabled;
        document.Indicators.Remove(marker);
        Check(added && !next.IsEnabled && !clear.IsEnabled && document.Revision == 0,
            "Host bookmark additions/removals refresh native command state without text edits or explicit Refresh");
        editor.SetDocumentAndLanguage(replacement, new PlainTextLanguage());
        var queries = bindings.QueryCount; document.Indicators.Add(0, 0, IndicatorKind.Bookmark);
        Check(bindings.QueryCount == queries && !next.IsEnabled,
            "Rebinding the same editor to a new document detaches prior indicator notifications");
        replacement.Indicators.Add(0, 0, IndicatorKind.Bookmark);
        Check(next.IsEnabled && clear.IsEnabled,
            "The new document's indicator collection drives its bound native commands");
        var find = new Button(); bindings.Bind(find, EditorCommand.Find);
        var pointerShield = ((Grid)editor.Content).Children.OfType<Border>().Single();
        editor.MacroRecording.Record(); var recordingDisabled = !find.IsEnabled;
        var recordingShielded = pointerShield.Visibility == Visibility.Visible;
        editor.MacroRecording.Pause();
        Check(recordingDisabled && find.IsEnabled && !editor.GetCommandState(EditorCommand.Find).IsEnabled,
            "Paused recording permits interactive command surfaces while context-menu policy remains suspended");
        Check(recordingShielded && pointerShield.Visibility == Visibility.Collapsed,
            "Pausing recording removes the native pointer shield so search fields and buttons can receive input");
        editor.Search.Open(); var searchOpened = editor.Search.IsOpened;
        editor.MacroRecording.Resume(); var resumedDisabled = !find.IsEnabled;
        Check(searchOpened && !editor.Search.IsOpened && pointerShield.Visibility == Visibility.Visible,
            "Resuming recording closes interactive search before restoring native pointer shielding");
        editor.MacroRecording.Cancel();
        Check(resumedDisabled && find.IsEnabled && pointerShield.Visibility == Visibility.Collapsed,
            "Resuming recording disables interactive commands and cancellation restores their normal state");
        bindings.Dispose(); queries = bindings.QueryCount; replacement.Indicators.Clear();
        Check(bindings.Count == 0 && bindings.QueryCount == queries && !editor.IsDisposed && replacement.Buffer.Text == "second",
            "Disposal releases indicator subscriptions without disposing the current editor or document");
    }
}
