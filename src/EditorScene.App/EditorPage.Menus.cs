using System.Text;
using EditorScene.Controls;
using EditorScene.Rendering.Skia;
using EditorScene.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace EditorScene.App;

public sealed partial class EditorPage
{
    private MenuBar BuildMenus()
    {
        var bar = new MenuBar { MinHeight = 24, Height = 26, Padding = new Thickness(2, 0, 2, 0) };
        MenuBarItem Menu(string title) { var menu = new MenuBarItem { Title = title, MinHeight = 24, Height = 24, FontSize = 12, Padding = new Thickness(7, 2, 7, 2) }; bar.Items.Add(menu); Register("Menu " + title, menu); return menu; }
        var file = Menu("File");
        Item(file, "New", () => { NewDocument(); return Task.CompletedTask; }, "Ctrl+N");
        Item(file, "Open…", OpenDocumentAsync, "Ctrl+O");
        Item(file, "Save", async () => { await SaveDocumentAsync(false); }, "Ctrl+S");
        Item(file, "Save As…", async () => { await SaveDocumentAsync(true); });
        AddSaveAllMenu(file);
        Item(file, "Close", CloseDocumentAsync, "Ctrl+W");
        Item(file, "Close Other Documents", () => CloseCurrentDocumentsAsync(DocumentCloseScope.Others));
        Item(file, "Close All Documents", () => CloseCurrentDocumentsAsync(DocumentCloseScope.All)); Separator(file);
        var examples = new MenuFlyoutSubItem { Text = "Open sample", FontSize = 12 };
        Sample(examples, "C# — SensorMonitor", "SensorMonitor.cs", Samples.CSharp, "csharp");
        Sample(examples, "Visual Basic — SensorMonitor", "SensorMonitor.vb", Samples.VisualBasic, "vb");
        Sample(examples, "JSON — Settings", "settings.json", Samples.Json, "json");
        Sample(examples, "XML — Workspace", "workspace.xml", Samples.Xml, "xml");
        Sample(examples, "XAML — Editor page", "EditorPage.xaml", Samples.Xaml, "xaml"); file.Items.Add(examples);
        Item(file, "100,000-line document", () => { LoadLargeDocument(); return Task.CompletedTask; }); Separator(file);
        Item(file, "Export highlighted HTML…", () => ExportAsync(false)); Item(file, "Export rich text (RTF)…", () => ExportAsync(true));
        Item(file, "About EditorScene", ShowAboutAsync);
        var edit = Menu("Edit");
        Command(edit, "Undo", EditorCommand.Undo, "Ctrl+Z"); Command(edit, "Redo", EditorCommand.Redo, "Ctrl+Y"); Separator(edit);
        Command(edit, "Cut", EditorCommand.Cut, "Ctrl+X"); Command(edit, "Copy", EditorCommand.Copy, "Ctrl+C"); Command(edit, "Paste", EditorCommand.Paste, "Ctrl+V"); Command(edit, "Delete", EditorCommand.Delete, "Del"); Separator(edit);
        Command(edit, "Select All", EditorCommand.SelectAll, "Ctrl+A"); Command(edit, "Select Line", EditorCommand.SelectLine, "Ctrl+L"); Command(edit, "Select Next Occurrence", EditorCommand.SelectNextOccurrence, "Ctrl+D"); Command(edit, "Clear Extra Selections", EditorCommand.ClearExtraSelections, "Esc"); Separator(edit);
        Command(edit, "Find…", EditorCommand.Find, "Ctrl+F"); Command(edit, "Replace…", EditorCommand.Replace, "Ctrl+H"); Command(edit, "Find Next", EditorCommand.FindNext, "F3"); Command(edit, "Find Previous", EditorCommand.FindPrevious, "Shift+F3"); Command(edit, "Go To Line…", EditorCommand.GoToLine, "Ctrl+G");
        Command(edit, "Select All Occurrences", EditorCommand.SelectAllOccurrences);
        Command(edit, "Go To Matching Bracket", EditorCommand.GoToMatchingBracket);
        Command(edit, "Select To Matching Bracket", EditorCommand.SelectToMatchingBracket);
        Command(edit, "Expand Selection", EditorCommand.ExpandSelection, "Alt+Shift+Right");
        Command(edit, "Contract Selection", EditorCommand.ContractSelection, "Alt+Shift+Left");
        var advanced = Menu("Advanced");
        Command(advanced, "Increase Line Indent", EditorCommand.Indent, "Tab"); Command(advanced, "Decrease Line Indent", EditorCommand.Outdent, "Shift+Tab"); Separator(advanced);
        Command(advanced, "Comment Lines", EditorCommand.Comment, "Ctrl+K, Ctrl+C"); Command(advanced, "Uncomment Lines", EditorCommand.Uncomment, "Ctrl+K, Ctrl+U"); Command(advanced, "Format Document", EditorCommand.FormatDocument, "Ctrl+K, Ctrl+D"); Separator(advanced);
        Command(advanced, "Make Uppercase", EditorCommand.Uppercase, "Ctrl+Shift+U"); Command(advanced, "Make Lowercase", EditorCommand.Lowercase, "Ctrl+U");
        Command(advanced, "Duplicate Line / Selection", EditorCommand.DuplicateLine); Command(advanced, "Delete Lines", EditorCommand.DeleteLine, "Ctrl+Shift+L"); Command(advanced, "Sort Selected Lines", EditorCommand.SortLines); Separator(advanced);
        AddWhitespaceMenu(advanced);
        Command(advanced, "Convert Line Endings to LF", EditorCommand.ConvertToLf); Command(advanced, "Convert Line Endings to CRLF", EditorCommand.ConvertToCrLf);
        var prompt = Menu("IntelliPrompt");
        Command(prompt, "Complete Word", EditorCommand.CompleteWord, "Ctrl+Space"); Command(prompt, "Quick Info", EditorCommand.QuickInfo); Command(prompt, "Signature Help", EditorCommand.SignatureHelp, "Ctrl+Shift+Space");
        Toggle(prompt, "Automatically Show Completion", () => _active.AutoShowCompletion, () => _active.AutoShowCompletion = !_active.AutoShowCompletion);
        Toggle(prompt, "Automatically Complete Brackets", () => _active.AutoCompleteBrackets, () => _active.AutoCompleteBrackets = !_active.AutoCompleteBrackets);
        Separator(prompt);
        Item(prompt, "Insert Snippet…", () => _active.ShowSnippetPickerAsync(), "Ctrl+K, Ctrl+X");
        Item(prompt, "Surround With…", () => _active.ShowSnippetPickerAsync(true), "Ctrl+K, Ctrl+S");
        Item(prompt, "Complete Snippet", () => { _active.EndSnippet(); _active.FocusEditor(); return Task.CompletedTask; });
        Separator(prompt);
        Item(prompt, "Import Snippets…", ImportSnippetsAsync);
        Item(prompt, "Export Snippets…", ExportSnippetsAsync);
        BuildMacroMenu(Menu("Macros"));
        var view = Menu("View");
        Toggle(view, "Word Wrap", () => _active.WordWrap, () => _ = _active.ExecuteAsync(EditorCommand.ToggleWordWrap));
        Toggle(view, "Line Numbers", () => _active.ShowLineNumbers, () => _ = _active.ExecuteAsync(EditorCommand.ToggleLineNumbers));
        Toggle(view, "Visible Whitespace", () => _active.ShowWhitespace, () => _ = _active.ExecuteAsync(EditorCommand.ToggleWhitespace));
        Toggle(view, "Indentation Guides", () => _active.ShowIndentGuides, () => _ = _active.ExecuteAsync(EditorCommand.ToggleIndentGuides));
        Toggle(view, "Column Ruler", () => _active.ShowRuler, () => _ = _active.ExecuteAsync(EditorCommand.ToggleRuler)); Separator(view);
        Toggle(view, "Read Only", () => _active.IsReadOnly, () => _ = _active.ExecuteAsync(EditorCommand.ToggleReadOnly));
        Toggle(view, "Document Outline", () => _outlinePane.Visibility == Visibility.Visible, ToggleOutline);
        Item(view, "Error List", () => { ToggleToolPane(false); return Task.CompletedTask; }); Item(view, "Events", () => { ToggleToolPane(true); return Task.CompletedTask; }); Separator(view);
        Command(view, "Zoom In", EditorCommand.ZoomIn); Command(view, "Zoom Out", EditorCommand.ZoomOut); Command(view, "Reset Zoom", EditorCommand.ResetZoom);
        var outlining = Menu("Outlining");
        Command(outlining, "Toggle Current Region", EditorCommand.ToggleFold, "Ctrl+M, Ctrl+M"); Command(outlining, "Collapse All", EditorCommand.CollapseAll, "Ctrl+M, Ctrl+O"); Command(outlining, "Expand All", EditorCommand.ExpandAll, "Ctrl+M, Ctrl+L");
        var languageMenu = Menu("Language");
        foreach (var language in _languages.Languages)
        {
            var captured = language;
            Toggle(languageMenu, captured.DisplayName, () => _active.SyntaxLanguage.Id == captured.Id, () => _languagePicker.SelectedItem = captured.DisplayName);
        }
        BuildLspMenu(languageMenu);
        var bookmarks = Menu("Bookmarks");
        Command(bookmarks, "Toggle Bookmark", EditorCommand.ToggleBookmark, "Ctrl+F2"); Command(bookmarks, "Next Bookmark", EditorCommand.NextBookmark, "F2"); Command(bookmarks, "Previous Bookmark", EditorCommand.PreviousBookmark, "Shift+F2"); Command(bookmarks, "Clear All Bookmarks", EditorCommand.ClearBookmarks); Separator(bookmarks); Command(bookmarks, "Toggle Breakpoint Indicator", EditorCommand.ToggleBreakpoint, "F9");
        Separator(view);
        Toggle(view, "Default Context Menu", () => _active.IsDefaultContextMenuEnabled, () => _ = _active.ExecuteAsync(EditorCommand.ToggleDefaultContextMenu));
        Toggle(view, "Right-click Moves Caret", () => _active.CanMoveCaretForSecondaryPointerButton, () => _ = _active.ExecuteAsync(EditorCommand.ToggleSecondaryPointerCaret));
        Item(view, "Editor Options…", () => _active.ShowViewOptionsAsync());
        var window = Menu("Window");
        Toggle(window, "Split Editor", () => _secondary is not null, ToggleSplit);
        Item(window, "Split Horizontally", () => { SetSplitOrientation(Orientation.Vertical); return Task.CompletedTask; });
        Item(window, "Split Vertically", () => { SetSplitOrientation(Orientation.Horizontal); return Task.CompletedTask; });
        Item(window, "Center Split", () => { _views.Ratio = .5; _active.FocusEditor(); return Task.CompletedTask; });
        Item(window, "Focus Other View", () => { (_secondary is not null && ReferenceEquals(_active, _primary) ? _secondary : _primary).FocusEditor(); return Task.CompletedTask; });
        Item(window, "Next Document", () => { NavigateDocument(1); return Task.CompletedTask; }, "Ctrl+PageDown");
        Item(window, "Previous Document", () => { NavigateDocument(-1); return Task.CompletedTask; }, "Ctrl+PageUp");
        Item(window, "Documents…", () => { DispatcherQueue.TryEnqueue(ShowDocumentList); return Task.CompletedTask; }, "Ctrl+Alt+Down");
        Item(window, "Tab Context Menu", () => { DispatcherQueue.TryEnqueue(ShowCurrentDocumentTabMenu); return Task.CompletedTask; }, "Alt+-");
        return bar;
    }
    private MenuFlyoutItem Item(MenuBarItem menu, string label, Func<Task> action, string shortcut = "")
    {
        var item = new MenuFlyoutItem { Text = label, FontSize = 12, KeyboardAcceleratorTextOverride = shortcut };
        item.Click += (_, _) => _ = GuardAsync(action); menu.Items.Add(item); Register("Command " + label, item); return item;
    }
    private void Command(MenuBarItem menu, string label, EditorCommand command, string shortcut = "") => AddBoundMenuCommand(menu, label, command, shortcut);
    private void Separator(MenuBarItem menu) => menu.Items.Add(new MenuFlyoutSeparator());
    private void Toggle(MenuBarItem menu, string label, Func<bool> get, Action action)
    {
        var item = new ToggleMenuFlyoutItem { Text = label, FontSize = 12 };
        item.Click += (_, _) => { action(); UpdateStatus(); }; _checks[item] = get; menu.Items.Add(item); Register("Command " + label, item);
    }
    private void Sample(MenuFlyoutSubItem menu, string label, string file, string text, string language)
    {
        var item = new MenuFlyoutItem { Text = label, FontSize = 12 }; item.Click += (_, _) => OpenSample(file, text, language); menu.Items.Add(item); Register("Sample " + language, item);
    }
    private void ToolCommand(string label, EditorIconKind icon, EditorCommand command) => AddBoundToolCommand(label, icon, command);
    private void Tool(string label, EditorIconKind icon, Func<Task> action)
    {
        var button = new Button { Content = NewIcon(icon), Width = 26, Height = 25, MinHeight = 25, Padding = new Thickness(4), BorderThickness = new Thickness(0), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        ToolTipService.SetToolTip(button, label); button.Click += (_, _) => _ = GuardAsync(action); _toolbar.Children.Add(button); Register("Toolbar " + label, button);
        var overflow = new MenuFlyoutItem { Text = label, FontSize = 12 };
        overflow.Click += (_, _) => _ = GuardAsync(action); AddToolbarOverflow(overflow);
    }
    private void ToolSeparator()
    {
        var border = new Border { Width = 1, Height = 17, Margin = new Thickness(4, 4, 4, 4), Background = SyntaxEditor.Brush(_theme.Border) };
        _toolbar.Children.Add(border); _toolbarSeparators.Add(border);
    }
    private void LoadLargeDocument()
    {
        var text = new StringBuilder(5_000_000);
        for (var i = 1; i <= 100_000; i++) { if (i > 1) text.Append('\n'); text.Append("Line ").Append(i).Append("  — virtualized, shaped text — 日本語 🙂"); }
        AddDocument(new EditorDocument(text.ToString(), "large-document.txt"), _languages.Get("plaintext")!);
        AddEvent("Loaded 100,000-line document");
    }
    private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        if (_dialog is not null) return ContentDialogResult.None;
        dialog.XamlRoot = XamlRoot; dialog.RequestedTheme = RequestedTheme; _dialog = dialog;
        dialog.Opened += (_, _) => QueueDiagnostics(); dialog.Closed += (_, _) => QueueDiagnostics();
        try { return await dialog.ShowAsync(); }
        finally { _dialog = null; QueueDiagnostics(); }
    }
    private async Task ShowAboutAsync() => await ShowDialogAsync(new ContentDialog
    {
        Title = "EditorScene", CloseButtonText = "Close",
        Content = new TextBlock { Text = "Native Uno Platform editor for desktop and WebAssembly.\n\nReusable document, language, protocol, rendering and control packages. Text is shaped and drawn into Uno’s Skia canvas; hardware acceleration depends on the host.\n\nIndependent implementation targeting Actipro SyntaxEditor’s documented features. Not affiliated with Actipro. Full API, feature and pixel parity is not yet established.\n\nThe MIT-licensed UnoEdit core and native port are pinned and credited in the repository. Microsoft Roslyn provides C# and Visual Basic analysis.\n\nSource: github.com/wieslawsoltes/EditorScene", TextWrapping = TextWrapping.Wrap, MaxWidth = 460, FontSize = 13 }
    });
    private async Task OpenDocumentAsync()
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*"); var files = await picker.PickMultipleFilesAsync();
        foreach (var file in files)
        {
            using var stream = await file.OpenStreamForReadAsync();
            var document = new EditorDocument("", file.Name);
            try { document.Load(stream); } catch { document.Dispose(); throw; }
            AddDocument(document, _languages.ForFile(file.Name) ?? _languages.Get("plaintext")!, file);
            AddEvent("Opened " + file.Name);
        }
    }
    private async Task ExportAsync(bool rtf)
    {
        var document = _active.Document; var language = _active.SyntaxLanguage; var snapshot = document.Snapshot(); var theme = _theme;
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = Path.GetFileNameWithoutExtension(document.FileName) };
        picker.FileTypeChoices.Add(rtf ? "Rich text" : "Highlighted HTML", new List<string> { rtf ? ".rtf" : ".html" });
        var file = await picker.PickSaveFileAsync(); if (file is null) return;
        var analysis = await language.AnalyzeAsync(snapshot);
        var styles = analysis.Classifications.Select(s => new ExportStyle(s.Range.Offset, s.Range.Length, "#" + theme.ColorFor(s.Kind).ToString()[3..]));
        var text = rtf ? TextExport.ToRtf(snapshot, styles) : TextExport.ToHtml(snapshot, styles, true);
        CachedFileManager.DeferUpdates(file); await FileIO.WriteTextAsync(file, text); await CachedFileManager.CompleteUpdatesAsync(file); AddEvent("Exported " + file.Name);
    }
}
