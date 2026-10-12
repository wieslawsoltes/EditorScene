using System.ComponentModel;
using EditorScene.Text;

namespace EditorScene.Controls;

public sealed partial class SyntaxEditor
{
    private TabColumnMap? _columnMap;
    private bool _applyingViewOptions;
    private int _rulerColumn = 120;
    public long ColumnMapBuildCount { get; private set; }
    /// <summary>The native editor's shared tab-stop/indentation width.</summary>
    public int TabSize
    {
        get => _editor.Options.IndentationSize;
        set { ObjectDisposedException.ThrowIf(_disposed, this); Document.VerifyAccess(); if (value is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(value)); _editor.Options.IndentationSize = value; }
    }
    public bool ConvertTabsToSpaces
    {
        get => _editor.Options.ConvertTabsToSpaces;
        set { ObjectDisposedException.ThrowIf(_disposed, this); Document.VerifyAccess(); _editor.Options.ConvertTabsToSpaces = value; }
    }
    public int RulerColumn
    {
        get => _rulerColumn;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this); Document.VerifyAccess();
            if (value is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(value));
            if (_rulerColumn == value) return; _rulerColumn = value;
            if (_adornments is not null) _adornments.RulerColumn = value;
            Redraw(); StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    private void OnNativeOptionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_disposed) return;
        if (args.PropertyName is null or "" or "IndentationSize") _columnMap = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
    public EditorViewOptions GetViewOptions()
    {
        ObjectDisposedException.ThrowIf(_disposed, this); Document.VerifyAccess();
        return new() { TabSize = TabSize, ConvertTabsToSpaces = ConvertTabsToSpaces, FontSize = FontSize,
            WordWrap = WordWrap, ShowLineNumbers = ShowLineNumbers, ShowWhitespace = ShowWhitespace,
            ShowIndentGuides = ShowIndentGuides, ShowRuler = ShowRuler, RulerColumn = RulerColumn,
            AutoCompleteBrackets = AutoCompleteBrackets, AutoShowCompletion = AutoShowCompletion,
            CanBackspaceOverSpacesToTabStop = CanBackspaceOverSpacesToTabStop };
    }
    /// <summary>Validate all values before applying. Stops if a synchronous host callback changes
    /// the target document, provider or any already-observed option. Completed changes are not rolled back.</summary>
    public bool ApplyViewOptions(EditorViewOptions desired)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); Document.VerifyAccess(); desired.Validate();
        if (_applyingViewOptions) return false;
        var expected = GetViewOptions(); var document = Document; var revision = document.Revision; var language = SyntaxLanguage;
        bool Current() => !_disposed && ReferenceEquals(Document, document) && document.Revision == revision
            && ReferenceEquals(SyntaxLanguage, language) && GetViewOptions() == expected;
        if (expected == desired) return true;
        _applyingViewOptions = true;
        try
        {
            for (var step = 0; step < 12; step++)
            {
                if (!Current()) return false;
                switch (step)
                {
                    case 0: if (expected.TabSize != desired.TabSize) { expected = expected with { TabSize = desired.TabSize }; TabSize = desired.TabSize; } break;
                    case 1: if (expected.ConvertTabsToSpaces != desired.ConvertTabsToSpaces) { expected = expected with { ConvertTabsToSpaces = desired.ConvertTabsToSpaces }; ConvertTabsToSpaces = desired.ConvertTabsToSpaces; } break;
                    case 2: if (expected.WordWrap != desired.WordWrap) { expected = expected with { WordWrap = desired.WordWrap }; WordWrap = desired.WordWrap; } break;
                    case 3: if (expected.ShowLineNumbers != desired.ShowLineNumbers) { expected = expected with { ShowLineNumbers = desired.ShowLineNumbers }; ShowLineNumbers = desired.ShowLineNumbers; } break;
                    case 4: if (expected.ShowWhitespace != desired.ShowWhitespace) { expected = expected with { ShowWhitespace = desired.ShowWhitespace }; ShowWhitespace = desired.ShowWhitespace; } break;
                    case 5: if (expected.ShowIndentGuides != desired.ShowIndentGuides) { expected = expected with { ShowIndentGuides = desired.ShowIndentGuides }; ShowIndentGuides = desired.ShowIndentGuides; } break;
                    case 6: if (expected.ShowRuler != desired.ShowRuler) { expected = expected with { ShowRuler = desired.ShowRuler }; ShowRuler = desired.ShowRuler; } break;
                    case 7: if (expected.RulerColumn != desired.RulerColumn) { expected = expected with { RulerColumn = desired.RulerColumn }; RulerColumn = desired.RulerColumn; } break;
                    case 8: expected = expected with { AutoCompleteBrackets = desired.AutoCompleteBrackets }; AutoCompleteBrackets = desired.AutoCompleteBrackets; break;
                    case 9: expected = expected with { AutoShowCompletion = desired.AutoShowCompletion }; AutoShowCompletion = desired.AutoShowCompletion; break;
                    case 10: if (expected.FontSize != desired.FontSize) { expected = expected with { FontSize = desired.FontSize }; FontSize = desired.FontSize; } break;
                    case 11: if (expected.CanBackspaceOverSpacesToTabStop != desired.CanBackspaceOverSpacesToTabStop)
                        { expected = expected with { CanBackspaceOverSpacesToTabStop = desired.CanBackspaceOverSpacesToTabStop }; CanBackspaceOverSpacesToTabStop = desired.CanBackspaceOverSpacesToTabStop; } break;
                }
            }
            if (!Current()) return false;
            StateChanged?.Invoke(this, EventArgs.Empty); return Current();
        }
        finally { _applyingViewOptions = false; }
    }
    /// <summary>One-based tab-expanded UTF-16 logical column, not a shaped glyph or bidi coordinate.</summary>
    public long GetCaretTabColumn()
    {
        ObjectDisposedException.ThrowIf(_disposed, this); Document.VerifyAccess();
        var snapshot = Document.Snapshot(); var line = snapshot.GetLineFromOffset(CaretOffset);
        var map = GetColumnMap(snapshot, line.LineNumber);
        return map.GetColumn(Math.Min(CaretOffset - line.Offset, line.Length)) + 1;
    }
    private TabColumnMap GetColumnMap(DocumentSnapshot snapshot, int lineIndex)
    {
        if (_columnMap is null || _columnMap.DocumentId != snapshot.DocumentId || _columnMap.Revision != snapshot.Revision
            || _columnMap.LineIndex != lineIndex || _columnMap.TabSize != TabSize)
        { _columnMap = new(snapshot, lineIndex, TabSize); ColumnMapBuildCount++; }
        return _columnMap;
    }
    private void IndentWithTabStops()
    {
        ObjectDisposedException.ThrowIf(_disposed, this); Document.VerifyAccess();
        if (IsReadOnly) return;
        if (!Selections.IsActive && SelectionLength != 0) { _editor.TextArea.Session.Indent(); return; }
        var document = Document; var snapshot = document.Snapshot(); var language = SyntaxLanguage;
        var options = GetViewOptions(); var selections = Selections;
        string ForOffset(int offset)
        {
            var line = snapshot.GetLineFromOffset(offset); var map = GetColumnMap(snapshot, line.LineNumber);
            return map.GetIndentation(Math.Min(offset - line.Offset, line.Length), options.ConvertTabsToSpaces);
        }
        bool GuardedPermission(TextEdit edit)
        {
            var allowed = CanEdit(edit);
            if (_disposed || !ReferenceEquals(Document, document) || !ReferenceEquals(SyntaxLanguage, language)
                || document.Revision != snapshot.Revision || GetViewOptions() != options || !ReferenceEquals(Selections, selections))
                throw new InvalidOperationException("The indentation target changed during edit preflight.");
            return allowed;
        }
        if (selections.IsActive)
        {
            var texts = selections.Ranges.Select(r => ForOffset(r.Start)).ToArray(); _multiEditing = true;
            try
            {
                selections.ReplaceEach(texts, GuardedPermission);
                if (!_disposed && ReferenceEquals(Selections, selections) && ReferenceEquals(Document, document)) SynchronizeMultiSelection();
            }
            finally { _multiEditing = false; }
        }
        else
        {
            var offset = CaretOffset; var edit = new TextEdit(offset, 0, ForOffset(offset));
            if (!GuardedPermission(edit)) throw new InvalidOperationException("The indentation intersects protected text.");
            if (CaretOffset != offset || SelectionLength != 0) throw new InvalidOperationException("The selection changed during indentation preflight.");
            document.ApplyEdits([edit], snapshot.Revision);
        }
    }
}
