using EditorScene.Text;
using EditorScene.Text.Macros;

namespace EditorScene.Controls;

public sealed partial class SyntaxEditor
{
    /// <summary>Move to a validated current-snapshot target without editing text or requesting
    /// focus. False means stale input, a suspended interactive session, or synchronous host
    /// interference. A host owns focus after successful navigation; this method never focuses
    /// a replacement document created by a selection callback.</summary>
    public bool TryNavigateTo(LineNavigationTarget target)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); Document.VerifyAccess();
        if (MacroRecording.State is MacroRecordingState.Recording or MacroRecordingState.Playing || IsSnippetActive) return false;
        var document = Document; var language = SyntaxLanguage; var session = _editor.TextArea.Session;
        if (!target.IsCurrent(document.Snapshot())) return false;
        bool Current() => !_disposed && ReferenceEquals(Document, document) && document.Revision == target.Revision
            && ReferenceEquals(SyntaxLanguage, language) && ReferenceEquals(_editor.TextArea.Session, session);
        ClearMultiSelection();
        if (!Current()) return false;
        session.MoveTo(target.Offset);
        if (!Current() || CaretOffset != target.Offset || SelectionLength != 0) return false;
        _editor.TextArea.TextView.EnsureCaretVisible();
        return Current() && CaretOffset == target.Offset && SelectionLength == 0;
    }
}
