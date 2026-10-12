using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace EditorScene.Controls;

/// <summary>Edits a private value snapshot. The responsive native panel never owns or
/// mutates an editor. Hosts explicitly apply a validated result after acceptance.</summary>
public sealed class EditorOptionsView : UserControl, IDisposable
{
    private bool _disposed, _stacked;
    private readonly Grid _sections = new() { ColumnSpacing = 24, MaxWidth = 560 };
    private readonly StackPanel _display = new() { Spacing = 6 };
    private readonly StackPanel _editing = new() { Spacing = 6 };
    private readonly NumberBox _tabSize = Number("Tab and indent size", 1, 1000);
    private readonly NumberBox _fontSize = Number("Editor font size", 8, 40);
    private readonly NumberBox _rulerColumn = Number("Ruler column", 1, 10000);
    private readonly CheckBox _spaces = Check("Insert spaces instead of tabs");
    private readonly CheckBox _softBackspace = Check("Backspace over spaces to tab stop");
    private readonly CheckBox _wrap = Check("Word wrap");
    private readonly CheckBox _numbers = Check("Show line numbers");
    private readonly CheckBox _whitespace = Check("Show whitespace");
    private readonly CheckBox _guides = Check("Show indentation guides");
    private readonly CheckBox _ruler = Check("Show column ruler");
    private readonly CheckBox _brackets = Check("Complete matching brackets");
    private readonly CheckBox _completion = Check("Show completion automatically");
    private readonly TextBlock _validation = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly Button _reset = new() { Content = "Restore defaults", FontSize = 12, MinHeight = 26,
        HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    public string ValidationMessage => _validation.Text;
    public bool UsesStackedLayout => _stacked;
    public Button RestoreDefaultsButton => _reset;
    public ScrollViewer SettingsScroller => _scroll;
    public EditorOptionsView() : this(new EditorViewOptions()) { }
    public EditorOptionsView(EditorViewOptions options)
    {
        options.Validate(); IsTabStop = false;
        _sections.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        _sections.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        _sections.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _sections.RowDefinitions.Add(new() { Height = GridLength.Auto });
        AutomationProperties.SetName(_sections, "Editor option sections");
        AutomationProperties.SetName(_display, "Editor display options");
        AutomationProperties.SetName(_editing, "Editor indentation options");
        AutomationProperties.SetName(_scroll, "Editor options scroll");
        AutomationProperties.SetName(_reset, "Restore editor option defaults");
        _display.Children.Add(Heading("Display")); _display.Children.Add(_fontSize);
        _display.Children.Add(_numbers); _display.Children.Add(_wrap); _display.Children.Add(_whitespace);
        _display.Children.Add(_guides); _display.Children.Add(_ruler); _display.Children.Add(_rulerColumn);
        _editing.Children.Add(Heading("Tabs and input")); _editing.Children.Add(_tabSize); _editing.Children.Add(_spaces);
        _editing.Children.Add(_softBackspace);
        _editing.Children.Add(new TextBlock { Text = "The tab-stop width is also the indentation unit. Existing text is not converted.", TextWrapping = TextWrapping.Wrap, FontSize = 11 });
        _editing.Children.Add(Heading("IntelliPrompt")); _editing.Children.Add(_brackets); _editing.Children.Add(_completion);
        _spaces.Checked += OnSpacesChanged; _spaces.Unchecked += OnSpacesChanged;
        _reset.Click += OnRestoreDefaults;
        _sections.Children.Add(_display); Grid.SetColumn(_editing, 1); _sections.Children.Add(_editing);
        _sections.SizeChanged += OnSectionsSizeChanged;
        _scroll.Content = _sections;
        // Actions stay outside the scroll content, including in narrow windows.
        var body = new Grid { MaxHeight = 450 };
        body.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        body.Children.Add(_scroll); Grid.SetRow(_validation, 1); body.Children.Add(_validation);
        Grid.SetRow(_reset, 2); body.Children.Add(_reset); Content = body;
        SetOptions(options);
    }
    private void OnRestoreDefaults(object sender, RoutedEventArgs args) { if (!_disposed) SetOptions(new EditorViewOptions()); }
    private void OnSpacesChanged(object sender, RoutedEventArgs args) { if (!_disposed) _softBackspace.IsEnabled = _spaces.IsChecked == true; }
    private void OnSectionsSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (_disposed || args.NewSize.Width <= 0) return;
        var stacked = args.NewSize.Width < 480;
        if (_stacked == stacked) return;
        _stacked = stacked;
        Grid.SetColumnSpan(_display, stacked ? 2 : 1);
        Grid.SetColumn(_editing, stacked ? 0 : 1); Grid.SetRow(_editing, stacked ? 1 : 0);
        Grid.SetColumnSpan(_editing, stacked ? 2 : 1);
        _editing.Margin = stacked ? new Thickness(0, 16, 0, 0) : new Thickness(0);
    }
    public new void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _sections.SizeChanged -= OnSectionsSizeChanged; _reset.Click -= OnRestoreDefaults;
        _spaces.Checked -= OnSpacesChanged; _spaces.Unchecked -= OnSpacesChanged; base.Dispose();
    }
    public NumberBox TabSizeInput => _tabSize;
    public NumberBox FontSizeInput => _fontSize;
    public NumberBox RulerColumnInput => _rulerColumn;
    public CheckBox InsertSpacesInput => _spaces;
    public CheckBox SoftTabBackspaceInput => _softBackspace;
    public CheckBox WordWrapInput => _wrap;
    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
    private static CheckBox Check(string text)
    {
        var control = new CheckBox { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12 },
            MinHeight = 28, FontSize = 12, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(control, text); return control;
    }
    private static NumberBox Number(string name, double min, double max)
    {
        // Do not silently coerce an invalid draft to its minimum on losing focus.
        // TryGetOptions validates the entire draft and keeps the dialog open instead.
        var control = new NumberBox { ValidationMode = NumberBoxValidationMode.Disabled, Header = name, Minimum = min, Maximum = max, SmallChange = 1, LargeChange = 4,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, FontSize = 12, MinWidth = 100 };
        AutomationProperties.SetName(control, name); return control;
    }
    public void SetOptions(EditorViewOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        options.Validate(); _tabSize.Value = options.TabSize; _fontSize.Value = options.FontSize; _rulerColumn.Value = options.RulerColumn;
        _spaces.IsChecked = options.ConvertTabsToSpaces; _wrap.IsChecked = options.WordWrap; _numbers.IsChecked = options.ShowLineNumbers;
        _whitespace.IsChecked = options.ShowWhitespace; _guides.IsChecked = options.ShowIndentGuides; _ruler.IsChecked = options.ShowRuler;
        _softBackspace.IsChecked = options.CanBackspaceOverSpacesToTabStop; _softBackspace.IsEnabled = options.ConvertTabsToSpaces;
        _brackets.IsChecked = options.AutoCompleteBrackets; _completion.IsChecked = options.AutoShowCompletion; _validation.Text = string.Empty;
    }
    public bool TryGetOptions(out EditorViewOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        options = default;
        if (!double.IsFinite(_tabSize.Value) || _tabSize.Value != Math.Truncate(_tabSize.Value)
            || !double.IsFinite(_rulerColumn.Value) || _rulerColumn.Value != Math.Truncate(_rulerColumn.Value)
            || !double.IsFinite(_fontSize.Value))
        { _validation.Text = "Enter a whole-number tab/ruler value and a valid font size."; return false; }
        try
        {
            options = new() { TabSize = checked((int)_tabSize.Value), ConvertTabsToSpaces = _spaces.IsChecked == true,
                CanBackspaceOverSpacesToTabStop = _softBackspace.IsChecked == true,
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
