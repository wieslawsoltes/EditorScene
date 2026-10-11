using EditorScene.Text.Macros;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace EditorScene.App;

public sealed partial class EditorPage
{
    private void BuildMacroMenu(MenuBarItem menu)
    {
        Item(menu, "Start Recording", () => { _active.MacroRecording.Record(); return Task.CompletedTask; });
        Item(menu, "Stop Recording", () => { _active.MacroRecording.Stop(); return Task.CompletedTask; });
        Item(menu, "Pause Recording", () => { _active.MacroRecording.Pause(); return Task.CompletedTask; });
        Item(menu, "Resume Recording", () => { _active.MacroRecording.Resume(); return Task.CompletedTask; });
        Item(menu, "Cancel Recording", () => { _active.MacroRecording.Cancel(); return Task.CompletedTask; });
        Separator(menu);
        Item(menu, "Run Last Macro", () => _active.RunMacroAsync());
        Item(menu, "Cancel Playback", () => { _active.CancelMacroPlayback(); return Task.CompletedTask; });
        Separator(menu);
        Item(menu, "Import Macro…", ImportMacroAsync);
        Item(menu, "Export Macro…", ExportMacroAsync);
    }
    private async Task ImportMacroAsync()
    {
        var editor = _active;
        if (editor.MacroRecording.State != MacroRecordingState.Idle) throw new InvalidOperationException("Stop recording or playback before importing a macro.");
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".esmacro"); picker.FileTypeFilter.Add(".xml");
        var file = await picker.PickSingleFileAsync(); if (file is null) return;
        using var stream = await file.OpenStreamForReadAsync(); var macro = MacroSerializer.Load(stream);
        if (!ReferenceEquals(_active, editor)) throw new InvalidOperationException("The active editor changed during macro import.");
        editor.MacroRecording.SetLastMacro(macro); AddEvent($"Imported macro with {macro.Steps.Length} actions; not executed");
    }
    private async Task ExportMacroAsync()
    {
        var macro = _active.MacroRecording.LastMacroAction ?? throw new InvalidOperationException("Record or import a macro first.");
        using var stream = new MemoryStream(); MacroSerializer.Save(stream, macro);
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = "EditorScene-macro" };
        picker.FileTypeChoices.Add("EditorScene macro", new List<string> { ".esmacro" });
        var file = await picker.PickSaveFileAsync(); if (file is null) return;
        CachedFileManager.DeferUpdates(file); await FileIO.WriteBytesAsync(file, stream.ToArray());
        var status = await CachedFileManager.CompleteUpdatesAsync(file);
        if (status is not Windows.Storage.Provider.FileUpdateStatus.Complete and not Windows.Storage.Provider.FileUpdateStatus.CompleteAndRenamed)
            throw new IOException("The file provider did not complete macro export: " + status);
        AddEvent("Exported macro to " + file.Name);
    }
}
