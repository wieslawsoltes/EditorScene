using EditorScene.Controls;
using EditorScene.LanguageServices;
using EditorScene.Text;
using UnoEdit.Document;
using UnoEdit.Editing;

namespace EditorScene.App;

internal static class NativeReviewChecks
{
    internal static async Task RunAsync(List<string> checks)
    {
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException("Native review regression: " + name); checks.Add(name); }
        using var document = new EditorDocument("aaaa", "review.txt");
        using var editor = new SyntaxEditor { Document = document, SyntaxLanguage = new PlainTextLanguage() };
        editor.Select(0, 2); editor.SelectNextOccurrence();
        Check(editor.Selections.Ranges.Count == 2, "Adjacent occurrences stay independent in the native control");
        editor.InsertText("x"); Check(editor.Text == "xx", "Native text input replaces both adjacent occurrences");
        await editor.ExecuteAsync(EditorCommand.Undo); Check(editor.Text == "aaaa", "Native multi-caret typing is one undo transaction");
        editor.Text = "one one one"; editor.Select(8, 3); editor.SelectNextOccurrence();
        Check(editor.SelectionStart == 0 && editor.Selections.PrimaryIndex == 0, "Occurrence wrapping keeps the newly selected occurrence primary");
        editor.SelectNextOccurrence(); Check(editor.SelectionStart == 4 && editor.Selections.Ranges.Count == 3, "Occurrence selection continues from the wrapped primary caret");
        editor.InsertText("x"); Check(editor.Text == "x x x", "All wrapped occurrence selections receive native text input");
        editor.Text = "ab ab ab"; editor.Select(0, 2); editor.SelectAllOccurrences();
        Check(editor.Selections.Ranges.Count == 3, "Select All Occurrences creates every non-overlapping selection");
        editor.ClearMultiSelection(); editor.Text = "([])"; editor.CaretOffset = 0; editor.RequestAnalysis();
        Check(editor.BracketPairCount == 2, "Native view receives streamed bracket analysis");
        editor.NavigateToMatchingBracket(); Check(editor.CaretOffset == 3, "Matching-bracket navigation moves before matching close");
        editor.CaretOffset = 4; editor.NavigateToMatchingBracket(); Check(editor.CaretOffset == 1, "Matching-bracket navigation preserves after-bracket affinity");
        editor.Select(0, 0); editor.NavigateToMatchingBracket(true);
        Check(editor.SelectionStart == 0 && editor.SelectionLength == 3, "Matching-bracket selection extends without editing");
        editor.ShowWhitespace = true; editor.ShowIndentGuides = false; editor.ShowRuler = true;
        using var replacement = new EditorDocument("replacement", "replacement.txt");
        var oldLanguage = new DocumentBoundLanguage(document.Id); editor.SyntaxLanguage = oldLanguage;
        var newLanguage = new DocumentBoundLanguage(replacement.Id);
        editor.SetDocumentAndLanguage(replacement, newLanguage);
        Check(oldLanguage.ForeignCalls == 0 && newLanguage.ForeignCalls == 0, "Atomic document/provider binding never analyzes a foreign document");
        Check(editor.ShowWhitespace && !editor.ShowIndentGuides && editor.ShowRuler, "View options survive document rebinding");
        var calls = newLanguage.Calls; editor.SyntaxLanguage = newLanguage;
        Check(newLanguage.Calls == calls, "Reassigning the same language avoids redundant analysis");
        editor.SetDocumentAndLanguage(document, new PlainTextLanguage()); editor.Text = "abc"; editor.Select(0, 3);
        var provider = editor.NativeEditor.TextArea.ReadOnlySectionProvider;
        editor.NativeEditor.TextArea.ReadOnlySectionProvider = new NoInsertionProvider();
        Exception? error = null; editor.OperationFailed += (_, e) => error = e;
        await editor.ExecuteAsync(EditorCommand.Uppercase);
        Check(error is InvalidOperationException && editor.Text == "abc", "Replacement honors insertion permission as well as deletion permission");
        editor.NativeEditor.TextArea.ReadOnlySectionProvider = provider;
        using var navigation = new NavigableSymbolSelector { Editor = editor };
        using var outline = new DocumentOutline { Editor = editor };
        Check(editor.Analysis is not null, "Current revision analysis remains available after native edits");
        // Holding a deferred provider makes stale-state behavior deterministic, not timing-dependent.
        var deferred = new DeferredLanguage(); editor.SyntaxLanguage = deferred;
        Check(editor.Analysis is null && navigation.TypeSelector.Items.Count == 0, "Switching to a pending provider clears stale navigation choices");
        editor.Text = "newer";
        Check(editor.Analysis is null, "A new revision cannot expose old analysis while work is pending");
    }
    private sealed class NoInsertionProvider : IReadOnlySectionProvider
    {
        public bool CanInsert(int offset) => false;
        public IEnumerable<ISegment> GetDeletableSegments(ISegment segment) => [segment];
    }
    private sealed class DocumentBoundLanguage(Guid id) : ISyntaxLanguage
    {
        public int Calls { get; private set; }
        public int ForeignCalls { get; private set; }
        public string Id => "plaintext";
        public string DisplayName => "Plain text";
        public IReadOnlyList<string> Extensions => [".txt"];
        public LanguageCapabilities Capabilities => LanguageCapabilities.None;
        public Task<LanguageAnalysis> AnalyzeAsync(DocumentSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            Calls++; if (snapshot.DocumentId != id) { ForeignCalls++; throw new InvalidOperationException("Wrong provider document"); }
            return Task.FromResult(LanguageAnalysis.Empty(snapshot));
        }
    }
    private sealed class DeferredLanguage : ISyntaxLanguage
    {
        public string Id => "plaintext";
        public string DisplayName => "Plain text";
        public IReadOnlyList<string> Extensions => [];
        public LanguageCapabilities Capabilities => LanguageCapabilities.None;
        public async Task<LanguageAnalysis> AnalyzeAsync(DocumentSnapshot snapshot, CancellationToken cancellationToken = default)
        { await Task.Delay(Timeout.Infinite, cancellationToken); return LanguageAnalysis.Empty(snapshot); }
    }
}
