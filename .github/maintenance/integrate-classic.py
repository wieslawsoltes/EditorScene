#!/usr/bin/env python3
"""One-shot exact integration, run only on this feature branch; removed before merge."""
from pathlib import Path
import os

if os.environ.get('GITHUB_REF_NAME') != 'feat/classic-editor-workspace':
    raise SystemExit('Refusing to edit any other branch')
files = {}
def load(path):
    if path not in files: files[path] = Path(path).read_text()
    return files[path]
def replace(path, old, new):
    text = load(path)
    if text.count(old) != 1: raise RuntimeError(f'Expected exactly one integration anchor in {path}: {old[:100]!r}')
    files[path] = text.replace(old, new, 1)
def section(path, start, end, replacement):
    text = load(path)
    if text.count(start) != 1 or text.count(end) != 1: raise RuntimeError(f'Ambiguous section in {path}')
    a, b = text.index(start), text.index(end)
    if b <= a: raise RuntimeError('Reversed integration section')
    files[path] = text[:a] + replacement + text[b:]

page = 'src/EditorScene.App/EditorPage.cs'
replace(page, 'private readonly Grid _views = new();', 'private readonly ResizableSplitView _views = new();')
replace(page, 'private readonly ListView _errors = new() { SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true };', 'private readonly DiagnosticList _errors = new();')
replace(page, 'private readonly Border _toolPane = new() { Height = 132,', 'private readonly Border _toolPane = new() { Height = 200,')
for name in ('_status', '_position'):
    text = load(page); matching = [line for line in text.splitlines(True) if f'private readonly TextBlock {name} =' in line]
    if len(matching) != 1: raise RuntimeError('Expected old status field')
    replace(page, matching[0], '')
text = load(page); matching = [line for line in text.splitlines(True) if '_errors.ItemClick +=' in line]
if len(matching) != 1: raise RuntimeError('Expected old diagnostic handler')
replace(page, matching[0], '')
section(page, '    private void BuildLayout()', '    private void Put(', '    private void BuildLayout() => BuildClassicLayout();\n')
section(page, '    private void ToggleSplit()', '    private void ToggleOutline()', '    private void ToggleSplit() => ToggleClassicSplit();\n')
section(page, '    private void RefreshDiagnostics(', '    private void UpdateStatus()', '    private void RefreshDiagnostics(LanguageAnalysis analysis) => UpdateErrorCaption();\n')
section(page, '    private void UpdateStatus()', '    private void ApplyTheme(', '    private void UpdateStatus() => UpdateClassicStatus();\n')
replace(page, '_active = editor; _navigator.Editor = editor; _outline.Editor = editor;', '_active = editor; _navigator.Editor = editor; _outline.Editor = editor; _errors.Editor = editor; _editorStatus.Editor = editor;')
replace(page, '_errors.ItemsSource = null; _errorTab.Content = "Error List (analyzing…)"; RebuildTabCaptions();', 'UpdateErrorCaption(); RebuildTabCaptions();')
replace(page, '_showEvents = events; _toolPane.Child = events ? _events : _errors; QueueDiagnostics();', '_showEvents = events; _toolTitle.Text = events ? "Events" : "Error List"; _toolContent.Content = events ? _events : _errors; QueueDiagnostics();')
replace(page, 'var icon = new EditorIcon { Kind = kind, IsDark = _theme.IsDark };', 'var icon = new EditorIcon { Kind = kind, IsDark = _theme.IsDark, IsHighContrast = ReferenceEquals(_theme, EditorTheme.HighContrast) };')
replace(page, '''        if (_ui.GetValueOrDefault("Status bar") is Grid status) status.Background = SyntaxEditor.Brush(theme.Accent);
        var statusColor = theme == EditorTheme.HighContrast ? SkiaSharp.SKColors.Black : SkiaSharp.SKColors.White;
        _status.Foreground = SyntaxEditor.Brush(statusColor); _position.Foreground = SyntaxEditor.Brush(statusColor);
''', '        ApplyClassicChrome(theme);\n')
replace(page, '''    private sealed record DiagnosticRow(EditorDiagnostic Diagnostic, int Line, Guid DocumentId, long Revision)
    { public override string ToString() => $"{Diagnostic.Severity}  {Diagnostic.Code}    Line {Line}    {Diagnostic.Message}"; }
''', '')
replace(page, 'if (_current is not null) _current.Caret = _primary.CaretOffset;', 'if (_current is not null) _current.Caret = ReferenceEquals(_active.Document, _current.Document) ? _active.CaretOffset : _primary.CaretOffset;')

