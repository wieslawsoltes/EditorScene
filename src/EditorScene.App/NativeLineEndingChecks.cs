using EditorScene.Controls;
using EditorScene.LanguageServices;
using EditorScene.Text;
using UnoEdit.Document;
using UnoEdit.Editing;

namespace EditorScene.App;

internal static class NativeLineEndingChecks
{
    internal static async Task RunAsync(List<string> checks)
    {
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException("Native line endings: " + name); checks.Add(name); }
        using var document = new EditorDocument("a\nb\n", "endings.txt"); using var editor = new SyntaxEditor();
        editor.SetDocumentAndLanguage(document, new PlainTextLanguage()); editor.Select(2, 1);
        Check(!editor.ChangeLineEndings("\n") && document.Revision == 0 && !document.IsModified && !document.Buffer.UndoStack.CanUndo && editor.SelectionLength == 1,
            "Canonical native line-ending conversion preserves selection, clean state, revision and empty undo history");
        document.Buffer.Insert(0, "x"); document.Buffer.UndoStack.Undo(); var revision = document.Revision;
        await editor.ExecuteAsync(EditorCommand.ConvertToLf);
        Check(document.Revision == revision && document.Buffer.UndoStack.CanRedo && !document.IsModified,
            "An already-LF native command does not erase an existing redo branch");
        const string mixed = "a\r\nb\nc\rd"; editor.Text = mixed;
        Check(editor.ChangeLineEndings("\r\n") && editor.Text == "a\r\nb\r\nc\r\nd", "Native CRLF conversion changes only noncanonical delimiters in mixed source");
        await editor.ExecuteAsync(EditorCommand.Undo);
        Check(editor.Text == mixed, "All minimal native line-ending changes undo in one transaction");
        var area = editor.NativeEditor.TextArea; var previous = area.ReadOnlySectionProvider;
        editor.Text = "a\r\nb\n"; var calls = 0;
        area.ReadOnlySectionProvider = new Permissions(_ => false, segment =>
        { calls++; return document.Buffer.GetText(segment.Offset, segment.Length) == "\r" ? [segment] : []; });
        Check(editor.ChangeLineEndings("\n") && editor.Text == "a\nb\n" && calls == 1,
            "Removing CR preserves protected LF and ordinary code without requesting unnecessary insertion permission");
        area.ReadOnlySectionProvider = previous; editor.Text = "a\nb\n"; calls = 0;
        area.ReadOnlySectionProvider = new Permissions(_ => ++calls != 2, segment => [segment]);
        var rejected = false; try { editor.ChangeLineEndings("\r\n"); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && calls == 2 && editor.Text == "a\nb\n", "A later insertion denial rejects the complete native line-ending plan before any edit");
        area.ReadOnlySectionProvider = new Permissions(_ => { editor.IsReadOnly = true; return true; }, segment => [segment]);
        rejected = false; try { editor.ChangeLineEndings("\r\n"); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && editor.Text == "a\nb\n" && editor.IsReadOnly, "A read-only transition inside a permission callback rejects line-ending mutation");
        area.ReadOnlySectionProvider = previous; editor.IsReadOnly = false;
        using var replacement = new EditorDocument("replacement");
        area.ReadOnlySectionProvider = new Permissions(_ => { editor.SetDocumentAndLanguage(replacement, new PlainTextLanguage()); return true; }, segment => [segment]);
        rejected = false; try { editor.ChangeLineEndings("\r\n"); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && document.Buffer.Text == "a\nb\n" && replacement.Buffer.Text == "replacement", "Permission callbacks cannot redirect an old delimiter plan into a replacement document");
        editor.SetDocumentAndLanguage(document, new PlainTextLanguage()); area.ReadOnlySectionProvider = previous;
        using var cancel = new CancellationTokenSource();
        area.ReadOnlySectionProvider = new Permissions(_ => { cancel.Cancel(); return true; }, segment => [segment]);
        rejected = false; try { editor.ChangeLineEndings("\r\n", cancel.Token); } catch (OperationCanceledException) { rejected = true; }
        Check(rejected && editor.Text == "a\nb\n", "Cancellation after successful native preflight prevents all line-ending edits");
        area.ReadOnlySectionProvider = previous;
        var crCommand = Enum.Parse<EditorCommand>("ConvertToCr");
        editor.MacroRecording.Record(); await editor.ExecuteAsync(crCommand); editor.MacroRecording.Stop();
        editor.Text = "x\r\ny\n"; await editor.RunMacroAsync();
        Check(editor.Text == "x\ry\r", "The new CR conversion command records and replays through the native semantic macro path");
        editor.IsReadOnly = true;
        Check(!editor.GetCommandState(crCommand, EditorCommandQueryMode.CommandSurface).IsEnabled,
            "Read-only native command advice disables the CR conversion alongside LF and CRLF");
        revision = document.Revision;
        Check(!editor.ChangeLineEndings("\n") && document.Revision == revision, "Direct read-only line-ending conversion remains a nonediting operation");
        editor.IsReadOnly = false; editor.Text = "a\r\nb";
        void MoveCaret(object? sender, EventArgs args) => editor.Select(1, 0);
        document.Changed += MoveCaret;
        try { editor.ChangeLineEndings("\n"); } finally { document.Changed -= MoveCaret; }
        Check(editor.Text == "a\nb" && editor.CaretOffset == 1, "Minimal line-ending application preserves a newer caret chosen by a document-change observer");
        NativeLineEndingStatusChecks.Run(checks);
    }
    private sealed class Permissions(Func<int, bool> insert, Func<ISegment, IEnumerable<ISegment>> delete) : IReadOnlySectionProvider
    {
        public bool CanInsert(int offset) => insert(offset);
        public IEnumerable<ISegment> GetDeletableSegments(ISegment segment) => delete(segment);
    }
}
