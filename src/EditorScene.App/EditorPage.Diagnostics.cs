using System.Text.Json;
using System.Text.Json.Serialization;
using EditorScene.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace EditorScene.App;

public sealed partial class EditorPage
{
    private static IEnumerable<FrameworkElement> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement element) yield return element;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
#if __WASM__
    private DispatcherTimer? _diagnosticTimer;
#endif
    private void QueueDiagnostics()
    {
        if (!_diagnostics || _diagnosticsQueued || !_loaded) return;
#if __WASM__
        if (_diagnosticTimer is null)
        {
            _diagnosticTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _diagnosticTimer.Tick += (_, _) => QueueDiagnostics();
            Unloaded += (_, _) => _diagnosticTimer.Stop();
        }
        if (!_diagnosticTimer.IsEnabled) _diagnosticTimer.Start();
#endif
        _diagnosticsQueued = true;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            _diagnosticsQueued = false; if (!_loaded) return;
#if __WASM__
            try
            {
                var json = JsonSerializer.Serialize(CaptureState(), BrowserJsonContext.Default.BrowserState);
                Uno.Foundation.WebAssemblyRuntime.InvokeJS("globalThis.__editorSceneState = " + json + ";");
            }
            catch (Exception error) { Console.Error.WriteLine("Diagnostics: " + error); }
#endif
        })) _diagnosticsQueued = false;
    }
    private BrowserState CaptureState()
    {
        var root = XamlRoot ?? throw new InvalidOperationException("Diagnostics require a loaded XAML root.");
        var area = _active.NativeEditor.TextArea; var view = area.TextView; var doc = _active.Document; var analysis = _active.Analysis;
        var controls = new Dictionary<string, double[]>(StringComparer.Ordinal);
        var optionInputs = new Dictionary<string, NumericOptionState>(StringComparer.Ordinal);
        string optionsValidation = "";
        var contextItems = new Dictionary<string, bool>(StringComparer.Ordinal);
        var visibleCommandEnabled = new Dictionary<string, bool>(StringComparer.Ordinal);
        void Add(string name, FrameworkElement element)
        {
            if (element.XamlRoot is null || element.Visibility != Visibility.Visible || element.ActualWidth <= 0 || element.ActualHeight <= 0) return;
            for (DependencyObject? parent = element; parent is not null; parent = VisualTreeHelper.GetParent(parent))
                if (parent is UIElement visual && visual.Visibility != Visibility.Visible) return;
            var origin = element.TransformToVisual(this).TransformPoint(new Point());
            controls[name] = [origin.X, origin.Y, element.ActualWidth, element.ActualHeight];
        }
        foreach (var (name, element) in _ui) Add(name, element);
        foreach (var element in Descendants(_errors).Where(e => e is ButtonBase or Thumb))
        {
            var name = AutomationProperties.GetName(element);
            if (!string.IsNullOrEmpty(name)) Add(name, element);
        }
        Add("Editor", view); Add("Primary editor", _primary.NativeEditor.TextArea.TextView); Add("Types", _navigator.TypeSelector); Add("Members", _navigator.MemberSelector);
        Add("Document format", _editorStatus.DocumentFormatLabel);
        if (_secondary is not null) Add("Secondary editor", _secondary.NativeEditor.TextArea.TextView);
        if (_active.Search.IsOpened)
            foreach (var element in Descendants(_active.Search).Where(e => e.Name.StartsWith("PART_", StringComparison.Ordinal))) Add(element.Name, element);
        var popups = VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Where(p => p.IsOpen).ToArray();
        foreach (var popup in popups)
            foreach (var element in popup.Child is FrameworkElement child ? Descendants(child).Prepend(child) : Enumerable.Empty<FrameworkElement>())
            {
                if (element is EditorOptionsView optionsView) optionsValidation = optionsView.ValidationMessage;
                var name = AutomationProperties.GetName(element);
                if (!string.IsNullOrEmpty(name)) Add("Visible " + name, element);
                if (element is MenuFlyoutItem item)
                {
                    Add("Visible command " + item.Text, item); visibleCommandEnabled[item.Text] = item.IsEnabled;
                    if (name.StartsWith("Editor context ", StringComparison.Ordinal)) contextItems[item.Text] = item.IsEnabled;
                }
                else if (element is MenuFlyoutSubItem sub) Add("Visible submenu " + sub.Text, sub);
                else if (element is Button button && button.Content is string caption) Add("Dialog " + caption, button);
                else if (element is ComboBoxItem choice) Add("Choice " + (choice.Content?.ToString() ?? ""), choice);
                else if (element is TextBox input)
                {
                    if (input.Name.Length > 0) Add("Dialog input " + input.Name, input);
                    for (DependencyObject? parent = VisualTreeHelper.GetParent(input); parent is not null; parent = VisualTreeHelper.GetParent(parent))
                        if (parent is NumberBox number)
                        {
                            var label = AutomationProperties.GetName(number); Add("Option input " + label, input);
                            optionInputs[label] = new() { Text = input.Text, SelectionStart = input.SelectionStart, SelectionLength = input.SelectionLength,
                                Focused = ReferenceEquals(FocusManager.GetFocusedElement(root), input) }; break;
                        }
                }
            }
        return new BrowserState
        {
            Ready = view.Viewport.RenderCount > 0, FileName = doc.FileName, Language = _active.SyntaxLanguage.Id, Theme = _theme.Name, Status = _active.Status,
            Revision = doc.Revision, AnalysisRevision = analysis?.Revision ?? -1, TextLength = doc.Buffer.TextLength,
            TextPrefix = doc.Buffer.GetText(0, Math.Min(8192, doc.Buffer.TextLength)), LineCount = doc.Buffer.LineCount,
            CaretOffset = _active.CaretOffset, SelectionStart = _active.SelectionStart, SelectionLength = _active.SelectionLength, Modified = doc.IsModified,
            Diagnostics = analysis?.Diagnostics.Select(d => d.Code + ": " + d.Message).ToArray() ?? [],
            Symbols = analysis?.Symbols.Select(s => s.Name).ToArray() ?? [], Foldings = _active.Foldings.Count, Folded = _active.Foldings.Count(f => f.IsFolded),
            BookmarkCount = _active.Indicators.Items.Count(i => i.Kind == EditorScene.Text.IndicatorKind.Bookmark),
            CompletionOpen = _active.IsCompletionOpen, CompletionLabels = _active.CompletionLabels.ToArray(), QuickInfoOpen = _active.IsQuickInfoOpen,
            SignatureOpen = _active.IsSignatureHelpOpen, Split = _secondary is not null,
            MacroState = _active.MacroRecording.State.ToString(), MacroStepCount = _active.MacroRecording.RecordedStepCount, MacroHasLast = _active.MacroRecording.LastMacroAction is not null,
            MacroInputReady = _active.IsMacroInputReady, FocusedControl = FocusManager.GetFocusedElement(root)?.GetType().FullName ?? "", OpenMenuCount = popups.Count(p => p.Child is MenuFlyoutPresenter),
            LspConnected = _active.SyntaxLanguage is EditorScene.Protocol.LspSyntaxLanguage,
            LspVersion = (_active.SyntaxLanguage as EditorScene.Protocol.LspSyntaxLanguage)?.ServerVersion ?? 0,
            ClassificationCount = analysis?.Classifications.Length ?? 0,
            SnippetActive = _active.IsSnippetActive, SnippetFields = _active.ActiveSnippetFields.Select(r => new[] { r.Start, r.Length }).ToArray(),
            SecondaryTextPrefix = _secondary?.Document.Buffer.GetText(0, Math.Min(8192, doc.Buffer.TextLength)),
            CachedLines = view.Viewport.CachedLineCount, VisibleLines = view.Viewport.LastVisibleLineCount, RenderCount = view.Viewport.RenderCount,
            SearchOpen = _active.Search.IsOpened, SearchPattern = _active.Search.SearchPattern, ReplacePattern = _active.Search.ReplacePattern,
            SearchResultCount = _active.Search.ResultCount, SearchStatus = _active.Search.StatusText,
            WordWrap = _active.WordWrap, ReadOnly = _active.IsReadOnly, Whitespace = _active.ShowWhitespace,
            SelectionCount = _active.Selections.Ranges.Count, DocumentCount = _documents.Count,
            SelectionExpansionDepth = _active.SelectionExpansionDepth, SelectionExpansionPending = _active.IsSelectionExpansionPending,
            BracketPairs = _active.BracketPairCount, IndentGuides = _active.ShowIndentGuides, Ruler = _active.ShowRuler,
            IconCount = _icons.Count, RegisteredTabCount = _tabControlNames.Count,
            SplitOrientation = _views.Orientation.ToString(), SplitRatio = _views.Ratio, FontSize = _active.FontSize,
            SecondaryTabSize = _secondary?.TabSize ?? 0, SecondaryConvertTabsToSpaces = _secondary?.ConvertTabsToSpaces ?? false, SecondaryRulerColumn = _secondary?.RulerColumn ?? 0,
            SecondaryReadOnly = _secondary?.IsReadOnly ?? false, SecondaryWordWrap = _secondary?.WordWrap ?? false,
            ToolPaneOpen = _toolPane.Visibility == Visibility.Visible, DiagnosticRowCount = _errors.VisibleCount,
            DiagnosticProjectionBuilds = _errors.ProjectionBuildCount, DiagnosticFilterBuilds = _errors.FilterBuildCount,
            DiagnosticRenderedTexts = Descendants(_errors.RowsView).OfType<TextBlock>().Select(t => t.Text).Take(200).ToArray(),
            DiagnosticGrid = CaptureDiagnosticGrid(),
            EditorScrollBars = CaptureEditorScrollBars(),
            TabSize = _active.TabSize, ConvertTabsToSpaces = _active.ConvertTabsToSpaces, RulerColumn = _active.RulerColumn,
            OptionsDialogOpen = _active.IsOptionsDialogOpen, ColumnMapBuilds = _active.ColumnMapBuildCount,
            StatusPosition = _editorStatus.PositionText, StatusRefreshes = _editorStatus.RefreshCount, OverwriteMode = _active.IsOverstrike,
            StatusFormat = _editorStatus.DocumentFormatText, StatusLineEndings = _editorStatus.LineEndingText, StatusFormatRefreshes = _editorStatus.DocumentFormatRefreshCount,
            OptionInputs = optionInputs, OptionsValidation = optionsValidation, ContextMenuOpen = _active.IsEditorContextMenuOpen, ContextMenuBuilds = _active.ContextMenuBuildCount,
            ContextMenuEnabled = _active.IsDefaultContextMenuEnabled, SecondaryPointerMovesCaret = _active.CanMoveCaretForSecondaryPointerButton, ContextItems = contextItems,
            ActiveView = ReferenceEquals(_active, _primary) ? "Primary" : "Secondary",
            ContextCommands = Enum.GetValues<EditorCommand>().ToDictionary(c => c.ToString(), c => _active.GetCommandState(c).IsEnabled),
            CommandSurfaceEnabled = _ui.Where(p => p.Value is Control && (p.Key.StartsWith("Toolbar ", StringComparison.Ordinal) || p.Key.StartsWith("Command ", StringComparison.Ordinal))).ToDictionary(p => p.Key, p => ((Control)p.Value).IsEnabled),
            ToolbarGlyphOpacity = _ui.Where(p => p.Key.StartsWith("Toolbar ", StringComparison.Ordinal) && p.Value is Button { Content: UIElement }).ToDictionary(p => p.Key, p => ((UIElement)((Button)p.Value).Content).Opacity),
            VisibleCommandEnabled = visibleCommandEnabled, CommandBindingCount = _commandBindings.Count, CommandBindingDistinct = _commandBindings.DistinctCommandCount,
            CommandBindingQueries = _commandBindings.QueryCount, CommandBindingWrites = _commandBindings.PropertyWriteCount,
            DocumentTabBuilds = _tabBuildCount, DocumentMenuBuilds = _documentList.ItemBuildCount,
            DocumentOrder = _documents.Select(d => d.Document.FileName).ToArray(), DocumentOperationBusy = _documentOperations.IsBusy,
            DocumentModified = _documents.Select(d => d.Document.IsModified).ToArray(), SaveAllRunning = _saveAllRunning, SaveAllTotal = _saveAllTotal, SaveAllCompleted = _saveAllCompleted,
            DocumentTabMenuBuilds = _documentTabMenuBuilds,
            DocumentClosePrompt = (_dialog?.Title as string) == "Save changes?" ? _dialog.Content as string : null,
            DocumentMenuCheckCount = _documentList.Items.OfType<ToggleMenuFlyoutItem>().Count(item => item.IsChecked),
            DocumentTabOffset = _tabScroll.HorizontalOffset, DocumentTabExtent = _tabScroll.ExtentWidth, DocumentTabViewportWidth = _tabScroll.ViewportWidth,
            GoToLineOpen = _goToLineView is not null, GoToLineValid = _goToLineView?.IsValid ?? false,
            GoToLineText = _goToLineView?.PositionInput.Text ?? string.Empty, GoToLineError = _goToLineView?.ValidationMessage ?? string.Empty,
            Controls = controls, Error = _lastError, NativeChecks = _nativeChecks.ToArray()
        };
    }
    private readonly List<string> _nativeChecks = [];
    private bool _nativeChecksStarted;
    private async Task RunNativeChecksAsync()
    {
        // Native mounted-control tests temporarily replace and restore this page.
        // Restoring it raises Loaded again; never launch a second overlapping suite.
        if (_nativeChecksStarted) return;
        _nativeChecksStarted = true;
        Console.WriteLine("EditorScene native checks started.");
        try
        {
            await NativeEditorChecks.RunAsync(_nativeChecks);
            foreach (var check in _nativeChecks) Console.WriteLine("PASS: " + check);
            Console.WriteLine("EditorScene native checks passed."); Environment.ExitCode = 0; App.MainWindow.Close();
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; App.MainWindow.Close(); }
    }
}
internal sealed class NumericOptionState
{
    public string Text { get; set; } = "";
    public int SelectionStart { get; set; }
    public int SelectionLength { get; set; }
    public bool Focused { get; set; }
}
internal sealed class BrowserState
{
    public Dictionary<string, bool> CommandSurfaceEnabled { get; set; } = [];
    public Dictionary<string, bool> VisibleCommandEnabled { get; set; } = [];
    public Dictionary<string, double> ToolbarGlyphOpacity { get; set; } = [];
    public int CommandBindingCount { get; set; }
    public int CommandBindingDistinct { get; set; }
    public long CommandBindingQueries { get; set; }
    public long CommandBindingWrites { get; set; }
    public long DocumentTabBuilds { get; set; }
    public string[] DocumentOrder { get; set; } = [];
    public bool[] DocumentModified { get; set; } = [];
    public bool SaveAllRunning { get; set; }
    public bool GoToLineOpen { get; set; }
    public bool GoToLineValid { get; set; }
    public string GoToLineText { get; set; } = string.Empty;
    public string GoToLineError { get; set; } = string.Empty;
    public int SaveAllTotal { get; set; }
    public int SaveAllCompleted { get; set; }
    public bool DocumentOperationBusy { get; set; }
    public int DocumentTabMenuBuilds { get; set; }
    public string? DocumentClosePrompt { get; set; }
    public long DocumentMenuBuilds { get; set; }
    public int DocumentMenuCheckCount { get; set; }
    public double DocumentTabOffset { get; set; }
    public double DocumentTabExtent { get; set; }
    public double DocumentTabViewportWidth { get; set; }
    public Dictionary<string, NumericOptionState> OptionInputs { get; set; } = [];
    public string OptionsValidation { get; set; } = "";
    public bool ContextMenuOpen { get; set; }
    public bool ContextMenuEnabled { get; set; }
    public bool SecondaryPointerMovesCaret { get; set; }
    public string ActiveView { get; set; } = "";
    public Dictionary<string, bool> ContextItems { get; set; } = [];
    public long ContextMenuBuilds { get; set; }
    public Dictionary<string, bool> ContextCommands { get; set; } = [];
    public DiagnosticGridState DiagnosticGrid { get; set; } = new();
    public Dictionary<string, EditorScrollBarState> EditorScrollBars { get; set; } = [];
    public string SplitOrientation { get; set; } = "";
    public double SplitRatio { get; set; }
    public double FontSize { get; set; }
    public int SecondaryTabSize { get; set; }
    public bool SecondaryConvertTabsToSpaces { get; set; }
    public int SecondaryRulerColumn { get; set; }
    public bool SecondaryReadOnly { get; set; }
    public bool SecondaryWordWrap { get; set; }
    public bool ToolPaneOpen { get; set; }
    public int DiagnosticRowCount { get; set; }
    public long DiagnosticProjectionBuilds { get; set; }
    public long DiagnosticFilterBuilds { get; set; }
    public string[] DiagnosticRenderedTexts { get; set; } = [];
    public int TabSize { get; set; }
    public bool ConvertTabsToSpaces { get; set; }
    public int RulerColumn { get; set; }
    public bool OptionsDialogOpen { get; set; }
    public long ColumnMapBuilds { get; set; }
    public string StatusPosition { get; set; } = "";
    public long StatusRefreshes { get; set; }
    public string StatusFormat { get; set; } = "";
    public string StatusLineEndings { get; set; } = "";
    public long StatusFormatRefreshes { get; set; }
    public bool OverwriteMode { get; set; }
    public bool Ready { get; set; }
    public string FileName { get; set; } = "";
    public string Language { get; set; } = "";
    public string Theme { get; set; } = "";
    public string Status { get; set; } = "";
    public long Revision { get; set; }
    public long AnalysisRevision { get; set; }
    public int TextLength { get; set; }
    public string TextPrefix { get; set; } = "";
    public int LineCount { get; set; }
    public int CaretOffset { get; set; }
    public int SelectionStart { get; set; }
    public int SelectionLength { get; set; }
    public bool Modified { get; set; }
    public string[] Diagnostics { get; set; } = [];
    public string[] Symbols { get; set; } = [];
    public int Foldings { get; set; }
    public int Folded { get; set; }
    public int BookmarkCount { get; set; }
    public bool CompletionOpen { get; set; }
    public string[] CompletionLabels { get; set; } = [];
    public bool QuickInfoOpen { get; set; }
    public bool SignatureOpen { get; set; }
    public string MacroState { get; set; } = "Idle";
    public int MacroStepCount { get; set; }
    public bool MacroHasLast { get; set; }
    public bool MacroInputReady { get; set; }
    public string FocusedControl { get; set; } = "";
    public int OpenMenuCount { get; set; }
    public bool LspConnected { get; set; }
    public int LspVersion { get; set; }
    public int ClassificationCount { get; set; }
    public bool SnippetActive { get; set; }
    public int[][] SnippetFields { get; set; } = [];
    public bool Split { get; set; }
    public string? SecondaryTextPrefix { get; set; }
    public int CachedLines { get; set; }
    public int VisibleLines { get; set; }
    public long RenderCount { get; set; }
    public bool SearchOpen { get; set; }
    public string SearchPattern { get; set; } = "";
    public string ReplacePattern { get; set; } = "";
    public int SearchResultCount { get; set; }
    public string SearchStatus { get; set; } = "";
    public bool WordWrap { get; set; }
    public bool ReadOnly { get; set; }
    public bool Whitespace { get; set; }
    public int SelectionCount { get; set; }
    public int DocumentCount { get; set; }
    public int SelectionExpansionDepth { get; set; }
    public bool SelectionExpansionPending { get; set; }
    public int BracketPairs { get; set; }
    public bool IndentGuides { get; set; }
    public bool Ruler { get; set; }
    public int IconCount { get; set; }
    public int RegisteredTabCount { get; set; }
    public Dictionary<string, double[]> Controls { get; set; } = [];
    public string? Error { get; set; }
    public string[] NativeChecks { get; set; } = [];
}
[JsonSerializable(typeof(BrowserState))]
internal partial class BrowserJsonContext : JsonSerializerContext { }
