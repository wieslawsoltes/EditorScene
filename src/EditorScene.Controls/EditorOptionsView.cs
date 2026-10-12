using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace EditorScene.Controls;

/// <summary>Edits a private value snapshot. It never owns or mutates a SyntaxEditor.
/// Hosts explicitly apply a validated result after their dialog is accepted.</summary>
public sealed class EditorOptionsView : UserControl
{
    private readonly NumberBox _tabSize = Number("Tab and indent size", 1, 1000);
    private readonly NumberBox _fontSize = Number("Editor font size", 8, 40);
    private readonly NumberBox _rulerColumn = Number("Ruler column", 1, 10000);
    private readonly CheckBox _spaces = Check("Insert spaces instead of tabs");
    private readonly CheckBox _wrap = Check("Word wrap");
    private readonly CheckBox _numbers = Check("Show line numbers");
    private readonly CheckBox _whitespace = Check("Show whitespace");
    private readonly CheckBox _guides = Check("Show indentation guides");
    private readonly CheckBox _ruler = Check("Show column ruler");
    private readonly CheckBox _brackets = Check("Complete matching brackets");
    private readonly CheckBox _completion = Check("Show completion automatically");
    private readonly TextBlock _validation = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    public EditorOptionsView() : this(new EditorViewOptions()) { }
    public EditorOptionsView(EditorViewOptions options)
    {
        options.Validate(); IsTabStop = false;
        var grid = new Grid { ColumnSpacing = 24, MaxWidth = 510, MinWidth = 280 };
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var display = new StackPanel { Spacing = 6 }; var editing = new StackPanel { Spacing = 6 };
        display.Children.Add(Heading("Display")); display.Children.Add(_fontSize);
        display.Children.Add(_numbers); display.Children.Add(_wrap); display.Children.Add(_whitespace);
        display.Children.Add(_guides); display.Children.Add(_ruler); display.Children.Add(_rulerColumn);
        editing.Children.Add(Heading("Tabs and input")); editing.Children.Add(_tabSize); editing.Children.Add(_spaces);
        editing.Children.Add(new TextBlock { Text = "The tab-stop width is also the indentation unit. Existing text is not converted.", TextWrapping = TextWrapping.Wrap, FontSize = 11 });
        editing.Children.Add(_brackets); editing.Children.Add(_completion);
        var reset = new Button { Content = "Restore defaults", FontSize = 12, MinHeight = 26, Margin = new Thickness(0, 12, 0, 0) };
        AutomationProperties.SetName(reset, "Restore editor option defaults"); reset.Click += (_, _) => SetOptions(new()); editing.Children.Add(reset);
        grid.Children.Add(display); Grid.SetColumn(editing, 1); grid.Children.Add(editing);
        var body = new StackPanel { Spacing = 10 }; body.Children.Add(grid); body.Children.Add(_validation);
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = 450 };
        SetOptions(options);
    }
    public NumberBox TabSizeInput => _tabSize;
    public NumberBox FontSizeInput => _fontSize;
    public NumberBox RulerColumnInput => _rulerColumn;
    public CheckBox InsertSpacesInput => _spaces;
    public CheckBox WordWrapInput => _wrap;
    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
    private static CheckBox Check(string text)
    {
        var control = new CheckBox { Content = text, MinHeight = 28, FontSize = 12 };
        AutomationProperties.SetName(control, text); return control;
    }
    private static NumberBox Number(string name, double min, double max)
    {
        var control = new NumberBox { Header = name, Minimum = min, Maximum = max, SmallChange = 1, LargeChange = 4,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, FontSize = 12, MinWidth = 100 };
        AutomationProperties.SetName(control, name); return control;
    }
    public void SetOptions(EditorViewOptions options)
    {
        options.Validate(); _tabSize.Value = options.TabSize; _fontSize.Value = options.FontSize; _rulerColumn.Value = options.RulerColumn;
        _spaces.IsChecked = options.ConvertTabsToSpaces; _wrap.IsChecked = options.WordWrap; _numbers.IsChecked = options.ShowLineNumbers;
        _whitespace.IsChecked = options.ShowWhitespace; _guides.IsChecked = options.ShowIndentGuides; _ruler.IsChecked = options.ShowRuler;
        _brackets.IsChecked = options.AutoCompleteBrackets; _completion.IsChecked = options.AutoShowCompletion; _validation.Text = string.Empty;
    }
    public bool TryGetOptions(out EditorViewOptions options)
    {
        options = default;
        if (!double.IsFinite(_tabSize.Value) || _tabSize.Value != Math.Truncate(_tabSize.Value)
            || !double.IsFinite(_rulerColumn.Value) || _rulerColumn.Value != Math.Truncate(_rulerColumn.Value)
            || !double.IsFinite(_fontSize.Value))
        { _validation.Text = "Enter a whole-number tab/ruler value and a valid font size."; return false; }
        try
        {
            options = new() { TabSize = checked((int)_tabSize.Value), ConvertTabsToSpaces = _spaces.IsChecked == true,
                FontSize = _fontSize.Value, WordWrap = _wrap.IsChecked == true, ShowLineNumbers = _numbers.IsChecked == true,
                ShowWhitespace = _whitespace.IsChecked == true, ShowIndentGuides = _guides.IsChecked == true,
                ShowRuler = _ruler.IsChecked == true, RulerColumn = checked((int)_rulerColumn.Value),
                AutoCompleteBrackets = _brackets.IsChecked == true, AutoShowCompletion = _completion.IsChecked == true };
            options.Validate(); _validation.Text = string.Empty; return true;
        }
        catch (Exception error) when (error is ArgumentOutOfRangeException or OverflowException)
        { options = default; _validation.Text = "Tab width: 1–1000. Font size: 8–40. Ruler column: 1–10000."; return false; }
    }
}
