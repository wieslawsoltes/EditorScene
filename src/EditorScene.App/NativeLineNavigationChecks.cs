using EditorScene.Controls;
using EditorScene.LanguageServices;
using EditorScene.Text;

namespace EditorScene.App;

internal static class NativeLineNavigationChecks
{
    internal static void Run(List<string> checks)
    {
        void Check(bool valid, string name) { if (!valid) throw new InvalidOperationException("Native line navigation: " + name); checks.Add(name); }
        using var document = new EditorDocument("a\n\t🙂z\nend", "navigation.txt");
        using var editor = new SyntaxEditor(); editor.SetDocumentAndLanguage(document, new PlainTextLanguage());
        var snapshot = document.Snapshot();
        using var panel = new EditorGoToLineView(snapshot, 0);
        Check(panel.LineCount == 3 && panel.PositionInput.Text == "1" && panel.IsValid && panel.TryGetTarget(out var initial) && initial.Offset == 0,
            "Go To Line native panel starts with the captured current line and a valid private draft");
        panel.PositionInput.Text = "2:0";
        Check(!panel.TryGetTarget(out _) && panel.ValidationMessage.Length > 0 && editor.CaretOffset == 0 && document.Revision == 0,
            "Invalid native character input shows an inline error without moving or editing the document");
        panel.PositionInput.Text = "2:-1";
        Check(!panel.IsValid && !panel.TryGetTarget(out _), "Negative Go To Line character input is rejected rather than coerced to one");
        panel.PositionInput.Text = "2:3";
        Check(!panel.IsValid && panel.ValidationMessage.Contains("Unicode", StringComparison.Ordinal), "Go To Line rejects a caret inside a surrogate pair with an explicit native message");
        panel.PositionInput.Text = "2:4";
        Check(panel.TryGetTarget(out var target) && panel.ValidationMessage.Length == 0 && target.Offset == 5,
            "Correcting the same native navigation draft clears its error and resolves the requested position");
        Check(editor.TryNavigateTo(target) && editor.CaretOffset == 5 && editor.SelectionLength == 0 && document.Revision == 0 && !document.IsModified,
            "Validated navigation moves the native caret without changing text revision or undo history");
        editor.IsReadOnly = true; LineNavigationTarget.TryParse(snapshot, "3:2", out var readOnlyTarget, out _);
        Check(editor.TryNavigateTo(readOnlyTarget) && editor.CaretOffset == 8 && editor.IsReadOnly,
            "Read-only editor views still permit valid line navigation");
        editor.IsReadOnly = false; document.Buffer.Insert(0, "prefix"); var caret = editor.CaretOffset;
        Check(!editor.TryNavigateTo(target) && editor.CaretOffset == caret, "A stale navigation revision is rejected without reinterpreting its old coordinates");
        using var foreign = new EditorDocument(document.Buffer.Text); LineNavigationTarget.TryParse(foreign.Snapshot(), "1", out var foreignTarget, out _);
        Check(!editor.TryNavigateTo(foreignTarget) && !editor.TryNavigateTo(default), "Foreign and default navigation targets cannot move the native editor");
        document.Buffer.Text = "first\nsecond"; LineNavigationTarget.TryParse(document.Snapshot(), "2:2", out var current, out _);
        editor.MacroRecording.Record(); var recordingRejected = !editor.TryNavigateTo(current); editor.MacroRecording.Pause();
        Check(recordingRejected && editor.TryNavigateTo(current), "Line navigation is suspended during recording and allowed when capture is paused");
        editor.MacroRecording.Cancel();
        using var replacement = new EditorDocument("replacement");
        editor.Select(0, 0); var rebound = false;
        void Rebind(object? sender, EventArgs args)
        {
            if (rebound || editor.CaretOffset != current.Offset) return;
            rebound = true; editor.SetDocumentAndLanguage(replacement, new PlainTextLanguage());
        }
        editor.StateChanged += Rebind; var accepted = editor.TryNavigateTo(current); editor.StateChanged -= Rebind;
        Check(rebound && !accepted && ReferenceEquals(editor.Document, replacement) && replacement.Buffer.Text == "replacement",
            "Host document replacement during navigation stops old scroll work without overwriting its replacement");
        editor.SetDocumentAndLanguage(document, new PlainTextLanguage()); editor.Select(0, 0); var moved = false;
        void MoveAgain(object? sender, EventArgs args)
        {
            if (moved || editor.CaretOffset != current.Offset) return; moved = true; editor.CaretOffset = 1;
        }
        editor.StateChanged += MoveAgain; accepted = editor.TryNavigateTo(current); editor.StateChanged -= MoveAgain;
        Check(moved && !accepted && editor.CaretOffset == 1, "Navigation preserves a newer caret chosen by a synchronous host callback");
        panel.SetUnavailable("The captured document changed."); panel.PositionInput.Text = "1";
        Check(!panel.TryGetTarget(out _) && panel.ValidationMessage == "The captured document changed.", "An invalidated native draft cannot silently attach itself to newer document content");
        panel.Dispose(); var rejected = false;
        try { panel.TryGetTarget(out _); } catch (ObjectDisposedException) { rejected = true; }
        Check(rejected && !panel.IsValid && !editor.IsDisposed, "Disposing navigation UI releases its snapshot while leaving caller-owned editors alive");
        using var empty = new EditorDocument(); using var emptyPanel = new EditorGoToLineView(empty.Snapshot(), 0);
        Check(emptyPanel.LineCount == 1 && emptyPanel.TryGetTarget(out var emptyTarget) && emptyTarget.Offset == 0,
            "An empty document exposes one valid line-start position in the native navigation panel");
        editor.Dispose(); rejected = false;
        try { editor.TryNavigateTo(current); } catch (ObjectDisposedException) { rejected = true; }
        Check(rejected, "Disposed native editors explicitly reject navigation requests");
    }
}
