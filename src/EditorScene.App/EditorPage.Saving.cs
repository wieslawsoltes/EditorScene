using System.Runtime.ExceptionServices;
using System.Text;
using EditorScene.Controls;
using EditorScene.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Provider;
using Windows.System;

namespace EditorScene.App;

public sealed partial class EditorPage
{
    private Button? _saveAllButton;
    private MenuFlyoutItem? _saveAllMenu, _saveAllOverflow;
    private bool _saveAllRunning;
    private int _saveAllCompleted, _saveAllTotal;

    private void AddSaveAllMenu(MenuBarItem file)
    {
        _saveAllMenu = Item(file, "Save All", SaveAllDocumentsAsync, "Ctrl+Alt+S");
        _saveAllMenu.Icon = EditorFileIcons.CreateSaveAll();
    }
    private void AddSaveAllTool()
    {
        _saveAllButton = new Button
        {
            Content = EditorFileIcons.CreateSaveAll(), Width = 26, Height = 25, MinHeight = 25,
            Padding = new Thickness(4), BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent)
        };
        ToolTipService.SetToolTip(_saveAllButton, "Save All (Ctrl+Alt+S)");
        _saveAllButton.Click += (_, _) => _ = GuardAsync(SaveAllDocumentsAsync);
        _toolbar.Children.Add(_saveAllButton); Register("Toolbar Save All", _saveAllButton);
        _saveAllOverflow = new MenuFlyoutItem { Text = "Save All", FontSize = 12, KeyboardAcceleratorTextOverride = "Ctrl+Alt+S", Icon = EditorFileIcons.CreateSaveAll() };
        _saveAllOverflow.Click += (_, _) => _ = GuardAsync(SaveAllDocumentsAsync);
        AddToolbarOverflow(_saveAllOverflow);
        RefreshSaveAllControls();
    }
    private void RefreshSaveAllControls()
    {
        var enabled = !_documentOperations.IsBusy && _documents.Any(entry => entry.Document.IsModified);
        if (_saveAllMenu is { } menu && menu.IsEnabled != enabled) menu.IsEnabled = enabled;
        if (_saveAllOverflow is { } overflow && overflow.IsEnabled != enabled) overflow.IsEnabled = enabled;
        if (_saveAllButton is { } button)
        {
            if (button.IsEnabled != enabled) button.IsEnabled = enabled;
            if (button.Content is UIElement icon)
            {
                var opacity = enabled ? 1 : .35;
                if (icon.Opacity != opacity) icon.Opacity = opacity;
            }
        }
    }
    private bool HandleSaveAllKey(KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.S) return false;
        bool Down(VirtualKey key) => (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        if (!Down(VirtualKey.Control) || !Down(VirtualKey.Menu) || Down(VirtualKey.Shift)) return false;
        _ = GuardAsync(SaveAllDocumentsAsync); return true;
    }
    private async Task SaveAllDocumentsAsync()
    {
        if (_dialog is not null || _documentOperations.IsBusy) return;
        var active = _active;
        try
        {
            await RunDocumentOperationAsync(async () =>
            {
                var plan = DocumentSavePlan.Create(_documents.Select(entry => new DocumentSaveCandidate(entry.Document.Id, entry.Document.IsModified)));
                _saveAllCompleted = 0; _saveAllTotal = plan.DocumentIds.Length; _saveAllRunning = true; QueueDiagnostics();
                try
                {
                    foreach (var id in plan.DocumentIds)
                    {
                        var entry = _documents.FirstOrDefault(candidate => candidate.Document.Id == id);
                        if (entry is null || !entry.Document.IsModified) continue;
                        if (!await SaveDocumentCoreAsync(entry, false))
                        { AddEvent("Save All stopped; later documents were not saved."); return false; }
                        _saveAllCompleted++; QueueDiagnostics();
                    }
                    if (_saveAllTotal != 0) AddEvent($"Save All completed: {_saveAllCompleted} document(s).");
                    return true;
                }
                finally { _saveAllRunning = false; QueueDiagnostics(); }
            });
        }
        finally
        {
            RefreshSaveAllControls();
            if (_loaded && _dialog is null && ReferenceEquals(active, _active) && !active.IsDisposed) active.FocusEditor();
        }
    }

    private async Task<bool> SaveDocumentCoreAsync(WorkspaceDocument entry, bool saveAs)
    {
        if (!_documents.Contains(entry)) return false;
        var file = entry.File;
#if __WASM__
        // Upload-picker files are private temporary copies, not writable original handles.
        saveAs = true;
#endif
        if (file is null || saveAs)
        {
            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = entry.Document.FileName };
            var extension = Path.GetExtension(entry.Document.FileName); if (string.IsNullOrEmpty(extension)) extension = ".txt";
            picker.FileTypeChoices.Add("Text document", new List<string> { extension });
            file = await picker.PickSaveFileAsync(); if (file is null) return false;
        }
        if (!_documents.Contains(entry)) return false;
        var snapshot = entry.Document.Snapshot(); var encoding = (Encoding)entry.Document.Encoding.Clone();
        // Strict encoder failures occur before destination opening/truncation, as with
        // the preceding GetBytes path, without allocating full text and byte arrays.
        var content = SnapshotSaveContent.Create(snapshot, encoding);
        await WritePreparedFileAsync(file, content);
        if (!_documents.Contains(entry)) return false;
        if (entry.Document.Revision == snapshot.Revision && entry.Document.Encoding.Equals(encoding)) entry.Document.MarkSaved();
        entry.File = file; entry.Document.FileName = file.Name;
        RebuildTabs(); UpdateStatus(); RefreshSaveAllControls(); AddEvent("Saved " + file.Name);
        await NotifyLspSavedAsync(entry, snapshot);
        return _documents.Contains(entry) && !entry.Document.IsModified && entry.Document.Revision == snapshot.Revision && entry.Document.Encoding.Equals(encoding);
    }

    internal static async Task WritePreparedFileAsync(StorageFile file, SnapshotSaveContent content)
    {
        ArgumentNullException.ThrowIfNull(file); ArgumentNullException.ThrowIfNull(content);
        CachedFileManager.DeferUpdates(file);
        Exception? writeError = null;
        try
        {
            using var stream = await file.OpenStreamForWriteAsync();
            stream.SetLength(0);
            await content.WriteToAsync(stream);
        }
        catch (Exception error) { writeError = error; }
        FileUpdateStatus result;
        try { result = await CachedFileManager.CompleteUpdatesAsync(file); }
        catch (Exception completionError)
        {
            if (writeError is not null) throw new AggregateException("Writing and completing the file provider both failed.", writeError, completionError);
            throw;
        }
        if (writeError is not null) ExceptionDispatchInfo.Capture(writeError).Throw();
        if (result is not FileUpdateStatus.Complete and not FileUpdateStatus.CompleteAndRenamed)
            throw new IOException("The file provider did not complete the save: " + result);
    }
}
