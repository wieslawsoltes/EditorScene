using EditorScene.Controls;
using EditorScene.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace EditorScene.App;

public sealed partial class EditorPage
{
    private readonly DocumentOperationCoordinator _documentOperations = new();
    private EditorDocumentTabMenu? _documentTabMenu;
    private int _documentTabMenuBuilds;
    private static readonly string[] DocumentWriteControlNames =
    ["Command Save", "Command Save As…", "Command Close", "Command Close All Documents", "Command Close Other Documents", "Toolbar Save"];

    private void InitializeDocumentClosing()
    {
        Unloaded += (_, _) => _documentTabMenu?.ClearContext();
    }
    private void WireDocumentTab(Button tab, Guid documentId)
    {
        // Store identity only. Removed native tabs must not keep an editor document
        // alive through event-handler closures or a flyout's old placement target.
        tab.Tag = documentId;
        tab.Click += OnDocumentTabClick;
        tab.Loaded += OnDocumentTabLoaded;
        tab.ContextRequested += OnDocumentTabContextRequested;
        tab.PointerPressed += OnDocumentTabPointerPressed;
    }
    private void UnwireDocumentTab(Button tab)
    {
        tab.Click -= OnDocumentTabClick;
        tab.Loaded -= OnDocumentTabLoaded;
        tab.ContextRequested -= OnDocumentTabContextRequested;
        tab.PointerPressed -= OnDocumentTabPointerPressed;
        tab.Tag = null;
    }
    private void OnDocumentTabClick(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: Guid id }) return;
        var entry = _documents.FirstOrDefault(d => d.Document.Id == id);
        if (entry is not null) ActivateDocument(entry);
    }
    private void OnDocumentTabLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: Guid id } && _current?.Document.Id == id) QueueActiveTabReveal();
    }
    private void OnDocumentTabContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (sender is not Button { Tag: Guid id } tab) return;
        args.Handled = true; ShowDocumentTabMenu(id, tab);
    }
    private void OnDocumentTabPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (sender is not Button { Tag: Guid id } tab || !args.GetCurrentPoint(tab).Properties.IsMiddleButtonPressed) return;
        args.Handled = true;
        // Defer tree removal until this routed pointer event has finished.
        DispatcherQueue.TryEnqueue(() => _ = GuardAsync(() => CloseDocumentsAsync(id, DocumentCloseScope.Current)));
    }
    private void ShowDocumentTabMenu(Guid id, Button tab)
    {
        if (!_loaded || _dialog is not null || _documentOperations.IsBusy) return;
        var index = _documents.FindIndex(d => d.Document.Id == id);
        if (index < 0 || !ReferenceEquals(_documents[index].Tab, tab)) return;
        if (_documentTabMenu is null)
        {
            _documentTabMenu = new(); _documentTabMenuBuilds++;
            _documentTabMenu.CloseRequested += (_, request) =>
            {
                _documentTabMenu.Hide();
                _ = GuardAsync(() => CloseDocumentsAsync(request.DocumentId, request.Scope));
            };
            _documentTabMenu.Opened += (_, _) => QueueDiagnostics();
            _documentTabMenu.Closed += (_, _) =>
            { if (_loaded && _dialog is null) _active.FocusEditor(); QueueDiagnostics(); };
        }
        _documentTabMenu.Configure(id, index, _documents.Count);
        _documentTabMenu.ShowAt(tab); QueueDiagnostics();
    }
    private void ShowCurrentDocumentTabMenu()
    { if (_current?.Tab is { } tab) ShowDocumentTabMenu(_current.Document.Id, tab); }

    private bool HandleDocumentClosingKey(KeyRoutedEventArgs args)
    {
        bool Down(VirtualKey key) => (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        if (Down(VirtualKey.Shift)) return false;
        var control = Down(VirtualKey.Control); var alt = Down(VirtualKey.Menu);
        if (control && !alt && args.Key == VirtualKey.F4)
        { _ = GuardAsync(CloseDocumentAsync); return true; }
        // 189 is VK_OEM_MINUS (the ordinary minus key); Subtract is its keypad counterpart.
        if (alt && !control && (args.Key == (VirtualKey)189 || args.Key == VirtualKey.Subtract))
        { ShowCurrentDocumentTabMenu(); return true; }
        return false;
    }
    private void RefreshDocumentOperationControls()
    {
        var enabled = !_documentOperations.IsBusy;
        foreach (var name in DocumentWriteControlNames)
            if (_ui.TryGetValue(name, out var element) && element is Control control && control.IsEnabled != enabled) control.IsEnabled = enabled;
        if (_closeTabButton is { } close && close.IsEnabled != enabled) close.IsEnabled = enabled;
        RefreshSaveAllControls();
        QueueDiagnostics();
    }
    private async Task<bool> RunDocumentOperationAsync(Func<Task<bool>> action)
    {
        try
        {
            return await _documentOperations.TryRunAsync(_ =>
            { RefreshDocumentOperationControls(); return action(); });
        }
        finally { RefreshDocumentOperationControls(); }
    }
    private Task<bool> SaveDocumentAsync(bool saveAs)
    {
        var entry = _current;
        if (entry is null || _dialog is not null) return Task.FromResult(false);
        return RunDocumentOperationAsync(() => SaveDocumentCoreAsync(entry, saveAs));
    }
    private Task CloseDocumentAsync()
        => _current is { } entry ? CloseDocumentsAsync(entry.Document.Id, DocumentCloseScope.Current) : Task.CompletedTask;
    private Task CloseCurrentDocumentsAsync(DocumentCloseScope scope)
        => _current is { } entry ? CloseDocumentsAsync(entry.Document.Id, scope) : Task.CompletedTask;

    private async Task CloseDocumentsAsync(Guid targetId, DocumentCloseScope scope)
    {
        if (_dialog is not null || !_documents.Any(d => d.Document.Id == targetId)) return;
        try
        {
            await RunDocumentOperationAsync(async () =>
            {
                // Capture before the first await. New/opened documents are not part
                // of this request, and an inactive target never becomes _current.
                var plan = DocumentClosePlan.Create(_documents.Select(d => d.Document.Id), targetId, scope);
                _documentTabMenu?.ClearContext();
                foreach (var id in plan.DocumentIds)
                {
                    var entry = _documents.FirstOrDefault(d => d.Document.Id == id);
                    if (entry is null) continue;
                    if (!await ConfirmDocumentCloseAsync(entry)) return false;
                    var index = _documents.IndexOf(entry); if (index < 0) continue;
                    var wasCurrent = ReferenceEquals(entry, _current);
                    _documents.RemoveAt(index); if (wasCurrent) _current = null;
                    // Remove UI ownership before awaiting optional server shutdown.
                    RebuildTabs(); entry.Tab = null;
                    if (_documents.Count == 0) NewDocument();
                    else if (wasCurrent) ActivateDocument(_documents[Math.Min(index, _documents.Count - 1)]);
                    try { await DisconnectLspAsync(entry); }
                    finally { entry.Document.Dispose(); }
                }
                return true;
            });
        }
        finally { if (_loaded && _dialog is null) _active.FocusEditor(); }
    }
    private async Task<bool> ConfirmDocumentCloseAsync(WorkspaceDocument entry)
    {
        if (!entry.Document.IsModified) return true;
        var promptedRevision = entry.Document.Revision;
        var result = await ShowDialogAsync(new()
        {
            Title = "Save changes?", Content = $"Save changes to {entry.Document.FileName} before closing?",
            PrimaryButtonText = "Save", SecondaryButtonText = "Discard", CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        });
        if (result == ContentDialogResult.None) return false;
        if (!_documents.Contains(entry)) return false;
        if (result == ContentDialogResult.Primary) return await SaveDocumentCoreAsync(entry, false);
        if (entry.Document.Revision != promptedRevision)
        { AddEvent("Close cancelled because the document changed while confirmation was open."); return false; }
        return true;
    }
}
