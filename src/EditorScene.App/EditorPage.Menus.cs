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
        var bar = new MenuBar { MinHeight = 26, Height = 28, Padding = new Thickness(2, 0, 2, 0) };
        MenuBarItem Menu(string title) { var menu = new MenuBarItem { Title = title }; bar.Items.Add(menu); Register("Menu " + title, menu); return menu; }
        var file = Menu("File");
        Item(file, "New", () => { NewDocument(); return Task.CompletedTask; }, "Ctrl+N");
        Item(file, "Open…", OpenDocumentAsync, "Ctrl+O");
        Item(file, "Save", async () => { await SaveDocumentAsync(false); }, "Ctrl+S");
        Item(file, "Save As…", async () => { await SaveDocumentAsync(true); });
        Item(file, "Close", CloseDocumentAsync, "Ctrl+W"); Separator(file);
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
        var advanced = Menu("Advanced");
        Command(advanced, "Increase Line Indent", EditorCommand.Indent, "Tab"); Command(advanced, "Decrease Line Indent", EditorCommand.Outdent, "Shift+Tab"); Separator(advanced);
        Command(advanced, "Comment Lines", EditorCommand.Comment, "Ctrl+K, Ctrl+C"); Command(advanced, "Uncomment Lines", EditorCommand.Uncomment, "Ctrl+K, Ctrl+U"); Command(advanced, "Format Document", EditorCommand.FormatDocument, "Ctrl+K, Ctrl+D"); Separator(advanced);
        Command(advanced, "Make Uppercase", EditorCommand.Uppercase, "Ctrl+Shift+U"); Command(advanced, "Make Lowercase", EditorCommand.Lowercase, "Ctrl+U");
        Command(advanced, "Duplicate Line / Selection", EditorCommand.DuplicateLine); Command(advanced, "Delete Lines", EditorCommand.DeleteLine, "Ctrl+Shift+L"); Command(advanced, "Sort Selected Lines", EditorCommand.SortLines); Command(advanced, "Trim Trailing Whitespace", EditorCommand.TrimTrailingWhitespace); Separator(advanced);
        Command(advanced, "Convert Leading Tabs to Spaces", EditorCommand.TabsToSpaces); Command(advanced, "Convert Leading Spaces to Tabs", EditorCommand.SpacesToTabs);
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
        var window = Menu("Window");
        Toggle(window, "Split Editor", () => _secondary is not null, ToggleSplit);
        Item(window, "Focus Other View", () => { (_secondary is not null && ReferenceEquals(_active, _primary) ? _secondary : _primary).FocusEditor(); return Task.CompletedTask; });
        Item(window, "Next Document", () => { if (_current is not null) ActivateDocument(_documents[(_documents.IndexOf(_current) + 1) % _documents.Count]); return Task.CompletedTask; });
        return bar;
    }
    private MenuFlyoutItem Item(MenuBarItem menu, string label, Func<Task> action, string shortcut = "")
    {
        var item = new MenuFlyoutItem { Text = label, FontSize = 12, KeyboardAcceleratorTextOverride = shortcut };
        item.Click += (_, _) => _ = GuardAsync(action); menu.Items.Add(item); Register("Command " + label, item); return item;
    }
    private void Command(MenuBarItem menu, string label, EditorCommand command, string shortcut = "") => Item(menu, label, async () => { await _active.ExecuteAsync(command); AddEvent(label); }, shortcut);
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
    private void BuildToolbar()
    {
        Tool("New", EditorIconKind.New, () => { NewDocument(); return Task.CompletedTask; });
        Tool("Open", EditorIconKind.Open, OpenDocumentAsync); Tool("Save", EditorIconKind.Save, async () => { await SaveDocumentAsync(false); }); ToolSeparator();
        ToolCommand("Cut", EditorIconKind.Cut, EditorCommand.Cut); ToolCommand("Copy", EditorIconKind.Copy, EditorCommand.Copy); ToolCommand("Paste", EditorIconKind.Paste, EditorCommand.Paste); ToolSeparator();
        ToolCommand("Undo", EditorIconKind.Undo, EditorCommand.Undo); ToolCommand("Redo", EditorIconKind.Redo, EditorCommand.Redo); ToolSeparator();
        ToolCommand("Find", EditorIconKind.Search, EditorCommand.Find); ToolCommand("Replace", EditorIconKind.Replace, EditorCommand.Replace); ToolSeparator();
        ToolCommand("Toggle bookmark", EditorIconKind.Bookmark, EditorCommand.ToggleBookmark); ToolCommand("Previous bookmark", EditorIconKind.Previous, EditorCommand.PreviousBookmark); ToolCommand("Next bookmark", EditorIconKind.Next, EditorCommand.NextBookmark); ToolSeparator();
        ToolCommand("Complete word", EditorIconKind.Completion, EditorCommand.CompleteWord); ToolCommand("Format document", EditorIconKind.Format, EditorCommand.FormatDocument); ToolSeparator();
        ToolCommand("Collapse all", EditorIconKind.Fold, EditorCommand.CollapseAll); ToolCommand("Expand all", EditorIconKind.Unfold, EditorCommand.ExpandAll);
        Tool("Split editor", EditorIconKind.Split, () => { ToggleSplit(); return Task.CompletedTask; });
    }
    private void ToolCommand(string label, EditorIconKind icon, EditorCommand command) => Tool(label, icon, () => _active.ExecuteAsync(command));
    private void Tool(string label, EditorIconKind icon, Func<Task> action)
    {
        var button = new Button { Content = NewIcon(icon), Width = 26, Height = 25, MinHeight = 25, Padding = new Thickness(4), BorderThickness = new Thickness(0), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        ToolTipService.SetToolTip(button, label); button.Click += (_, _) => _ = GuardAsync(action); _toolbar.Children.Add(button); Register("Toolbar " + label, button);
    }
    private void ToolSeparator() => _toolbar.Children.Add(new Border { Width = 1, Height = 17, Margin = new Thickness(4, 4, 4, 4), Background = SyntaxEditor.Brush(_theme.Border) });
    private void LoadLargeDocument()
    {
        var text = new StringBuilder(5_000_000);
        for (var i = 1; i <= 100_000; i++) { if (i > 1) text.Append('\n'); text.Append("Line ").Append(i).Append("  — virtualized, shaped text — 日本語 🙂"); }
        AddDocument(new EditorDocument(text.ToString(), "large-document.txt"), _languages.Get("plaintext")!);
        AddEvent("Loaded 100,000-line document");
    }
    private async Task GoToLineAsync()
    {
        var input = new TextBox { Text = _active.Document.Buffer.GetLineByOffset(_active.CaretOffset).LineNumber.ToString(), MinWidth = 240, PlaceholderText = "Line[:column]", FontSize = 14 };
        Register("Go to line input", input);
        var dialog = new ContentDialog { Title = $"Go to line (1–{_active.Document.Buffer.LineCount:N0})", Content = input, PrimaryButtonText = "Go", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) return;
        var pieces = input.Text.Split(':');
        if (!int.TryParse(pieces[0], out var line) || line <= 0 || line > _active.Document.Buffer.LineCount || pieces.Length > 2 || (pieces.Length == 2 && !int.TryParse(pieces[1], out _))) throw new FormatException("Enter a valid line number, optionally followed by :column.");
        var column = pieces.Length == 2 ? Math.Max(1, int.Parse(pieces[1], System.Globalization.CultureInfo.InvariantCulture)) : 1;
        _active.GoToLine(line, column);
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
    private async Task<bool> SaveDocumentAsync(bool saveAs)
    {
        var entry = _current; if (entry is null) return false;
        var file = entry.File;
#if __WASM__
        // Upload-picker files are private temporary copies, not writable handles to the user's original file.
        saveAs = true;
#endif
        if (file is null || saveAs)
        {
            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = entry.Document.FileName };
            var extension = Path.GetExtension(entry.Document.FileName); if (string.IsNullOrEmpty(extension)) extension = ".txt";
            picker.FileTypeChoices.Add("Text document", new List<string> { extension }); file = await picker.PickSaveFileAsync(); if (file is null) return false;
        }
        var snapshot = entry.Document.Snapshot(); var encoding = entry.Document.Encoding;
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(snapshot.Text)).ToArray();
        CachedFileManager.DeferUpdates(file); await FileIO.WriteBytesAsync(file, bytes);
        var result = await CachedFileManager.CompleteUpdatesAsync(file);
        if (result is not Windows.Storage.Provider.FileUpdateStatus.Complete and not Windows.Storage.Provider.FileUpdateStatus.CompleteAndRenamed) throw new IOException("The file provider did not complete the save: " + result);
        if (entry.Document.Revision == snapshot.Revision) entry.Document.MarkSaved();
        entry.File = file; entry.Document.FileName = file.Name; RebuildTabs(); UpdateStatus(); AddEvent("Saved " + file.Name); await NotifyLspSavedAsync(entry, snapshot); return !entry.Document.IsModified;
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
