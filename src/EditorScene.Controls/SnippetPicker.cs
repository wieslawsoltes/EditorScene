using EditorScene.Snippets;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace EditorScene.Controls;

/// <summary>Reusable native Uno folder/filter/snippet selection control. It never modifies a document.</summary>
public sealed class SnippetPicker : UserControl
{
    private readonly SnippetCatalog _catalog;
    private readonly string _language;
    private readonly SnippetTypes _types;
    private readonly TextBox _filter = new() { PlaceholderText = "Filter snippets…", MinHeight = 30 };
    private readonly ComboBox _folder = new() { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 30 };
    private readonly ListView _list = new() { MinHeight = 180, Height = 240, SelectionMode = ListViewSelectionMode.Single, DisplayMemberPath = nameof(Choice.Label) };
    private readonly TextBlock _description = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly TextBlock _preview = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, MaxHeight = 140, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("monospace") };
    private bool _initializing;
    public SnippetPicker(SnippetCatalog catalog, string language, SnippetTypes types = SnippetTypes.Expansion)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog)); _language = language; _types = types;
        var layout = new StackPanel { Spacing = 8, Width = 460, MaxWidth = 560 };
        layout.Children.Add(_filter); layout.Children.Add(_folder); layout.Children.Add(_list); layout.Children.Add(_description); layout.Children.Add(_preview); Content = layout;
        AutomationProperties.SetName(_filter, "Filter snippets"); AutomationProperties.SetName(_folder, "Snippet folder"); AutomationProperties.SetName(_list, "Code snippets");
        _filter.TextChanged += (_, _) => Refresh(); _folder.SelectionChanged += (_, _) => Refresh();
        _list.SelectionChanged += (_, _) =>
        {
            _description.Text = SelectedSnippet?.Description ?? ""; _preview.Text = SelectedSnippet?.Code ?? "";
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        };
        _initializing = true;
        _folder.ItemsSource = new[] { "All folders" }.Concat(_catalog.Find(language, types).Select(e => e.Folder).Where(s => s.Length > 0).Distinct().Order(StringComparer.Ordinal)).ToArray();
        _folder.SelectedIndex = 0; _initializing = false; Refresh();
    }
    public CodeSnippet? SelectedSnippet => (_list.SelectedItem as Choice)?.Entry.Snippet;
    public event EventHandler? SelectionChanged;
    private void Refresh()
    {
        if (_initializing) return;
        var folder = _folder.SelectedIndex > 0 ? _folder.SelectedItem as string ?? "" : "";
        _list.ItemsSource = _catalog.Find(_language, _types, _filter.Text, folder).Select(e => new Choice(e)).ToArray();
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
    }
    private sealed record Choice(SnippetEntry Entry)
    { public string Label => Entry.Snippet.Title + (Entry.Snippet.Shortcut.Length == 0 ? "" : "  (" + Entry.Snippet.Shortcut + ")"); }
}
