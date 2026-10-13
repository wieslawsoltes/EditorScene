using EditorScene.Controls;
using EditorScene.LanguageServices;
using EditorScene.Rendering.Skia;
using EditorScene.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace EditorScene.App;

internal static class NativeLineEndingStatusChecks
{
    internal static void Run(List<string> checks)
    {
        void Check(bool condition, string name)
        { if (!condition) throw new InvalidOperationException("Native format status: " + name); checks.Add(name); }
        using var document = new EditorDocument("first\r\nsecond\nthird\r");
        using var editor = new SyntaxEditor(); editor.SetDocumentAndLanguage(document, new PlainTextLanguage());
        using var bar = new EditorStatusBar { Editor = editor };
        Check(bar.LineEndingText == "Mixed" && bar.DocumentFormatText == "UTF-8  Mixed",
            "Native status identifies mixed delimiters instead of reporting only the first kind");
        Check(AutomationProperties.GetName(bar.DocumentFormatLabel) == "Document format"
            && AutomationProperties.GetHelpText(bar.DocumentFormatLabel) == "Line terminators: CRLF 1, LF 1, CR 1.",
            "Native format label exposes exact delimiter counts in accessibility help and tooltip");
        var formats = bar.DocumentFormatRefreshCount; var label = bar.DocumentFormatLabel;
        editor.CaretOffset = 1; bar.Refresh(); document.Buffer.Insert(1, "x"); bar.Refresh();
        Check(bar.DocumentFormatRefreshCount == formats && ReferenceEquals(label, bar.DocumentFormatLabel),
            "Caret movement and ordinary edits reuse native format text and its label");
        foreach (var (delimiter, expected) in new[] { ("\r\n", "CRLF"), ("\n", "LF"), ("\r", "CR") })
        {
            editor.ChangeLineEndings(delimiter); bar.Refresh();
            Check(bar.LineEndingText == expected, "Native status follows " + expected + " normalization");
            document.Buffer.UndoStack.Undo(); bar.Refresh();
            Check(bar.LineEndingText == "Mixed", "Native status restores mixed metadata after " + "undoing " + expected);
        }
        using var empty = new EditorDocument(); editor.SetDocumentAndLanguage(empty, new PlainTextLanguage()); bar.Refresh();
        Check(bar.DocumentFormatText == "UTF-8  None" && bar.LineEndingText == "None",
            "Native status distinguishes an unterminated document from a real LF document");
        empty.Encoding = System.Text.Encoding.Unicode; bar.Refresh();
        Check(bar.DocumentFormatText == "UTF-16  None", "Explicit native status refresh updates a changed document encoding without a text edit");
        editor.SetDocumentAndLanguage(document, new PlainTextLanguage()); editor.IsReadOnly = true; bar.Refresh();
        foreach (var theme in new[] { EditorTheme.Light, EditorTheme.Dark, EditorTheme.HighContrast }) bar.ApplyTheme(theme);
        Check(bar.LineEndingText == "Mixed" && ReferenceEquals(label, bar.DocumentFormatLabel) && editor.IsReadOnly,
            "Readonly and theme transitions retain correct native format text and label identity");
        bar.Editor = null;
        Check(bar.DocumentFormatText == "" && bar.LineEndingText == "" && !bar.IsEnabled
            && AutomationProperties.GetHelpText(label) == "" && ToolTipService.GetToolTip(label) is null,
            "Unbinding native status clears format metadata and stale tooltip ownership");
        bar.Dispose();
        Check(!editor.IsDisposed && document.Buffer.Text.Contains("second", StringComparison.Ordinal),
            "Disposing native format status leaves caller-owned editor and document intact");
    }
}
