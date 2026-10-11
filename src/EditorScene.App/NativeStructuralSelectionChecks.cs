using System.Collections.Immutable;
using EditorScene.Controls;
using EditorScene.LanguageServices;
using EditorScene.Languages.DotNet;
using EditorScene.Text;

namespace EditorScene.App;

internal static class NativeStructuralSelectionChecks
{
    internal static async Task RunAsync(List<string> checks)
    {
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException("Structural selection regression: " + name); checks.Add(name); }
        using var document = new EditorDocument("alpha beta", "selection.txt");
        using var editor = new SyntaxEditor { Document = document, SyntaxLanguage = new PlainTextLanguage() };
        editor.Select(2, 0);
        Check(await editor.ExpandSelectionAsync() && editor.SelectedText == "alpha", "Native fallback expands a caret to its Unicode word");
        Check(await editor.ExpandSelectionAsync() && editor.SelectedText == "alpha beta", "Native fallback expands a word to its line");
        Check(editor.ContractSelection() && editor.SelectedText == "alpha", "Native contraction restores the preceding selection");
        Check(editor.ContractSelection() && editor.CaretOffset == 2 && editor.SelectionLength == 0, "Native contraction restores the exact original caret");
        editor.NativeEditor.TextArea.Session.SetCaretOffset(0); editor.NativeEditor.TextArea.Session.SetSelection(5, 0);
        await editor.ExpandSelectionAsync();
        Check(editor.NativeEditor.TextArea.Session.AnchorOffset == 10 && editor.CaretOffset == 0, "Reverse selection remains reversed during structural expansion");
        editor.ContractSelection();
        Check(editor.NativeEditor.TextArea.Session.AnchorOffset == 5 && editor.CaretOffset == 0, "Reverse selection contracts to its original endpoints");
        editor.NativeEditor.TextArea.Session.SetCaretOffset(2);
        editor.NativeEditor.TextArea.Session.SetSelection(5, 0);
        await editor.ExpandSelectionAsync();
        Check(editor.CaretOffset == 0 && editor.SelectionLength == 10, "Expansion positions the insertion caret at the active structural endpoint");
        editor.ContractSelection();
        Check(editor.CaretOffset == 2 && editor.NativeEditor.TextArea.Session.AnchorOffset == 5 && editor.NativeEditor.TextArea.Session.SelectionEndOffset == 0,
            "Contraction restores an insertion caret positioned independently of the selection endpoints");
        editor.IsReadOnly = true; await editor.ExpandSelectionAsync(); editor.ContractSelection();
        Check(editor.Text == "alpha beta" && document.Revision == 0, "Structural selection works in read-only documents without editing"); editor.IsReadOnly = false;
        editor.Select(2, 0); await editor.ExpandSelectionAsync(); editor.CaretOffset = 8;
        Check(!editor.ContractSelection() && editor.SelectionExpansionDepth == 0, "Manual caret movement invalidates structural selection history");

        var pendingProvider = new DeferredSelections(); editor.SyntaxLanguage = pendingProvider; editor.Select(1, 0);
        var pending = editor.ExpandSelectionAsync();
        Check(editor.IsSelectionExpansionPending, "Native view observes an outstanding selection provider request");
        editor.CaretOffset = 8; pendingProvider.Complete([new(0, 5), new(0, 10)]);
        Check(!await pending && editor.CaretOffset == 8 && editor.SelectionLength == 0, "Delayed expansion cannot move a newer caret");
        pendingProvider = new(); editor.SyntaxLanguage = pendingProvider; editor.Select(1, 0); pending = editor.ExpandSelectionAsync();
        editor.Text = "replacement"; pendingProvider.Complete([new(0, 5)]);
        Check(!await pending && editor.Text == "replacement" && editor.SelectionExpansionDepth == 0, "Delayed expansion cannot select an obsolete edited revision");
        pendingProvider = new(); editor.SyntaxLanguage = pendingProvider; editor.Select(1, 0); pending = editor.ExpandSelectionAsync();
        editor.SyntaxLanguage = new PlainTextLanguage(); pendingProvider.Complete([new(0, 5)]);
        Check(!await pending && editor.SelectionLength == 0, "Changing the provider invalidates its delayed selection result");
        pendingProvider = new(); editor.SyntaxLanguage = pendingProvider; pending = editor.ExpandSelectionAsync();
        Check(!editor.ContractSelection() && !editor.IsSelectionExpansionPending, "Contract cancels an outstanding expansion request");
        pendingProvider.Complete([new(0, 5)]); Check(!await pending, "Cancelled expansion cannot reappear after its response arrives");

