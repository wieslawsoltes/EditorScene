using System.Collections.Immutable;
using EditorScene.LanguageServices;
using EditorScene.Text;

namespace EditorScene.Controls;

public sealed partial class SyntaxEditor
{
    private ImmutableArray<ClassificationSpan> _bracketClassifications;
    public int BracketPairCount => _brackets?.Revision == Document.Revision ? _brackets.PairCount : 0;
    public RangeQueryStatistics DiagnosticQueryStatistics => _adornments?.LastDiagnosticQuery ?? default;
    /// <summary>Bind a document and provider together, without analyzing the new document with the old document's server session.</summary>
    public void SetDocumentAndLanguage(EditorDocument document, ISyntaxLanguage language)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(document); ArgumentNullException.ThrowIfNull(language);
        document.VerifyAccess();
        if (ReferenceEquals(document, Document)) { SyntaxLanguage = language; return; }
        _language = language; document.LanguageId = language.Id; Document = document;
    }
    private void PrepareBrackets(LanguageAnalysis analysis)
    {
        if (_brackets is not null && _brackets.DocumentId == Document.Id && _brackets.Revision == Document.Revision && _bracketClassifications.Equals(analysis.Classifications)) return;
        var prepared = new BracketMap(Document.Snapshot(), analysis.Classifications);
        _brackets = prepared; _bracketClassifications = analysis.Classifications;
    }
    /// <summary>Move to a matched bracket, optionally extending the active selection. No text is changed.</summary>
    public void NavigateToMatchingBracket(bool extendSelection = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_brackets is null || _brackets.DocumentId != Document.Id || _brackets.Revision != Document.Revision || !_brackets.TryGetPair(CaretOffset, out var source, out var target))
        { SetStatus("No current matching bracket at the caret."); return; }
        var destination = target.Offset + (CaretOffset > source.Offset ? 1 : 0);
        var anchor = _editor.TextArea.Session.AnchorOffset;
        ClearMultiSelection();
        if (extendSelection) _editor.TextArea.Session.SetSelection(anchor, destination);
        else _editor.TextArea.Session.MoveTo(destination);
        _editor.TextArea.TextView.EnsureCaretVisible(); FocusEditor();
    }
    private bool SelectWordForOccurrences()
    {
        if (SelectionLength != 0) return false;
        var source = Document.Buffer; var start = CaretOffset; var end = start;
        while (start > 0 && IsWord(source.GetCharAt(start - 1))) start--;
        while (end < source.TextLength && IsWord(source.GetCharAt(end))) end++;
        if (start == end) return true;
        _editor.Select(start, end - start); Selections.Set([new(start, end)]); return true;
    }
    private void SelectNextOccurrenceCore()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (SelectWordForOccurrences()) return;
        var snapshot = Document.Snapshot(); var pattern = new SnapshotLiteralSearch(SelectedText);
        var ranges = Selections.Ranges.ToList();
        if (ranges.Count == 0) ranges.Add(new(SelectionStart, SelectionStart + SelectionLength));
        var start = SelectionStart + SelectionLength;
        var matches = pattern.FindAll(new(snapshot, start, snapshot.Length - start))
            .Concat(pattern.FindAll(new(snapshot, 0, start)));
        foreach (var match in matches)
        {
            if (ranges.Any(r => match.Offset < r.End && match.End > r.Start || r.Length == 0 && r.Start == match.Offset)) continue;
            ranges.Add(new(match.Offset, match.End)); Selections.Set(ranges); SynchronizeMultiSelection(); return;
        }
    }
    /// <summary>Select every non-overlapping occurrence of the selected literal; reject more than 10,000 carets.</summary>
    public void SelectAllOccurrences()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SelectWordForOccurrences(); if (SelectionLength == 0) return;
        var snapshot = Document.Snapshot(); var pattern = new SnapshotLiteralSearch(SelectedText);
        var ranges = pattern.FindAll(new(snapshot, 0, snapshot.Length), 10_001).Select(m => new SelectionRange(m.Offset, m.End)).ToArray();
        if (ranges.Length > 10_000) throw new InvalidOperationException("Select All Occurrences is limited to 10,000 carets.");
        Selections.Set(ranges); SynchronizeMultiSelection();
    }
}
