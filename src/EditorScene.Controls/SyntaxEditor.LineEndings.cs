using EditorScene.Text;

namespace EditorScene.Controls;

public sealed partial class SyntaxEditor
{
    private bool _lineEndingEditing;

    /// <summary>Convert CR/LF/CRLF delimiters through the view's normal permissions.
    /// Already-canonical text is a true no-op. Does not change focus or force a caret
    /// position. All permissions are checked before the first edit; cancellation or
    /// a changed provider/document rejects the entire plan before application.</summary>
    public bool ChangeLineEndings(string delimiter, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); Document.VerifyAccess();
        if (delimiter is not ("\n" or "\r" or "\r\n")) throw new ArgumentException("Choose LF, CRLF or CR.", nameof(delimiter));
        cancellationToken.ThrowIfCancellationRequested();
        if (_lineEndingEditing || IsReadOnly || _editor.IsReadOnly) return false;
        if (IsSnippetActive) throw new InvalidOperationException("Finish snippet editing before converting the document's line endings.");
        var document = Document; var revision = document.Revision; var language = SyntaxLanguage;
        var provider = _editor.TextArea.ReadOnlySectionProvider;
        _lineEndingEditing = true;
        try
        {
            var plan = LineEndingEditPlan.Create(document.Snapshot(), delimiter, cancellationToken);
            if (!PermissionContextIsCurrent(document, revision, language, provider)) throw new InvalidOperationException("The conversion context changed during planning.");
            if (plan.Edits.IsEmpty) return false;
            foreach (var edit in plan.Edits)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!CanEditWithCurrentPermissions(edit) || !PermissionContextIsCurrent(document, revision, language, provider))
                    throw new InvalidOperationException("The line-ending conversion intersects protected text or its permission context changed.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            document.ApplyEdits(plan.Edits, revision);
            return true;
        }
        finally { _lineEndingEditing = false; }
    }
}
