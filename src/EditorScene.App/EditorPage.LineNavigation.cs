using EditorScene.Controls;
using EditorScene.Text;
using EditorScene.Text.Macros;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EditorScene.App;

public sealed partial class EditorPage
{
    private EditorGoToLineView? _goToLineView;
    private async Task GoToLineAsync()
    {
        if (_dialog is not null || _goToLineView is not null) return;
        var editor = _active;
        if (editor.IsDisposed || editor.IsSnippetActive || editor.MacroRecording.State is MacroRecordingState.Recording or MacroRecordingState.Playing) return;
        var document = editor.Document; var snapshot = document.Snapshot(); var language = editor.SyntaxLanguage;
        var session = editor.NativeEditor.TextArea.Session;
        var caret = editor.CaretOffset; var anchor = session.AnchorOffset; var selectionEnd = session.SelectionEndOffset;
        using var view = new EditorGoToLineView(snapshot, caret); _goToLineView = view;
        var input = view.PositionInput; Register("Go to line input", input);
        var dialog = new ContentDialog
        {
            Title = "Go To Line", Content = view, PrimaryButtonText = "Go", CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        LineNavigationTarget target = default;
        bool Current() => !editor.IsDisposed && ReferenceEquals(_active, editor) && ReferenceEquals(editor.Document, document)
            && document.Revision == snapshot.Revision && ReferenceEquals(editor.SyntaxLanguage, language)
            && ReferenceEquals(editor.NativeEditor.TextArea.Session, session) && editor.CaretOffset == caret
            && session.AnchorOffset == anchor && session.SelectionEndOffset == selectionEnd;
        void StateChanged(object? sender, EventArgs args)
        {
            if (!Current()) view.SetUnavailable("The document or selection changed. Cancel and reopen Go To Line.");
            QueueDiagnostics();
        }
        void Unloaded(object sender, RoutedEventArgs args) => dialog.Hide();
        void Validate(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            if (!Current()) view.SetUnavailable("The document or selection changed. Cancel and reopen Go To Line.");
            if (!view.TryGetTarget(out target))
            { args.Cancel = true; input.Focus(FocusState.Programmatic); QueueDiagnostics(); }
        }
        dialog.PrimaryButtonClick += Validate;
        dialog.Opened += (_, _) => { input.Focus(FocusState.Programmatic); input.SelectAll(); QueueDiagnostics(); };
        editor.StateChanged += StateChanged; editor.Unloaded += Unloaded;
        ContentDialogResult result;
        try { result = await ShowDialogAsync(dialog); }
        finally
        {
            editor.StateChanged -= StateChanged; editor.Unloaded -= Unloaded; dialog.PrimaryButtonClick -= Validate;
            if (_ui.TryGetValue("Go to line input", out var registered) && ReferenceEquals(registered, input)) _ui.Remove("Go to line input");
            _goToLineView = null; QueueDiagnostics();
        }
        if (!_loaded || !Current()) return;
        if (result != ContentDialogResult.Primary) { editor.FocusEditor(); return; }
        if (editor.TryNavigateTo(target) && !editor.IsDisposed && ReferenceEquals(_active, editor)
            && ReferenceEquals(editor.Document, document) && document.Revision == snapshot.Revision
            && editor.CaretOffset == target.Offset && editor.SelectionLength == 0) editor.FocusEditor();
    }
}
