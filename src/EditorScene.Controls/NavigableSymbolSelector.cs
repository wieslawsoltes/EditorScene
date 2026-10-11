using EditorScene.LanguageServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace EditorScene.Controls;

/// <summary>Revision-safe symbol selectors with indexed, allocation-free caret lookup after cache warmup.</summary>
public sealed class NavigableSymbolSelector : UserControl, IDisposable
{
    private readonly ComboBox _types = new() { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 26, Height = 26, FontSize = 12, Padding = new Thickness(6, 0, 6, 0) };
    private readonly ComboBox _members = new() { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 26, Height = 26, FontSize = 12, Padding = new Thickness(6, 0, 6, 0) };
    private readonly List<ComboBoxItem> _matches = [];
    private RangeIndex<ComboBoxItem>? _typeIndex, _memberIndex;
    private LanguageAnalysis? _shownAnalysis;
    private SyntaxEditor? _editor;
    private bool _updating;
    public NavigableSymbolSelector()
    {
        var grid = new Grid { ColumnSpacing = 2 };
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(_types); Grid.SetColumn(_members, 1); grid.Children.Add(_members); Content = grid;
        AutomationProperties.SetName(_types, "Navigate types and containers"); AutomationProperties.SetName(_members, "Navigate members");
        _types.SelectionChanged += (_, _) => { if (!_updating && _types.SelectedItem is ComboBoxItem { Tag: NavigationSymbol symbol }) { UpdateMembers(symbol); Navigate(symbol); } };
        _members.SelectionChanged += (_, _) => { if (!_updating && _members.SelectedItem is ComboBoxItem { Tag: NavigationSymbol symbol }) Navigate(symbol); };
    }
    public SyntaxEditor? Editor
    {
        get => _editor;
        set
        {
            if (ReferenceEquals(value, _editor)) return;
            if (_editor is not null) { _editor.AnalysisUpdated -= AnalysisUpdated; _editor.StateChanged -= StateChanged; }
            _editor = value;
            if (_editor is not null) { _editor.AnalysisUpdated += AnalysisUpdated; _editor.StateChanged += StateChanged; }
            Rebuild();
        }
    }
    public ComboBox TypeSelector => _types;
    public ComboBox MemberSelector => _members;
    private void AnalysisUpdated(object? sender, LanguageAnalysis analysis) => Rebuild();
    private void StateChanged(object? sender, EventArgs args)
    { if (!ReferenceEquals(_shownAnalysis, _editor?.Analysis)) Rebuild(); else SelectAtCaret(); }
    private static bool IsContainer(NavigationSymbol symbol) => symbol.Children.Length > 0 || symbol.Kind is "NamedType" or "Namespace" or "Class" or "Module";
    private static IEnumerable<NavigationSymbol> Flatten(IEnumerable<NavigationSymbol> symbols)
    {
        var stack = new Stack<NavigationSymbol>(symbols.Reverse());
        while (stack.TryPop(out var symbol))
        { yield return symbol; for (var i = symbol.Children.Length - 1; i >= 0; i--) stack.Push(symbol.Children[i]); }
    }
    private static ComboBoxItem Item(NavigationSymbol symbol) => new() { Content = symbol.Name, Tag = symbol, FontSize = 12, MinHeight = 24 };
    private static RangeIndex<ComboBoxItem> Index(ComboBox combo) => new(combo.Items.OfType<ComboBoxItem>(), i => ((NavigationSymbol)i.Tag).Range);
    private void Rebuild()
    {
        if (_updating) return;
        _updating = true;
        try
        {
            _shownAnalysis = _editor?.Analysis; _typeIndex = _memberIndex = null; _matches.Clear();
            _types.Items.Clear(); _members.Items.Clear();
            _types.PlaceholderText = "(No symbols)"; _members.PlaceholderText = "(Navigate member)";
            if (_shownAnalysis is not { } analysis) return;
            foreach (var symbol in Flatten(analysis.Symbols).Where(IsContainer)) _types.Items.Add(Item(symbol));
            if (_types.Items.Count == 0) foreach (var symbol in analysis.Symbols) _types.Items.Add(Item(symbol));
            _types.PlaceholderText = _types.Items.Count == 0 ? "(No symbols)" : "(Navigate symbol)";
            _typeIndex = Index(_types);
        }
        finally { _updating = false; }
        SelectAtCaret();
    }
    private ComboBoxItem? AtCaret(RangeIndex<ComboBoxItem>? index)
    {
        if (index is null || _editor is null) return null;
        _matches.Clear(); index.CopyOverlaps(new(_editor.CaretOffset, 0), _matches);
        ComboBoxItem? best = null; var length = int.MaxValue;
        foreach (var candidate in _matches)
            if (candidate.Tag is NavigationSymbol symbol && symbol.Range.Length < length) { best = candidate; length = symbol.Range.Length; }
        return best;
    }
    private void SelectAtCaret()
    {
        if (_updating || _editor is null) return;
        _updating = true;
        try
        {
            var item = AtCaret(_typeIndex);
            if (!ReferenceEquals(item, _types.SelectedItem)) { _types.SelectedItem = item; UpdateMembers(item?.Tag as NavigationSymbol); }
            _members.SelectedItem = AtCaret(_memberIndex);
        }
        finally { _updating = false; }
    }
    private void UpdateMembers(NavigationSymbol? symbol)
    {
        var before = _updating; _updating = true;
        try
        {
            _members.Items.Clear(); _memberIndex = null;
            if (symbol is not null) foreach (var child in Flatten(symbol.Children).Where(s => !IsContainer(s))) _members.Items.Add(Item(child));
            _memberIndex = Index(_members);
        }
        finally { _updating = before; }
    }
    private void Navigate(NavigationSymbol symbol)
    {
        if (_editor?.Analysis is null || !ReferenceEquals(_shownAnalysis, _editor.Analysis)) { Rebuild(); return; }
        _editor.Select(symbol.SelectionRange.Offset, symbol.SelectionRange.Length); _editor.FocusEditor();
    }
    public new void Dispose() { Editor = null; base.Dispose(); }
}
