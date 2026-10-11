using EditorScene.LanguageServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace EditorScene.Controls;

/// <summary>Two synchronized symbol selectors. Selections navigate the attached view without modifying the document.</summary>
public sealed class NavigableSymbolSelector : UserControl, IDisposable
{
    private readonly ComboBox _types = new() { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 26, Height = 26, FontSize = 12, Padding = new Thickness(6, 0, 6, 0) };
    private readonly ComboBox _members = new() { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 26, Height = 26, FontSize = 12, Padding = new Thickness(6, 0, 6, 0) };
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
    private void StateChanged(object? sender, EventArgs args) => SelectAtCaret();
    private static bool IsContainer(NavigationSymbol symbol) => symbol.Children.Length > 0 || symbol.Kind is "NamedType" or "Namespace" or "Class" or "Module";
    private static IEnumerable<NavigationSymbol> Flatten(IEnumerable<NavigationSymbol> symbols)
    { foreach (var symbol in symbols) { yield return symbol; foreach (var child in Flatten(symbol.Children)) yield return child; } }
    private static ComboBoxItem Item(NavigationSymbol symbol) => new() { Content = symbol.Name, Tag = symbol, FontSize = 12, MinHeight = 24 };
    private void Rebuild()
    {
        _updating = true;
        try
        {
            _types.Items.Clear(); _members.Items.Clear();
            if (_editor?.Analysis is not { } analysis || analysis.DocumentId != _editor.Document.Id) return;
            foreach (var symbol in Flatten(analysis.Symbols).Where(IsContainer)) _types.Items.Add(Item(symbol));
            if (_types.Items.Count == 0) foreach (var symbol in analysis.Symbols) _types.Items.Add(Item(symbol));
            _types.PlaceholderText = _types.Items.Count == 0 ? "(No symbols)" : "(Navigate symbol)";
            _members.PlaceholderText = "(Navigate member)";
        }
        finally { _updating = false; }
        SelectAtCaret();
    }
    private void SelectAtCaret()
    {
        if (_updating || _editor is null) return;
        _updating = true;
        try
        {
            var item = _types.Items.OfType<ComboBoxItem>().Where(i => i.Tag is NavigationSymbol s && s.Range.Contains(_editor.CaretOffset)).OrderBy(i => ((NavigationSymbol)i.Tag).Range.Length).FirstOrDefault();
            if (!ReferenceEquals(item, _types.SelectedItem)) { _types.SelectedItem = item; UpdateMembers(item?.Tag as NavigationSymbol); }
            _members.SelectedItem = _members.Items.OfType<ComboBoxItem>().Where(i => i.Tag is NavigationSymbol s && s.Range.Contains(_editor.CaretOffset)).OrderBy(i => ((NavigationSymbol)i.Tag).Range.Length).FirstOrDefault();
        }
        finally { _updating = false; }
    }
    private void UpdateMembers(NavigationSymbol? symbol)
    {
        var before = _updating; _updating = true;
        try { _members.Items.Clear(); if (symbol is not null) foreach (var child in Flatten(symbol.Children).Where(s => !IsContainer(s))) _members.Items.Add(Item(child)); }
        finally { _updating = before; }
    }
    private void Navigate(NavigationSymbol symbol) { _editor?.Select(symbol.SelectionRange.Offset, symbol.SelectionRange.Length); _editor?.FocusEditor(); }
    public new void Dispose() { Editor = null; base.Dispose(); }
}
