using EditorScene.Controls;
using EditorScene.LanguageServices;
using EditorScene.Text;
using EditorScene.Text.Macros;

namespace EditorScene.App;

internal static class NativeDiagnosticNavigationChecks
{
    internal static async Task RunAsync(List<string> checks)
    {
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException("Diagnostic navigation: " + name); checks.Add(name); }
        using var document = Document(); using var editor = new SyntaxEditor();
        editor.SetDocumentAndLanguage(document, new FixtureLanguage());
        using var grid = new DiagnosticList { Editor = editor };
        Check(grid.CanNavigateDiagnostics && grid.NextDiagnosticButton.IsEnabled && grid.PreviousDiagnosticButton.IsEnabled, "Diagnostic navigation buttons reflect current nonempty analysis");
        grid.SortBy(DiagnosticSortColumn.Code);
        Check(grid.NavigateNextDiagnostic() && editor.SelectionStart == 20 && grid.RowsView.SelectedIndex == 0, "Next diagnostic follows the active sort order from the first visible row");
        var second = grid.NavigateNextDiagnostic(); var index = grid.RowsView.SelectedIndex;
        var third = grid.NavigateNextDiagnostic();
        Check(second && third && index == 1 && grid.RowsView.SelectedIndex == 2 && editor.SelectionStart == 1, "Diagnostics at identical offsets remain separately navigable");
        var wrapped = grid.NavigateNextDiagnostic(); var first = editor.SelectionStart;
        var reversed = grid.NavigateNextDiagnostic(backwards: true);
        Check(wrapped && first == 20 && reversed && grid.RowsView.SelectedIndex == 2, "Native diagnostic traversal wraps in both directions");
        var previousOffset = editor.CaretOffset;
        Check(!grid.NavigateNextDiagnostic(wrap: false) && editor.CaretOffset == previousOffset && grid.RowsView.SelectedIndex == 2, "Non-wrapping diagnostic boundary leaves source and row selection unchanged");
        editor.IsReadOnly = true; var revision = document.Revision; grid.NavigateNextDiagnostic(); editor.IsReadOnly = false;
        Check(document.Revision == revision && !document.IsModified && editor.SelectionStart == 20, "Read-only diagnostic navigation creates no text edit or undo unit");
        await FilterAsync(grid, "same"); grid.RowsView.SelectedIndex = -1;
        var firstDuplicate = grid.NavigateNextDiagnostic(); var duplicateIndex = grid.RowsView.SelectedIndex;
        var secondDuplicate = grid.NavigateNextDiagnostic();
        Check(firstDuplicate && secondDuplicate && grid.VisibleCount == 2 && duplicateIndex == 0 && grid.RowsView.SelectedIndex == 1 && editor.SelectionStart == 1, "Native diagnostic traversal respects the current filtered result");
        await FilterAsync(grid, "absent"); previousOffset = editor.CaretOffset;
        Check(!grid.NavigateNextDiagnostic() && !grid.NextDiagnosticButton.IsEnabled && !grid.PreviousDiagnosticButton.IsEnabled && editor.CaretOffset == previousOffset, "Empty diagnostic filters disable traversal without moving the caret");
        await FilterAsync(grid, ""); editor.MacroRecording.Record(); previousOffset = editor.CaretOffset;
        Check(!grid.NavigateNextDiagnostic() && !grid.CanNavigateDiagnostics && editor.CaretOffset == previousOffset && editor.MacroRecording.RecordedStepCount == 0, "Diagnostic commands cannot bypass active macro recording");
        editor.MacroRecording.Pause();
        Check(grid.NavigateNextDiagnostic() && editor.MacroRecording.State == MacroRecordingState.Paused && editor.MacroRecording.RecordedStepCount == 0, "Paused macro recording allows unrecorded diagnostic navigation");
        editor.MacroRecording.Cancel();
        using var otherDocument = Document(); using var other = new SyntaxEditor(); other.SetDocumentAndLanguage(otherDocument, new PlainTextLanguage());
        grid.Editor = other; previousOffset = editor.CaretOffset;
        Check(!grid.NavigateNextDiagnostic() && grid.VisibleCount == 0 && editor.CaretOffset == previousOffset, "Rebinding the diagnostic grid prevents traversal of the previous document");
        grid.Dispose(); var rejected = false;
        try { grid.NavigateNextDiagnostic(); } catch (ObjectDisposedException) { rejected = true; }
        Check(rejected, "Released diagnostic views reject public navigation");

