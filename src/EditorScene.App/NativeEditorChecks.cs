using EditorScene.Controls;
using EditorScene.LanguageServices;
using EditorScene.Languages.DotNet;
using EditorScene.Languages.Structured;
using EditorScene.Rendering.Skia;
using EditorScene.Text;

namespace EditorScene.App;

/// <summary>Runs inside an actual Uno host. No web mutation bridge or mocked UI framework.</summary>
internal static class NativeEditorChecks
{
    internal static async Task RunAsync(List<string> checks)
    {
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException("Native regression failed: " + name); checks.Add(name); }
        using var document = new EditorDocument("alpha\nbeta\ngamma", "checks.txt");
        using var editor = new SyntaxEditor { Document = document, SyntaxLanguage = new PlainTextLanguage() };
        using var other = new SyntaxEditor { Document = document, SyntaxLanguage = new PlainTextLanguage() };
        Exception? failure = null; editor.OperationFailed += (_, exception) => failure = exception;
        void Text(string expected, string name) { Check(editor.Text == expected, name + " / text (expected " + expected.Replace("\n", "⏎") + ", got " + editor.Text.Replace("\n", "⏎") + ")"); Check(other.Text == expected, name + " / shared document"); }
        Check(editor.ShowLineNumbers && !editor.WordWrap && !editor.IsReadOnly, "Editor defaults");
        editor.Select(0, 5); await editor.ExecuteAsync(EditorCommand.Uppercase); Text("ALPHA\nbeta\ngamma", "Uppercase");
        await editor.ExecuteAsync(EditorCommand.Undo); Text("alpha\nbeta\ngamma", "Undo transform");
        await editor.ExecuteAsync(EditorCommand.Redo); Text("ALPHA\nbeta\ngamma", "Redo transform");
        editor.Select(0, 5); await editor.ExecuteAsync(EditorCommand.Lowercase); Text("alpha\nbeta\ngamma", "Lowercase");
        editor.GoToLine(2); await editor.ExecuteAsync(EditorCommand.DuplicateLine); Text("alpha\nbeta\nbeta\ngamma", "Duplicate line");
        await editor.ExecuteAsync(EditorCommand.DeleteLine); Text("alpha\nbeta\ngamma", "Delete line");
        editor.Select(0, document.Buffer.TextLength); await editor.ExecuteAsync(EditorCommand.Comment); Text("//alpha\n//beta\n//gamma", "Comment selected lines");
        editor.Select(0, document.Buffer.TextLength); await editor.ExecuteAsync(EditorCommand.Uncomment); Text("alpha\nbeta\ngamma", "Uncomment selected lines");
        editor.Text = "c\nb\na\n"; editor.Select(0, 5); await editor.ExecuteAsync(EditorCommand.SortLines); Text("a\nb\nc\n", "Sort selected lines");
        editor.Text = "  alpha  \n\tbeta\t\n"; editor.Select(0, 0); await editor.ExecuteAsync(EditorCommand.TrimTrailingWhitespace); Text("  alpha\n\tbeta\n", "Trim trailing whitespace");
        await editor.ExecuteAsync(EditorCommand.TabsToSpaces); Text("  alpha\n    beta\n", "Expand leading tabs");
        await editor.ExecuteAsync(EditorCommand.SpacesToTabs); Text("  alpha\n\tbeta\n", "Compress leading spaces");
        await editor.ExecuteAsync(EditorCommand.ConvertToCrLf); Text("  alpha\r\n\tbeta\r\n", "Convert CRLF");
        await editor.ExecuteAsync(EditorCommand.ConvertToLf); Text("  alpha\n\tbeta\n", "Convert LF");
        editor.GoToLine(2); await editor.ExecuteAsync(EditorCommand.ToggleBookmark);
        Check(document.Indicators.Items.Count(i => i.Kind == IndicatorKind.Bookmark) == 1, "Bookmark shared document marker");
        editor.GoToLine(1); await editor.ExecuteAsync(EditorCommand.NextBookmark); Check(editor.CaretOffset == document.Buffer.GetLineByNumber(2).Offset, "Bookmark navigation");
        await editor.ExecuteAsync(EditorCommand.ToggleBreakpoint); Check(document.Indicators.Items.Any(i => i.Kind == IndicatorKind.Breakpoint), "Breakpoint marker");
        await editor.ExecuteAsync(EditorCommand.ClearBookmarks); Check(!document.Indicators.Items.Any(i => i.Kind == IndicatorKind.Bookmark), "Clear bookmarks preserves breakpoint");
        editor.Text = "one two one"; editor.Select(0, 3); editor.SelectNextOccurrence();
        Check(editor.Selections.Ranges.Count == 2, "Multiple occurrence selections"); editor.Selections.Replace("A"); Text("A two A", "Multi-selection replacement");
        await editor.ExecuteAsync(EditorCommand.Undo); Text("one two one", "Multi-selection single undo");
        editor.ClearMultiSelection(); editor.CaretOffset = 0; editor.Search.SearchPattern = "one"; editor.Search.FindNext();
        Check(editor.Search.ResultCount == 2 && editor.SelectionLength == 3, "Search selects first match");
        editor.Search.IsReplaceMode = true; editor.Search.ReplacePattern = "item"; editor.Search.ReplaceAll(); Text("item two item", "Native replace all");
        await editor.ExecuteAsync(EditorCommand.Undo); Text("one two one", "Replace-all single undo");
        editor.Search.Close(); editor.IsReadOnly = true; editor.Select(0, 3); await editor.ExecuteAsync(EditorCommand.Uppercase);
        Check(failure is InvalidOperationException && editor.Text == "one two one", "Read-only command rejection"); failure = null;
        editor.IsReadOnly = false; await editor.ExecuteAsync(EditorCommand.ToggleWordWrap); Check(editor.WordWrap, "Word wrap command");
        await editor.ExecuteAsync(EditorCommand.ToggleWhitespace); Check(editor.ShowWhitespace, "Whitespace command");
        await editor.ExecuteAsync(EditorCommand.ToggleRuler); Check(editor.ShowRuler, "Ruler command");
        var oldSize = editor.FontSize; await editor.ExecuteAsync(EditorCommand.ZoomIn); Check(editor.FontSize == oldSize + 1, "Zoom command");
        await editor.ExecuteAsync(EditorCommand.ResetZoom); Check(editor.FontSize == 13, "Reset zoom");
        editor.Theme = EditorTheme.Dark; Check(editor.Theme.IsDark, "Dark theme"); editor.Theme = EditorTheme.HighContrast; Check(editor.Theme == EditorTheme.HighContrast, "High contrast theme");
        editor.SyntaxLanguage = new JsonLanguage(); editor.Text = "{\"x\":1}"; await editor.ExecuteAsync(EditorCommand.FormatDocument); Check(editor.Text.Contains('\n'), "JSON formatting command");
        editor.SyntaxLanguage = new DotNetLanguage(); editor.Text = "using System; class C { void M() { Console.Wri } }";
        editor.CaretOffset = editor.Text.IndexOf("Wri", StringComparison.Ordinal) + 3;
        var completion = await editor.SyntaxLanguage.CompleteAsync(document.Snapshot(), editor.CaretOffset);
        Check(completion.Any(c => c.Label == "WriteLine"), "Native control uses semantic provider");
        using var selector = new NavigableSymbolSelector { Editor = editor }; using var outline = new DocumentOutline { Editor = editor };
        selector.Editor = other; outline.Editor = other; selector.Editor = null; outline.Editor = null;
        Check(failure is null, "No unexpected command errors");
        NativeSnippetChecks.Run(checks);
        await NativeMacroChecks.RunAsync(checks);
        await NativeReviewChecks.RunAsync(checks);
        await NativeStructuralSelectionChecks.RunAsync(checks);
        await NativeWorkspaceChecks.RunAsync(checks);
        await NativeDiagnosticNavigationChecks.RunAsync(checks);
        await NativeOptionsChecks.RunAsync(checks);
        await NativePermissionReviewChecks.RunAsync(checks);
        await NativeWhitespaceChecks.RunAsync(checks);
        await NativeContextMenuChecks.RunAsync(checks);
    }
}
