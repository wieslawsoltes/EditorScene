using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EditorScene.Controls;

public sealed partial class SyntaxEditor
{
    private bool _optionsDialogOpen;
    public bool IsOptionsDialogOpen => _optionsDialogOpen;
    /// <summary>Show an editor-local options dialog. Cancel/default-preview do not mutate the view.
    /// Changes apply only if the original document/provider and options remain current.</summary>
    public async Task ShowViewOptionsAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this); Document.VerifyAccess();
        if (_optionsDialogOpen) return;
        if (XamlRoot is null) throw new InvalidOperationException("Load the editor before opening its options.");
        if (MacroSessionActive) throw new InvalidOperationException("Stop macro recording/playback before changing editor options.");
        var document = Document; var language = SyntaxLanguage; var before = GetViewOptions();
        var root = XamlRoot; var caret = CaretOffset; var revision = document.Revision;
        var session = _editor.TextArea.Session; var anchor = session.AnchorOffset; var active = session.SelectionEndOffset;
        using var view = new EditorOptionsView(before); var desired = before;
        var dialog = new ContentDialog { Title = "Text Editor Options", Content = view, XamlRoot = root, RequestedTheme = RequestedTheme,
            PrimaryButtonText = "Apply", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        // A default ContentDialog is too narrow for two useful options columns even
        // on a desktop. Scope the width to this dialog, not all application dialogs.
        dialog.Resources["ContentDialogMaxWidth"] = 664d;
        void UpdateOptionsWidth() => view.Width = Math.Max(0, Math.Min(560, root.Size.Width - 104));
        void OnOptionsRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => UpdateOptionsWidth();
        UpdateOptionsWidth();
        dialog.PrimaryButtonClick += (_, args) => { if (!view.TryGetOptions(out desired)) args.Cancel = true; };
        var unloaded = false; var restoreFocus = false; var expected = before;
        void OnOptionsUnloaded(object sender, RoutedEventArgs args) { unloaded = true; dialog.Hide(); }
        bool Current() => !_disposed && !unloaded && ReferenceEquals(XamlRoot, root) && ReferenceEquals(Document, document)
            && ReferenceEquals(SyntaxLanguage, language) && !MacroSessionActive && GetViewOptions() == expected;
        _optionsDialogOpen = true; Unloaded += OnOptionsUnloaded; root.Changed += OnOptionsRootChanged;
        try
        {
            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                restoreFocus = Current() && document.Revision == revision && CaretOffset == caret
                    && session.AnchorOffset == anchor && session.SelectionEndOffset == active;
                return;
            }
            if (!Current()) throw new InvalidOperationException("The options target changed while the dialog was open; the pending values were not applied.");
            if (!ApplyViewOptions(desired)) throw new InvalidOperationException("A host callback changed the editor while options were applied; remaining values were not applied.");
            expected = desired;
            if (Current()) SetStatus("Editor options applied");
            restoreFocus = Current();
        }
        finally
        {
            Unloaded -= OnOptionsUnloaded; root.Changed -= OnOptionsRootChanged;
            dialog.Content = null; _optionsDialogOpen = false;
            if (!_disposed) StateChanged?.Invoke(this, EventArgs.Empty);
            if (restoreFocus && !_optionsDialogOpen && Current() && document.Revision == revision && CaretOffset == caret
                && session.AnchorOffset == anchor && session.SelectionEndOffset == active) FocusAfterEditCommand();
        }
    }
}
