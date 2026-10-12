using EditorScene.LanguageServices;
using EditorScene.Rendering.Skia;
using EditorScene.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using UnoEdit.Document;
using UnoEdit.Folding;
using UnoEdit.Highlighting;
using UnoEdit.Search;
using Windows.Foundation;
using NativeEditor = UnoEdit.TextEditor;

namespace EditorScene.Controls;

/// <summary>A reusable native Uno syntax editor. Documents and language providers can be shared by independent views.</summary>
public sealed partial class SyntaxEditor : UserControl, IDisposable
{
    private readonly NativeEditor _editor = new();
    private readonly InputHandler _input;
    private readonly long _fontSizeCallback;
    private EditorDocument? _ownedDocument;
    private AnalysisCoordinator? _coordinator;
    private AnalysisStyleSource? _styles;
    private EditorAdornments? _adornments;
    private FoldingManager? _folding;
    private ISyntaxLanguage _language = new PlainTextLanguage();
    private EditorTheme _theme = EditorTheme.Light;
    private BracketMap? _brackets;
    private bool _disposed, _multiEditing;
    private MultiSelection? _selections;
    private bool _showWhitespace, _showRuler, _showIndentGuides = true;

    public static readonly DependencyProperty DocumentProperty = DependencyProperty.Register(nameof(Document), typeof(EditorDocument), typeof(SyntaxEditor), new PropertyMetadata(null, (o, e) => ((SyntaxEditor)o).Rebind((EditorDocument?)e.OldValue, (EditorDocument?)e.NewValue)));
    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(SyntaxEditor), new PropertyMetadata(false, (o, e) => ((SyntaxEditor)o)._editor.IsReadOnly = (bool)e.NewValue));
    public static readonly DependencyProperty ShowLineNumbersProperty = DependencyProperty.Register(nameof(ShowLineNumbers), typeof(bool), typeof(SyntaxEditor), new PropertyMetadata(true, (o, e) => ((SyntaxEditor)o)._editor.ShowLineNumbers = (bool)e.NewValue));
    public static readonly DependencyProperty WordWrapProperty = DependencyProperty.Register(nameof(WordWrap), typeof(bool), typeof(SyntaxEditor), new PropertyMetadata(false, (o, e) => ((SyntaxEditor)o)._editor.WordWrap = (bool)e.NewValue));
    public SyntaxEditor()
    {
        IsTabStop = false; Content = _editor; FontSize = 13;
        _editor.ShowLineNumbers = true; _editor.FontSize = 13;
        AutomationProperties.SetName(this, "Syntax editor"); AutomationProperties.SetName(_editor.TextArea, "Code editor input");
        _input = new InputHandler(this); _editor.TextArea.PushStackedInputHandler(_input);
        _editor.TextArea.TextEntering += OnTextEntering; _editor.TextArea.TextEntered += OnTextEntered;
        _editor.SelectionChanged += OnSelectionChanged; _editor.InputError += OnInputError;
        _editor.TextArea.TextView.PointerPressed += OnPointerPressed;
        _fontSizeCallback = RegisterPropertyChangedCallback(FontSizeProperty, (_, _) => { _editor.FontSize = FontSize; ApplyTheme(); });
        _ownedDocument = new EditorDocument(); Document = _ownedDocument;
        InitializeMacroSupport(); Loaded += OnLoaded; _editor.OptionChanged += OnNativeOptionChanged;
    }
    public NativeEditor NativeEditor => _editor;
    public EditorDocument Document { get => (EditorDocument)GetValue(DocumentProperty); set => SetValue(DocumentProperty, value ?? throw new ArgumentNullException(nameof(value))); }
    public string Text { get => Document.Buffer.Text; set => SetText(value ?? ""); }
    public bool IsReadOnly { get => (bool)GetValue(IsReadOnlyProperty); set => SetValue(IsReadOnlyProperty, value); }
    public bool ShowLineNumbers { get => (bool)GetValue(ShowLineNumbersProperty); set => SetValue(ShowLineNumbersProperty, value); }
    public bool WordWrap { get => (bool)GetValue(WordWrapProperty); set => SetValue(WordWrapProperty, value); }
    public int CaretOffset { get => _editor.CaretOffset; set => _editor.CaretOffset = value; }
    public int SelectionStart => _editor.SelectionStart;
    public int SelectionLength => _editor.SelectionLength;
    public string SelectedText => _editor.SelectedText;
    public bool CanUndo => _editor.CanUndo;
    public bool CanRedo => _editor.CanRedo;
    public SearchPanel Search => SearchPanel.Install(_editor);
    public LanguageAnalysis? Analysis => _coordinator?.Current is { } value && value.DocumentId == Document.Id && value.Revision == Document.Revision ? value : null;
    public IndicatorCollection Indicators => Document.Indicators;
    public MultiSelection Selections => _selections ?? throw new InvalidOperationException("No document is attached.");
    public IReadOnlyList<FoldingSection> Foldings => _folding?.AllFoldings.ToArray() ?? [];
    public string Status { get; private set; } = "Ready";
    public bool AutoCompleteBrackets { get; set; } = true;
    public bool AutoShowCompletion { get; set; } = true;
    public bool IsOverstrike { get => _editor.TextArea.OverstrikeMode; set { _editor.TextArea.OverstrikeMode = value; StateChanged?.Invoke(this, EventArgs.Empty); } }
    public EditorTheme Theme { get => _theme; set { ArgumentNullException.ThrowIfNull(value); if (ReferenceEquals(_theme, value)) return; _theme = value; ApplyTheme(); } }
    public ISyntaxLanguage SyntaxLanguage
    {
        get => _language;
        set { ArgumentNullException.ThrowIfNull(value); if (ReferenceEquals(_language, value)) return; _language = value; Document.LanguageId = value.Id; ConfigureLanguage(); }
    }
    public bool ShowWhitespace { get => _showWhitespace; set { _showWhitespace = value; if (_adornments is not null) { _adornments.ShowWhitespace = value; Redraw(); } } }
    public bool ShowIndentGuides { get => _showIndentGuides; set { _showIndentGuides = value; if (_adornments is not null) { _adornments.ShowIndentGuides = value; Redraw(); } } }
    public bool ShowRuler { get => _showRuler; set { _showRuler = value; if (_adornments is not null) { _adornments.ShowRuler = value; Redraw(); } } }
    public event EventHandler? TextChanged;
    public event EventHandler? StateChanged;
    public event EventHandler<LanguageAnalysis>? AnalysisUpdated;
    public event EventHandler<Exception>? OperationFailed;
    public event EventHandler? GoToLineRequested;

    private void Rebind(EditorDocument? previous, EditorDocument? document)
    {
        if (_disposed || document is null) return;
        _brackets = null;
        ClosePopups(); _coordinator?.Dispose(); DetachStyles(); _adornments?.Dispose(); _folding?.Dispose();
        if (previous is not null) previous.Changed -= OnDocumentChanged;
        if (_ownedDocument is not null && !ReferenceEquals(_ownedDocument, document)) { _ownedDocument.Dispose(); _ownedDocument = null; }
        _editor.Document = document.Buffer; _selections = new(document);
        _folding = FoldingManager.Install(_editor.TextArea);
        _adornments = new(document, _editor.TextArea.TextView.Viewport, document.Indicators) { Theme = Theme, ShowWhitespace = _showWhitespace, ShowIndentGuides = _showIndentGuides, ShowRuler = _showRuler, RulerColumn = _rulerColumn };
        document.Changed += OnDocumentChanged;
        ConfigureLanguage(); ApplyTheme();
    }
    private void DetachStyles()
    {
        if (_styles is null) return;
        var viewport = _editor.TextArea.TextView.Viewport;
        if (ReferenceEquals(viewport.LineStyleSource, _styles)) viewport.LineStyleSource = null;
        _styles.Dispose(); _styles = null;
    }
    private void ConfigureLanguage()
    {
        if (_disposed || GetValue(DocumentProperty) is not EditorDocument document) return;
        ResetSelectionExpansion();
        ClosePopups(); _coordinator?.Dispose();
        var viewport = _editor.TextArea.TextView.Viewport;
        DetachStyles();
        // Reassign via null so the native lexical source is recreated even for the same language.
        _editor.SyntaxHighlighting = null;
        var name = SyntaxLanguage.Id switch { "csharp" => "C#", "vb" => "VB", "xml" or "xaml" => "XML", "json" => "JavaScript", _ => SyntaxLanguage.DisplayName };
        _editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition(name) ?? HighlightingManager.Instance.GetDefinitionByExtension(Path.GetExtension(document.FileName));
        _styles = new(document, Theme, viewport.LineStyleSource); viewport.LineStyleSource = _styles;
        _folding?.Clear(); _brackets = null;
        _coordinator = new(document, SyntaxLanguage, action => { if (DispatcherQueue.HasThreadAccess) action(); else DispatcherQueue.TryEnqueue(() => { if (!_disposed) action(); }); });
        _coordinator.Published += OnAnalysisPublished; _coordinator.Failed += OnInputError;
        StateChanged?.Invoke(this, EventArgs.Empty);
        _coordinator.RequestAnalysis(true);
    }
    private void OnAnalysisPublished(object? sender, LanguageAnalysis analysis)
    {
        if (_disposed || analysis.DocumentId != Document.Id || analysis.Revision != Document.Revision) return;
        try
        {
            _styles?.Update(analysis); _adornments?.Update(analysis);
            _folding?.UpdateFoldings(analysis.Foldings.Where(f => f.Range.Length > 1).Select(f => new NewFolding(f.Range.Offset, f.Range.End) { Name = f.Caption, IsDefinition = f.IsDefinition }), -1);
            PrepareBrackets(analysis); UpdateBracketHighlights();
            AnalysisUpdated?.Invoke(this, analysis); StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) { ReportError(exception); }
    }
    private void OnLoaded(object sender, RoutedEventArgs args) { ApplyTheme(); _coordinator?.RequestAnalysis(true); }
    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        ResetSelectionExpansion(); _columnMap = null;
        if (!_multiEditing) { Selections.Clear(); if (_adornments is not null) _adornments.ExtraSelections = []; }
        _brackets = null; TextChanged?.Invoke(this, EventArgs.Empty); StateChanged?.Invoke(this, EventArgs.Empty);
    }
    private void SetText(string text) { if (GetValue(DocumentProperty) is EditorDocument document && document.Buffer.Text != text) document.ApplyEdits([new(0, document.Buffer.TextLength, text)], document.Revision); }
    private void OnSelectionChanged(object? sender, EventArgs args) { if (!_changingStructuralSelection) ResetSelectionExpansion(); UpdateBracketHighlights(); StateChanged?.Invoke(this, EventArgs.Empty); }
    private void UpdateBracketHighlights()
    {
        if (_adornments is null) return;
        if (_brackets?.DocumentId == Document.Id && _brackets.Revision == Document.Revision && _brackets.TryGetPair(CaretOffset, out var first, out var second))
        {
            var old = _adornments.BracketHighlights;
            if (old.Count == 2 && old[0] == first && old[1] == second) return;
            _adornments.BracketHighlights = [first, second];
        }
        else { if (_adornments.BracketHighlights.Count == 0) return; _adornments.BracketHighlights = []; }
        Redraw();
    }
    private void OnInputError(object? sender, Exception exception) => ReportError(exception);
    public void ReportError(Exception exception) { Status = exception.Message; OperationFailed?.Invoke(this, exception); StateChanged?.Invoke(this, EventArgs.Empty); }
    private void SetStatus(string text) { Status = text; StateChanged?.Invoke(this, EventArgs.Empty); }
    public void ApplyTheme()
    {
        if (_disposed) return;
        RequestedTheme = Theme.IsDark ? ElementTheme.Dark : ElementTheme.Light;
        _editor.RequestedTheme = RequestedTheme; _editor.Foreground = Brush(Theme.Foreground); _editor.Background = Brush(Theme.Background);
        _editor.TextArea.TextView.Viewport.Style = _editor.TextArea.TextView.Viewport.Style with { Foreground = Theme.Foreground, Background = Theme.Background, Selection = Theme.Selection, CurrentLine = Theme.CurrentLine, LineNumber = Theme.LineNumber, FontSize = (float)FontSize };
        if (_styles is not null) _styles.Theme = Theme;
        if (_adornments is not null) _adornments.Theme = Theme;
        Redraw(); StateChanged?.Invoke(this, EventArgs.Empty);
    }
    public static SolidColorBrush Brush(SkiaSharp.SKColor color) => new(Windows.UI.Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue));
    public void FocusEditor() => _editor.TextArea.Focus(FocusState.Programmatic);
    public void Redraw() { if (!_disposed) _editor.TextArea.TextView.Viewport.InvalidateDrawing(); }
    public void Select(int offset, int length) { ClearMultiSelection(); _editor.Select(offset, length); _editor.TextArea.TextView.EnsureCaretVisible(); }
    public void GoToLine(int oneBasedLine, int oneBasedColumn = 1)
    {
        var line = Document.Buffer.GetLineByNumber(Math.Clamp(oneBasedLine, 1, Document.Buffer.LineCount));
        ClearMultiSelection(); _editor.TextArea.Session.MoveTo(line.Offset + Math.Clamp(oneBasedColumn - 1, 0, line.Length)); _editor.ScrollTo(line.LineNumber, oneBasedColumn); FocusEditor();
    }
    public Rect GetEditorBounds()
    {
        var view = _editor.TextArea.TextView;
        var point = view.TransformToVisual(XamlRoot?.Content).TransformPoint(new Point(0, 0));
        return new(point, new Size(view.ActualWidth, view.ActualHeight));
    }
    public void RequestAnalysis() => _coordinator?.RequestAnalysis(true);
    public new void Dispose()
    {
        if (_disposed) return;
        ResetSelectionExpansion(); DisposeMacroSupport(); ClosePopups(); _disposed = true; Loaded -= OnLoaded;
        _editor.OptionChanged -= OnNativeOptionChanged; _columnMap = null;
        _coordinator?.Dispose(); DetachStyles(); _adornments?.Dispose(); _folding?.Dispose();
        Document.Changed -= OnDocumentChanged; _editor.SelectionChanged -= OnSelectionChanged; _editor.InputError -= OnInputError;
        _editor.TextArea.TextEntering -= OnTextEntering; _editor.TextArea.TextEntered -= OnTextEntered; _editor.TextArea.TextView.PointerPressed -= OnPointerPressed;
        _editor.TextArea.PopStackedInputHandler(_input); UnregisterPropertyChangedCallback(FontSizeProperty, _fontSizeCallback);
        _editor.Dispose(); _ownedDocument?.Dispose(); base.Dispose();
    }
}
