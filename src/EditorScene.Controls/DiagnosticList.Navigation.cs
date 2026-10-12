using EditorScene.LanguageServices;
using EditorScene.Text.Macros;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace EditorScene.Controls;

public sealed partial class DiagnosticList
{
    private readonly Button _previousDiagnostic = NavigationButton("Previous diagnostic", "Previous diagnostic (Shift+F8)");
    private readonly Button _nextDiagnostic = NavigationButton("Next diagnostic", "Next diagnostic (F8)");
    private readonly EditorIcon _previousIcon = new() { Kind = EditorIconKind.Previous, Width = 14, Height = 14 };
    private readonly EditorIcon _nextIcon = new() { Kind = EditorIconKind.Next, Width = 14, Height = 14 };
    private DiagnosticNavigationCursor _navigation = new([]);
    private bool _navigatingDiagnostic;
    public Button PreviousDiagnosticButton => _previousDiagnostic;
    public Button NextDiagnosticButton => _nextDiagnostic;
    public bool CanNavigateDiagnostics => !_disposed && !_visible.IsEmpty && _editor is { IsDisposed: false } editor
        && _table is not null && ReferenceEquals(_analysis, editor.Analysis)
        && _table.FileName == editor.Document.FileName && NavigationAllowed(editor);

    private static Button NavigationButton(string name, string tooltip)
    {
        var button = new Button { Width = 24, Height = 24, MinWidth = 0, MinHeight = 0,
            Padding = new Thickness(4), BorderThickness = new Thickness(0), IsEnabled = false };
        AutomationProperties.SetName(button, name); ToolTipService.SetToolTip(button, tooltip); return button;
    }
    private static bool NavigationAllowed(SyntaxEditor editor) => editor.MacroRecording.State is not (MacroRecordingState.Recording or MacroRecordingState.Playing);
    private void InitializeDiagnosticNavigation()
    {
        _previousDiagnostic.Content = _previousIcon; _nextDiagnostic.Content = _nextIcon;
        _previousDiagnostic.Click += OnPreviousDiagnostic; _nextDiagnostic.Click += OnNextDiagnostic;
        KeyDown += OnDiagnosticNavigationKey;
    }
    private void OnPreviousDiagnostic(object sender, RoutedEventArgs args) { if (!_disposed) NavigateNextDiagnostic(backwards: true); }
    private void OnNextDiagnostic(object sender, RoutedEventArgs args) { if (!_disposed) NavigateNextDiagnostic(); }
    private static bool KeyDownNow(VirtualKey key) => (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;
    private void OnDiagnosticNavigationKey(object sender, KeyRoutedEventArgs args)
    {
        if (_disposed || args.Handled || args.Key != VirtualKey.F8 || KeyDownNow(VirtualKey.Control)
            || KeyDownNow(VirtualKey.Menu) || KeyDownNow(VirtualKey.LeftWindows) || KeyDownNow(VirtualKey.RightWindows)) return;
        args.Handled = true;
        NavigateNextDiagnostic(backwards: KeyDownNow(VirtualKey.Shift));
    }
    private void UpdateNavigationAvailability()
    {
        if (_disposed) return;
        var enabled = CanNavigateDiagnostics;
        if (_previousDiagnostic.IsEnabled != enabled) _previousDiagnostic.IsEnabled = enabled;
        if (!_disposed && _nextDiagnostic.IsEnabled != enabled) _nextDiagnostic.IsEnabled = enabled;
    }
    private void ApplyNavigationTheme()
    {
        var highContrast = _theme.Name == EditorScene.Rendering.Skia.EditorTheme.HighContrast.Name;
        _previousIcon.IsDark = _nextIcon.IsDark = _theme.IsDark;
        _previousIcon.IsHighContrast = _nextIcon.IsHighContrast = highContrast;
    }

    /// <summary>Navigate one row in the current sorted/filtered result. With no selected row,
    /// starts at the first/last row. Includes separate diagnostics at identical offsets.
    /// Returns false at a non-wrapping boundary, for obsolete results, during macro recording/playback,
    /// or when synchronous host callbacks supersede the selection. Text is never edited.</summary>
    public bool NavigateNextDiagnostic(bool backwards = false, bool wrap = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_navigatingDiagnostic) return false;
        // Refresh may itself publish rows and run host callbacks; include it in
        // the recursion guard rather than only guarding the final caret change.
        _navigatingDiagnostic = true;
        try
        {
            Refresh(); if (!CanNavigateDiagnostics || _editor is not { } editor || _table is not { } table) return false;
            editor.Document.VerifyAccess();
            if (!table.IsCurrent(editor.Document.Snapshot())) return false;
            var document = editor.Document; var analysis = _analysis; var publication = _publication;
            var rows = _visible; var cursor = _navigation;
            var session = editor.NativeEditor.TextArea.Session;
            var caret = editor.CaretOffset; var anchor = session.AnchorOffset; var active = session.SelectionEndOffset;
            cursor.SelectIndex(_rows.SelectedIndex);
            if (!cursor.TryMove(out var row, backwards, wrap)) return false;
            _rows.SelectedIndex = cursor.CurrentIndex;
            if (_disposed || publication != _publication || !ReferenceEquals(_editor, editor) || editor.IsDisposed
                || !ReferenceEquals(editor.Document, document) || !ReferenceEquals(analysis, editor.Analysis)
                || _visible != rows || !ReferenceEquals(_rows.SelectedItem, row) || editor.CaretOffset != caret
                || session.AnchorOffset != anchor || session.SelectionEndOffset != active) return false;
            if (!NavigateCore(row)) return false;
            if (!_disposed && publication == _publication && _rows.IsLoaded) _rows.ScrollIntoView(row);
            return !_disposed && publication == _publication;
        }
        finally { _navigatingDiagnostic = false; }
    }
    private bool Navigate(DiagnosticTableRow row)
    {
        if (_disposed || _navigatingDiagnostic) return false;
        _navigatingDiagnostic = true;
        try { return NavigateCore(row); }
        finally { _navigatingDiagnostic = false; }
    }
    private bool NavigateCore(DiagnosticTableRow row)
    {
        if (_disposed) return false;
        var index = _rows.SelectedIndex;
        var selectedMember = index >= 0 && index < _visible.Length && ReferenceEquals(_visible[index], row);
        if (!selectedMember && !_visible.Any(r => ReferenceEquals(r, row))) return false;
        var editor = _editor;
        if (editor is null || editor.IsDisposed || _table is null || !ReferenceEquals(_analysis, editor.Analysis)) { Refresh(); return false; }
        if (!NavigationAllowed(editor)) return false;
        try { if (!_table.IsCurrent(editor.Document.Snapshot())) { Refresh(); return false; } }
        catch (ObjectDisposedException) { Refresh(); return false; }
        var document = editor.Document; var analysis = _analysis; var result = _visible; var generation = _publication;
        var native = editor.NativeEditor.TextArea; var session = native.Session;
        var anchor = session.AnchorOffset; var active = session.SelectionEndOffset; var caret = editor.CaretOffset;
        bool Current() => !_disposed && generation == _publication && _visible == result && !editor.IsDisposed
            && ReferenceEquals(_editor, editor) && ReferenceEquals(editor.Document, document)
            && ReferenceEquals(editor.Analysis, analysis) && NavigationAllowed(editor);
        editor.ClearMultiSelection();
        if (!Current() || session.AnchorOffset != anchor || session.SelectionEndOffset != active || editor.CaretOffset != caret || editor.Selections.Ranges.Count != 0) return false;
        var range = row.Diagnostic.Range;
        // UnoEdit moves empty endpoints with a changed insertion caret, whereas
        // a nonempty selection keeps its endpoints until explicitly selected.
        var followsCaret = anchor == active && caret != range.End;
        var expectedAnchor = followsCaret ? range.End : anchor;
        var expectedActive = followsCaret ? range.End : active;
        session.SetCaretOffset(range.End);
        if (!Current() || editor.CaretOffset != range.End || session.AnchorOffset != expectedAnchor || session.SelectionEndOffset != expectedActive || editor.Selections.Ranges.Count != 0) return false;
        session.SetSelection(range.Offset, range.End);
        if (!Current() || session.AnchorOffset != range.Offset || session.SelectionEndOffset != range.End || editor.CaretOffset != range.End || editor.Selections.Ranges.Count != 0) return false;
        native.TextView.EnsureCaretVisible();
        if (!Current()) return false;
        editor.FocusEditor();
        return Current() && editor.SelectionStart == range.Offset && editor.SelectionLength == range.Length;
    }
    private void DisposeDiagnosticNavigation()
    {
        KeyDown -= OnDiagnosticNavigationKey;
        _previousDiagnostic.Click -= OnPreviousDiagnostic; _nextDiagnostic.Click -= OnNextDiagnostic;
        _navigation = new([]); _previousDiagnostic.Content = null; _nextDiagnostic.Content = null;
        _previousIcon.Dispose(); _nextIcon.Dispose();
    }
}
