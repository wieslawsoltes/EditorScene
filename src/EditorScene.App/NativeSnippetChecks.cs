using EditorScene.Controls;
using EditorScene.Languages.DotNet;
using EditorScene.Snippets;
using EditorScene.Text;

namespace EditorScene.App;

internal static class NativeSnippetChecks
{
    internal static void Run(List<string> checks)
    {
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException("Native snippet regression: " + name); checks.Add(name); }
        using var document = new EditorDocument("for", "snippet.cs");
        using var editor = new SyntaxEditor { Document = document, SyntaxLanguage = new DotNetLanguage() };
        editor.CaretOffset = 3;
        Check(editor.NativeEditor.TextArea.StackedInputHandlers.Count() == 1, "Persistent shortcut handler survives initial document assignment");
        Check(editor.TryExpandSnippetShortcut(), "Snippet shortcut expansion");
        Check(editor.IsSnippetActive && editor.ActiveSnippetFields.Count == 2, "Native linked-field session has two editable fields");
        Check(editor.SelectedText == "i", "First snippet field selected");
        editor.NativeEditor.TextArea.Session.ReplaceSelection("row");
        Check(editor.Text.Contains("int row = 0; row < length; row++", StringComparison.Ordinal), "Repeated fields update in the same document transaction");
        editor.NativeEditor.Undo();
        Check(editor.Text.Contains("int i = 0; i < length; i++", StringComparison.Ordinal), "Mirrored field edit undoes atomically");
        editor.EndSnippet(); Check(!editor.IsSnippetActive, "Explicit snippet completion deactivates session");
        Check(editor.CaretOffset > editor.Text.IndexOf('{'), "Snippet end marker controls final caret");
        editor.NativeEditor.Undo(); Check(editor.Text == "for", "Shortcut removal and snippet insertion form one undo unit");
        editor.Text = "Console.WriteLine();"; editor.Select(0, editor.Text.Length);
        editor.InsertSnippet(editor.Snippets.FindShortcut("if", "csharp")!);
        Check(editor.Text.Contains("Console.WriteLine();", StringComparison.Ordinal) && editor.Text.StartsWith("if (true)", StringComparison.Ordinal), "Surround-with preserves selected code");
        editor.EndSnippet(false); editor.NativeEditor.Undo();
        Check(editor.Text == "Console.WriteLine();", "Surround-with single undo restores selection text");
        editor.IsReadOnly = true; var before = editor.Text; var blocked = false;
        try { editor.InsertSnippet(editor.Snippets.FindShortcut("for", "csharp")!); } catch (InvalidOperationException) { blocked = true; }
        Check(blocked && editor.Text == before, "Read-only snippet insertion is rejected before mutation"); editor.IsReadOnly = false;
        var function = new CodeSnippet("Function", "fun", "csharp", "$name$-$name$$end$", [new("name", "default", Function: "HostName()")]);
        var refused = false;
        try { editor.InsertSnippet(function); } catch (NotSupportedException) { refused = true; }
        Check(refused && editor.Text == before, "Unknown snippet functions are not silently executed or ignored");
        editor.SnippetFunctionResolver = (_, _, _) => "resolved"; editor.Select(0, editor.Text.Length); editor.InsertSnippet(function);
        Check(editor.Text == "resolved-resolved", "Explicit host function resolver supplies linked fields"); editor.EndSnippet(false);
        using var replacement = new EditorDocument("other", "other.cs");
        editor.Document = replacement;
        Check(editor.NativeEditor.TextArea.StackedInputHandlers.Count() == 1, "Repeated document changes retain exactly one persistent shortcut handler");
        editor.Document = document;
        Check(editor.NativeEditor.TextArea.StackedInputHandlers.Count() == 1 && !editor.IsSnippetActive, "Document switch detaches snippets but retains editor commands");
    }
}
