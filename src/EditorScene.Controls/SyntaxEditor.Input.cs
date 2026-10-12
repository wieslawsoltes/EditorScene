using EditorScene.Text;
using EditorScene.Text.Macros;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using UnoEdit.Editing;
using Windows.System;
using Windows.UI.Core;

namespace EditorScene.Controls;

public sealed partial class SyntaxEditor
{
    private string? _chord;
    private long _chordTime;
    private static bool IsKeyDown(VirtualKey key) => (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;
    private sealed class InputHandler : TextAreaStackedInputHandler
    {
        private readonly SyntaxEditor _owner;
        public InputHandler(SyntaxEditor owner) : base(owner._editor.TextArea)
        {
            _owner = owner;
            // Native document changes remove transient popup/snippet handlers. This
            // command layer belongs to the control and must survive that reset.
            TextArea.DocumentChanged += OnDocumentChanged;
        }
        private void OnDocumentChanged(object? sender, UnoEdit.Document.DocumentChangedEventArgs e)
        {
            _owner._chord = null;
            if (!_owner._disposed && !TextArea.StackedInputHandlers.Contains(this)) TextArea.PushStackedInputHandler(this);
        }
        public override void OnPreviewKeyDown(KeyRoutedEventArgs e)
        {
            try { _owner.HandleKey(e); _owner.RecordNativeMacroKey(e); }
            catch (Exception error) { e.Handled = true; _owner.ReportError(error); }
        }
    }
    private void HandleKey(KeyRoutedEventArgs e)
    {
        if (_disposed || e.Handled) return;
        if (MacroRecording.State == MacroRecordingState.Playing)
        { if (e.Key == VirtualKey.Escape) CancelMacroPlayback(); e.Handled = true; return; }
        // Modifier-only keydowns must not consume a pending chord prefix.
        if (e.Key is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl
            or VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift
            or VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu
            or VirtualKey.LeftWindows or VirtualKey.RightWindows) return;
        var command = IsKeyDown(VirtualKey.Control) || IsKeyDown(VirtualKey.LeftWindows) || IsKeyDown(VirtualKey.RightWindows);
        var shift = IsKeyDown(VirtualKey.Shift); var alt = IsKeyDown(VirtualKey.Menu);
        if (command && !alt && _chord == "K" && Environment.TickCount64 - _chordTime < 2000 && e.Key is VirtualKey.X or VirtualKey.S)
        {
            _chord = null; e.Handled = true; _ = ShowSnippetPickerAsync(e.Key == VirtualKey.S); return;
        }
        if (!command && !alt && !shift && e.Key == VirtualKey.Tab && !Selections.IsActive && !MacroSessionActive)
        {
            if (TryExpandSnippetShortcut()) { e.Handled = true; return; }
        }
        if (!command && !alt && !shift && e.Key == VirtualKey.Tab && !IsSnippetActive && !IsCompletionOpen && _editor.Options.AcceptsTab)
        { e.Handled = true; _ = ExecuteAsync(EditorCommand.Indent); return; }
        // Native Shift+Backspace is also ordinary deletion. Preserve that mapping
        // while leaving word deletion and interactive snippet/completion policy alone.
        if (!command && !alt && e.Key == VirtualKey.Back && !IsSnippetActive && !IsCompletionOpen)
        { e.Handled = true; Backspace(); return; }
        EditorCommand? action = null;
        if (alt && shift && !command && e.Key is VirtualKey.Right or VirtualKey.Left)
        { _chord = null; e.Handled = true; _ = ExecuteAsync(e.Key == VirtualKey.Right ? EditorCommand.ExpandSelection : EditorCommand.ContractSelection); return; }
        if (command && !alt)
        {
            if (_chord is not null && Environment.TickCount64 - _chordTime < 2000)
            {
                action = (_chord, e.Key) switch { ("K", VirtualKey.C) => EditorCommand.Comment, ("K", VirtualKey.U) => EditorCommand.Uncomment, ("K", VirtualKey.D) => EditorCommand.FormatDocument, ("M", VirtualKey.M) => EditorCommand.ToggleFold, ("M", VirtualKey.O) => EditorCommand.CollapseAll, ("M", VirtualKey.L) => EditorCommand.ExpandAll, _ => null };
                _chord = null;
            }
            else if (e.Key is VirtualKey.K or VirtualKey.M)
            { _chord = e.Key.ToString(); _chordTime = Environment.TickCount64; SetStatus($"Ctrl+{_chord}, …"); e.Handled = true; return; }
            action ??= e.Key switch
            {
                VirtualKey.Space => shift ? EditorCommand.SignatureHelp : EditorCommand.CompleteWord,
                VirtualKey.G => EditorCommand.GoToLine, VirtualKey.D => EditorCommand.SelectNextOccurrence,
                VirtualKey.U => shift ? EditorCommand.Uppercase : EditorCommand.Lowercase,
                VirtualKey.L => shift ? EditorCommand.DeleteLine : EditorCommand.SelectLine,
                VirtualKey.F2 => EditorCommand.ToggleBookmark,
                _ => null
            };
        }
        else if (!command && !alt)
        {
            action = e.Key switch { VirtualKey.F2 => shift ? EditorCommand.PreviousBookmark : EditorCommand.NextBookmark, VirtualKey.F9 => EditorCommand.ToggleBreakpoint, _ => null };
            if (e.Key == VirtualKey.Insert)
            {
                if (MacroRecording.State == MacroRecordingState.Recording) MacroRecording.NotifyEditAction(new("native.Overstrike"));
                IsOverstrike = !IsOverstrike; e.Handled = true; return;
            }
        }
        if (action is { } selected) { e.Handled = true; _ = ExecuteAsync(selected); return; }
        if (e.Key == VirtualKey.Escape)
        { _chord = null; if (Selections.IsActive) { ClearMultiSelection(); e.Handled = true; } return; }
        if (Selections.IsActive && e.Key is VirtualKey.Back or VirtualKey.Delete or VirtualKey.Enter or VirtualKey.Tab)
        {
            e.Handled = true; _multiEditing = true;
            try
            {
                if (e.Key is VirtualKey.Back or VirtualKey.Delete) Selections.Delete(e.Key == VirtualKey.Back, CanEdit);
                else Selections.Replace(e.Key == VirtualKey.Enter ? Document.PreferredLineEnding : new string(' ', _editor.Options.IndentationSize), CanEdit);
                SynchronizeMultiSelection();
            }
            finally { _multiEditing = false; }
        }
        else if (e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down or VirtualKey.Home or VirtualKey.End or VirtualKey.PageDown or VirtualKey.PageUp) ClearMultiSelection();
    }
    private void OnPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (MacroSessionActive) { args.Handled = true; return; }
        var point = args.GetCurrentPoint(_editor.TextArea.TextView);
        if (!point.Properties.IsLeftButtonPressed) return;
        if ((args.KeyModifiers & VirtualKeyModifiers.Menu) != 0)
        {
            var offset = _editor.TextArea.TextView.Viewport.HitTest(point.Position.X, point.Position.Y);
            var ranges = Selections.Ranges.ToList();
            if (ranges.Count == 0) ranges.Add(new(_editor.TextArea.Session.AnchorOffset, CaretOffset));
            ranges.Add(new(offset, offset)); Selections.Set(ranges); SynchronizeMultiSelection(); args.Handled = true; FocusEditor();
        }
        else ClearMultiSelection();
    }
    private void OnTextEntering(object? sender, TextInputEventArgs args)
    {
        if (_disposed || args.Handled || IsReadOnly) return;
        if (MacroRecording.State == MacroRecordingState.Playing && !_replayingText) { args.Handled = true; return; }
        // Normal input must not allocate a discarded macro step or inherit the
        // recorder's text-size limit. Recording still validates before any edit.
        try { if (MacroRecording.State == MacroRecordingState.Recording) MacroRecording.NotifyEditAction(MacroStep.InsertText(args.Text)); }
        catch (Exception error) { args.Handled = true; ReportError(error); return; }
        if (Selections.IsActive)
        {
            args.Handled = true; _multiEditing = true;
            try { Selections.Replace(args.Text, CanEdit); SynchronizeMultiSelection(); }
            catch (Exception exception) { ReportError(exception); }
            finally { _multiEditing = false; }
            return;
        }
        if (!AutoCompleteBrackets || IsOverstrike || args.Text.Length != 1) return;
        var c = args.Text[0]; var caret = CaretOffset; var buffer = Document.Buffer;
        if (c == '\'' && SyntaxLanguage.Id == "vb") return;
        if (SelectionLength == 0 && caret < buffer.TextLength && c is ')' or ']' or '}' or '\'' or '"' && buffer.GetCharAt(caret) == c)
        {
            if (caret == 0 || buffer.GetCharAt(caret - 1) != '\\') { args.Handled = true; CaretOffset++; }
            return;
        }
        var close = c switch { '(' => ')', '[' => ']', '{' => '}', '"' => '"', '\'' => '\'', _ => '\0' };
        if (close == '\0') return;
        if (Analysis is { } analysis && analysis.Revision == Document.Revision && analysis.Classifications.Any(span => span.Kind is "string" or "comment" && span.Range.Offset < caret && span.Range.End > caret)) return;
        if (SelectionLength == 0 && caret < buffer.TextLength && IsWord(buffer.GetCharAt(caret))) return;
        var selected = SelectedText; var start = SelectionStart;
        if (!CanEdit(new(start, SelectionLength, c + selected + close))) return;
        args.Handled = true;
        ApplyUserEdits([new(start, SelectionLength, c + selected + close)]); _editor.Select(start + 1, selected.Length);
        if (AutoShowCompletion && !MacroSessionActive && c == '(' && selected.Length == 0) _ = ExecuteAsync(EditorCommand.SignatureHelp);
    }
    private void OnTextEntered(object? sender, TextInputEventArgs args)
    {
        if (_disposed || !AutoShowCompletion || MacroSessionActive) return;
        if (args.Text == ".") _ = ExecuteAsync(EditorCommand.CompleteWord);
        else if (args.Text is "(" or ",") _ = ExecuteAsync(EditorCommand.SignatureHelp);
    }
}
