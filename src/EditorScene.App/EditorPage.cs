using System.Collections.ObjectModel;
using EditorScene.Controls;
using EditorScene.LanguageServices;
using EditorScene.Languages.DotNet;
using EditorScene.Languages.Structured;
using EditorScene.Rendering.Skia;
using EditorScene.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using Windows.System;

namespace EditorScene.App;

public sealed partial class EditorPage : Page
{
    private readonly Grid _root = new();
    private readonly Grid _editorGrid = new();
    private readonly Grid _views = new();
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Spacing = 1 };
    private readonly StackPanel _toolbar = new() { Orientation = Orientation.Horizontal, Spacing = 1 };
    private readonly TextBlock _fileTitle = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { FontSize = 11, Margin = new Thickness(8, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _position = new() { FontSize = 11, Margin = new Thickness(8, 2, 10, 2), VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _languagePicker = new() { Width = 128, MinHeight = 25, Height = 25, FontSize = 12, Padding = new Thickness(7, 0, 7, 0) };
    private readonly ComboBox _themePicker = new() { Width = 104, MinHeight = 25, Height = 25, FontSize = 12, Padding = new Thickness(7, 0, 7, 0) };
    private readonly NavigableSymbolSelector _navigator = new();
    private readonly DocumentOutline _outline = new();
    private readonly Border _outlinePane = new() { Visibility = Visibility.Collapsed, Width = 240, BorderThickness = new Thickness(1, 0, 0, 0) };
    private readonly Border _toolPane = new() { Height = 132, Visibility = Visibility.Collapsed, BorderThickness = new Thickness(0, 1, 0, 0) };
    private readonly ListView _errors = new() { SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true };
    private readonly ListView _events = new() { SelectionMode = ListViewSelectionMode.None };
    private readonly Button _errorTab = new() { Content = "Error List (0)", Height = 25, MinHeight = 25, FontSize = 11, Padding = new Thickness(10, 2, 10, 2), BorderThickness = new Thickness(0) };
    private readonly ObservableCollection<string> _eventItems = [];
    private readonly LanguageRegistry _languages = new();
    private readonly List<WorkspaceDocument> _documents = [];
    private readonly Dictionary<string, FrameworkElement> _ui = new(StringComparer.Ordinal);
    private readonly Dictionary<MenuFlyoutItem, Func<bool>> _checks = [];
    private readonly List<EditorIcon> _icons = [];
    private readonly List<Border> _chromeBorders = [];
    private readonly List<Panel> _chromePanels = [];
    private readonly SyntaxEditor _primary = new();
    private SyntaxEditor? _secondary;
    private SyntaxEditor _active = null!;
    private WorkspaceDocument? _current;
    private EditorTheme _theme = EditorTheme.Light;
    private int _untitled;
    private bool _updatingPickers, _loaded, _diagnosticsQueued;
    private bool _showEvents, _diagnostics;
    private string? _lastError;
    private ContentDialog? _dialog;

    public EditorPage()
    {
        FontSize = 12; IsTabStop = false;
        _languages.Register(new DotNetLanguage()); _languages.Register(new DotNetLanguage(true));
        _languages.Register(new JsonLanguage()); _languages.Register(new XmlLanguage()); _languages.Register(new XmlLanguage(true)); _languages.Register(new PlainTextLanguage());
#if __WASM__
        _diagnostics = Uno.Foundation.WebAssemblyRuntime.InvokeJS("new URLSearchParams(location.search).get('test') === '1' ? 'yes' : 'no'") == "yes";
        // Reliable browser upload/download, including browsers without File System Access API support.
        Uno.WinRTFeatureConfiguration.Storage.Pickers.WasmConfiguration = Uno.Storage.Pickers.WasmPickerConfiguration.DownloadUpload;
#endif
        _diagnostics |= Environment.GetEnvironmentVariable("EDITORSCENE_NATIVE_CHECKS") == "1";
        BuildLayout(); WireEditor(_primary); SetActive(_primary);
        _events.ItemsSource = _eventItems;
        _errors.ItemClick += (_, e) => { if (e.ClickedItem is DiagnosticRow row) { _active.Select(row.Diagnostic.Range.Offset, row.Diagnostic.Range.Length); _active.FocusEditor(); } };
        _languagePicker.ItemsSource = _languages.Languages.Select(l => l.DisplayName).ToArray();
        _languagePicker.SelectionChanged += (_, _) =>
        {
            if (_updatingPickers || _current is null || _languagePicker.SelectedItem is not string name) return;
            var language = _languages.Languages.First(l => l.DisplayName == name); _current.Language = language;
            _primary.SyntaxLanguage = language; if (_secondary is not null) _secondary.SyntaxLanguage = language;
            AddEvent($"Syntax language: {name}"); UpdateStatus();
        };
        _themePicker.ItemsSource = new[] { "Light", "Dark", "High contrast" }; _themePicker.SelectedIndex = 0;
        _themePicker.SelectionChanged += (_, _) => ApplyTheme(_themePicker.SelectedIndex switch { 1 => EditorTheme.Dark, 2 => EditorTheme.HighContrast, _ => EditorTheme.Light });
        Loaded += async (_, _) =>
        {
            _loaded = true; ApplyTheme(_theme); _primary.FocusEditor(); QueueDiagnostics();
            if (Environment.GetEnvironmentVariable("EDITORSCENE_NATIVE_CHECKS") == "1") await RunNativeChecksAsync();
        };
        Unloaded += (_, _) => _loaded = false;
        KeyDown += OnPageKeyDown; SizeChanged += (_, _) => QueueDiagnostics();
        AddDocument(new EditorDocument(_diagnostics ? "" : Samples.CSharp, _diagnostics ? "Untitled.cs" : "SensorMonitor.cs"), _languages.Get("csharp")!);
    }
    private void BuildLayout()
    {
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto }) _root.RowDefinitions.Add(new() { Height = height });
        Content = _root;
        var title = new Grid { Height = 29, Padding = new Thickness(8, 0, 8, 0), ColumnSpacing = 7 };
        title.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); title.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); title.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        title.Children.Add(NewIcon(EditorIconKind.Code)); Grid.SetColumn(_fileTitle, 1); title.Children.Add(_fileTitle);
        var about = new Button { Content = "Uno Platform · Skia", FontSize = 10, MinHeight = 22, Height = 22, Padding = new Thickness(7, 1, 7, 1), BorderThickness = new Thickness(0) };
        about.Click += (_, _) => _ = GuardAsync(ShowAboutAsync); Register("About", about); Grid.SetColumn(about, 2); title.Children.Add(about); Put(title, 0); _chromePanels.Add(title);
        var menus = BuildMenus(); Put(menus, 1);
        var toolbarGrid = new Grid { Padding = new Thickness(5, 2, 5, 2), ColumnSpacing = 5 };
        toolbarGrid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); toolbarGrid.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); toolbarGrid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var toolsScroll = new ScrollViewer { Content = _toolbar, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        toolbarGrid.Children.Add(toolsScroll); Grid.SetColumn(_languagePicker, 1); Grid.SetColumn(_themePicker, 2); toolbarGrid.Children.Add(_languagePicker); toolbarGrid.Children.Add(_themePicker);
        Register("Language", _languagePicker); Register("Theme", _themePicker); Put(toolbarGrid, 2); _chromePanels.Add(toolbarGrid); BuildToolbar();
        var tabsScroll = new ScrollViewer { Content = _tabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, MinHeight = 27 };
        Put(tabsScroll, 3); _chromePanels.Add(_tabs);
        Put(_navigator, 4);
        _editorGrid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); _editorGrid.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); _editorGrid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _views.Children.Add(_primary); _editorGrid.Children.Add(_views);
        var outlineGrid = new Grid(); outlineGrid.RowDefinitions.Add(new() { Height = GridLength.Auto }); outlineGrid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var outlineHeading = new TextBlock { Text = "DOCUMENT OUTLINE", FontSize = 10, Margin = new Thickness(10, 8, 10, 8) }; outlineGrid.Children.Add(outlineHeading); Grid.SetRow(_outline, 1); outlineGrid.Children.Add(_outline); _outlinePane.Child = outlineGrid;
        Grid.SetColumn(_outlinePane, 1); _editorGrid.Children.Add(_outlinePane); _chromeBorders.Add(_outlinePane);
        var rail = new StackPanel { Width = 24 }; var outlineButton = new Button { Content = new TextBlock { Text = "D\no\nc\nu\nm\ne\nn\nt\n\nO\nu\nt\nl\ni\nn\ne", FontSize = 10, TextAlignment = TextAlignment.Center }, Width = 24, Padding = new Thickness(2, 8, 2, 8), BorderThickness = new Thickness(0) };
        outlineButton.Click += (_, _) => ToggleOutline(); Register("Document outline", outlineButton); rail.Children.Add(outlineButton); _chromePanels.Add(rail); Grid.SetColumn(rail, 2); _editorGrid.Children.Add(rail); Put(_editorGrid, 5);
        _toolPane.Child = _errors; Put(_toolPane, 6); _chromeBorders.Add(_toolPane);
        var toolTabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
        _errorTab.Click += (_, _) => ToggleToolPane(false); Register("Error List", _errorTab); toolTabs.Children.Add(_errorTab);
        var eventTab = new Button { Content = "Events", Height = 25, MinHeight = 25, FontSize = 11, Padding = new Thickness(10, 2, 10, 2), BorderThickness = new Thickness(0) };
        eventTab.Click += (_, _) => ToggleToolPane(true); Register("Events", eventTab); toolTabs.Children.Add(eventTab); Put(toolTabs, 7); _chromePanels.Add(toolTabs);
        var statusGrid = new Grid { MinHeight = 23 };
        statusGrid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); statusGrid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        statusGrid.Children.Add(_status); Grid.SetColumn(_position, 1); statusGrid.Children.Add(_position); Put(statusGrid, 8); Register("Status bar", statusGrid);
    }
    private void Put(UIElement element, int row) { Grid.SetRow(element, row); _root.Children.Add(element); }
    private void Register(string name, FrameworkElement element) { _ui[name] = element; AutomationProperties.SetName(element, name); }
    private EditorIcon NewIcon(EditorIconKind kind) { var icon = new EditorIcon { Kind = kind, IsDark = _theme.IsDark }; _icons.Add(icon); return icon; }
    private void WireEditor(SyntaxEditor editor)
    {
        editor.GotFocus += (_, _) => SetActive(editor);
        editor.StateChanged += (_, _) => { if (ReferenceEquals(editor, _active)) UpdateStatus(); QueueDiagnostics(); };
        editor.AnalysisUpdated += (_, analysis) => { if (ReferenceEquals(editor, _active)) RefreshDiagnostics(analysis); };
        editor.OperationFailed += (_, error) => { _lastError = error.Message; AddEvent("Error: " + error.Message); QueueDiagnostics(); };
        editor.GoToLineRequested += (_, _) => _ = GuardAsync(GoToLineAsync);
        editor.NativeEditor.TextArea.TextView.Rendered += (_, _) => QueueDiagnostics();
        editor.NativeEditor.TextArea.TextView.ScrollOffsetChanged += (_, _) => QueueDiagnostics();
    }
    private void SetActive(SyntaxEditor editor)
    {
        if (ReferenceEquals(_active, editor)) return;
        _active = editor; _navigator.Editor = editor; _outline.Editor = editor;
        if (editor.Analysis is { } analysis) RefreshDiagnostics(analysis); UpdateStatus();
    }
    private void AddDocument(EditorDocument document, ISyntaxLanguage language, StorageFile? file = null)
    {
        var entry = new WorkspaceDocument(document, language, file); _documents.Add(entry);
        document.Changed += (_, _) => { RebuildTabCaptions(); QueueDiagnostics(); };
        RebuildTabs(); ActivateDocument(entry);
    }
    private void ActivateDocument(WorkspaceDocument entry)
    {
        if (_current is not null) _current.Caret = _primary.CaretOffset;
        _current = entry; _primary.Document = entry.Document; _primary.SyntaxLanguage = entry.Language;
        if (_secondary is not null) { _secondary.Document = entry.Document; _secondary.SyntaxLanguage = entry.Language; }
        _primary.CaretOffset = Math.Clamp(entry.Caret, 0, entry.Document.Buffer.TextLength);
        SetActive(_primary); _updatingPickers = true; _languagePicker.SelectedItem = entry.Language.DisplayName; _updatingPickers = false;
        _errors.ItemsSource = null; _errorTab.Content = "Error List (analyzing…)"; RebuildTabCaptions(); UpdateStatus(); _primary.FocusEditor();
    }
    private void RebuildTabs()
    {
        _tabs.Children.Clear();
        foreach (var entry in _documents)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            panel.Children.Add(new TextBlock { Text = entry.Document.FileName, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            var button = new Button { Content = panel, Height = 27, MinHeight = 27, Padding = new Thickness(9, 2, 9, 2), BorderThickness = new Thickness(0, 0, 0, 2) };
            button.Click += (_, _) => ActivateDocument(entry); entry.Tab = button; Register("Document " + entry.Document.FileName, button); _tabs.Children.Add(button);
        }
        var close = new Button { Content = NewIcon(EditorIconKind.Close), Height = 26, MinHeight = 26, Width = 25, Padding = new Thickness(4), BorderThickness = new Thickness(0) };
        close.Click += (_, _) => _ = GuardAsync(CloseDocumentAsync); Register("Close document", close); _tabs.Children.Add(close); RebuildTabCaptions();
    }
    private void RebuildTabCaptions()
    {
        foreach (var entry in _documents)
        {
            if (entry.Tab?.Content is StackPanel panel && panel.Children[0] is TextBlock text) text.Text = entry.Document.FileName + (entry.Document.IsModified ? " *" : "");
            if (entry.Tab is { } tab) { tab.Background = SyntaxEditor.Brush(ReferenceEquals(entry, _current) ? _theme.Background : _theme.Chrome); tab.BorderBrush = SyntaxEditor.Brush(ReferenceEquals(entry, _current) ? _theme.Accent : _theme.Chrome); }
        }
    }
    private async Task CloseDocumentAsync()
    {
        var entry = _current; if (entry is null) return;
        if (entry.Document.IsModified)
        {
            var result = await ShowDialogAsync(new() { Title = "Save changes?", Content = $"Save changes to {entry.Document.FileName} before closing?", PrimaryButtonText = "Save", SecondaryButtonText = "Discard", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary });
            if (result == ContentDialogResult.None) return;
            if (result == ContentDialogResult.Primary && !await SaveDocumentAsync(false)) return;
        }
        var index = _documents.IndexOf(entry); _documents.Remove(entry); _current = null;
        if (_documents.Count == 0) AddDocument(new EditorDocument("", $"Untitled{++_untitled}.cs"), _languages.Get("csharp")!);
        else ActivateDocument(_documents[Math.Min(index, _documents.Count - 1)]);
        entry.Document.Dispose(); RebuildTabs();
    }
    private void NewDocument() => AddDocument(new EditorDocument("", $"Untitled{++_untitled}.cs"), _languages.Get("csharp")!);
    private void OpenSample(string file, string text, string language)
    {
        var existing = _documents.FirstOrDefault(d => d.Document.FileName == file && !d.Document.IsModified);
        if (existing is not null) ActivateDocument(existing); else AddDocument(new EditorDocument(text, file), _languages.Get(language)!);
    }
    private void ToggleSplit()
    {
        if (_secondary is not null)
        {
            SetActive(_primary); _views.Children.Remove(_secondary); _secondary.Dispose(); _secondary = null; _views.ColumnDefinitions.Clear(); _primary.FocusEditor();
        }
        else if (_current is not null)
        {
            _views.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); _views.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            _secondary = new SyntaxEditor { Document = _current.Document, SyntaxLanguage = _current.Language, Theme = _theme, Margin = new Thickness(1, 0, 0, 0), FontSize = _primary.FontSize };
            WireEditor(_secondary); Grid.SetColumn(_secondary, 1); _views.Children.Add(_secondary); _secondary.CaretOffset = _primary.CaretOffset;
        }
        AddEvent(_secondary is null ? "Split closed" : "Split view opened — shared document, independent caret and viewport"); QueueDiagnostics();
    }
    private void ToggleOutline() { _outlinePane.Visibility = _outlinePane.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; QueueDiagnostics(); }
    private void ToggleToolPane(bool events)
    {
        _toolPane.Visibility = _toolPane.Visibility == Visibility.Visible && events == _showEvents ? Visibility.Collapsed : Visibility.Visible;
        _showEvents = events; _toolPane.Child = events ? _events : _errors; QueueDiagnostics();
    }
    private void RefreshDiagnostics(LanguageAnalysis analysis)
    {
        if (_active is null || analysis.DocumentId != _active.Document.Id || analysis.Revision != _active.Document.Revision) return;
        _errors.ItemsSource = analysis.Diagnostics.Select(d => new DiagnosticRow(d, _active.Document.Buffer.GetLocation(Math.Clamp(d.Range.Offset, 0, _active.Document.Buffer.TextLength)).Line)).ToArray();
        _errorTab.Content = $"Error List ({analysis.Diagnostics.Length})"; QueueDiagnostics();
    }
    private void UpdateStatus()
    {
        if (_active is null || _current is null) return;
        var location = _active.Document.Buffer.GetLocation(_active.CaretOffset);
        _fileTitle.Text = "EditorScene  /  " + _current.Document.FileName + (_current.Document.IsModified ? " *" : "");
        _status.Text = _active.IsReadOnly ? "Read only" : _active.Status;
        _position.Text = $"Ln {location.Line:N0}   Col {location.Column:N0}   Ch {_active.CaretOffset:N0}     {(_active.IsOverstrike ? "OVR" : "INS")}     {_active.Document.Encoding.WebName.ToUpperInvariant()}     {(_active.Document.PreferredLineEnding == "\r\n" ? "CRLF" : "LF")}     {(int)Math.Round(_active.FontSize / 13 * 100)}%";
        foreach (var (item, test) in _checks) if (item is ToggleMenuFlyoutItem toggle) toggle.IsChecked = test();
        RebuildTabCaptions(); QueueDiagnostics();
    }
    private void ApplyTheme(EditorTheme theme)
    {
        _theme = theme; RequestedTheme = theme.IsDark ? ElementTheme.Dark : ElementTheme.Light;
        _root.Background = SyntaxEditor.Brush(theme.Background); _root.RequestedTheme = RequestedTheme;
        _primary.Theme = theme; if (_secondary is not null) _secondary.Theme = theme;
        foreach (var panel in _chromePanels) panel.Background = SyntaxEditor.Brush(theme.Chrome);
        foreach (var border in _chromeBorders) { border.Background = SyntaxEditor.Brush(theme.Background); border.BorderBrush = SyntaxEditor.Brush(theme.Border); }
        foreach (var icon in _icons) icon.IsDark = theme.IsDark;
        if (_ui.GetValueOrDefault("Status bar") is Grid status) status.Background = SyntaxEditor.Brush(theme.Accent);
        var statusColor = theme == EditorTheme.HighContrast ? SkiaSharp.SKColors.Black : SkiaSharp.SKColors.White;
        _status.Foreground = SyntaxEditor.Brush(statusColor); _position.Foreground = SyntaxEditor.Brush(statusColor);
        _fileTitle.Foreground = SyntaxEditor.Brush(theme.Foreground); RebuildTabCaptions(); QueueDiagnostics();
    }
    private void AddEvent(string message) { _eventItems.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}"); while (_eventItems.Count > 200) _eventItems.RemoveAt(_eventItems.Count - 1); }
    private async Task GuardAsync(Func<Task> action)
    { try { await action(); } catch (OperationCanceledException) { } catch (Exception error) { _lastError = error.Message; _active.ReportError(error); AddEvent(error.Message); } finally { QueueDiagnostics(); } }
    private void OnPageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled) return;
        var control = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        if (!control) return;
        switch (e.Key)
        {
            case VirtualKey.N: e.Handled = true; NewDocument(); break;
            case VirtualKey.O: e.Handled = true; _ = GuardAsync(OpenDocumentAsync); break;
            case VirtualKey.S: e.Handled = true; _ = GuardAsync(async () => { await SaveDocumentAsync(false); }); break;
            case VirtualKey.W: e.Handled = true; _ = GuardAsync(CloseDocumentAsync); break;
        }
    }
    private sealed class WorkspaceDocument(EditorDocument document, ISyntaxLanguage language, StorageFile? file)
    {
        internal EditorDocument Document { get; } = document;
        internal ISyntaxLanguage Language { get; set; } = language;
        internal StorageFile? File { get; set; } = file;
        internal int Caret { get; set; }
        internal Button? Tab { get; set; }
    }
    private sealed record DiagnosticRow(EditorDiagnostic Diagnostic, int Line)
    { public override string ToString() => $"{Diagnostic.Severity}  {Diagnostic.Code}    Line {Line}    {Diagnostic.Message}"; }
}
