using EditorScene.LanguageServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace EditorScene.Controls;

/// <summary>Virtualized hierarchical symbol navigator driven by language analysis.</summary>
public sealed class DocumentOutline : UserControl, IDisposable
{
    private readonly TreeView _tree = new() { SelectionMode = TreeViewSelectionMode.Single };
    private SyntaxEditor? _editor;
    private LanguageAnalysis? _shownAnalysis;
    public DocumentOutline()
    {
        Content = _tree; AutomationProperties.SetName(_tree, "Document outline");
        _tree.ItemInvoked += (_, args) => { if (args.InvokedItem is TreeViewNode { Content: OutlineItem item }) { if (_editor?.Analysis is { } current && ReferenceEquals(current, _shownAnalysis)) { _editor.Select(item.Symbol.SelectionRange.Offset, item.Symbol.SelectionRange.Length); _editor.FocusEditor(); } } };
    }
    public SyntaxEditor? Editor
    {
        get => _editor;
        set
        {
            if (ReferenceEquals(_editor, value)) return;
            if (_editor is not null) { _editor.AnalysisUpdated -= OnAnalysis; _editor.StateChanged -= OnStateChanged; }
            _editor = value;
            if (_editor is not null) { _editor.AnalysisUpdated += OnAnalysis; _editor.StateChanged += OnStateChanged; }
            Rebuild();
        }
    }
    private void OnAnalysis(object? sender, LanguageAnalysis analysis) => Rebuild();
    private void OnStateChanged(object? sender, EventArgs args) { if (!ReferenceEquals(_shownAnalysis, _editor?.Analysis)) Rebuild(); }
    private void Rebuild()
    {
        _shownAnalysis = _editor?.Analysis; _tree.RootNodes.Clear();
        if (_editor?.Analysis is not { } analysis || analysis.DocumentId != _editor.Document.Id) return;
        foreach (var symbol in analysis.Symbols) _tree.RootNodes.Add(CreateNode(symbol));
    }
    private static TreeViewNode CreateNode(NavigationSymbol symbol)
    {
        var node = new TreeViewNode { Content = new OutlineItem(symbol), IsExpanded = true };
        foreach (var child in symbol.Children) node.Children.Add(CreateNode(child));
        return node;
    }
    private sealed record OutlineItem(NavigationSymbol Symbol) { public override string ToString() => Symbol.Name; }
    public new void Dispose() { Editor = null; base.Dispose(); }
}