        var provider = new FixedSelections([new(0, 5), new(0, 11)]); editor.SyntaxLanguage = provider; editor.Select(2, 0);
        await editor.ExpandSelectionAsync(); await editor.ExpandSelectionAsync(); editor.ContractSelection(); editor.ContractSelection(); await editor.ExpandSelectionAsync();
        Check(provider.Calls == 1, "Repeated native expansion and contraction reuse one provider result");
        editor.SyntaxLanguage = new FixedSelections([new(-1, 4)]); editor.Select(1, 0); var rejected = false;
        try { await editor.ExpandSelectionAsync(); } catch (ArgumentException) { rejected = true; }
        Check(rejected && editor.CaretOffset == 1 && editor.SelectionLength == 0, "Invalid provider ranges cannot partially change the native selection");
        using var other = new EditorDocument("other", "other.txt"); pendingProvider = new(); editor.SyntaxLanguage = pendingProvider;
        pending = editor.ExpandSelectionAsync(); editor.SetDocumentAndLanguage(other, new PlainTextLanguage()); pendingProvider.Complete([new(0, 5)]);
        Check(!await pending && editor.Document == other && editor.Text == "other", "Document rebinding cancels structural selection for the previous document");
        editor.SetDocumentAndLanguage(document, new DotNetLanguage()); editor.Text = "class C { int M() => 1 + 2; }";
        var number = editor.Text.IndexOf('1'); editor.Select(number, 0);
        await editor.ExecuteAsync(EditorCommand.ExpandSelection); Check(editor.SelectedText == "1", "Native Expand Selection command uses the C# syntax token");
        await editor.ExecuteAsync(EditorCommand.ExpandSelection); Check(editor.SelectedText == "1 + 2", "Native expansion proceeds to the containing C# expression");
        await editor.ExecuteAsync(EditorCommand.ContractSelection); Check(editor.SelectedText == "1", "Native Contract Selection command restores the C# token");
        pendingProvider = new(); var disposed = new SyntaxEditor { Document = document, SyntaxLanguage = pendingProvider }; disposed.Select(number, 0);
        pending = disposed.ExpandSelectionAsync(); disposed.Dispose(); pendingProvider.Complete([new(0, document.Buffer.TextLength)]);
        Check(!await pending, "Disposing a native view prevents late expansion from touching its controls");
        await RunReentrancyChecksAsync(Check);
    }
    private static async Task RunReentrancyChecksAsync(Action<bool, string> check)
    {
        foreach (var scenario in new[] { "move", "range", "edit", "rebind", "provider", "dispose", "multiple" })
        {
            using var document = new EditorDocument("alpha beta"); using var other = new EditorDocument("x");
            using var editor = new SyntaxEditor { Document = document, SyntaxLanguage = new PlainTextLanguage() };
            editor.Select(2, 0); var fired = false;
            void Redirect(object? sender, EventArgs args)
            {
                if (fired || editor.SelectionExpansionDepth != 1 || editor.CaretOffset != 5) return;
                if (scenario == "range" && editor.SelectionLength != 5) return;
                fired = true;
                switch (scenario)
                {
                    case "move": case "range": editor.NativeEditor.TextArea.Session.MoveTo(8); break;
                    case "edit": editor.Text = "changed"; editor.NativeEditor.TextArea.Session.MoveTo(1); break;
                    case "rebind": editor.SetDocumentAndLanguage(other, new PlainTextLanguage()); break;
                    case "provider": editor.SyntaxLanguage = new PlainTextLanguage(); break;
                    case "dispose": editor.Dispose(); break;
                    case "multiple": editor.Selections.Set([new(8, 8), new(9, 9)]); break;
                }
            }
            editor.StateChanged += Redirect;
            var expanded = await editor.ExpandSelectionAsync(); editor.StateChanged -= Redirect;
            var preserved = scenario switch
            {
                "move" or "range" => editor.CaretOffset == 8 && editor.SelectionLength == 0,
                "edit" => editor.Text == "changed" && editor.CaretOffset == 1,
                "rebind" => ReferenceEquals(editor.Document, other) && editor.Text == "x",
                "provider" => editor.SelectionLength == 0,
                "multiple" => editor.Selections.Ranges.Count == 2,
                _ => true
            };
            check(fired && !expanded && preserved && editor.SelectionExpansionDepth == 0,
                "Synchronous structural-selection callback preserves host " + scenario + " instead of overwriting it");
        }
        using var doc = new EditorDocument("alpha beta");
        using var contraction = new SyntaxEditor { Document = doc, SyntaxLanguage = new PlainTextLanguage() };
        contraction.Select(2, 0); await contraction.ExpandSelectionAsync(); await contraction.ExpandSelectionAsync();
        var redirected = false;
        void OnContract(object? sender, EventArgs args)
        {
            if (redirected || contraction.CaretOffset != 5) return;
            redirected = true; contraction.NativeEditor.TextArea.Session.MoveTo(8);
        }
        contraction.StateChanged += OnContract;
        var contracted = contraction.ContractSelection(); contraction.StateChanged -= OnContract;
        check(redirected && !contracted && contraction.CaretOffset == 8 && contraction.SelectionLength == 0 && contraction.SelectionExpansionDepth == 0,
            "Contraction preserves a synchronous host redirection and discards its obsolete history");
        var provider = new DeferredSelections(); contraction.SyntaxLanguage = provider; contraction.Select(2, 0);
        var pending = contraction.ExpandSelectionAsync(); contraction.Selections.Set([new(8, 8), new(9, 9)]);
        provider.Complete([new(0, 5)]);
        check(!await pending && contraction.Selections.Ranges.Count == 2 && contraction.CaretOffset == 2,
            "Late structural ranges cannot discard additional carets created during the request");
    }
    private class FixedSelections(ImmutableArray<SourceRange> ranges) : ISyntaxLanguage, ISelectionRangeProvider
    {
        public string Id => "plaintext";
        public string DisplayName => "Selection test";
        public IReadOnlyList<string> Extensions => [];
        public LanguageCapabilities Capabilities => LanguageCapabilities.SelectionRanges;
        public int Calls { get; private set; }
        public Task<LanguageAnalysis> AnalyzeAsync(DocumentSnapshot snapshot, CancellationToken cancellationToken = default) => Task.FromResult(LanguageAnalysis.Empty(snapshot));
        public virtual Task<ImmutableArray<SourceRange>> GetSelectionRangesAsync(DocumentSnapshot snapshot, int offset, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(ranges); }
    }
    private sealed class DeferredSelections() : FixedSelections([])
    {
        private readonly TaskCompletionSource<ImmutableArray<SourceRange>> _source = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // Deliberately ignore cancellation to verify the view's independent stale-result guard.
        public override Task<ImmutableArray<SourceRange>> GetSelectionRangesAsync(DocumentSnapshot snapshot, int offset, CancellationToken cancellationToken = default) => _source.Task;
        internal void Complete(ImmutableArray<SourceRange> ranges) => _source.TrySetResult(ranges);
    }
}
