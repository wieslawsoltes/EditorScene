using EditorScene.Controls;
using EditorScene.LanguageServices;
using EditorScene.Text;

namespace EditorScene.App;

internal static class NativeOptionsChecks
{
    internal static async Task RunAsync(List<string> checks)
    {
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException("Editor options: " + name); checks.Add(name); }
        using var document = new EditorDocument("\tX\nz", "options.txt");
        using var editor = new SyntaxEditor(); editor.SetDocumentAndLanguage(document, new PlainTextLanguage());
        using var status = new EditorStatusBar { Editor = editor };
        editor.CaretOffset = 2;
        Check(editor.GetCaretTabColumn() == 6 && status.PositionText.Contains("Col 6") && status.PositionText.Contains("Ch 3"), "Status distinguishes expanded tab columns from UTF-16 character position");
        var maps = editor.ColumnMapBuildCount;
        for (var i = 0; i < 40; i++) { editor.CaretOffset = i % 3; _ = editor.GetCaretTabColumn(); }
        Check(editor.ColumnMapBuildCount == maps, "Caret movement on an unchanged line reuses one sparse column map");
        var desired = new EditorViewOptions { TabSize = 8, ConvertTabsToSpaces = true, ShowWhitespace = true, ShowRuler = true, RulerColumn = 80, WordWrap = true };
        Check(editor.ApplyViewOptions(desired) && editor.GetViewOptions() == desired, "Native view applies validated indentation and display preferences");
        Check(document.Revision == 0 && !document.IsModified, "Changing view preferences does not edit the document or its undo history");
        editor.CaretOffset = 2;
        Check(editor.GetCaretTabColumn() == 10 && status.PositionText.Contains("Col 10"), "Changing tab size refreshes the rendered status and column mapping");
        var invalid = false; try { editor.ApplyViewOptions(desired with { TabSize = 0, FontSize = 30 }); } catch (ArgumentOutOfRangeException) { invalid = true; }
        Check(invalid && editor.GetViewOptions() == desired, "All options validate before any valid-looking sibling setting is changed");
        var calls = 0; void OnState(object? sender, EventArgs args) => calls++;
        editor.StateChanged += OnState; editor.ApplyViewOptions(desired); editor.StateChanged -= OnState;
        Check(calls == 0, "Reapplying the same options does not issue redundant native updates");
        editor.Text = "\tX"; editor.CaretOffset = 2;
        await editor.ExecuteAsync(EditorCommand.Indent);
        Check(editor.Text == "\tX       ", "Indent after an existing tab uses the next expanded tab stop");
        await editor.ExecuteAsync(EditorCommand.Undo);
        Check(editor.Text == "\tX", "Tab-stop indentation remains one native undo transaction");
        editor.ConvertTabsToSpaces = false; editor.CaretOffset = 2; await editor.ExecuteAsync(EditorCommand.Indent);
        Check(editor.Text == "\tX\t", "Tab indentation inserts a literal tab when space conversion is disabled");
        editor.ConvertTabsToSpaces = true; editor.Text = "\ta\nxyz"; editor.Selections.Set([new(6, 6), new(2, 2)]);
        await editor.ExecuteAsync(EditorCommand.Indent);
        Check(editor.Text == "\ta       \nxyz     " && editor.Selections.PrimaryIndex == 0 && editor.CaretOffset == 9, "Each extra caret gets its own tab-stop width without losing primary selection identity");
        await editor.ExecuteAsync(EditorCommand.Undo);
        Check(editor.Text == "\ta\nxyz", "Different multi-caret indentation strings undo together");
        editor.ClearMultiSelection(); editor.Text = "\tX"; editor.Select(2, 0);
        editor.MacroRecording.Record(); await editor.ExecuteAsync(EditorCommand.Indent); editor.MacroRecording.Stop();
        editor.Text = "\tX"; editor.Select(2, 0); await editor.RunMacroAsync();
        Check(editor.Text == "\tX       ", "Recorded indentation commands replay with the same tab-stop semantics");
        using var second = new SyntaxEditor(); second.SetDocumentAndLanguage(document, editor.SyntaxLanguage); second.CopyViewSettingsFrom(editor);
        Check(second.TabSize == 8 && second.ConvertTabsToSpaces && second.RulerColumn == 80, "New split views inherit tab mode and ruler position");
        second.TabSize = 2;
        Check(editor.TabSize == 8, "Split tab-width preferences remain independent after copying");
        using var other = new EditorDocument("other"); editor.SetDocumentAndLanguage(other, new PlainTextLanguage());
        Check(editor.TabSize == 8 && editor.RulerColumn == 80, "Document rebinding preserves view-owned indentation and ruler settings");
        using var panel = new EditorOptionsView(editor.GetViewOptions()); var before = editor.GetViewOptions();
        panel.TabSizeInput.Value = 2; panel.InsertSpacesInput.IsChecked = false;
        Check(panel.TryGetOptions(out var draft) && draft.TabSize == 2 && editor.GetViewOptions() == before, "Editing the native options panel does not mutate the attached editor before acceptance");
        panel.TabSizeInput.Value = double.NaN;
        Check(!panel.TryGetOptions(out _), "An empty numeric option is rejected by the panel instead of applied as zero");
        foreach (var interference in new[] { "document", "provider", "options", "dispose" })
        {
            using var targetDoc = new EditorDocument("x"); using var replacementDoc = new EditorDocument("y");
            using var target = new SyntaxEditor(); target.SetDocumentAndLanguage(targetDoc, new PlainTextLanguage());
            var touched = false;
            target.StateChanged += (_, _) =>
            {
                if (touched || target.TabSize != 8) return; touched = true;
                if (interference == "document") target.SetDocumentAndLanguage(replacementDoc, new PlainTextLanguage());
                else if (interference == "provider") target.SyntaxLanguage = new PlainTextLanguage();
                else if (interference == "options") target.FontSize = 19;
                else target.Dispose();
            };
            var applied = target.ApplyViewOptions(new() { TabSize = 8, FontSize = 30 });
            Check(touched && !applied && (target.IsDisposed || target.FontSize == (interference == "options" ? 19 : 13)), "Option application preserves synchronous host " + interference + " changes");
        }
        await NativeSoftTabChecks.RunAsync(checks);
    }
}
