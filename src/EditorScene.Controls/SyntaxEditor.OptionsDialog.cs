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
        using var view = new EditorOptionsView(before); var desired = before;
        var dialog = new ContentDialog { Title = "Text Editor Options", Content = view, XamlRoot = XamlRoot, RequestedTheme = RequestedTheme,
            PrimaryButtonText = "Apply", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        dialog.PrimaryButtonClick += (_, args) => { if (!view.TryGetOptions(out desired)) args.Cancel = true; };
        _optionsDialogOpen = true;
        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            if (_disposed || !ReferenceEquals(Document, document) || !ReferenceEquals(SyntaxLanguage, language)
                || GetViewOptions() != before || MacroSessionActive)
                throw new InvalidOperationException("The options target changed while the dialog was open; the pending values were not applied.");
            if (!ApplyViewOptions(desired)) throw new InvalidOperationException("A host callback changed the editor while options were applied; remaining values were not applied.");
            if (!_disposed) SetStatus("Editor options applied");
            if (!_disposed && ReferenceEquals(Document, document) && ReferenceEquals(SyntaxLanguage, language) && GetViewOptions() == desired) FocusEditor();
        }
        finally
        {
            // Detach the dialog-owned panel before disposing its native children.
            // No document, provider or editor is owned by that panel.
            dialog.Content = null; _optionsDialogOpen = false;
            if (!_disposed) StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
