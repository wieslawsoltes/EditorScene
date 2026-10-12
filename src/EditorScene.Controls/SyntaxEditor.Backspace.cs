using EditorScene.LanguageServices;
using EditorScene.Text;
using EditorScene.Text.Macros;
using UnoEdit;

namespace EditorScene.Controls;

public sealed partial class SyntaxEditor
{
    private bool _canBackspaceOverSpacesToTabStop = true;
    private bool _backspacing;
    /// <summary>In spaces mode, Backspace in leading spaces reaches the previous tab stop.
    /// Literal tabs, selections, line joins and non-indentation text keep native behavior.</summary>
    public bool CanBackspaceOverSpacesToTabStop
    {
        get => _canBackspaceOverSpacesToTabStop;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this); Document.VerifyAccess();
            if (_canBackspaceOverSpacesToTabStop == value) return;
            _canBackspaceOverSpacesToTabStop = value; StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    /// <summary>Perform ordinary Backspace with the current view's indentation policy.
    /// Multiple carets share one atomic edit; records the same semantic action as the key.</summary>
    public void Backspace()
    {
        ObjectDisposedException.ThrowIf(_disposed, this); Document.VerifyAccess();
        if (_backspacing) throw new InvalidOperationException("A Backspace operation is already active.");
        if (MacroRecording.State == MacroRecordingState.Playing) throw new InvalidOperationException("Macro playback is in progress.");
        if (MacroRecording.State == MacroRecordingState.Recording) MacroRecording.NotifyEditAction(new("native.Backspace"));
        BackspaceWithTabStops();
    }
    private readonly record struct BackspaceContext(EditorDocument Document, long Revision, ISyntaxLanguage Language,
        EditorViewOptions Options, MultiSelection Selections, int Caret, int Anchor, int Active);
    private bool CanBackspaceEdit(TextEdit edit, BackspaceContext context)
    {
        var allowed = CanEdit(edit);
        var session = _editor.TextArea.Session;
        if (_disposed || !ReferenceEquals(Document, context.Document) || context.Document.Revision != context.Revision
            || !ReferenceEquals(SyntaxLanguage, context.Language) || !ReferenceEquals(Selections, context.Selections)
            || GetViewOptions() != context.Options || CaretOffset != context.Caret
            || session.AnchorOffset != context.Anchor || session.SelectionEndOffset != context.Active)
            throw new InvalidOperationException("The Backspace target changed during edit preflight.");
        return allowed;
    }
    private void BackspaceWithTabStops()
    {
        ObjectDisposedException.ThrowIf(_disposed, this); Document.VerifyAccess();
        if (_backspacing) throw new InvalidOperationException("A Backspace operation is already active.");
        if (IsReadOnly || _editor.IsReadOnly) return;
        _backspacing = true;
        try
        {
            var document = Document; var session = _editor.TextArea.Session;
            var options = GetViewOptions(); var selections = Selections;
            var context = new BackspaceContext(document, document.Revision, SyntaxLanguage, options, selections,
                CaretOffset, session.AnchorOffset, session.SelectionEndOffset);
            var soft = options.ConvertTabsToSpaces && options.CanBackspaceOverSpacesToTabStop;
            if (selections.IsActive)
            {
                _multiEditing = true;
                try
                {
                    if (soft) selections.DeleteToPreviousTabStop(options.TabSize, edit => CanBackspaceEdit(edit, context));
                    else selections.Delete(true, edit => CanBackspaceEdit(edit, context));
                    if (!_disposed && ReferenceEquals(Document, document) && ReferenceEquals(Selections, selections)) SynchronizeMultiSelection();
                }
                finally { _multiEditing = false; }
                return;
            }
            if (soft && SelectionLength == 0)
            {
                var snapshot = document.Snapshot(); var line = snapshot.GetLineFromOffset(context.Caret);
                var local = context.Caret - line.Offset;
                var count = local <= line.Length ? GetColumnMap(snapshot, line.LineNumber).GetSoftTabBackspaceLength(local) : 0;
                if (count > 0)
                {
                    var edit = new TextEdit(context.Caret - count, count, "");
                    if (!CanBackspaceEdit(edit, context)) throw new InvalidOperationException("The Backspace span intersects protected text.");
                    // Native anchors track the deletion. Do not reposition the caret
                    // after edit notifications, which may intentionally navigate elsewhere.
                    document.ApplyEdits([edit], context.Revision); return;
                }
            }
            var command = EditingCommands.Backspace; var area = _editor.TextArea;
            if (command.CanExecute(null, area)) command.Execute(null, area);
        }
        finally { _backspacing = false; }
    }
}
