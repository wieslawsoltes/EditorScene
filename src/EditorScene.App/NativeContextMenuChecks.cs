using EditorScene.Controls;
using EditorScene.LanguageServices;
using EditorScene.Languages.DotNet;
using EditorScene.Rendering.Skia;
using EditorScene.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using UnoEdit.Document;
using UnoEdit.Editing;
using Windows.Foundation;

namespace EditorScene.App;

internal static class NativeContextMenuChecks
{
    internal static async Task RunAsync(List<string> checks)
    {
        void Check(bool test, string name) { if (!test) throw new InvalidOperationException("Native context menu: " + name); checks.Add(name); }
        using var document = new EditorDocument("alpha beta\nsecond line", "context.txt");
        using var editor = new SyntaxEditor(); editor.SetDocumentAndLanguage(document, new PlainTextLanguage());
        var menu = editor.EditorContextMenu;
        Check(menu.BuildCount == 0 && !editor.IsEditorContextMenuOpen, "Context command menus are built lazily, not for every editor instance");
        Check(!editor.GetCommandState(EditorCommand.Copy).IsEnabled && !editor.GetCommandState(EditorCommand.Undo).IsEnabled && editor.GetCommandState(EditorCommand.Paste).IsEnabled,
            "Context command advice reflects empty selection, undo history and optimistic user-initiated paste");
        editor.Select(0, 5);
        Check(editor.GetCommandState(EditorCommand.Copy).IsEnabled && editor.GetCommandState(EditorCommand.Cut).IsEnabled, "Selected text enables native Cut and Copy commands");
        editor.IsReadOnly = true;
        Check(editor.GetCommandState(EditorCommand.Copy).IsEnabled && !editor.GetCommandState(EditorCommand.Cut).IsEnabled && !editor.GetCommandState(EditorCommand.Paste).IsEnabled,
            "Read-only command advice retains Copy and disables mutations");
        editor.IsReadOnly = false; editor.NativeEditor.IsReadOnly = true;
        Check(!editor.GetCommandState(EditorCommand.Delete).IsEnabled, "Command advice honors native as well as wrapper read-only state");
        editor.NativeEditor.IsReadOnly = false;
        Check(!editor.GetCommandState(EditorCommand.CompleteWord).IsEnabled && !editor.GetCommandState(EditorCommand.FormatDocument).IsEnabled, "Language capability flags control IntelliPrompt and formatting availability");
        editor.SyntaxLanguage = new DotNetLanguage();
        Check(editor.GetCommandState(EditorCommand.CompleteWord).IsEnabled, "Compiler language enables completion without requesting a completion result");
        var permissions = editor.NativeEditor.TextArea.ReadOnlySectionProvider;
        var calls = 0; editor.NativeEditor.TextArea.ReadOnlySectionProvider = new ProbePermissions(() => calls++);
        for (var i = 0; i < 10_000; i++) _ = editor.GetCommandState(EditorCommand.Cut);
        Check(calls == 0, "Command-state queries never invoke protected-range host callbacks");
        var allocated = GC.GetAllocatedBytesForCurrentThread(); var enabled = 0;
        for (var i = 0; i < 10_000; i++) if (editor.GetCommandState(EditorCommand.Copy).IsEnabled) enabled++;
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Check(enabled == 10_000 && allocated == 0, "Warm selection command queries allocate no managed memory for 10000 calls");
        editor.NativeEditor.TextArea.ReadOnlySectionProvider = permissions;
        NativeCommandBenchmarks.Run(editor);
        var flyout = menu.Flyout;
        Check(ReferenceEquals(flyout, menu.Flyout) && menu.BuildCount == 1 && menu.GetCommandItem(EditorCommand.Copy)?.KeyboardAcceleratorTextOverride == "Ctrl+C",
            "One native context menu and its keyboard hints are retained across requests");
        Check(menu.GetCommandItem(EditorCommand.Copy)?.Icon is PathIcon && menu.GetCommandItem(EditorCommand.Cut)?.Icon is PathIcon,
            "Native context command icons use original vector paths without a font dependency");
        editor.MacroRecording.Record();
        Check(!editor.GetCommandState(EditorCommand.Copy).IsEnabled && !menu.Open(), "Context menus remain suspended during macro sessions");
        editor.MacroRecording.Cancel();
        using var split = new SyntaxEditor(); split.SetDocumentAndLanguage(document, new PlainTextLanguage());
        editor.IsDefaultContextMenuEnabled = false; editor.CanMoveCaretForSecondaryPointerButton = false;
        split.CopyViewSettingsFrom(editor);
        Check(!split.IsDefaultContextMenuEnabled && !split.CanMoveCaretForSecondaryPointerButton, "New views copy context-menu settings without copying menu instances or handlers");
        split.IsDefaultContextMenuEnabled = true;
        Check(!editor.IsDefaultContextMenuEnabled && split.ContextMenuBuildCount == 0, "Split context preferences remain independent and do not eagerly allocate a menu");
        editor.IsDefaultContextMenuEnabled = true; editor.CanMoveCaretForSecondaryPointerButton = true;
        await editor.ExecuteAsync(EditorCommand.ToggleDefaultContextMenu);
        await editor.ExecuteAsync(EditorCommand.ToggleSecondaryPointerCaret);
        Check(!editor.GetCommandState(EditorCommand.ToggleDefaultContextMenu).IsChecked && !editor.GetCommandState(EditorCommand.ToggleSecondaryPointerCaret).IsChecked,
            "Native context preference commands expose their actual toggle state");
        await editor.ExecuteAsync(EditorCommand.ToggleDefaultContextMenu);
        await editor.ExecuteAsync(EditorCommand.ToggleSecondaryPointerCaret);
        using var panel = new EditorOptionsView(new());
        panel.RulerColumnInput.Value = 0;
        Check(panel.RulerColumnInput.Value == 0 && !panel.TryGetOptions(out _), "Invalid numeric draft remains invalid instead of silently coercing to its minimum");
        panel.RulerColumnInput.Value = 80;
        Check(panel.TryGetOptions(out var options) && options.RulerColumn == 80, "Corrected numeric draft is accepted without changing other options");

        // Mount the real control so menu lifetime and callback guards execute in
        // the same native popup/focus machinery used by the application.
        var previous = App.MainWindow.Content;
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Loaded(object sender, RoutedEventArgs e) => loaded.TrySetResult();
        editor.Loaded += Loaded; App.MainWindow.Content = editor;
        try
        {
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5)); editor.Loaded -= Loaded;
            editor.SyntaxLanguage = new PlainTextLanguage(); editor.Select(0, 5);
            var view = editor.NativeEditor.TextArea.TextView;
            var inside = view.Viewport.GetCaretRectangle(2);
            Check(menu.Open(new Point(inside.Left, inside.Top + 2)) && editor.SelectionStart == 0 && editor.SelectionLength == 5,
                "Pointer menu opening inside a selection preserves that selection");
            menu.Close();
            editor.CanMoveCaretForSecondaryPointerButton = false;
            var outside = view.Viewport.GetCaretRectangle(12);
            Check(menu.Open(new Point(outside.Left, outside.Top + 2)) && editor.SelectionStart == 0 && editor.SelectionLength == 5,
                "Hosts can disable secondary-pointer caret relocation");
            menu.Close(); editor.CanMoveCaretForSecondaryPointerButton = true;
            Check(menu.Open(new Point(outside.Left, outside.Top + 2)) && editor.CaretOffset == 12 && editor.SelectionLength == 0,
                "Pointer requests outside the selection relocate to the hit-tested position");
            menu.Close();
            void Cancel(object? sender, EditorContextMenuEventArgs args) => args.Cancel = true;
            menu.Requested += Cancel; Check(!menu.Open(), "Host menu request callbacks can cancel native context opening"); menu.Requested -= Cancel;
            using var replacement = new EditorDocument("replacement");
            void Rebind(object? sender, EditorContextMenuEventArgs args) => editor.SetDocumentAndLanguage(replacement, new PlainTextLanguage());
            menu.Requested += Rebind; Check(!menu.Open() && ReferenceEquals(editor.Document, replacement), "Host document replacement aborts a stale menu request"); menu.Requested -= Rebind;
            editor.SetDocumentAndLanguage(document, new PlainTextLanguage());
            editor.IsDefaultContextMenuEnabled = false;
            Check(!menu.Open() && editor.NativeEditor.TextArea.ContextFlyout is null, "Disabled context menus do not fall through to the pinned unguarded menu");
            editor.IsDefaultContextMenuEnabled = true;
            menu.Open(); editor.SetDocumentAndLanguage(replacement, new PlainTextLanguage());
            Check(!menu.IsOpen, "An open menu detaches and closes when its document binding changes");
            menu.Dispose(); var rejected = false; try { menu.Open(); } catch (ObjectDisposedException) { rejected = true; }
            Check(rejected && !editor.IsDisposed && replacement.Buffer.Text == "replacement", "Disposing a context surface does not dispose its caller-owned editor or document");
        }
        finally { editor.Loaded -= Loaded; App.MainWindow.Content = previous; }
        using var drafts = new EditorOptionsView(new() { RulerColumn = 80 });
        var draftLoaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void DraftLoaded(object sender, RoutedEventArgs args) => draftLoaded.TrySetResult();
        drafts.Loaded += DraftLoaded; App.MainWindow.Content = drafts;
        try
        {
            await draftLoaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var ruler = Input(drafts.RulerColumnInput)!;
            ruler.Text = "invalid";
            Check(drafts.RulerColumnInput.Value == 80 && !drafts.TryGetOptions(out _), "Visible invalid numeric text cannot reuse the old NumberBox value before blur");
            drafts.SetOptions(new() { RulerColumn = 80 });
            Check(ruler.Text == "80" && drafts.TryGetOptions(out var resetDraft) && resetDraft.RulerColumn == 80, "Same-value option reset replaces invalid visible text");
            ruler.Text = "96";
            Check(drafts.TryGetOptions(out var uncommitted) && uncommitted.RulerColumn == 96, "Valid visible numeric drafts are read before native value commit");
            Input(drafts.TabSizeInput)!.Text = "3.5";
            Check(!drafts.TryGetOptions(out _), "Fractional visible tab-size drafts are rejected before native formatting can round them");
            drafts.SetOptions(new()); Input(drafts.FontSizeInput)!.Text = "";
            Check(!drafts.TryGetOptions(out _), "Empty realized numeric input cannot reuse its prior font size");
        }
        finally { drafts.Loaded -= DraftLoaded; App.MainWindow.Content = previous; }
        static TextBox? Input(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is TextBox { Name: "InputBox" } value) return value;
                if (Input(child) is { } nested) return nested;
            }
            return null;
        }
    }
    private sealed class ProbePermissions(Action onQuery) : IReadOnlySectionProvider
    {
        public bool CanInsert(int offset) { onQuery(); return true; }
        public IEnumerable<ISegment> GetDeletableSegments(ISegment segment) { onQuery(); return [segment]; }
    }
}
