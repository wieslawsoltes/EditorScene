using System.Net.WebSockets;
using EditorScene.LanguageServices;
using EditorScene.Protocol;
using EditorScene.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EditorScene.App;

public sealed partial class EditorPage
{
    private sealed record LspBinding(WorkspaceDocument Entry, ISyntaxLanguage Previous, LspSyntaxLanguage Provider);
    private readonly Dictionary<Guid, LspBinding> _lspBindings = [];
    private CancellationTokenSource? _lspConnectionAttempt;
    private void BuildLspMenu(MenuBarItem menu)
    {
        Separator(menu);
        Item(menu, "Connect Language Server…", ConnectLanguageServerAsync);
        Item(menu, "Disconnect Language Server", async () =>
        {
            _lspConnectionAttempt?.Cancel();
            if (_current is { } entry) await DisconnectLspAsync(entry);
        });
        Unloaded += async (_, _) =>
        {
            _lspConnectionAttempt?.Cancel();
            foreach (var binding in _lspBindings.Values.ToArray()) await GuardAsync(() => DisconnectLspAsync(binding.Entry));
        };
    }
    private void UpdateLspPicker()
    {
        if (_current is null) return;
        _updatingPickers = true;
        try
        {
            var connected = _lspBindings.TryGetValue(_current.Document.Id, out var binding);
            _languagePicker.IsEnabled = !connected;
            _languagePicker.SelectedItem = connected ? binding!.Previous.DisplayName : _current.Language.DisplayName;
        }
        finally { _updatingPickers = false; }
    }
    private string LspStatusPrefix => _active?.SyntaxLanguage is LspSyntaxLanguage language ? "LSP: " + language.Id + "  •  " : "";
    private async Task ConnectLanguageServerAsync()
    {
        var entry = _current; if (entry is null) return;
        if (_lspConnectionAttempt is not null || _lspBindings.ContainsKey(entry.Document.Id)) throw new InvalidOperationException("Disconnect the current language-server session or connection attempt first.");
        var endpointInput = new TextBox { Text = "wss://", PlaceholderText = "wss://trusted-server/lsp", MinWidth = 400 };
        var documentInput = new TextBox { Text = "file:///workspace/" + Uri.EscapeDataString(entry.Document.FileName) };
        var languageInput = new TextBox { Text = entry.Language.Id };
        Register("LSP endpoint", endpointInput); Register("LSP document URI", documentInput); Register("LSP language ID", languageInput);
        var panel = new StackPanel { Spacing = 9, MaxWidth = 480 };
        panel.Children.Add(new TextBlock
        {
            Text = "Connect sends this document, its URI and subsequent edits to the selected server. Use a trusted endpoint. No local process is launched, no other files are read, and server-requested commands/workspace edits are not executed. Connections are not saved or reopened automatically.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12
        });
        panel.Children.Add(new TextBlock { Text = "WebSocket endpoint (TLS, or loopback ws:// for development)", FontSize = 12 }); panel.Children.Add(endpointInput);
        panel.Children.Add(new TextBlock { Text = "Document URI known to the server", FontSize = 12 }); panel.Children.Add(documentInput);
        panel.Children.Add(new TextBlock { Text = "Server language identifier", FontSize = 12 }); panel.Children.Add(languageInput);
        var dialog = new ContentDialog { Title = "Connect language server", Content = panel, PrimaryButtonText = "Connect", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.None };
        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) return;
        if (!_documents.Contains(entry) || !ReferenceEquals(_current, entry)) throw new InvalidOperationException("The selected document changed while configuring the server.");
        if (!Uri.TryCreate(endpointInput.Text.Trim(), UriKind.Absolute, out var endpoint) ||
            !(endpoint.Scheme == "wss" || endpoint.Scheme == "ws" && endpoint.IsLoopback) || endpoint.UserInfo.Length != 0 || endpoint.Fragment.Length != 0)
            throw new FormatException("Use wss://, or ws:// on loopback only, without embedded user information or a fragment.");
        if (!Uri.TryCreate(documentInput.Text.Trim(), UriKind.Absolute, out var documentUri)) throw new FormatException("Enter an absolute document URI.");
        var languageId = languageInput.Text.Trim(); ArgumentException.ThrowIfNullOrWhiteSpace(languageId);
        var previous = entry.Language; var snapshot = entry.Document.Snapshot();
        using var attempt = new CancellationTokenSource(TimeSpan.FromSeconds(30)); _lspConnectionAttempt = attempt;
        var socket = new ClientWebSocket(); LspSyntaxLanguage? provider = null;
        try
        {
            await socket.ConnectAsync(endpoint, attempt.Token);
            provider = await LspSyntaxLanguage.OpenAsync(new JsonRpcConnection(new WebSocketJsonRpcTransport(socket)), snapshot, documentUri, languageId, cancellationToken: attempt.Token);
            if (!_loaded || !_documents.Contains(entry) || !ReferenceEquals(entry.Language, previous) || attempt.IsCancellationRequested)
            { await provider.DisposeAsync(); return; }
            _lspBindings.Add(entry.Document.Id, new(entry, previous, provider)); entry.Language = provider;
            provider.ProtocolWarning += (_, warning) => DispatcherQueue.TryEnqueue(() => { if (_loaded) AddEvent("LSP: " + warning); });
            if (ReferenceEquals(_current, entry))
            {
                _primary.SyntaxLanguage = provider; if (_secondary is not null) _secondary.SyntaxLanguage = provider;
                UpdateLspPicker(); UpdateStatus(); _active.FocusEditor();
            }
            AddEvent("Connected language server for " + entry.Document.FileName + " at " + endpoint.IdnHost); QueueDiagnostics();
        }
        catch { if (provider is not null) await provider.DisposeAsync(); else socket.Dispose(); throw; }
        finally { if (ReferenceEquals(_lspConnectionAttempt, attempt)) _lspConnectionAttempt = null; }
    }
    private async Task DisconnectLspAsync(WorkspaceDocument entry)
    {
        if (!_lspBindings.Remove(entry.Document.Id, out var binding)) return;
        if (ReferenceEquals(entry.Language, binding.Provider)) entry.Language = binding.Previous;
        if (ReferenceEquals(_current, entry))
        {
            _primary.SyntaxLanguage = entry.Language; if (_secondary is not null) _secondary.SyntaxLanguage = entry.Language;
            UpdateLspPicker(); UpdateStatus();
        }
        await binding.Provider.DisposeAsync();
        AddEvent("Language server disconnected from " + entry.Document.FileName); QueueDiagnostics();
        if (ReferenceEquals(_current, entry)) _active.FocusEditor();
    }
    private async Task NotifyLspSavedAsync(WorkspaceDocument entry, DocumentSnapshot saved)
    {
        if (entry.Document.Revision != saved.Revision || entry.Language is not LspSyntaxLanguage provider) return;
        try { await provider.NotifySavedAsync(saved); }
        catch (Exception error) { AddEvent("File saved; language-server save notification failed: " + error.Message); }
    }
}