        foreach (var mode in new[] { "row", "caret", "dispose" })
        {
            using var doc = Document(); using var view = new SyntaxEditor(); view.SetDocumentAndLanguage(doc, new FixtureLanguage()); view.Select(0, 0);
            using var list = new DiagnosticList { Editor = view }; var changed = false;
            list.RowsView.SelectionChanged += (_, _) =>
            {
                if (changed) return; changed = true;
                if (mode == "row") list.RowsView.SelectedIndex = 2;
                else if (mode == "caret") view.CaretOffset = 9;
                else list.Dispose();
            };
            var moved = list.NavigateNextDiagnostic();
            Check(changed && !moved && (mode == "caret" ? view.CaretOffset == 9 : view.CaretOffset == 0), "Diagnostic row-selection callback preserves host " + mode + " changes");
        }
        foreach (var mode in new[] { "caret", "selection", "document", "provider", "edit", "dispose", "multi" })
        {
            using var doc = Document(); using var replacement = Document(); using var view = new SyntaxEditor(); view.SetDocumentAndLanguage(doc, new FixtureLanguage()); view.Select(0, 0);
            using var list = new DiagnosticList { Editor = view }; var changed = false;
            view.StateChanged += (_, _) =>
            {
                if (changed || view.CaretOffset != 2) return; changed = true;
                switch (mode)
                {
                    case "caret": view.CaretOffset = 9; break;
                    case "selection": view.NativeEditor.TextArea.Session.SetSelection(6, 9); break;
                    case "document": view.SetDocumentAndLanguage(replacement, new FixtureLanguage()); break;
                    case "provider": view.SyntaxLanguage = new PlainTextLanguage(); break;
                    case "edit": doc.Buffer.Insert(0, "X"); break;
                    case "dispose": list.Dispose(); break;
                    case "multi": view.Selections.Set([new(4, 4), new(7, 7)]); break;
                }
            };
            var moved = list.NavigateNextDiagnostic();
            var preserved = mode switch
            {
                "caret" => view.CaretOffset == 9,
                "selection" => view.NativeEditor.TextArea.Session.AnchorOffset == 6 && view.NativeEditor.TextArea.Session.SelectionEndOffset == 9,
                "document" => ReferenceEquals(view.Document, replacement),
                "provider" => view.SyntaxLanguage is PlainTextLanguage,
                "edit" => doc.Buffer.GetCharAt(0) == 'X',
                "multi" => view.Selections.Ranges.Count == 2,
                _ => list.Editor is null
            };
            Check(changed && !moved && preserved, "Diagnostic source-navigation callback preserves host " + mode + " changes");
        }
    }
    private static EditorDocument Document() => new("abcdefghijklmnop\nabcdefghijklmnop", "navigation.txt");
    private static async Task FilterAsync(DiagnosticList grid, string query)
    {
        var published = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, EventArgs args) { if (grid.FilterBox.Text == query) published.TrySetResult(true); }
        grid.RowsChanged += Changed;
        try { grid.FilterBox.Text = query; await published.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { grid.RowsChanged -= Changed; }
    }
    private sealed class FixtureLanguage : ISyntaxLanguage
    {
        public string Id => "diagnostic-navigation-fixture";
        public string DisplayName => Id;
        public IReadOnlyList<string> Extensions => [];
        public LanguageCapabilities Capabilities => LanguageCapabilities.Diagnostics;
        public Task<LanguageAnalysis> AnalyzeAsync(DocumentSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(LanguageAnalysis.Empty(snapshot) with { Diagnostics = [
                new("Z", "same first", IssueSeverity.Error, new(1, 1)),
                new("Y", "same second", IssueSeverity.Warning, new(1, 1)),
                new("A", "last position", IssueSeverity.Information, new(20, 1))] });
        }
    }
}
