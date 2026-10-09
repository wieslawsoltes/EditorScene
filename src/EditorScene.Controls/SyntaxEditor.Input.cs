using EditorScene.Text;
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
    private sealed class InputHandler(SyntaxEditor owner) : TextAreaStackedInputHandler(owner._editor.TextArea)
    { public override void OnPreviewKeyDown(KeyRoutedEventArgs e) => owner.HandleKey(e); }
    private void HandleKey(KeyRoutedEventArgs e)
    {
        if (_disposed || e.Handled) return;
        var command = IsKeyDown(VirtualKey.Control) || IsKeyDown(VirtualKey.LeftWindows) || IsKeyDown(VirtualKey.RightWindows);
        var shift = IsKeyDown(VirtualKey.Shift); var alt = IsKeyDown(VirtualKey.Menu);
        EditorCommand? action = null;
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
            if (e.Key == VirtualKey.Insert) { IsOverstrike = !IsOverstrike; e.Handled = true; return; }
        }
        if (action is { } selected) { e.Handled = true; _ = ExecuteAsync(selected); return; }
        if (e.Key == VirtualKey.Escape && Selections.IsActive) { ClearMultiSelection(); e.Handled = true; return; }
        if (Selections.IsActive && e.Key is VirtualKey.Back or VirtualKey.Delete or VirtualKey.Enter or VirtualKey.Tab)
        {
            e.Handled = true; _multiEditing = true;
            try
            {
                if (e.Key is VirtualKey.Back or VirtualKey.Delete) Selections.Delete(e.Key == VirtualKey.Back, CanEdit);
                else Selections.Replace(e.Key == VirtualKey.Enter ? Document.PreferredLineEnding : new string(' ', _editor.Options.IndentationSize), CanEdit);
                SynchronizeMultiSelection();
            }
            catch (Exception exception) { ReportError(exception); }
            finally { _multiEditing = false; }
        }
        else if (e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down or VirtualKey.Home or VirtualKey.End or VirtualKey.PageDown or VirtualKey.PageUp) ClearMultiSelection();
    }
    private void OnPointerPressed(object sender, PointerRoutedEventArgs args)
    {
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
        ApplyUserEdits([new(start, SelectionLength, c + selected + close)]);
        _editor.Select(start + 1, selected.Length);
    }
    private void OnTextEntered(object? sender, TextInputEventArgs args)
    {
        if (_disposed || !AutoShowCompletion) return;
        if (args.Text == ".") _ = ExecuteAsync(EditorCommand.CompleteWord);
        else if (args.Text is "(" or ",") _ = ExecuteAsync(EditorCommand.SignatureHelp);
    }
}
