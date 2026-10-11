using EditorScene.Controls;
using EditorScene.Text;
using EditorScene.Text.Macros;

namespace EditorScene.App;

internal static class NativeMacroChecks
{
    internal static async Task RunAsync(List<string> checks)
    {
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException("Native macro regression: " + name); checks.Add(name); }
        using var document = new EditorDocument("", "macro.txt");
        using var editor = new SyntaxEditor { Document = document };
        editor.MacroRecording.Record(); editor.InsertText("alpha");
        await editor.ExecuteAsync(EditorCommand.SelectAll); await editor.ExecuteAsync(EditorCommand.Uppercase);
        var macro = editor.MacroRecording.Stop()!;
        Check(macro.Steps.Select(step => step.Command).SequenceEqual(new[] { "text", "editor.SelectAll", "editor.Uppercase" }), "Macro records text and semantic editor commands in order");
        Check(editor.Text == "ALPHA", "Macro recording executes normal editing");
        editor.Text = ""; editor.Select(0, 0); await editor.RunMacroAsync();
        Check(editor.Text == "ALPHA", "Native macro playback applies recorded text and transformation");
        editor.NativeEditor.Undo(); Check(editor.Text == "alpha", "Macro playback preserves ordinary native undo behavior");
        editor.MacroRecording.Record(); editor.MacroRecording.Stop();
        Check(ReferenceEquals(editor.MacroRecording.LastMacroAction, macro), "Empty recording preserves saved macro");
        editor.MacroRecording.Record(); editor.InsertText("discard"); editor.MacroRecording.Cancel();
        Check(ReferenceEquals(editor.MacroRecording.LastMacroAction, macro), "Cancelled recording preserves saved macro");
        editor.Text = ""; editor.Select(0, 0); editor.MacroRecording.Record(); editor.InsertText("a");
        editor.MacroRecording.Pause(); editor.InsertText("ignored"); editor.MacroRecording.Resume(); editor.InsertText("b"); editor.MacroRecording.Stop();
        Check(editor.MacroRecording.LastMacroAction!.Steps.Select(step => step.Argument).SequenceEqual(new[] { "a", "b" }), "Paused editor text is excluded from macro recording");
        editor.Text = ""; editor.Select(0, 0); await editor.RunMacroAsync(2);
        Check(editor.Text == "abab", "Native macro supports bounded repetition");
        editor.MacroRecording.SetLastMacro(new([new("native.Home"), new("native.SelectWordRight"), new("editor.Uppercase")]));
        editor.Text = "alpha beta"; editor.CaretOffset = editor.Text.Length; await editor.RunMacroAsync();
        Check(editor.Text.StartsWith("ALPHA", StringComparison.Ordinal), "Native navigation and word selection replay through actual routed commands");
        editor.IsReadOnly = true; var before = editor.Text; var rejected = false;
        try { await editor.RunMacroAsync(); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && editor.Text == before, "Read-only macro playback is rejected before mutation"); editor.IsReadOnly = false;
        editor.MacroRecording.SetLastMacro(new([MacroStep.InsertText("must not insert"), new("untrusted.ExecuteProcess")])); rejected = false;
        try { await editor.RunMacroAsync(); } catch (NotSupportedException) { rejected = true; }
        Check(rejected && editor.Text == before, "Unknown imported action preflight rejects the whole macro before editing");
        using var second = new EditorDocument("second"); editor.MacroRecording.Record(); editor.Document = second;
        Check(editor.MacroRecording.State == MacroRecordingState.Idle, "Document changes cancel active macro recording");
        editor.MacroRecording.SetLastMacro(new([MacroStep.InsertText("x")]));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); rejected = false;
        try { await editor.RunMacroAsync(cancellationToken: cancellation.Token); } catch (OperationCanceledException) { rejected = true; }
        Check(rejected && editor.Text == "second" && editor.MacroRecording.State == MacroRecordingState.Idle, "Cancelled macro playback restores idle without edits");
        editor.Document = document; editor.Text = ""; editor.Select(0, 0);
        editor.MacroRecording.Record(); editor.InsertText("("); editor.InsertText("x"); editor.InsertText(")"); editor.MacroRecording.Stop();
        Check(editor.Text == "(x)", "Macro recording retains bracket pairing and closing-character behavior");
        editor.Text = ""; editor.Select(0, 0); await editor.RunMacroAsync(); Check(editor.Text == "(x)", "Bracket-paired macro replays without duplicated delimiters");
        Check(editor.AutoShowCompletion, "Macro lifecycle restores automatic completion preference");
    }
}
