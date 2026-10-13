using EditorScene.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace EditorScene.App;

public sealed partial class EditorPage
{
    private readonly EditorDocumentList _documentList = new();
    private readonly ScrollViewer _tabScroll = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
        HorizontalScrollMode = ScrollMode.Enabled,
        VerticalScrollMode = ScrollMode.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        MinHeight = 27
    };
    private Button? _documentListButton;
    private long _tabBuildCount;
    private bool _tabRevealQueued;

    private Grid BuildDocumentStrip()
    {
        InitializeDocumentClosing();
        var strip = new Grid { MinHeight = 27 };
        strip.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        strip.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        strip.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _tabScroll.Content = _tabs; strip.Children.Add(_tabScroll);
        _tabScroll.SizeChanged += (_, _) => QueueActiveTabReveal();
        // The child editor consumes PageUp/PageDown even with Control. Tunnel
        // only our workspace shortcuts before it changes the source caret.
        PreviewKeyDown += OnDocumentNavigationPreviewKeyDown;
        _documentListButton = new Button
        {
            Content = "▾", Flyout = _documentList, Width = 25, Height = 26, MinHeight = 26,
            Padding = new Thickness(3, 0, 3, 0), BorderThickness = new Thickness(0)
        };
        Register("All documents", _documentListButton);
        ToolTipService.SetToolTip(_documentListButton, "Active documents (Ctrl+Alt+Down)");
        Grid.SetColumn(_documentListButton, 1); strip.Children.Add(_documentListButton);
        _closeTabButton = new Button
        {
            Content = NewIcon(EditorIconKind.Close),
            Width = 25, Height = 26, MinHeight = 26, Padding = new Thickness(4), BorderThickness = new Thickness(0)
        };
        _closeTabButton.Click += (_, _) => _ = GuardAsync(CloseDocumentAsync);
        Register("Close document", _closeTabButton); ToolTipService.SetToolTip(_closeTabButton, "Close document (Ctrl+W)");
        Grid.SetColumn(_closeTabButton, 2); strip.Children.Add(_closeTabButton);
        _documentList.DocumentInvoked += (_, args) =>
        {
            if (_dialog is not null) return;
            var entry = _documents.FirstOrDefault(d => d.Document.Id == args.Document.Id);
            if (entry is not null) ActivateDocument(entry);
        };
        _documentList.Opened += (_, _) => QueueDiagnostics();
        _documentList.Closed += (_, _) => { if (_loaded) _active?.FocusEditor(); QueueDiagnostics(); };
        _chromePanels.Add(strip);
        return strip;
    }
    private void RefreshDocumentList()
    {
        _documentList.SetDocuments(_documents.Select(d => new EditorDocumentListEntry(d.Document.Id, d.Document.FileName, d.Document.IsModified)));
        _documentList.SelectedDocumentId = _current?.Document.Id;
    }
    private void ShowDocumentList()
    {
        if (_dialog is null && _documentListButton is not null && _documentList.DocumentCount != 0)
            _documentList.ShowAt(_documentListButton);
    }
    private void NavigateDocument(int direction)
    {
        if (_dialog is not null || _current is null || _documents.Count == 0) return;
        var index = _documents.IndexOf(_current);
        if (index < 0) return;
        ActivateDocument(_documents[(index + direction % _documents.Count + _documents.Count) % _documents.Count]);
    }
    private void OnDocumentNavigationPreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Handled || !_loaded || _dialog is not null) return;
        if (HandleSaveAllKey(args)) { args.Handled = true; return; }
        if (HandleDocumentClosingKey(args)) { args.Handled = true; return; }
        var control = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        if (control && HandleDocumentNavigationKey(args)) args.Handled = true;
    }
    private bool HandleDocumentNavigationKey(KeyRoutedEventArgs args)
    {
        var alt = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        var shift = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        if (shift) return false;
        if (alt && args.Key == VirtualKey.Down) { ShowDocumentList(); return true; }
        if (!alt && args.Key is VirtualKey.PageUp or VirtualKey.PageDown)
        { NavigateDocument(args.Key == VirtualKey.PageUp ? -1 : 1); return true; }
        return false;
    }
    private void QueueActiveTabReveal()
    {
        if (_tabRevealQueued || !_loaded || _current?.Tab is null) return;
        _tabRevealQueued = true;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            _tabRevealQueued = false;
            if (_loaded && _current?.Tab is { IsLoaded: true } tab && _tabs.Children.Contains(tab))
                tab.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
            QueueDiagnostics();
        })) _tabRevealQueued = false;
    }
}
