using EditorScene.LanguageServices;
using EditorScene.Text;
using UnoEdit.Document;
using UnoEdit.Editing;

namespace EditorScene.Controls;

public sealed partial class SyntaxEditor
{
    private bool CanEditWithCurrentPermissions(TextEdit edit)
    {
        if (_disposed || IsReadOnly || _editor.IsReadOnly) return false;
        var document = Document; document.VerifyAccess();
        var revision = document.Revision; var language = SyntaxLanguage;
        var provider = _editor.TextArea.ReadOnlySectionProvider;
        if (edit.NewText.Length > 0 || edit.Length == 0)
        {
            // Permission callbacks are host code. Query once, then verify that
            // the callback did not replace the target or make it read-only.
            var allowed = provider.CanInsert(edit.Offset);
            if (!allowed || !PermissionContextIsCurrent(document, revision, language, provider)) return false;
        }
        if (edit.Length == 0) return true;
        var ranges = _editor.TextArea.GetDeletableSegments(new SimpleSegment(edit.Offset, edit.Length));
        return PermissionContextIsCurrent(document, revision, language, provider)
            && ranges.Length == 1 && ranges[0].Offset == edit.Offset && ranges[0].Length == edit.Length;
    }
    // Keep context checks as a method rather than allocating a captured closure
    // on every ordinary insertion permission query.
    private bool PermissionContextIsCurrent(EditorDocument document, long revision, ISyntaxLanguage language, IReadOnlySectionProvider provider)
        => !_disposed && !IsReadOnly && !_editor.IsReadOnly && ReferenceEquals(Document, document) && document.Revision == revision
            && ReferenceEquals(SyntaxLanguage, language) && ReferenceEquals(_editor.TextArea.ReadOnlySectionProvider, provider);
}
