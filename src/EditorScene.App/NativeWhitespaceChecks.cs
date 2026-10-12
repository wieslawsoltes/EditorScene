using EditorScene.Controls;
using EditorScene.LanguageServices;
using EditorScene.Text;
using UnoEdit.Document;
using UnoEdit.Editing;

namespace EditorScene.App;

internal static class NativeWhitespaceChecks
{
    internal static async Task RunAsync(List<string> checks)
    {
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException("Native whitespace regression: " + name); checks.Add(name); }
        using var document = new EditorDocument("x\ty\n\tz", "whitespace.txt");
        using var editor = new SyntaxEditor(); editor.SetDocumentAndLanguage(document, new PlainTextLanguage());
        editor.Select(1, 1); editor.ConvertSelectedWhitespace(false);
        Check(editor.Text == "x   y\n\tz", "Selected whitespace conversion includes inline tabs but leaves unselected lines unchanged");
        await editor.ExecuteAsync(EditorCommand.Undo);
        Check(editor.Text == "x\ty\n\tz", "Selected whitespace edits undo in one native transaction");
        editor.Text = "    a\tb\n    c\td"; editor.Select(0, 8); editor.TabifySelectedLines(true);
        Check(editor.Text == "\ta\tb\n    c\td", "Tabifying selected lines preserves inline tabs");
        Check(editor.Text.EndsWith("    c\td", StringComparison.Ordinal), "Selection ending at a line start excludes that following line");
        editor.Text = "\ta\n\tb"; editor.Select(0, 0); editor.TabifySelectedLines(false);
        Check(editor.Text == "    a\n\tb", "Line untabification with no selection affects only the current line");
        editor.Select(0, 0); var revision = document.Revision;
        Check(!editor.ConvertSelectedWhitespace(true) && document.Revision == revision, "Selected-text whitespace conversion is a no-op for an empty selection");
        editor.Text = "a  \r\nb\t\rc  "; editor.Select(0, 0); editor.TrimTrailingWhitespace();
        Check(editor.Text == "a\r\nb\rc", "Trailing trim preserves mixed line terminators and all non-whitespace text");

        var native = editor.NativeEditor.TextArea; var previous = native.ReadOnlySectionProvider;
        editor.Text = "    protected    "; editor.Select(0, 0);
        native.ReadOnlySectionProvider = new Permissions(_ => true, segment => document.Buffer.GetText(segment.Offset, segment.Length).All(c => c is ' ' or '\t') ? [segment] : []);
        editor.TabifySelectedLines(true); editor.TrimTrailingWhitespace();
        Check(editor.Text == "\tprotected", "Whitespace plans can edit allowed indentation and tails without replacing protected code");
        native.ReadOnlySectionProvider = previous;

        editor.Text = "\ta\n\tb"; editor.Select(0, editor.Text.Length); editor.TabSize = 4;
        native.ReadOnlySectionProvider = new Permissions(_ => { editor.TabSize = 8; return true; }, segment => [segment]);
        var rejected = false; try { editor.ConvertSelectedWhitespace(false); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && editor.Text == "\ta\n\tb" && editor.TabSize == 8, "Changing tab options during permission checks rejects the entire whitespace plan");
        native.ReadOnlySectionProvider = previous; editor.TabSize = 4; editor.Select(0, editor.Text.Length);
        native.ReadOnlySectionProvider = new Permissions(_ => { editor.Select(2, 0); return true; }, segment => [segment]);
        rejected = false; try { editor.ConvertSelectedWhitespace(false); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && editor.Text == "\ta\n\tb" && editor.CaretOffset == 2, "Whitespace preflight preserves a host's newer caret and does not change text");
        native.ReadOnlySectionProvider = previous; editor.Select(0, editor.Text.Length); var calls = 0;
        native.ReadOnlySectionProvider = new Permissions(_ => { if (++calls == 2) editor.IsReadOnly = true; return true; }, segment => [segment]);
        rejected = false; try { editor.ConvertSelectedWhitespace(false); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && calls == 2 && editor.Text == "\ta\n\tb", "A later permission failure prevents partial whitespace edits");
        native.ReadOnlySectionProvider = previous; editor.IsReadOnly = false;

        editor.Text = "\tword"; editor.Select(0, editor.Text.Length);
        void Changed(object? sender, EventArgs args) => editor.Select(2, 0);
        document.Changed += Changed;
        try { editor.ConvertSelectedWhitespace(false); } finally { document.Changed -= Changed; }
        Check(editor.Text == "    word" && editor.CaretOffset == 2 && editor.SelectionLength == 0, "Whitespace application does not overwrite a host caret moved by edit notification");
        editor.Text = "\tword"; native.Session.SetCaretOffset(0); native.Session.SetSelection(5, 0);
        editor.ConvertSelectedWhitespace(false);
        Check(native.Session.AnchorOffset > native.Session.SelectionEndOffset, "Native anchors retain reversed selection direction during whitespace conversion");
        editor.Text = "\tx\t"; editor.Selections.Set([new(0, 1), new(2, 3)]);
        rejected = false; try { editor.ConvertSelectedWhitespace(false); } catch (NotSupportedException) { rejected = true; }
        Check(rejected && editor.Text == "\tx\t", "Unsupported multi-selection whitespace conversion is explicit rather than editing an unintended range");
        editor.ClearMultiSelection(); editor.Select(0, editor.Text.Length);
        editor.MacroRecording.Record(); await editor.ExecuteAsync(EditorCommand.ConvertSelectedTabsToSpaces); editor.MacroRecording.Stop();
        editor.Text = "\tx\t"; editor.Select(0, editor.Text.Length); await editor.RunMacroAsync();
        Check(editor.Text == "    x   ", "New selected-whitespace commands record and replay through native macro actions");
        editor.Text = "\tvalue"; editor.Select(0, 0); revision = document.Revision;
        Check(!editor.TabifySelectedLines(true) && document.Revision == revision, "Already tabified indentation does not create an edit or undo step");
        editor.IsReadOnly = true; editor.Select(0, editor.Text.Length);
        Check(!editor.ConvertSelectedWhitespace(false) && !editor.TrimTrailingWhitespace() && editor.Text == "\tvalue", "Read-only whitespace commands leave text and selection intact");
    }
    private sealed class Permissions(Func<int, bool> insert, Func<ISegment, IEnumerable<ISegment>> delete) : IReadOnlySectionProvider
    {
        public bool CanInsert(int offset) => insert(offset);
        public IEnumerable<ISegment> GetDeletableSegments(ISegment segment) => delete(segment);
    }
}