menus = 'src/EditorScene.App/EditorPage.Menus.cs'
section(menus, '    private void BuildToolbar()', '    private void ToolCommand(', '')
replace(menus, 'var bar = new MenuBar { MinHeight = 26, Height = 28, Padding = new Thickness(2, 0, 2, 0) };', 'var bar = new MenuBar { MinHeight = 24, Height = 26, Padding = new Thickness(2, 0, 2, 0) };')
replace(menus, 'new MenuBarItem { Title = title }', 'new MenuBarItem { Title = title, MinHeight = 24, Height = 24, FontSize = 12, Padding = new Thickness(7, 2, 7, 2) }')
replace(menus, 'Command(edit, "Expand Selection", EditorCommand.ExpandSelection);', 'Command(edit, "Expand Selection", EditorCommand.ExpandSelection, "Alt+Shift+Right");')
replace(menus, 'Command(edit, "Contract Selection", EditorCommand.ContractSelection);', 'Command(edit, "Contract Selection", EditorCommand.ContractSelection, "Alt+Shift+Left");')
replace(menus, '        Toggle(window, "Split Editor", () => _secondary is not null, ToggleSplit);', '''        Toggle(window, "Split Editor", () => _secondary is not null, ToggleSplit);
        Item(window, "Split Horizontally", () => { SetSplitOrientation(Orientation.Vertical); return Task.CompletedTask; });
        Item(window, "Split Vertically", () => { SetSplitOrientation(Orientation.Horizontal); return Task.CompletedTask; });
        Item(window, "Center Split", () => { _views.Ratio = .5; _active.FocusEditor(); return Task.CompletedTask; });''')
replace(menus, '        ToolTipService.SetToolTip(button, label); button.Click += (_, _) => _ = GuardAsync(action); _toolbar.Children.Add(button); Register("Toolbar " + label, button);', '''        ToolTipService.SetToolTip(button, label); button.Click += (_, _) => _ = GuardAsync(action); _toolbar.Children.Add(button); Register("Toolbar " + label, button);
        var overflow = new MenuFlyoutItem { Text = label, FontSize = 12 };
        overflow.Click += (_, _) => _ = GuardAsync(action); _toolbarOverflow.Items.Add(overflow);''')
replace(menus, '    private void ToolSeparator() => _toolbar.Children.Add(new Border { Width = 1, Height = 17, Margin = new Thickness(4, 4, 4, 4), Background = SyntaxEditor.Brush(_theme.Border) });', '''    private void ToolSeparator()
    {
        var border = new Border { Width = 1, Height = 17, Margin = new Thickness(4, 4, 4, 4), Background = SyntaxEditor.Brush(_theme.Border) };
        _toolbar.Children.Add(border); _toolbarSeparators.Add(border); _toolbarOverflow.Items.Add(new MenuFlyoutSeparator());
    }''')

observer = 'src/EditorScene.App/EditorPage.Diagnostics.cs'
replace(observer, 'Add("Editor", view); Add("Types",', 'Add("Editor", view); Add("Primary editor", _primary.NativeEditor.TextArea.TextView); Add("Types",')
replace(observer, '            Controls = controls, Error = _lastError, NativeChecks = _nativeChecks.ToArray()', '''            SplitOrientation = _views.Orientation.ToString(), SplitRatio = _views.Ratio, FontSize = _active.FontSize,
            SecondaryReadOnly = _secondary?.IsReadOnly ?? false, SecondaryWordWrap = _secondary?.WordWrap ?? false,
            ToolPaneOpen = _toolPane.Visibility == Visibility.Visible, DiagnosticRowCount = _errors.VisibleCount,
            DiagnosticProjectionBuilds = _errors.ProjectionBuildCount, DiagnosticFilterBuilds = _errors.FilterBuildCount,
            DiagnosticRenderedTexts = Descendants(_errors.RowsView).OfType<TextBlock>().Select(t => t.Text).Take(200).ToArray(),
            StatusPosition = _editorStatus.PositionText, StatusRefreshes = _editorStatus.RefreshCount, OverwriteMode = _active.IsOverstrike,
            Controls = controls, Error = _lastError, NativeChecks = _nativeChecks.ToArray()''')
replace(observer, '    public bool Ready { get; set; }', '''    public string SplitOrientation { get; set; } = "";
    public double SplitRatio { get; set; }
    public double FontSize { get; set; }
    public bool SecondaryReadOnly { get; set; }
    public bool SecondaryWordWrap { get; set; }
    public bool ToolPaneOpen { get; set; }
    public int DiagnosticRowCount { get; set; }
    public long DiagnosticProjectionBuilds { get; set; }
    public long DiagnosticFilterBuilds { get; set; }
    public string[] DiagnosticRenderedTexts { get; set; } = [];
    public string StatusPosition { get; set; } = "";
    public long StatusRefreshes { get; set; }
    public bool OverwriteMode { get; set; }
    public bool Ready { get; set; }''')

replace('src/EditorScene.App/NativeEditorChecks.cs', '        await NativeStructuralSelectionChecks.RunAsync(checks);', '        await NativeStructuralSelectionChecks.RunAsync(checks);\n        await NativeWorkspaceChecks.RunAsync(checks);')
replace('tools/build.py', 'if len(checks) < 136', 'if len(checks) < 160')
replace('tools/EditorScene.Benchmarks/Program.cs', 'CompletedAnalysisBenchmarks.Add(rows);', 'CompletedAnalysisBenchmarks.Add(rows);\nWorkspaceBenchmarks.Add(rows);')
replace('tools/browser-smoke.mjs', "import {checkStructuralSelection} from './browser-structural-selection.mjs';", "import {checkStructuralSelection} from './browser-structural-selection.mjs';\nimport {checkWorkspace} from './browser-workspace.mjs';")
replace('tools/browser-smoke.mjs', "  const snippetXml=", "  await checkWorkspace({page,state,wait,textIs,paste,click,fill,command,focusEditor,output,checks});\n  const snippetXml=")

