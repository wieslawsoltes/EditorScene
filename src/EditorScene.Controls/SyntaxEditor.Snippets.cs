using EditorScene.Snippets;
using EditorScene.Text;
using Microsoft.UI.Xaml.Controls;
using UnoEdit.Snippets;

namespace EditorScene.Controls;

public sealed partial class SyntaxEditor
{
    private InsertionContext? _snippetSession;
    public SnippetCatalog Snippets { get; } = SnippetCatalog.CreateDefault();
    public bool IsSnippetActive => _snippetSession is not null;
    public IReadOnlyList<SelectionRange> ActiveSnippetFields => _snippetSession?.ActiveElements.Where(e => e.IsEditable && e.Segment is not null)
        .Select(e => new SelectionRange(e.Segment.Offset, e.Segment.EndOffset)).ToArray() ?? [];
    /// <summary>Optional trusted host evaluator. Unhandled functions are rejected before text mutation.</summary>
    public Func<CodeSnippet, SnippetDeclaration, DocumentSnapshot, string>? SnippetFunctionResolver { get; set; }
    /// <summary>The host must explicitly satisfy requested imports/references before insertion. No URLs or assemblies are loaded by the control.</summary>
    public Func<CodeSnippet, DocumentSnapshot, bool>? SnippetDependenciesHandler { get; set; }
    public event EventHandler? SnippetSessionChanged;

    public void InsertSnippet(CodeSnippet snippet) => InsertSnippetCore(snippet, 0);
    private void InsertSnippetCore(CodeSnippet snippet, int shortcutLength)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(snippet);
        var document = Document; var snapshot = document.Snapshot(); var language = SyntaxLanguage;
        if (snippet.Language != CodeSnippet.NormalizeLanguage(language.Id)) throw new InvalidOperationException("The snippet language does not match this editor.");
        var start = shortcutLength > 0 ? CaretOffset - shortcutLength : SelectionStart;
        var length = shortcutLength > 0 ? shortcutLength : SelectionLength;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in snippet.Declarations)
        {
            var value = field.Function.Length == 0 ? field.DefaultText : SnippetFunctionResolver?.Invoke(snippet, field, snapshot)
                ?? throw new NotSupportedException("The host must provide a resolver for snippet function: " + field.Function);
            if (value.Length > 1_048_576) throw new InvalidOperationException("A resolved snippet field exceeds the size limit.");
            values.Add(field.Id, value);
        }
        if ((!snippet.Imports.IsEmpty || !snippet.References.IsEmpty) && SnippetDependenciesHandler?.Invoke(snippet, snapshot) != true)
            throw new NotSupportedException("This snippet requires a host that can apply its imports and assembly references.");
        var native = new Snippet(); var declarations = snippet.Declarations.ToDictionary(f => f.Id, StringComparer.Ordinal);
        var linked = new Dictionary<string, SnippetReplaceableTextElement>(StringComparer.Ordinal);
        foreach (var part in snippet.Parts)
        {
            switch (part.Kind)
            {
                case SnippetPartKind.Text: native.Elements.Add(new SnippetTextElement { Text = part.Text }); break;
                case SnippetPartKind.SelectedText: native.Elements.Add(new SnippetSelectionElement()); break;
                case SnippetPartKind.End: native.Elements.Add(new SnippetCaretElement()); break;
                case SnippetPartKind.Field:
                    if (!declarations[part.Text].Editable) native.Elements.Add(new SnippetTextElement { Text = values[part.Text] });
                    else if (linked.TryGetValue(part.Text, out var target)) native.Elements.Add(new SnippetBoundElement { TargetElement = target });
                    else
                    {
                        var field = new SnippetReplaceableTextElement { Text = values[part.Text] };
                        linked.Add(part.Text, field); native.Elements.Add(field);
                    }
                    break;
                default: throw new InvalidOperationException("Unknown snippet part.");
            }
        }
        if (!snippet.Parts.Any(p => p.Kind == SnippetPartKind.End)) native.Elements.Add(new SnippetCaretElement());
        if (!ReferenceEquals(Document, document) || document.Revision != snapshot.Revision || !ReferenceEquals(SyntaxLanguage, language))
            throw new InvalidOperationException("A snippet host callback changed the insertion target.");
        if (!CanEdit(new(start, length, ""))) throw new InvalidOperationException("Cannot insert a snippet into read-only text.");
        ClosePopups(); EndSnippet(false); ClearMultiSelection();
        using (document.Buffer.RunUpdate())
        {
            if (shortcutLength > 0)
            {
                _editor.TextArea.Session.Select(start, length);
                _editor.TextArea.Session.ReplaceSelection("");
            }
            var context = native.Insert(_editor.TextArea);
            if (context.ActiveElements.Any(e => e.IsEditable))
            {
                _snippetSession = context;
                context.Deactivated += (_, _) =>
                {
                    if (!ReferenceEquals(_snippetSession, context)) return;
                    _snippetSession = null; SnippetSessionChanged?.Invoke(this, EventArgs.Empty);
                    StateChanged?.Invoke(this, EventArgs.Empty);
                };
            }
        }
        FocusEditor(); SetStatus(IsSnippetActive ? "Snippet: Tab / Shift+Tab fields, Enter completes, Esc exits" : "Snippet inserted");
        SnippetSessionChanged?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>Exit snippet editing. Cancel leaves the inserted text intact; undo reverts insertion.</summary>
    public void EndSnippet(bool commit = true)
    {
        _snippetSession?.Deactivate(new SnippetEventArgs(commit ? DeactivateReason.ReturnPressed : DeactivateReason.EscapePressed));
    }
    /// <summary>Expand the shortcut immediately preceding the caret. Returns false for ordinary Tab input.</summary>
    public bool TryExpandSnippetShortcut()
    {
        if (IsReadOnly || SelectionLength != 0 || IsSnippetActive || IsCompletionOpen) return false;
        var end = CaretOffset; var start = end;
        while (start > 0 && end - start < 128 && IsWord(Document.Buffer.GetCharAt(start - 1))) start--;
        if (start == end) return false;
        var snippet = Snippets.FindShortcut(Document.Buffer.GetText(start, end - start), SyntaxLanguage.Id);
        if (snippet is null) return false;
        InsertSnippetCore(snippet, end - start); return true;
    }
    /// <summary>Open a native reusable snippet selector without losing the original replacement selection.</summary>
    public async Task ShowSnippetPickerAsync(bool surroundsWith = false)
    {
        try
        {
            if (IsReadOnly) throw new InvalidOperationException("The document is read only.");
            if (XamlRoot is null) throw new InvalidOperationException("The editor must be loaded before showing a snippet selector.");
            var document = Document; var revision = document.Revision; var start = SelectionStart; var length = SelectionLength;
            var picker = new SnippetPicker(Snippets, SyntaxLanguage.Id, surroundsWith ? SnippetTypes.SurroundsWith : SnippetTypes.Expansion);
            var dialog = new ContentDialog
            {
                Title = surroundsWith ? "Surround with snippet" : "Insert snippet", Content = picker, XamlRoot = XamlRoot,
                RequestedTheme = RequestedTheme, PrimaryButtonText = "Insert", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || picker.SelectedSnippet is not { } snippet) return;
            if (_disposed || !ReferenceEquals(Document, document) || document.Revision != revision) throw new InvalidOperationException("The document changed while selecting a snippet.");
            Select(start, length); InsertSnippet(snippet);
        }
        catch (Exception error) { if (!_disposed) ReportError(error); }
    }
}
