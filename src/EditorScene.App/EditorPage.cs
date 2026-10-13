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
    private readonly ResizableSplitView _views = new();
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Spacing = 1 };
    private readonly StackPanel _toolbar = new() { Orientation = Orientation.Horizontal, Spacing = 1 };
    private readonly TextBlock _fileTitle = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _languagePicker = new() { Width = 128, MinHeight = 25, Height = 25, FontSize = 12, Padding = new Thickness(7, 0, 7, 0) };
    private readonly ComboBox _themePicker = new() { Width = 104, MinHeight = 25, Height = 25, FontSize = 12, Padding = new Thickness(7, 0, 7, 0) };
    private readonly NavigableSymbolSelector _navigator = new();
    private readonly DocumentOutline _outline = new();
    private readonly Border _outlinePane = new() { Visibility = Visibility.Collapsed, Width = 240, BorderThickness = new Thickness(1, 0, 0, 0) };
    private readonly Border _toolPane = new() { Height = 200, Visibility = Visibility.Collapsed, BorderThickness = new Thickness(0, 1, 0, 0) };
    private readonly DiagnosticList _errors = new();
    private readonly ListView _events = new() { SelectionMode = ListViewSelectionMode.None };
    private readonly Button _errorTab = new() { Content = "Error List (0)", Height = 25, MinHeight = 25, FontSize = 11, Padding = new Thickness(10, 2, 10, 2), BorderThickness = new Thickness(0) };
    private readonly ObservableCollection<string> _eventItems = [];
    private readonly LanguageRegistry _languages = new();
    private readonly List<WorkspaceDocument> _documents = [];
    private readonly Dictionary<string, FrameworkElement> _ui = new(StringComparer.Ordinal);
    private readonly Dictionary<MenuFlyoutItem, Func<bool>> _checks = [];
    private readonly List<EditorIcon> _icons = [];
    private readonly HashSet<string> _tabControlNames = [];
    private Button? _closeTabButton;
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
        Uno.WinRTFeatureConfiguration.Storage.Pickers.WasmConfiguration = Uno.WasmPickerConfiguration.DownloadUpload;
