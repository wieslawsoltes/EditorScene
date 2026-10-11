using EditorScene.Text.Macros;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using UnoEdit;
using UnoEdit.Editing;
using UnoEdit.Utils;
using Windows.System;

namespace EditorScene.Controls;

public sealed partial class SyntaxEditor
{
    private readonly MacroRecorder _macroRecording = new();
    private Border? _macroPointerShield;
    private bool _macroPopupsSuppressed, _macroAutoCompletion;
    private CancellationTokenSource? _macroPlayback;
    private bool _replayingText;
    private int _macroFocusGeneration;
    private readonly List<Action> _macroFocusUnsubscribe = [];
    public MacroRecorder MacroRecording => _macroRecording;
    public event EventHandler? MacroRecordingStateChanged;
    private bool MacroSessionActive => MacroRecording.State != MacroRecordingState.Idle;
    private void InitializeMacroSupport()
    {
        // Cover pointer selection, not keyboard input. Clicking the shield returns
        // keyboard focus without forwarding a hit test that would move the caret.
        Content = null;
        _macroPointerShield = new Border { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Visibility = Visibility.Collapsed };
        _macroPointerShield.PointerPressed += (_, args) => { args.Handled = true; if (!_disposed) FocusEditor(); };
        var layout = new Grid(); layout.Children.Add(_editor); layout.Children.Add(_macroPointerShield); Content = layout;
        MacroRecording.StateChanged += OnMacroStateChanged;
        _editor.TextArea.DocumentChanged += OnMacroDocumentChanged;
    }
    private void OnMacroStateChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        if (MacroSessionActive)
        {
            if (MacroRecording.State is MacroRecordingState.Recording or MacroRecordingState.Playing)
            { ClosePopups(); EndSnippet(false); ClearMultiSelection(); }
            if (!_macroPopupsSuppressed)
            { _macroAutoCompletion = AutoShowCompletion; _macroPopupsSuppressed = true; ClosePopups(); EndSnippet(false); ClearMultiSelection(); }
            AutoShowCompletion = false;
        }
        else if (_macroPopupsSuppressed) { AutoShowCompletion = _macroAutoCompletion; _macroPopupsSuppressed = false; }
        if (_macroPointerShield is not null) _macroPointerShield.Visibility = MacroSessionActive ? Visibility.Visible : Visibility.Collapsed;
        SetStatus(MacroRecording.State switch
        {
            MacroRecordingState.Recording => "Recording macro — mouse selection and interactive popups suspended",
            MacroRecordingState.Paused => "Macro recording paused",
            MacroRecordingState.Playing => "Playing macro — Escape cancels",
            _ => "Macro ready"
        });
        MacroRecordingStateChanged?.Invoke(this, EventArgs.Empty); FocusEditor();
        RestoreMacroFocusAfterMenusClose();
    }
    private void CancelPendingMacroFocus()
    {
        _macroFocusGeneration++;
        foreach (var unsubscribe in _macroFocusUnsubscribe) unsubscribe();
        _macroFocusUnsubscribe.Clear();
    }
    private void RestoreMacroFocusAfterMenusClose()
    {
        CancelPendingMacroFocus();
        if (_disposed || !IsLoaded || XamlRoot is null) return;
        var generation = _macroFocusGeneration; var document = Document;
        void Restore()
        {
            // MenuFlyout restores its old focus after invoking the item's Click
            // handler. Defer until Closed has finished instead of racing that
            // restoration or teaching the test to click the editor again.
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (!_disposed && IsLoaded && generation == _macroFocusGeneration && ReferenceEquals(Document, document)) FocusEditor();
            });
        }
        var menus = VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot)
            .Where(p => p.IsOpen && p.Child is MenuFlyoutPresenter).ToArray();
        if (menus.Length == 0) { Restore(); return; }
        var remaining = menus.Length;
        foreach (var popup in menus)
        {
            EventHandler<object>? closed = null;
            closed = (_, _) =>
            {
                popup.Closed -= closed;
                if (generation != _macroFocusGeneration) return;
                if (--remaining == 0)
                {
                    foreach (var unsubscribe in _macroFocusUnsubscribe) unsubscribe();
                    _macroFocusUnsubscribe.Clear(); Restore();
                }
            };
            popup.Closed += closed;
            _macroFocusUnsubscribe.Add(() => popup.Closed -= closed);
        }
    }
    private void OnMacroDocumentChanged(object? sender, UnoEdit.Document.DocumentChangedEventArgs e)
    {
        if (MacroRecording.State is MacroRecordingState.Recording or MacroRecordingState.Paused) MacroRecording.Cancel();
        _macroPlayback?.Cancel(); CancelPendingMacroFocus();
    }
    private static bool CanRecordEditorCommand(EditorCommand command) => Enum.IsDefined(command) && command is not
        (EditorCommand.Find or EditorCommand.Replace or EditorCommand.GoToLine or EditorCommand.CompleteWord or EditorCommand.QuickInfo or EditorCommand.SignatureHelp or EditorCommand.SelectNextOccurrence);
    private void RecordEditorMacroCommand(EditorCommand command)
    {
        if (MacroRecording.State == MacroRecordingState.Playing) throw new InvalidOperationException("Wait for macro playback to finish or cancel it.");
        if (MacroRecording.State != MacroRecordingState.Recording) return;
        if (!CanRecordEditorCommand(command)) throw new NotSupportedException("Pause macro recording before using an interactive popup or adding extra selections.");
        MacroRecording.NotifyEditAction(new("editor." + command));
    }
    private static readonly IReadOnlyDictionary<string, RoutedCommand> NativeMacroCommands = new Dictionary<string, RoutedCommand>(StringComparer.Ordinal)
    {
        ["Left"] = EditingCommands.MoveLeftByCharacter, ["Right"] = EditingCommands.MoveRightByCharacter,
        ["SelectLeft"] = EditingCommands.SelectLeftByCharacter, ["SelectRight"] = EditingCommands.SelectRightByCharacter,
        ["WordLeft"] = EditingCommands.MoveLeftByWord, ["WordRight"] = EditingCommands.MoveRightByWord,
        ["SelectWordLeft"] = EditingCommands.SelectLeftByWord, ["SelectWordRight"] = EditingCommands.SelectRightByWord,
        ["Up"] = EditingCommands.MoveUpByLine, ["Down"] = EditingCommands.MoveDownByLine,
        ["SelectUp"] = EditingCommands.SelectUpByLine, ["SelectDown"] = EditingCommands.SelectDownByLine,
        ["PageUp"] = EditingCommands.MoveUpByPage, ["PageDown"] = EditingCommands.MoveDownByPage,
        ["SelectPageUp"] = EditingCommands.SelectUpByPage, ["SelectPageDown"] = EditingCommands.SelectDownByPage,
        ["Home"] = EditingCommands.MoveToLineStart, ["End"] = EditingCommands.MoveToLineEnd,
        ["SelectHome"] = EditingCommands.SelectToLineStart, ["SelectEnd"] = EditingCommands.SelectToLineEnd,
        ["DocumentHome"] = EditingCommands.MoveToDocumentStart, ["DocumentEnd"] = EditingCommands.MoveToDocumentEnd,
        ["SelectDocumentHome"] = EditingCommands.SelectToDocumentStart, ["SelectDocumentEnd"] = EditingCommands.SelectToDocumentEnd,
        ["Backspace"] = EditingCommands.Backspace, ["Delete"] = EditingCommands.Delete,
        ["DeletePreviousWord"] = EditingCommands.DeletePreviousWord, ["DeleteNextWord"] = EditingCommands.DeleteNextWord,
        ["Enter"] = EditingCommands.EnterParagraphBreak, ["Tab"] = EditingCommands.TabForward, ["BackTab"] = EditingCommands.TabBackward,
        ["Overstrike"] = UnoEditCommands.ToggleOverstrike
    };
    private void RecordNativeMacroKey(KeyRoutedEventArgs e)
    {
        if (MacroRecording.State != MacroRecordingState.Recording || e.Handled || IsKeyDown(VirtualKey.Menu)) return;
        var command = IsKeyDown(VirtualKey.Control) || IsKeyDown(VirtualKey.LeftWindows) || IsKeyDown(VirtualKey.RightWindows);
        var shift = IsKeyDown(VirtualKey.Shift);
        var prefix = shift ? "Select" : "";
        string? id = e.Key switch
        {
            VirtualKey.Left => prefix + (command ? "WordLeft" : "Left"), VirtualKey.Right => prefix + (command ? "WordRight" : "Right"),
            VirtualKey.Up => prefix + "Up", VirtualKey.Down => prefix + "Down", VirtualKey.PageUp => prefix + "PageUp", VirtualKey.PageDown => prefix + "PageDown",
            VirtualKey.Home => prefix + (command ? "DocumentHome" : "Home"), VirtualKey.End => prefix + (command ? "DocumentEnd" : "End"),
            VirtualKey.Back => command ? "DeletePreviousWord" : "Backspace", VirtualKey.Delete => command ? "DeleteNextWord" : "Delete",
            VirtualKey.Enter => "Enter", VirtualKey.Tab when !command => shift ? "BackTab" : "Tab", _ => null
        };
        if (id is not null) MacroRecording.NotifyEditAction(new("native." + id));
        else if (command)
        {
            EditorCommand? editor = e.Key switch
            {
                VirtualKey.A => EditorCommand.SelectAll, VirtualKey.C => EditorCommand.Copy, VirtualKey.X => EditorCommand.Cut,
                VirtualKey.V => EditorCommand.Paste, VirtualKey.Z => shift ? EditorCommand.Redo : EditorCommand.Undo,
                VirtualKey.Y => EditorCommand.Redo, VirtualKey.F => EditorCommand.Find, VirtualKey.H => EditorCommand.Replace, _ => null
            };
            if (editor is { } action) RecordEditorMacroCommand(action);
        }
        else if (e.Key == VirtualKey.F3) RecordEditorMacroCommand(shift ? EditorCommand.FindPrevious : EditorCommand.FindNext);
        else if (e.Key == VirtualKey.Escape) MacroRecording.NotifyEditAction(new("clearSelection"));
    }
    /// <summary>Insert text through EditorScene's pairing/selection/overstrike rules. Also usable by trusted host commands.</summary>
    public void InsertText(string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(text);
        if (IsReadOnly) throw new InvalidOperationException("The document is read only.");
        if (MacroRecording.State == MacroRecordingState.Playing && !_replayingText) throw new InvalidOperationException("Macro playback is in progress.");
        var args = new TextInputEventArgs(text); OnTextEntering(this, args);
        if (args.Handled) return;
        var session = _editor.TextArea.Session;
        if (IsOverstrike && SelectionLength == 0 && text.IndexOfAny(['\r', '\n']) < 0 && CaretOffset < Document.Buffer.GetLineByOffset(CaretOffset).EndOffset)
            session.MoveHorizontal(1, true);
        if (!CanEdit(new(SelectionStart, SelectionLength, text))) throw new InvalidOperationException("The operation intersects read-only text.");
        session.ReplaceSelection(text); OnTextEntered(this, args);
    }
    private void InsertMacroText(string text)
    {
        _replayingText = true;
        try { InsertText(text); } finally { _replayingText = false; }
    }
    private static bool IsSupportedMacroStep(MacroStep step)
    {
        if (step.Command == "text") return true;
        if (step.Argument.Length != 0) return false;
        if (step.Command == "clearSelection") return true;
        if (step.Command.StartsWith("native.", StringComparison.Ordinal)) return NativeMacroCommands.ContainsKey(step.Command[7..]);
        return step.Command.StartsWith("editor.", StringComparison.Ordinal) && Enum.TryParse<EditorCommand>(step.Command[7..], out var command)
            && command.ToString() == step.Command[7..] && CanRecordEditorCommand(command);
    }
    public void CancelMacroPlayback() => _macroPlayback?.Cancel();
    /// <summary>Explicit playback. Unknown imported commands are rejected before any edit. Completed actions keep normal native undo semantics.</summary>
    public async Task<int> RunMacroAsync(int repeat = 1, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsReadOnly) throw new InvalidOperationException("The document is read only.");
        var macro = MacroRecording.LastMacroAction ?? throw new InvalidOperationException("No macro is available.");
        if (MacroSessionActive) throw new InvalidOperationException("Stop recording or playback before running a macro.");
        var unsupported = macro.Steps.FirstOrDefault(step => !IsSupportedMacroStep(step));
        if (unsupported is not null) throw new NotSupportedException("Unsupported macro action: " + unsupported.Command);
        var document = Document; var language = SyntaxLanguage; var revision = document.Revision; var completed = 0;
        using var token = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); _macroPlayback = token;
        try
        {
            return await MacroRecording.PlayAsync(async (step, cancellation) =>
            {
                cancellation.ThrowIfCancellationRequested();
                if (_disposed || !ReferenceEquals(Document, document) || !ReferenceEquals(SyntaxLanguage, language) || document.Revision != revision)
                    throw new InvalidOperationException("The macro target changed during playback.");
                if (step.Command == "text") InsertMacroText(step.Argument);
                else if (step.Command == "clearSelection") { ClearMultiSelection(); _editor.TextArea.ClearSelection(); }
                else if (step.Command.StartsWith("editor.", StringComparison.Ordinal)) await ExecuteCommandCoreAsync(Enum.Parse<EditorCommand>(step.Command[7..]));
                else
                {
                    var command = NativeMacroCommands[step.Command[7..]]; var area = _editor.TextArea;
                    if (!command.CanExecute(null, area)) throw new InvalidOperationException("Macro action cannot execute: " + step.Command);
                    command.Execute(null, area);
                }
                cancellation.ThrowIfCancellationRequested();
                if (_disposed || !ReferenceEquals(Document, document)) throw new InvalidOperationException("The macro document was replaced.");
                revision = document.Revision;
                if (++completed % 64 == 0) await Task.Yield();
            }, repeat, token.Token);
        }
        finally { _macroPlayback = null; if (!_disposed) { _editor.TextArea.TextView.EnsureCaretVisible(); FocusEditor(); } }
    }
    private void DisposeMacroSupport()
    {
        _macroPlayback?.Cancel(); CancelPendingMacroFocus();
        MacroRecording.StateChanged -= OnMacroStateChanged; _editor.TextArea.DocumentChanged -= OnMacroDocumentChanged;
        if (MacroRecording.State is MacroRecordingState.Recording or MacroRecordingState.Paused) MacroRecording.Cancel();
        MacroRecordingStateChanged = null;
    }
}
