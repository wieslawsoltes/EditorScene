using EditorScene.Text;

namespace EditorScene.Controls;

public sealed partial class SyntaxEditor
{
    private bool _whitespaceEditing;
    /// <summary>Convert ASCII whitespace only inside the current selection, including inline
    /// whitespace. An empty selection is a no-op. Extra selections/snippet sessions must end first.</summary>
    public bool ConvertSelectedWhitespace(bool toTabs) => ChangeWhitespace(toTabs ? WhitespaceConversion.Tabs : WhitespaceConversion.Spaces, false, false, false);
    /// <summary>Canonicalize indentation on selected lines, or the current line with no selection.
    /// A selection ending at the next line's start does not include that next line.</summary>
    public bool TabifySelectedLines(bool toTabs) => ChangeWhitespace(toTabs ? WhitespaceConversion.Tabs : WhitespaceConversion.Spaces, true, true, false);
    /// <summary>Trim selected lines, or the whole document when no text is selected.</summary>
    public bool TrimTrailingWhitespace() => ChangeWhitespace(WhitespaceConversion.TrimTrailing, true, false, true);
    private bool ChangeWhitespace(WhitespaceConversion mode, bool expandToLines, bool leadingOnly, bool wholeDocumentWhenEmpty)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); Document.VerifyAccess();
        if (_whitespaceEditing || IsReadOnly || _editor.IsReadOnly) return false;
        if (Selections.IsActive) throw new NotSupportedException("Whitespace conversion currently requires a single selection; clear extra selections first.");
        if (IsSnippetActive) throw new InvalidOperationException("Complete snippet editing before converting whitespace.");
        var document = Document; var language = SyntaxLanguage; var snapshot = document.Snapshot();
        var session = _editor.TextArea.Session; var options = GetViewOptions();
        var provider = _editor.TextArea.ReadOnlySectionProvider;
        var anchor = session.AnchorOffset; var active = session.SelectionEndOffset; var caret = CaretOffset;
        var selections = Selections; var ranges = selections.Ranges;
        var start = Math.Min(anchor, active); var end = Math.Max(anchor, active);
        if (start == end && !expandToLines) return false;
        if (expandToLines)
        {
            if (start == end && wholeDocumentWhenEmpty) { start = 0; end = snapshot.Length; }
            else
            {
                var first = snapshot.GetLineFromOffset(start); var last = snapshot.GetLineFromOffset(end);
                if (end > start && last.Offset == end && last.LineNumber > first.LineNumber) last = snapshot.GetLine(last.LineNumber - 1);
                start = first.Offset; end = last.Offset + last.Length + last.DelimiterLength;
            }
        }
        var plan = WhitespaceEditPlan.Create(snapshot.GetSpan(start, end - start), mode, options.TabSize, leadingOnly);
        if (plan.Edits.IsEmpty) return false;
        bool Current() => PermissionContextIsCurrent(document, snapshot.Revision, language, provider)
            && ReferenceEquals(_editor.TextArea.Session, session) && session.AnchorOffset == anchor && session.SelectionEndOffset == active
            && CaretOffset == caret && GetViewOptions() == options && ReferenceEquals(Selections, selections) && ReferenceEquals(selections.Ranges, ranges) && !IsSnippetActive;
        _whitespaceEditing = true;
        try
        {
            foreach (var edit in plan.Edits)
            {
                if (!Current()) throw new InvalidOperationException("The whitespace target changed before permission preflight.");
                var allowed = CanEdit(edit);
                if (!Current()) throw new InvalidOperationException("A permission callback changed the whitespace target; no edits were applied.");
                if (!allowed) throw new InvalidOperationException("Whitespace conversion intersects protected text; no edits were applied.");
            }
            if (!Current()) throw new InvalidOperationException("The whitespace target changed before application.");
            // Native anchors preserve selection direction and independent caret state.
            // Do not overwrite a newer host selection by reselecting old integer offsets.
            plan.Apply(document);
            return !_disposed && ReferenceEquals(Document, document) && ReferenceEquals(SyntaxLanguage, language)
                && ReferenceEquals(_editor.TextArea.Session, session) && GetViewOptions() == options;
        }
        finally { _whitespaceEditing = false; }
    }
}
