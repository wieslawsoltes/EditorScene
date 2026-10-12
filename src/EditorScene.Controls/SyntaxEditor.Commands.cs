using EditorScene.Text;
using UnoEdit.Document;

namespace EditorScene.Controls;

public sealed partial class SyntaxEditor
{
    public async Task ExecuteAsync(EditorCommand command)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try { RecordEditorMacroCommand(command); await ExecuteCommandCoreAsync(command); }
        catch (Exception exception) { ReportError(exception); }
    }
    private async Task ExecuteCommandCoreAsync(EditorCommand command)
    {
        switch (command)
            {
                case EditorCommand.Undo: ClearMultiSelection(); _editor.Undo(); break;
                case EditorCommand.Redo: ClearMultiSelection(); _editor.Redo(); break;
                case EditorCommand.Cut: _editor.Cut(); break;
                case EditorCommand.Copy: _editor.Copy(); break;
                case EditorCommand.Paste: await _editor.PasteAsync(); break;
                case EditorCommand.Delete: _editor.TextArea.Session.Delete(false); break;
                case EditorCommand.SelectAll: ClearMultiSelection(); _editor.SelectAll(); break;
                case EditorCommand.SelectLine: SelectCurrentLine(); break;
                case EditorCommand.DuplicateLine: DuplicateCurrentLine(); break;
                case EditorCommand.DeleteLine: DeleteCurrentLine(); break;
                case EditorCommand.Find: Search.IsReplaceMode = false; Search.Reactivate(); return;
                case EditorCommand.Replace: Search.IsReplaceMode = true; Search.Reactivate(); return;
                case EditorCommand.FindNext: Search.FindNext(); break;
                case EditorCommand.FindPrevious: Search.FindPrevious(); break;
                case EditorCommand.GoToLine: GoToLineRequested?.Invoke(this, EventArgs.Empty); return;
                case EditorCommand.Indent: IndentWithTabStops(); break;
                case EditorCommand.Outdent: _editor.TextArea.Session.Indent(true); break;
                case EditorCommand.Comment: CommentLines(false); break;
                case EditorCommand.Uncomment: CommentLines(true); break;
                case EditorCommand.Uppercase: TransformSelection(s => s.ToUpperInvariant()); break;
                case EditorCommand.Lowercase: TransformSelection(s => s.ToLowerInvariant()); break;
                case EditorCommand.TrimTrailingWhitespace: ExecuteWhitespaceCommand(WhitespaceConversion.TrimTrailing, true, false, true); return;
                case EditorCommand.SortLines: SortSelectedLines(); break;
                case EditorCommand.TabsToSpaces: ExecuteWhitespaceCommand(WhitespaceConversion.Spaces, true, true, true); return;
                case EditorCommand.SpacesToTabs: ExecuteWhitespaceCommand(WhitespaceConversion.Tabs, true, true, true); return;
                case EditorCommand.TabifySelectedLines: ExecuteWhitespaceCommand(WhitespaceConversion.Tabs, true, true, false); return;
                case EditorCommand.UntabifySelectedLines: ExecuteWhitespaceCommand(WhitespaceConversion.Spaces, true, true, false); return;
                case EditorCommand.ConvertSelectedSpacesToTabs: ExecuteWhitespaceCommand(WhitespaceConversion.Tabs, false, false, false); return;
                case EditorCommand.ConvertSelectedTabsToSpaces: ExecuteWhitespaceCommand(WhitespaceConversion.Spaces, false, false, false); return;
                case EditorCommand.CompleteWord: await ShowCompletionAsync(); return;
                case EditorCommand.QuickInfo: await ShowQuickInfoAsync(); return;
                case EditorCommand.SignatureHelp: await ShowSignatureHelpAsync(); return;
                case EditorCommand.FormatDocument: await FormatDocumentAsync(); break;
                case EditorCommand.ToggleBookmark: Indicators.ToggleLine(Document.Buffer.GetLineByOffset(CaretOffset).LineNumber, IndicatorKind.Bookmark); break;
                case EditorCommand.ToggleBreakpoint: Indicators.ToggleLine(Document.Buffer.GetLineByOffset(CaretOffset).LineNumber, IndicatorKind.Breakpoint); break;
                case EditorCommand.NextBookmark: NavigateIndicator(false); break;
                case EditorCommand.PreviousBookmark: NavigateIndicator(true); break;
                case EditorCommand.ClearBookmarks: Indicators.Clear(IndicatorKind.Bookmark); break;
                case EditorCommand.ToggleFold: ToggleFoldAtCaret(); break;
                case EditorCommand.CollapseAll: foreach (var fold in Foldings) fold.IsFolded = true; break;
                case EditorCommand.ExpandAll: foreach (var fold in Foldings) fold.IsFolded = false; break;
                case EditorCommand.ToggleWordWrap: WordWrap = !WordWrap; break;
                case EditorCommand.ToggleWhitespace: ShowWhitespace = !ShowWhitespace; break;
                case EditorCommand.ToggleLineNumbers: ShowLineNumbers = !ShowLineNumbers; break;
                case EditorCommand.ToggleIndentGuides: ShowIndentGuides = !ShowIndentGuides; break;
                case EditorCommand.ToggleRuler: ShowRuler = !ShowRuler; break;
                case EditorCommand.ToggleReadOnly: IsReadOnly = !IsReadOnly; break;
                case EditorCommand.ZoomIn: FontSize = Math.Min(40, FontSize + 1); break;
                case EditorCommand.ZoomOut: FontSize = Math.Max(8, FontSize - 1); break;
                case EditorCommand.ResetZoom: FontSize = 13; break;
                case EditorCommand.SelectNextOccurrence: SelectNextOccurrence(); break;
                case EditorCommand.SelectAllOccurrences: SelectAllOccurrences(); break;
                case EditorCommand.GoToMatchingBracket: NavigateToMatchingBracket(); break;
                case EditorCommand.SelectToMatchingBracket: NavigateToMatchingBracket(true); break;
                case EditorCommand.ExpandSelection: if (await ExpandSelectionAsync()) FocusAfterEditCommand(); return;
                case EditorCommand.ContractSelection: if (ContractSelection()) FocusAfterEditCommand(); return;
                case EditorCommand.ClearExtraSelections: ClearMultiSelection(); break;
                case EditorCommand.ConvertToLf: ConvertLineEndings("\n"); break;
                case EditorCommand.ConvertToCrLf: ConvertLineEndings("\r\n"); break;
                default: throw new ArgumentOutOfRangeException(nameof(command));
            }
            SetStatus(IsReadOnly ? "Read only" : "Ready"); FocusAfterEditCommand();
    }
    private void ExecuteWhitespaceCommand(WhitespaceConversion conversion, bool expandToLines, bool leadingOnly, bool wholeDocumentWhenEmpty)
    {
        if (_whitespaceEditing) return;
        var document = Document; var revision = document.Revision; var language = SyntaxLanguage;
        var session = _editor.TextArea.Session; var anchor = session.AnchorOffset; var active = session.SelectionEndOffset; var caret = CaretOffset;
        var options = GetViewOptions(); var protection = _editor.TextArea.ReadOnlySectionProvider;
        var readOnly = IsReadOnly; var nativeReadOnly = _editor.IsReadOnly;
        if (ChangeWhitespace(conversion, expandToLines, leadingOnly, wholeDocumentWhenEmpty)) { FocusAfterEditCommand(); return; }
        // Empty selections, already canonical input and read-only commands still
        // close a native menu. Return keyboard focus only if this was a genuine
        // unchanged operation, not a callback which replaced its target state.
        if (!_disposed && ReferenceEquals(Document, document) && document.Revision == revision && ReferenceEquals(SyntaxLanguage, language)
            && ReferenceEquals(_editor.TextArea.Session, session) && session.AnchorOffset == anchor && session.SelectionEndOffset == active && CaretOffset == caret
            && ReferenceEquals(_editor.TextArea.ReadOnlySectionProvider, protection) && IsReadOnly == readOnly && _editor.IsReadOnly == nativeReadOnly && GetViewOptions() == options)
            FocusAfterEditCommand();
    }
    private bool CanEdit(TextEdit edit) => CanEditWithCurrentPermissions(edit);
    private void ApplyUserEdits(IEnumerable<TextEdit> changes, long? expectedRevision = null)
    {
        var document = Document; var revision = expectedRevision ?? document.Revision;
        var edits = changes.ToArray();
        if (Document.Revision != revision) throw new InvalidOperationException("The document changed before the operation could be applied.");
        if (edits.Any(edit => !CanEdit(edit))) throw new InvalidOperationException("The operation intersects read-only text.");
        if (!ReferenceEquals(document, Document)) throw new InvalidOperationException("The edit target changed during preflight.");
        document.ApplyEdits(edits, revision);
    }
    private IEnumerable<DocumentLine> SelectedLines(bool wholeDocumentWhenEmpty = false)
    {
        if (wholeDocumentWhenEmpty && SelectionLength == 0) return Document.Buffer.Lines;
        var first = Document.Buffer.GetLineByOffset(SelectionStart); var last = Document.Buffer.GetLineByOffset(SelectionStart + SelectionLength);
        if (SelectionLength > 0 && last.Offset == SelectionStart + SelectionLength && last.LineNumber > first.LineNumber) last = last.PreviousLine;
        return Enumerable.Range(first.LineNumber, last.LineNumber - first.LineNumber + 1).Select(Document.Buffer.GetLineByNumber);
    }
    private void SelectCurrentLine()
    { var line = Document.Buffer.GetLineByOffset(CaretOffset); Select(line.Offset, line.TotalLength); }
    private void DuplicateCurrentLine()
    {
        if (SelectionLength > 0) { var start = SelectionStart; var text = SelectedText; ApplyUserEdits([new(start + text.Length, 0, text)]); Select(start + text.Length, text.Length); return; }
        var line = Document.Buffer.GetLineByOffset(CaretOffset); var offset = line.Offset; var textLine = Document.Buffer.GetText(line.Offset, line.TotalLength);
        if (line.DelimiterLength == 0) textLine += Document.PreferredLineEnding;
        ApplyUserEdits([new(offset, 0, textLine)]); CaretOffset = Math.Min(Document.Buffer.TextLength, offset + textLine.Length);
    }
    private void DeleteCurrentLine()
    {
        var lines = SelectedLines().ToArray(); var start = lines[0].Offset; var end = lines[^1].Offset + lines[^1].TotalLength;
        if (end == Document.Buffer.TextLength && lines[^1].DelimiterLength == 0 && start > 0) start -= lines[0].PreviousLine.DelimiterLength;
        ApplyUserEdits([new(start, end - start, "")]); CaretOffset = Math.Min(start, Document.Buffer.TextLength);
    }
    private void TransformSelection(Func<string, string> transform)
    { if (SelectionLength > 0) { var start = SelectionStart; var result = transform(SelectedText); ApplyUserEdits([new(start, SelectionLength, result)]); Select(start, result.Length); } }
    private void EditSelectedLines(Func<string, string> transform, bool wholeDocumentWhenEmpty = false)
    {
        var edits = SelectedLines(wholeDocumentWhenEmpty).Select(line => new TextEdit(line.Offset, line.Length, transform(Document.Buffer.GetText(line.Offset, line.Length)))).Where(edit => Document.Buffer.GetText(edit.Offset, edit.Length) != edit.NewText).ToArray();
        ApplyUserEdits(edits);
    }
    private void CommentLines(bool remove)
    {
        if (SyntaxLanguage.Id is "xml" or "xaml")
        {
            if (SelectionLength == 0) SelectCurrentLine(); var text = SelectedText;
            var result = remove && text.StartsWith("<!--", StringComparison.Ordinal) && text.EndsWith("-->", StringComparison.Ordinal) ? text[4..^3] : !remove ? "<!--" + text + "-->" : text;
            var start = SelectionStart; ApplyUserEdits([new(start, SelectionLength, result)]); Select(start, result.Length); return;
        }
        var prefix = SyntaxLanguage.Id == "vb" ? "'" : SyntaxLanguage.Id is "python" or "powershell" or "shell" ? "#" : "//";
        EditSelectedLines(text =>
        {
            var indent = text.Length - text.TrimStart(' ', '\t').Length;
            if (!remove) return text.Insert(indent, prefix);
            return text.AsSpan(indent).StartsWith(prefix, StringComparison.Ordinal) ? text.Remove(indent, prefix.Length) : text;
        });
    }
    private void SortSelectedLines()
    {
        var lines = SelectedLines().ToArray(); if (lines.Length < 2) return;
        var sorted = lines.Select(line => Document.Buffer.GetText(line.Offset, line.Length)).Order(StringComparer.Ordinal).ToArray();
        ApplyUserEdits(lines.Select((line, i) => new TextEdit(line.Offset, line.Length, sorted[i])));
    }
    private void ConvertLineEndings(string eol) => ApplyUserEdits(Document.Buffer.Lines.Where(l => l.DelimiterLength > 0).Select(l => new TextEdit(l.EndOffset, l.DelimiterLength, eol)));
    private async Task FormatDocumentAsync()
    {
        if (IsReadOnly) throw new InvalidOperationException("The document is read only.");
        var document = Document; var snapshot = document.Snapshot(); var caret = CaretOffset; var language = SyntaxLanguage;
        var edits = await language.FormatAsync(snapshot);
        if (_disposed || !ReferenceEquals(Document, document) || !ReferenceEquals(SyntaxLanguage, language)) return;
        ApplyUserEdits(edits, snapshot.Revision); CaretOffset = Math.Min(caret, Document.Buffer.TextLength);
    }
    private void NavigateIndicator(bool backwards)
    { if (Indicators.Next(CaretOffset, IndicatorKind.Bookmark, backwards) is { } indicator) { ClearMultiSelection(); _editor.TextArea.Session.MoveTo(indicator.Offset); _editor.TextArea.TextView.EnsureCaretVisible(); } }
    private void ToggleFoldAtCaret()
    {
        var line = Document.Buffer.GetLineByOffset(CaretOffset);
        var fold = Foldings.Where(f => f.StartOffset <= line.EndOffset && f.EndOffset >= line.Offset).OrderBy(f => f.Length).FirstOrDefault();
        if (fold is not null) fold.IsFolded = !fold.IsFolded;
    }
    public void ClearMultiSelection()
    {
        _selections?.Clear(); if (_adornments is not null) _adornments.ExtraSelections = []; Redraw();
    }
    private void SynchronizeMultiSelection()
    {
        if (_selections is null || _selections.Ranges.Count == 0) return;
        var primary = _selections.PrimaryRange!.Value; _editor.TextArea.Session.SetCaretOffset(primary.Active); _editor.TextArea.Session.SetSelection(primary.Anchor, primary.Active);
        if (_adornments is not null) _adornments.ExtraSelections = _selections.Ranges.Where((_, index) => index != _selections.PrimaryIndex).ToArray(); Redraw();
    }
    public void SelectNextOccurrence() => SelectNextOccurrenceCore();
    private static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';
}
