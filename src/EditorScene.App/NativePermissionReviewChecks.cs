using EditorScene.Controls;
using EditorScene.LanguageServices;
using EditorScene.Text;
using UnoEdit.Document;
using UnoEdit.Editing;

namespace EditorScene.App;

internal static class NativePermissionReviewChecks
{
    internal static async Task RunAsync(List<string> checks)
    {
        void Check(bool valid, string name) { if (!valid) throw new InvalidOperationException("Permission review: " + name); checks.Add(name); }
        foreach (var mode in new[] { "read-only", "provider", "language" })
        {
            using var doc = new EditorDocument("abc"); using var editor = new SyntaxEditor();
            editor.SetDocumentAndLanguage(doc, new PlainTextLanguage()); editor.Select(0, 0);
            var previous = editor.NativeEditor.TextArea.ReadOnlySectionProvider;
            var provider = new CallbackProvider(() =>
            {
                if (mode == "read-only") editor.IsReadOnly = true;
                else if (mode == "provider") editor.NativeEditor.TextArea.ReadOnlySectionProvider = previous;
                else editor.SyntaxLanguage = new PlainTextLanguage();
            });
            editor.NativeEditor.TextArea.ReadOnlySectionProvider = provider;
            Exception? failure = null; editor.OperationFailed += (_, error) => failure = error;
            await editor.ExecuteAsync(EditorCommand.Indent);
            Check(failure is InvalidOperationException && doc.Revision == 0 && editor.Text == "abc" && provider.InsertCalls == 1,
                "Indent preflight preserves host " + mode + " changes and does not repeat insertion permission callbacks");
        }
        using (var doc = new EditorDocument("abc"))
        using (var editor = new SyntaxEditor())
        {
            editor.SetDocumentAndLanguage(doc, new PlainTextLanguage()); editor.Select(0, 0);
            var provider = new CallbackProvider(() => { }); editor.NativeEditor.TextArea.ReadOnlySectionProvider = provider;
            await editor.ExecuteAsync(EditorCommand.Indent);
            Check(editor.Text == "\tabc" && provider.InsertCalls == 1,
                "Ordinary permitted Tab indentation queries host insertion permission once");
        }
        using (var doc = new EditorDocument("\ta\nxy"))
        using (var editor = new SyntaxEditor())
        {
            editor.SetDocumentAndLanguage(doc, new PlainTextLanguage()); editor.ConvertTabsToSpaces = true;
            editor.Selections.Set([new(2, 2), new(5, 5)]); var selections = editor.Selections.Ranges;
            CallbackProvider? provider = null;
            provider = new CallbackProvider(() => { if (provider!.InsertCalls == 2) editor.IsReadOnly = true; });
            editor.NativeEditor.TextArea.ReadOnlySectionProvider = provider;
            Exception? failure = null; editor.OperationFailed += (_, error) => failure = error;
            await editor.ExecuteAsync(EditorCommand.Indent);
            Check(failure is InvalidOperationException && editor.Text == "\ta\nxy" && doc.Revision == 0
                && ReferenceEquals(selections, editor.Selections.Ranges) && provider.InsertCalls == 2,
                "A later caret permission callback cannot partially indent a newly read-only view");
        }
        using (var doc = new EditorDocument("abc"))
        using (var editor = new SyntaxEditor())
        {
            editor.SetDocumentAndLanguage(doc, new PlainTextLanguage()); editor.Select(0, 0);
            var provider = new CallbackProvider(() => { }, () => editor.IsReadOnly = true);
            editor.NativeEditor.TextArea.ReadOnlySectionProvider = provider;
            Exception? failure = null; editor.OperationFailed += (_, error) => failure = error;
            await editor.ExecuteAsync(EditorCommand.DeleteLine);
            Check(failure is InvalidOperationException && editor.Text == "abc" && doc.Revision == 0 && provider.DeleteCalls == 1,
                "Deletion permission callbacks cannot bypass a newly read-only editor state");
        }
    }
    private sealed class CallbackProvider(Action insert, Action? delete = null) : IReadOnlySectionProvider
    {
        public int InsertCalls { get; private set; }
        public int DeleteCalls { get; private set; }
        public bool CanInsert(int offset) { InsertCalls++; insert(); return true; }
        public IEnumerable<ISegment> GetDeletableSegments(ISegment segment)
        { DeleteCalls++; delete?.Invoke(); return [segment]; }
    }
}
