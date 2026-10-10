using EditorScene.Snippets;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace EditorScene.App;

public sealed partial class EditorPage
{
    private async Task ImportSnippetsAsync()
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".snippet"); picker.FileTypeFilter.Add(".xml");
        var files = await picker.PickMultipleFilesAsync();
        var entries = new List<SnippetEntry>();
        foreach (var file in files)
        {
            using var stream = await file.OpenStreamForReadAsync();
            var folder = Path.GetFileNameWithoutExtension(file.Name);
            entries.AddRange(CodeSnippetSerializer.Load(stream).Select(snippet => new SnippetEntry(folder, snippet)));
        }
        if (entries.Count == 0) return;
        // Parse and validate the complete selection before changing the shared catalog.
        _primary.Snippets.RegisterRange(entries);
        if (_secondary is not null) _secondary.Snippets = _primary.Snippets;
        AddEvent($"Imported {entries.Count} snippets"); _active.FocusEditor();
    }
    private async Task ExportSnippetsAsync()
    {
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = "EditorScene" };
        picker.FileTypeChoices.Add("Visual Studio code snippets", new List<string> { ".snippet" });
        var file = await picker.PickSaveFileAsync(); if (file is null) return;
        using var stream = new MemoryStream(); CodeSnippetSerializer.Save(stream, _primary.Snippets.Entries.Select(entry => entry.Snippet));
        CachedFileManager.DeferUpdates(file); await FileIO.WriteBytesAsync(file, stream.ToArray());
        var status = await CachedFileManager.CompleteUpdatesAsync(file);
        if (status is not Windows.Storage.Provider.FileUpdateStatus.Complete and not Windows.Storage.Provider.FileUpdateStatus.CompleteAndRenamed)
            throw new IOException("The file provider did not complete snippet export: " + status);
        AddEvent("Exported snippet catalog to " + file.Name);
    }
}