#endif
        _diagnostics |= Environment.GetEnvironmentVariable("EDITORSCENE_NATIVE_CHECKS") == "1";
        BuildLayout(); WireEditor(_primary); SetActive(_primary);
        _commandBindings.ExecutionFailed += (_, error) => { _lastError = error.Message; AddEvent("Error: " + error.Message); QueueDiagnostics(); };
        _events.ItemsSource = _eventItems;
        _languagePicker.ItemsSource = _languages.Languages.Select(l => l.DisplayName).ToArray();
        _languagePicker.SelectionChanged += (_, _) =>
        {
            if (_updatingPickers || _current is null || _languagePicker.SelectedItem is not string name) return;
            if (_current.Language is EditorScene.Protocol.LspSyntaxLanguage) { UpdateLspPicker(); return; }
            var language = _languages.Languages.First(l => l.DisplayName == name); _current.Language = language;
            _primary.SyntaxLanguage = language; if (_secondary is not null) _secondary.SyntaxLanguage = language;
            AddEvent($"Syntax language: {name}"); UpdateStatus();
        };
        _themePicker.ItemsSource = new[] { "Light", "Dark", "High contrast" }; _themePicker.SelectedIndex = 0;
        _themePicker.SelectionChanged += (_, _) => ApplyTheme(_themePicker.SelectedIndex switch { 1 => EditorTheme.Dark, 2 => EditorTheme.HighContrast, _ => EditorTheme.Light });
        Loaded += async (_, _) =>
        {
            _loaded = true; _commandBindings.Editor = _active; ApplyTheme(_theme); _primary.FocusEditor(); QueueActiveTabReveal(); QueueDiagnostics();
            if (Environment.GetEnvironmentVariable("EDITORSCENE_NATIVE_CHECKS") == "1") await RunNativeChecksAsync();
        };
        Unloaded += (_, _) => { _loaded = false; _commandBindings.Editor = null; _documentList.Hide(); };
        KeyDown += OnPageKeyDown; SizeChanged += (_, _) => QueueDiagnostics();
        AddDocument(new EditorDocument(_diagnostics ? "" : Samples.CSharp, _diagnostics ? "Untitled.cs" : "SensorMonitor.cs"), _languages.Get("csharp")!);
    }
    private void BuildLayout() => BuildClassicLayout();
    private void Put(UIElement element, int row) { Grid.SetRow(element, row); _root.Children.Add(element); }
    private void Register(string name, FrameworkElement element) { _ui[name] = element; AutomationProperties.SetName(element, name); }
    private EditorIcon NewIcon(EditorIconKind kind) { var icon = new EditorIcon { Kind = kind, IsDark = _theme.IsDark, IsHighContrast = ReferenceEquals(_theme, EditorTheme.HighContrast) }; _icons.Add(icon); return icon; }
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
        _active = editor; _navigator.Editor = editor; _outline.Editor = editor; _errors.Editor = editor; _editorStatus.Editor = editor; _commandBindings.Editor = editor;
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
        if (_dialog is not null || !_documents.Contains(entry)) return;
        if (ReferenceEquals(_current, entry)) { _active.FocusEditor(); QueueActiveTabReveal(); return; }
        if (_current is not null) _current.Caret = ReferenceEquals(_active.Document, _current.Document) ? _active.CaretOffset : _primary.CaretOffset;
        _current = entry; _documentList.SelectedDocumentId = entry.Document.Id; _primary.SetDocumentAndLanguage(entry.Document, entry.Language);
        if (_secondary is not null) { _secondary.SetDocumentAndLanguage(entry.Document, entry.Language); }
        _primary.CaretOffset = Math.Clamp(entry.Caret, 0, entry.Document.Buffer.TextLength);
        SetActive(_primary); UpdateLspPicker();
        UpdateErrorCaption(); RebuildTabCaptions(); UpdateStatus(); _primary.FocusEditor(); QueueActiveTabReveal();
    }
    private void RebuildTabs()
    {
        foreach (var name in _tabControlNames) _ui.Remove(name);
        _tabControlNames.Clear();
        var retained = _documents.Where(d => d.Tab is not null).Select(d => (UIElement)d.Tab!).ToHashSet();
        for (var i = _tabs.Children.Count - 1; i >= 0; i--)
            if (!retained.Contains(_tabs.Children[i]))
            {
                if (_tabs.Children[i] is Button removed) UnwireDocumentTab(removed);
                _tabs.Children.RemoveAt(i);
            }
        for (var i = 0; i < _documents.Count; i++)
        {
            var entry = _documents[i];
            if (entry.Tab is null)
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
                panel.Children.Add(new TextBlock { Text = entry.Document.FileName, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
                var button = new Button { Content = panel, Height = 27, MinHeight = 27, Padding = new Thickness(9, 2, 9, 2), BorderThickness = new Thickness(0, 0, 0, 2) };
                WireDocumentTab(button, entry.Document.Id);
                entry.Tab = button; entry.TabTheme = null; _tabBuildCount++;
            }
            var tab = entry.Tab;
            if (i >= _tabs.Children.Count || !ReferenceEquals(_tabs.Children[i], tab))
            {
                var previous = _tabs.Children.IndexOf(tab);
                if (previous >= 0) _tabs.Children.RemoveAt(previous);
                _tabs.Children.Insert(i, tab);
            }
            var name = "Document " + entry.Document.FileName;
            Register(name, tab); _tabControlNames.Add(name);
        }
        RefreshDocumentList(); RebuildTabCaptions(); QueueActiveTabReveal();
    }
    private void RebuildTabCaptions()
    {
        foreach (var entry in _documents)
        {
            var active = ReferenceEquals(entry, _current); var modified = entry.Document.IsModified; var name = entry.Document.FileName;
            if (entry.Tab is not { } tab) continue;
            if (entry.CaptionName != name || entry.CaptionModified != modified || entry.TabTheme is null)
            {
                if (tab.Content is StackPanel panel && panel.Children[0] is TextBlock text) text.Text = name + (modified ? " *" : "");
                entry.CaptionName = name; entry.CaptionModified = modified;
                ToolTipService.SetToolTip(tab, name);
                _documentList.UpdateDocument(new(entry.Document.Id, name, modified));
            }
            if (!ReferenceEquals(entry.TabTheme, _theme) || entry.TabActive != active)
            {
                tab.Background = SyntaxEditor.Brush(active ? _theme.Background : _theme.Chrome);
                tab.BorderBrush = SyntaxEditor.Brush(active ? _theme.Accent : _theme.Chrome);
                entry.TabTheme = _theme; entry.TabActive = active;
            }
        }
    }
    private void NewDocument() => AddDocument(new EditorDocument("", $"Untitled{++_untitled}.cs"), _languages.Get("csharp")!);
    private void OpenSample(string file, string text, string language)
    {
        var existing = _documents.FirstOrDefault(d => d.Document.FileName == file && !d.Document.IsModified);
        if (existing is not null) ActivateDocument(existing); else AddDocument(new EditorDocument(text, file), _languages.Get(language)!);
    }
    private void ToggleSplit() => ToggleClassicSplit();
    private void ToggleOutline() { _outlinePane.Visibility = _outlinePane.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; QueueDiagnostics(); }
    private void ToggleToolPane(bool events)
    {
        _toolPane.Visibility = _toolPane.Visibility == Visibility.Visible && events == _showEvents ? Visibility.Collapsed : Visibility.Visible;
        _showEvents = events; _toolTitle.Text = events ? "Events" : "Error List"; _toolContent.Content = events ? _events : _errors; QueueDiagnostics();
    }
    private void RefreshDiagnostics(LanguageAnalysis analysis) => UpdateErrorCaption();
    private void UpdateStatus() => UpdateClassicStatus();
    private void ApplyTheme(EditorTheme theme)
    {
        _theme = theme; RequestedTheme = theme.IsDark ? ElementTheme.Dark : ElementTheme.Light;
        _root.Background = SyntaxEditor.Brush(theme.Background); _root.RequestedTheme = RequestedTheme;
        _primary.Theme = theme; if (_secondary is not null) _secondary.Theme = theme;
        foreach (var panel in _chromePanels) panel.Background = SyntaxEditor.Brush(theme.Chrome);
        foreach (var border in _chromeBorders) { border.Background = SyntaxEditor.Brush(theme.Background); border.BorderBrush = SyntaxEditor.Brush(theme.Border); }
        foreach (var icon in _icons) icon.IsDark = theme.IsDark;
        ApplyClassicChrome(theme);
        _fileTitle.Foreground = SyntaxEditor.Brush(theme.Foreground); RebuildTabCaptions(); QueueDiagnostics();
    }
    private void AddEvent(string message) { _eventItems.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}"); while (_eventItems.Count > 200) _eventItems.RemoveAt(_eventItems.Count - 1); }
    private async Task GuardAsync(Func<Task> action)
    { try { await action(); } catch (OperationCanceledException) { } catch (Exception error) { _lastError = error.Message; _active.ReportError(error); AddEvent(error.Message); } finally { QueueDiagnostics(); } }
    private void OnPageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled || _dialog is not null) return;
        var control = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        if (!control) return;
        if (HandleDocumentNavigationKey(e)) { e.Handled = true; return; }
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
        internal EditorTheme? TabTheme { get; set; }
        internal bool TabActive { get; set; }
        internal string? CaptionName { get; set; }
        internal bool CaptionModified { get; set; }
    }
}
