using EditorScene.Controls;
using EditorScene.LanguageServices;
using EditorScene.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace EditorScene.App;

internal static class NativeCommandBindingChecks
{
    internal static async Task RunAsync(List<string> checks)
    {
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException("Native command bindings: " + name); checks.Add(name); }
        using var document = new EditorDocument("alpha", "bindings.txt");
        using var editor = new SyntaxEditor(); editor.SetDocumentAndLanguage(document, new PlainTextLanguage());
        using var bindings = new EditorCommandBindings { Editor = editor };
        var copy = new Button(); var duplicate = new MenuFlyoutItem(); var cut = new Button(); var glyph = new Border { Opacity = .8 };
        using var copyRegistration = bindings.Bind(copy, EditorCommand.Copy, glyph);
        using var duplicateRegistration = bindings.Bind(duplicate, EditorCommand.Copy);
        using var cutRegistration = bindings.Bind(cut, EditorCommand.Cut);
        Check(!copy.IsEnabled && !duplicate.IsEnabled && Math.Abs(glyph.Opacity - .28) < .00001, "Empty selection disables both native Copy surfaces and dims the original glyph");
        editor.Select(0, 5);
        Check(copy.IsEnabled && duplicate.IsEnabled && cut.IsEnabled && glyph.Opacity == .8, "Selecting text updates bound buttons, menu items and original glyph opacity");
        Check(bindings.Count == 3 && bindings.DistinctCommandCount == 2, "Duplicate toolbar/menu commands share one query group");
        var queries = bindings.QueryCount; var writes = bindings.PropertyWriteCount;
        bindings.Refresh();
        Check(bindings.QueryCount - queries == 2 && bindings.PropertyWriteCount == writes, "One refresh queries each command once and leaves unchanged native properties untouched");
        editor.IsReadOnly = true;
        Check(copy.IsEnabled && duplicate.IsEnabled && !cut.IsEnabled, "Read-only command bindings leave Copy available but disable Cut");
        editor.IsReadOnly = false; editor.NativeEditor.IsReadOnly = true; bindings.Refresh();
        Check(!cut.IsEnabled, "Explicit refresh observes a host changing native read-only state directly");
        editor.NativeEditor.IsReadOnly = false; bindings.Refresh();
        var wrapButton = new ToggleButton(); var wrapMenu = new ToggleMenuFlyoutItem();
        using var wrap1 = bindings.Bind(wrapButton, EditorCommand.ToggleWordWrap);
        using var wrap2 = bindings.Bind(wrapMenu, EditorCommand.ToggleWordWrap);
        editor.WordWrap = true;
        Check(wrapButton.IsChecked == true && wrapMenu.IsChecked, "Native toggle buttons and toggle menu items reflect editor checked state");
        var oldRevision = document.Revision;
        await bindings.TryExecuteAsync(EditorCommand.ToggleWordWrap);
        Check(!editor.WordWrap && wrapButton.IsChecked == false && !wrapMenu.IsChecked && document.Revision == oldRevision, "Bound view commands execute without modifying document text");
        await bindings.TryExecuteAsync(EditorCommand.Uppercase);
        Check(editor.Text == "ALPHA", "Bound editor commands invoke the existing text transformation path");
        await bindings.TryExecuteAsync(EditorCommand.Undo);
        Check(editor.Text == "alpha", "Bound text commands retain native undo behavior");
        editor.Select(0, 0); oldRevision = document.Revision;
        Check(!await bindings.TryExecuteAsync(EditorCommand.Cut) && document.Revision == oldRevision, "Unavailable bound commands are rejected before execution");
        editor.Text = string.Empty;
        Check(editor.GetCommandState(EditorCommand.Indent).IsEnabled && await bindings.TryExecuteAsync(EditorCommand.Indent) && editor.Text.Length != 0,
            "Indent advice and binding execution support an initially empty document");
        editor.Text = "alpha"; editor.Select(0, 5); editor.MacroRecording.Record();
        Check(!editor.GetCommandState(EditorCommand.Copy).IsEnabled && copy.IsEnabled && !editor.GetCommandState(EditorCommand.CompleteWord, EditorCommandQueryMode.CommandSurface).IsEnabled,
            "Context menus remain suspended while recordable toolbar commands stay available during macro recording");
        await bindings.TryExecuteAsync(EditorCommand.Uppercase); editor.MacroRecording.Stop();
        editor.Text = "alpha"; editor.Select(0, 5); await editor.RunMacroAsync();
        Check(editor.Text == "ALPHA", "Binding-invoked recordable commands replay through the real macro recorder");
        var invalid = 0;
        try { bindings.Bind(copy, EditorCommand.Paste); } catch (ArgumentException) { invalid++; }
        try { bindings.Bind(new TextBox(), EditorCommand.Copy); } catch (ArgumentException) { invalid++; }
        try { editor.GetCommandState(EditorCommand.Copy, (EditorCommandQueryMode)99); } catch (ArgumentOutOfRangeException) { invalid++; }
        Check(invalid == 3 && bindings.Count == 5, "Duplicate targets, unsupported controls and invalid query modes fail without changing bindings");
        editor.Select(0, 5); bindings.Refresh(); queries = bindings.QueryCount; writes = bindings.PropertyWriteCount;
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) bindings.Refresh();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
        Check(allocated == 0 && bindings.QueryCount - queries == 3000 && bindings.PropertyWriteCount == writes,
            "One thousand warm native binding refreshes allocate zero managed bytes and perform no redundant native writes");
        CommandBindingBenchmarks.Run(editor);
        using var otherDocument = new EditorDocument("other"); using var other = new SyntaxEditor(); other.SetDocumentAndLanguage(otherDocument, new PlainTextLanguage());
        bindings.Editor = other; queries = bindings.QueryCount;
        editor.CaretOffset = 0; editor.IsReadOnly = true;
        Check(!copy.IsEnabled && bindings.QueryCount == queries, "Rebinding command surfaces detaches the old editor's notifications");
        other.Select(0, 5); duplicateRegistration.Dispose(); other.Select(0, 0);
        Check(duplicate.IsEnabled && !copy.IsEnabled && bindings.Count == 4, "Removing a binding stops updates without disposing or resetting its caller-owned target");
        bindings.Editor = editor; editor.IsReadOnly = false; editor.Select(0, 0);
        var rebound = false;
        var token = cut.RegisterPropertyChangedCallback(Control.IsEnabledProperty, (_, _) =>
        {
            if (!rebound && cut.IsEnabled) { rebound = true; bindings.Editor = other; }
        });
        try { editor.Select(0, 5); }
        finally { cut.UnregisterPropertyChangedCallback(Control.IsEnabledProperty, token); }
        Check(rebound && ReferenceEquals(bindings.Editor, other) && !copy.IsEnabled && !cut.IsEnabled,
            "A synchronous native property callback can rebind without stale writes from the previous editor");
        other.Select(0, 5); bindings.Editor = null;
        Check(!copy.IsEnabled && !cut.IsEnabled && wrapButton.IsChecked == false, "Unbinding disables native targets and clears their checked state");
        bindings.Editor = other;
        using var disposable = new EditorCommandBindings { Editor = other }; var target = new Button(); disposable.Bind(target, EditorCommand.Cut);
        token = target.RegisterPropertyChangedCallback(Control.IsEnabledProperty, (_, _) => { if (!target.IsEnabled) disposable.Dispose(); });
        try { other.IsReadOnly = true; }
        finally { target.UnregisterPropertyChangedCallback(Control.IsEnabledProperty, token); }
        Check(disposable.Count == 0 && !other.IsDisposed && other.Text == "other", "Disposal from a native state callback detaches safely and leaves caller-owned editor/text alive");
        var rejected = false;
        try { disposable.Refresh(); } catch (ObjectDisposedException) { rejected = true; }
        Check(rejected, "Disposed command bindings explicitly reject further mutation");
    }
}
