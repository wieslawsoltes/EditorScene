using EditorScene.Controls;
using EditorScene.LanguageServices;
using EditorScene.Rendering.Skia;
using EditorScene.Text.Macros;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace EditorScene.App;

public sealed partial class EditorPage
{
    private readonly EditorStatusBar _editorStatus = new();
    private readonly ContentPresenter _toolContent = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly TextBlock _toolTitle = new() { Text = "Error List", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 7, 0) };
    private readonly List<Border> _toolbarSeparators = [];
    private readonly MenuFlyout _toolbarOverflow = new();
    private ResourceDictionary? _chromeResources;
    private Orientation _splitOrientation = Orientation.Vertical;
    private string? _shownTitleFile;
    private bool _shownTitleModified;
    private long _shownErrorRevision = -2;
    private Guid _shownErrorDocument;
    private int _shownErrorCount = -2;

    private void BuildClassicLayout()
    {
        _chromeResources = EditorChrome.Create(_theme); Resources.MergedDictionaries.Add(_chromeResources);
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto }) _root.RowDefinitions.Add(new() { Height = height });
        Content = _root;
        var title = new Grid { Height = 24, Padding = new Thickness(7, 0, 7, 0), ColumnSpacing = 6 };
        title.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); title.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); title.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        title.Children.Add(NewIcon(EditorIconKind.Code)); Grid.SetColumn(_fileTitle, 1); title.Children.Add(_fileTitle);
        var about = new Button { Content = "About", FontSize = 11, MinHeight = 22, Height = 22, Padding = new Thickness(7, 0, 7, 0), BorderThickness = new Thickness(0) };
        about.Click += (_, _) => _ = GuardAsync(ShowAboutAsync); Register("About", about); Grid.SetColumn(about, 2); title.Children.Add(about); Put(title, 0); _chromePanels.Add(title);
        var menus = BuildMenus();
        var menuScroll = new ScrollViewer { Content = menus, HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Put(menuScroll, 1);
        var toolbarGrid = new Grid { Padding = new Thickness(3, 1, 3, 2), ColumnSpacing = 3 };
        toolbarGrid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 3; i++) toolbarGrid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var toolsScroll = new ScrollViewer { Content = _toolbar, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        toolbarGrid.Children.Add(toolsScroll);
        var overflow = new Button { Content = "»", Flyout = _toolbarOverflow, Width = 24, Height = 24, MinHeight = 24, Padding = new Thickness(3, 0, 3, 0), BorderThickness = new Thickness(0) };
        Register("Toolbar commands", overflow); ToolTipService.SetToolTip(overflow, "All toolbar commands");
        Grid.SetColumn(overflow, 1); Grid.SetColumn(_languagePicker, 2); Grid.SetColumn(_themePicker, 3);
        toolbarGrid.Children.Add(overflow); toolbarGrid.Children.Add(_languagePicker); toolbarGrid.Children.Add(_themePicker);
        Register("Language", _languagePicker); Register("Theme", _themePicker); Put(toolbarGrid, 2); _chromePanels.Add(toolbarGrid); BuildClassicToolbar();
        Put(BuildDocumentStrip(), 3); _chromePanels.Add(_tabs); Put(_navigator, 4);
        _editorGrid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); _editorGrid.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); _editorGrid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _views.First = _primary; _views.Orientation = _splitOrientation; _views.LayoutChanged += (_, _) => QueueDiagnostics(); _editorGrid.Children.Add(_views); Register("Split separator", _views.Separator);
        var outlineGrid = new Grid(); outlineGrid.RowDefinitions.Add(new() { Height = GridLength.Auto }); outlineGrid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var outlineHeading = PaneHeading("Document Outline", ToggleOutline); outlineGrid.Children.Add(outlineHeading); Grid.SetRow(_outline, 1); outlineGrid.Children.Add(_outline);
        var outlineHost = new Grid(); outlineHost.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); outlineHost.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        outlineHost.Children.Add(_outlineSplitter); Grid.SetColumn(outlineGrid, 1); outlineHost.Children.Add(outlineGrid); _outlinePane.Child = outlineHost;
        Register("Resize document outline", _outlineSplitter);
        Grid.SetColumn(_outlinePane, 1); _editorGrid.Children.Add(_outlinePane); _chromeBorders.Add(_outlinePane);
        var rail = new StackPanel { Width = 24 };
        var tabCanvas = new Canvas { Width = 22, Height = 126 };
        var tabText = new TextBlock { Text = "Document Outline", Width = 120, Height = 20, FontSize = 11, TextAlignment = TextAlignment.Center, RenderTransformOrigin = new Point(.5, .5), RenderTransform = new RotateTransform { Angle = 90 } };
        Canvas.SetLeft(tabText, -49); Canvas.SetTop(tabText, 53); tabCanvas.Children.Add(tabText);
        var outlineButton = new Button { Content = tabCanvas, Width = 24, Height = 132, Padding = new Thickness(0), BorderThickness = new Thickness(0) };
        outlineButton.Click += (_, _) => ToggleOutline(); Register("Document outline", outlineButton); rail.Children.Add(outlineButton); _chromePanels.Add(rail); Grid.SetColumn(rail, 2); _editorGrid.Children.Add(rail); Put(_editorGrid, 5);
        var toolGrid = new Grid(); toolGrid.RowDefinitions.Add(new() { Height = GridLength.Auto }); toolGrid.RowDefinitions.Add(new() { Height = GridLength.Auto }); toolGrid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        Register("Resize tool window", _toolSplitter); toolGrid.Children.Add(_toolSplitter);
        var heading = PaneHeading(_toolTitle, () => { _toolPane.Visibility = Visibility.Collapsed; QueueDiagnostics(); }); Grid.SetRow(heading, 1); toolGrid.Children.Add(heading);
        _toolContent.Content = _errors; Grid.SetRow(_toolContent, 2); toolGrid.Children.Add(_toolContent); _toolPane.Child = toolGrid; Put(_toolPane, 6); _chromeBorders.Add(_toolPane);
        _errors.RowsChanged += (_, _) => { UpdateErrorCaption(); QueueDiagnostics(); };
        Register("Diagnostic rows", _errors.RowsView); Register("Filter diagnostics", _errors.FilterBox);
        Register("Show errors", _errors.ErrorsToggle); Register("Show warnings", _errors.WarningsToggle); Register("Show information", _errors.InformationToggle);
        var toolTabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
        _errorTab.Click += (_, _) => ToggleToolPane(false); Register("Error List", _errorTab); toolTabs.Children.Add(_errorTab);
        var eventTab = new Button { Content = "Events", Height = 24, MinHeight = 24, FontSize = 11, Padding = new Thickness(10, 2, 10, 2), BorderThickness = new Thickness(0) };
        eventTab.Click += (_, _) => ToggleToolPane(true); Register("Events", eventTab); toolTabs.Children.Add(eventTab); Put(toolTabs, 7); _chromePanels.Add(toolTabs);
        Put(_editorStatus, 8); Register("Status bar", _editorStatus); Register("Editor zoom", _editorStatus.ZoomSlider); Register("Reset zoom", _editorStatus.ResetZoomButton); Register("Insert or overwrite", _editorStatus.OverwriteButton);
        InitializePaneSizing();
    }
    private Grid PaneHeading(string title, Action close) => PaneHeading(new TextBlock { Text = title, FontSize = 12, Margin = new Thickness(7, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center }, close);
    private Grid PaneHeading(TextBlock title, Action close)
    {
        var grid = new Grid { Height = 25 };
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        title.TextTrimming = TextTrimming.CharacterEllipsis; _paneCaptions.Add(title); grid.Children.Add(title);
        var button = new Button { Content = NewIcon(EditorIconKind.Close), Width = 24, Height = 24, MinHeight = 24, Padding = new Thickness(4), BorderThickness = new Thickness(0) };
        button.Click += (_, _) => close(); ToolTipService.SetToolTip(button, "Hide tool window");
        Register(ReferenceEquals(title, _toolTitle) ? "Hide bottom tool window" : "Hide " + title.Text, button);
        Grid.SetColumn(button, 1); grid.Children.Add(button); _chromePanels.Add(grid); return grid;
    }
    private void BuildClassicToolbar()
    {
        Tool("New", EditorIconKind.New, () => { NewDocument(); return Task.CompletedTask; });
        Tool("Open", EditorIconKind.Open, OpenDocumentAsync); Tool("Save", EditorIconKind.Save, async () => { await SaveDocumentAsync(false); }); AddSaveAllTool(); ToolSeparator();
        ToolCommand("Cut", EditorIconKind.Cut, EditorCommand.Cut); ToolCommand("Copy", EditorIconKind.Copy, EditorCommand.Copy); ToolCommand("Paste", EditorIconKind.Paste, EditorCommand.Paste); ToolCommand("Delete", EditorIconKind.Delete, EditorCommand.Delete); ToolSeparator();
        ToolCommand("Undo", EditorIconKind.Undo, EditorCommand.Undo); ToolCommand("Redo", EditorIconKind.Redo, EditorCommand.Redo); ToolSeparator();
        ToolCommand("Complete word", EditorIconKind.Completion, EditorCommand.CompleteWord); ToolCommand("Signature help", EditorIconKind.Signature, EditorCommand.SignatureHelp); ToolCommand("Quick info", EditorIconKind.Information, EditorCommand.QuickInfo);
        Tool("Insert snippet", EditorIconKind.Snippet, () => _active.ShowSnippetPickerAsync()); ToolSeparator();
        ToolCommand("Outdent", EditorIconKind.Outdent, EditorCommand.Outdent); ToolCommand("Indent", EditorIconKind.Indent, EditorCommand.Indent); ToolSeparator();
        ToolCommand("Comment lines", EditorIconKind.Comment, EditorCommand.Comment); ToolCommand("Uncomment lines", EditorIconKind.Uncomment, EditorCommand.Uncomment); ToolCommand("Format document", EditorIconKind.Format, EditorCommand.FormatDocument); ToolSeparator();
        ToolCommand("Find", EditorIconKind.Search, EditorCommand.Find); ToolCommand("Replace", EditorIconKind.Replace, EditorCommand.Replace); ToolSeparator();
        ToolCommand("Toggle bookmark", EditorIconKind.Bookmark, EditorCommand.ToggleBookmark); ToolCommand("Previous bookmark", EditorIconKind.Previous, EditorCommand.PreviousBookmark); ToolCommand("Next bookmark", EditorIconKind.Next, EditorCommand.NextBookmark); ToolSeparator();
        ToolCommand("Collapse all", EditorIconKind.Fold, EditorCommand.CollapseAll); ToolCommand("Expand all", EditorIconKind.Unfold, EditorCommand.ExpandAll); ToolSeparator();
        Tool("Run last macro", EditorIconKind.Play, () => _active.RunMacroAsync());
        Tool("Record or stop macro", EditorIconKind.Record, () => { if (_active.MacroRecording.State == MacroRecordingState.Idle) _active.MacroRecording.Record(); else _active.MacroRecording.Stop(); return Task.CompletedTask; });
        Tool("Pause or resume recording", EditorIconKind.Pause, () => { if (_active.MacroRecording.State == MacroRecordingState.Paused) _active.MacroRecording.Resume(); else _active.MacroRecording.Pause(); return Task.CompletedTask; }); ToolSeparator();
        Tool("Split editor", EditorIconKind.HorizontalSplit, () => { ToggleSplit(); return Task.CompletedTask; });
    }
    private void ToggleClassicSplit()
    {
        if (_secondary is not null)
        {
            var removed = _secondary; SetActive(_primary); _views.Second = null; _secondary = null; removed.Dispose(); _primary.FocusEditor();
        }
        else if (_current is not null)
        {
            var source = _active;
            var secondary = new SyntaxEditor { Snippets = source.Snippets };
            try
            {
                secondary.SetDocumentAndLanguage(_current.Document, _current.Language);
                secondary.CopyViewSettingsFrom(source); secondary.CaretOffset = source.CaretOffset;
                WireEditor(secondary); _secondary = secondary; _views.Second = secondary; _views.Orientation = _splitOrientation;
            }
            catch { secondary.Dispose(); throw; }
        }
        AddEvent(_secondary is null ? "Split closed" : "Split view opened — shared document, independent caret and viewport"); UpdateStatus();
    }
    private void SetSplitOrientation(Orientation orientation)
    {
        _splitOrientation = orientation; _views.Orientation = orientation;
        if (_secondary is null) ToggleSplit();
        UpdateStatus(); _active.FocusEditor();
    }
    private void UpdateErrorCaption()
    {
        if (_active is null) return;
        var analysis = _active.Analysis; var count = analysis?.Diagnostics.Length ?? -1;
        var document = _active.Document;
        if (document.Id == _shownErrorDocument && document.Revision == _shownErrorRevision && count == _shownErrorCount) return;
        _shownErrorDocument = document.Id; _shownErrorRevision = document.Revision; _shownErrorCount = count;
        _errorTab.Content = count < 0 ? "Error List (analyzing…)" : $"Error List ({count})";
    }
    private void UpdateClassicStatus()
    {
        if (_active is null || _current is null) return;
        var fileName = _current.Document.FileName; var modified = _current.Document.IsModified;
        if (_shownTitleFile != fileName || _shownTitleModified != modified)
        { _shownTitleFile = fileName; _shownTitleModified = modified; _fileTitle.Text = fileName + (modified ? " *" : "") + " — EditorScene"; }
        _editorStatus.MessagePrefix = LspStatusPrefix; _editorStatus.Refresh(); UpdateErrorCaption();
        foreach (var (item, test) in _checks) if (item is ToggleMenuFlyoutItem toggle) { var value = test(); if (toggle.IsChecked != value) toggle.IsChecked = value; }
        RebuildTabCaptions(); QueueDiagnostics();
    }
    private void ApplyClassicChrome(EditorTheme theme)
    {
        if (_chromeResources is not null) Resources.MergedDictionaries.Remove(_chromeResources);
        _chromeResources = EditorChrome.Create(theme); Resources.MergedDictionaries.Add(_chromeResources);
        _editorStatus.ApplyTheme(theme); _errors.ApplyTheme(theme); _views.ApplyTheme(theme); ApplyPaneSizingTheme(theme);
        foreach (var separator in _toolbarSeparators) separator.Background = SyntaxEditor.Brush(theme.Border);
        foreach (var icon in _icons) icon.IsHighContrast = ReferenceEquals(theme, EditorTheme.HighContrast);
    }
}
