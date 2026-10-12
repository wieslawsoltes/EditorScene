using EditorScene.Controls;
using EditorScene.LanguageServices;
using EditorScene.Text;
using UnoEdit.Document;
using UnoEdit.Editing;

namespace EditorScene.App;

internal static class NativeSoftTabChecks
{
    internal static async Task RunAsync(List<string> checks)
    {
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException("Soft-tab Backspace: " + name); checks.Add(name); }
        using var document = new EditorDocument("      word", "backspace.txt");
        using var editor = new SyntaxEditor(); editor.SetDocumentAndLanguage(document, new PlainTextLanguage());
        editor.ApplyViewOptions(new() { TabSize = 4, ConvertTabsToSpaces = true });
        editor.Select(6, 0); editor.Backspace();
        Check(editor.Text == "    word" && editor.CaretOffset == 4, "Soft-tab Backspace reaches the previous leading-space stop with native anchor tracking");
        await editor.ExecuteAsync(EditorCommand.Undo);
        Check(editor.Text == "      word", "One Undo restores the entire soft-tab deletion");
        editor.Select(6, 0); editor.CanBackspaceOverSpacesToTabStop = false; editor.Backspace();
        Check(editor.Text == "     word", "Disabling soft-tab Backspace restores single-character native deletion");
        editor.CanBackspaceOverSpacesToTabStop = true; editor.ConvertTabsToSpaces = false;
        editor.Text = "      word"; editor.Select(6, 0); editor.Backspace();
        Check(editor.Text == "     word", "Literal-tab input mode does not enable soft-space Backspace");
        editor.ConvertTabsToSpaces = true; editor.TabSize = 8;
        editor.Text = "\t   word"; editor.Select(4, 0); editor.Backspace();
        Check(editor.Text == "\tword" && editor.CaretOffset == 1, "Mixed indentation removes spaces without consuming its preceding literal tab");
        editor.Backspace(); Check(editor.Text == "word", "Backspace directly after a literal tab retains ordinary deletion");
        editor.Text = "x    "; editor.Select(5, 0); editor.Backspace();
        Check(editor.Text == "x   ", "Spaces following non-indentation content are removed one character at a time");
        editor.Text = "    word"; editor.Select(1, 2); editor.Backspace();
        Check(editor.Text == "  word", "A nonempty selection uses ordinary selected-text deletion");
        editor.Text = "a\r\nb"; editor.Select(3, 0); editor.Backspace();
        Check(editor.Text == "ab", "Line-start Backspace removes the complete CRLF delimiter");
        editor.Text = "🙂é"; editor.Select(editor.Text.Length, 0); editor.Backspace();
        Check(editor.Text == "🙂", "Non-indentation Backspace retains native combining-grapheme deletion");
        editor.TabSize = 4; editor.Text = "      a\r\n\t   b";
        editor.Selections.Set([new(6, 6), new(13, 13)]); editor.Backspace();
        Check(editor.Text == "    a\r\n\tb" && editor.CaretOffset == 8 && editor.Selections.PrimaryIndex == 1,
            "Native multiple carets delete different soft-tab spans and retain the primary caret");
        await editor.ExecuteAsync(EditorCommand.Undo);
        Check(editor.Text == "      a\r\n\t   b", "Native multiple soft-tab deletions share one Undo transaction");
        editor.ClearMultiSelection(); editor.Text = "      word"; editor.Select(6, 0);
        editor.MacroRecording.Record(); editor.Backspace(); editor.MacroRecording.Stop();
        Check(editor.MacroRecording.LastMacroAction!.Steps.Length == 1 && editor.MacroRecording.LastMacroAction.Steps[0].Command == "native.Backspace",
            "Soft-tab Backspace records exactly one compatible semantic macro step");
        editor.Text = "      word"; editor.Select(6, 0); await editor.RunMacroAsync();
        Check(editor.Text == "    word", "Native Backspace macro playback uses the current soft-tab policy");
        editor.Text = "    word"; editor.Select(4, 0); editor.IsReadOnly = true; editor.Backspace();
        Check(editor.Text == "    word", "Read-only soft-tab Backspace is harmless"); editor.IsReadOnly = false;
        editor.CanBackspaceOverSpacesToTabStop = false;
        using var split = new SyntaxEditor(); split.CopyViewSettingsFrom(editor);
        Check(!split.CanBackspaceOverSpacesToTabStop, "New split views inherit the soft-tab Backspace policy");
        split.CanBackspaceOverSpacesToTabStop = true;
        Check(!editor.CanBackspaceOverSpacesToTabStop, "Copied soft-tab policies remain independent between views");
        var before = editor.GetViewOptions();
        Check(editor.ApplyViewOptions(before with { CanBackspaceOverSpacesToTabStop = true }) && editor.CanBackspaceOverSpacesToTabStop,
            "Immutable view options apply the soft-tab preference");
        foreach (var interference in new[] { "options", "caret", "provider", "read-only" })
        {
            using var doc = new EditorDocument("    x"); using var target = new SyntaxEditor();
            target.SetDocumentAndLanguage(doc, new PlainTextLanguage()); target.ConvertTabsToSpaces = true; target.Select(4, 0);
            target.NativeEditor.TextArea.ReadOnlySectionProvider = new CallbackProvider(() =>
            {
                if (interference == "options") target.TabSize = 8;
                else if (interference == "caret") target.CaretOffset = 1;
                else if (interference == "provider") target.SyntaxLanguage = new PlainTextLanguage();
                else target.NativeEditor.IsReadOnly = true;
            });
            var rejected = false; try { target.Backspace(); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && doc.Buffer.Text == "    x", "Soft-tab deletion rejects synchronous " + interference + " interference before editing");
        }
        using var inputDocument = new EditorDocument(""); using var inputEditor = new SyntaxEditor();
        inputEditor.SetDocumentAndLanguage(inputDocument, new PlainTextLanguage());
        var payload = new string('x', EditorScene.Text.Macros.EditorMacro.MaxTextLength + 1);
        Exception? inputError = null; inputEditor.OperationFailed += (_, error) => inputError = error;
        inputEditor.InsertText(payload);
        Check(inputError is null && inputDocument.Buffer.TextLength == payload.Length,
            "Ordinary native text insertion is not restricted by the inactive macro recorder text limit");
        inputEditor.Text = string.Empty; inputEditor.Select(0, 0); inputEditor.MacroRecording.Record();
        inputEditor.InsertText(payload);
        Check(inputError is ArgumentOutOfRangeException && inputDocument.Buffer.TextLength == 0 && inputEditor.MacroRecording.RecordedStepCount == 0,
            "Active macro recording still rejects an oversized text step before editing or recording it");
        inputEditor.MacroRecording.Cancel();
    }
    private sealed class CallbackProvider(Action callback) : IReadOnlySectionProvider
    {
        public bool CanInsert(int offset) => true;
        public IEnumerable<ISegment> GetDeletableSegments(ISegment segment) { callback(); return [segment]; }
    }
}