package = 'tools/package.py'
replace(package, 'var analysis = await new DotNetLanguage().AnalyzeAsync(snapshot);', '''var analysis = await new DotNetLanguage().AnalyzeAsync(snapshot);
var diagnosticTable = new EditorScene.LanguageServices.DiagnosticTable(snapshot, analysis);
if (diagnosticTable.Filter() != diagnosticTable.Rows || !diagnosticTable.IsCurrent(snapshot)) throw new Exception("Packaged diagnostic table failed.");
using var glyphBitmap = new SkiaSharp.SKBitmap(16, 16); using var glyphCanvas = new SkiaSharp.SKCanvas(glyphBitmap);
using var glyphRenderer = new EditorGlyphRenderer();
glyphRenderer.Draw(glyphCanvas, 16, 16, EditorGlyph.Save); glyphRenderer.Draw(glyphCanvas, 16, 16, EditorGlyph.Save);
if (glyphRenderer.BuildCount != 1) throw new Exception("Packaged glyph cache failed.");''')
replace(package, '        Content = new StackPanel { Children = { navigation, Editor, outline, picker, new EditorIcon() } };', '''        Editor.CopyViewSettingsFrom(Editor);
        var diagnostics = new DiagnosticList { Editor = Editor };
        var status = new EditorStatusBar { Editor = Editor };
        var split = new ResizableSplitView { First = Editor, Ratio = .4 };
        Resources.MergedDictionaries.Add(EditorChrome.Create(EditorScene.Rendering.Skia.EditorTheme.Light));
        Content = new StackPanel { Children = { navigation, split, status, diagnostics, outline, picker, new EditorIcon() } };''')

# Avoid rebinding unchanged native properties/palettes on common state events.
replace('src/EditorScene.Controls/EditorStatusBar.cs', '        IsEnabled = true;', '        if (!IsEnabled) IsEnabled = true;')
replace('src/EditorScene.Controls/SyntaxEditor.cs', 'public EditorTheme Theme { get => _theme; set { _theme = value ?? throw new ArgumentNullException(nameof(value)); ApplyTheme(); } }', 'public EditorTheme Theme { get => _theme; set { ArgumentNullException.ThrowIfNull(value); if (ReferenceEquals(_theme, value)) return; _theme = value; ApplyTheme(); } }')
replace('src/EditorScene.Controls/DiagnosticList.cs', '            catch (ArgumentException) { _analysis = null; }', '            catch (ArgumentException) { /* Reject this immutable result once; a replacement result is retried. */ }')
replace('src/EditorScene.Controls/DiagnosticList.cs', '        var range = row.Diagnostic.Range; editor.Select(range.Offset, range.Length); editor.FocusEditor();', '''        var document = editor.Document; var analysis = _analysis;
        var range = row.Diagnostic.Range; editor.Select(range.Offset, range.Length);
        if (!_disposed && !editor.IsDisposed && ReferenceEquals(_editor, editor) && ReferenceEquals(editor.Document, document) && ReferenceEquals(editor.Analysis, analysis) && editor.SelectionStart == range.Offset && editor.SelectionLength == range.Length) editor.FocusEditor();''')
replace('src/EditorScene.Controls/EditorStatusBar.cs', 'editor.IsOverstrike = !editor.IsOverstrike; editor.FocusEditor();', 'editor.IsOverstrike = !editor.IsOverstrike; if (!_disposed && !editor.IsDisposed && ReferenceEquals(_editor, editor)) editor.FocusEditor();')
replace('src/EditorScene.LanguageServices/DiagnosticTable.cs', '    public string Severity => Diagnostic.Severity.ToString();', '    public string Severity => Diagnostic.Severity switch { IssueSeverity.Error => "Error", IssueSeverity.Warning => "Warning", _ => "Information" };')
replace('src/EditorScene.App/NativeWorkspaceChecks.cs', 'split.First = null;', 'split.First = null; split.Dispose();')

# Do not allocate the title string on each caret event.
workspace = 'src/EditorScene.App/EditorPage.Workspace.cs'
replace(workspace, '    private string? _shownTitle;', '    private string? _shownTitleFile;\n    private bool _shownTitleModified;')
replace(workspace, '''        var title = _current.Document.FileName + (_current.Document.IsModified ? " *" : "") + " — EditorScene";
        if (_shownTitle != title) { _shownTitle = title; _fileTitle.Text = title; }''', '''        var fileName = _current.Document.FileName; var modified = _current.Document.IsModified;
        if (_shownTitleFile != fileName || _shownTitleModified != modified)
        { _shownTitleFile = fileName; _shownTitleModified = modified; _fileTitle.Text = fileName + (modified ? " *" : "") + " — EditorScene"; }''')

# All anchors have been checked before changing any tracked file.
for path, text in files.items():
    Path(path).write_text(text)
    print('Integrated ' + path)
