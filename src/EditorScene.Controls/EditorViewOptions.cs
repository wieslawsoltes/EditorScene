namespace EditorScene.Controls;

/// <summary>View-owned options. The pinned native editor uses one width for both tab stops
/// and indentation units; this model does not pretend to configure those independently.</summary>
public readonly record struct EditorViewOptions
{
    public EditorViewOptions() { }
    public int TabSize { get; init; } = 4;
    public bool ConvertTabsToSpaces { get; init; }
    public bool CanBackspaceOverSpacesToTabStop { get; init; } = true;
    public double FontSize { get; init; } = 13;
    public bool WordWrap { get; init; }
    public bool ShowLineNumbers { get; init; } = true;
    public bool ShowWhitespace { get; init; }
    public bool ShowIndentGuides { get; init; } = true;
    public bool ShowRuler { get; init; }
    public int RulerColumn { get; init; } = 120;
    public bool AutoCompleteBrackets { get; init; } = true;
    public bool AutoShowCompletion { get; init; } = true;
    public void Validate()
    {
        if (TabSize is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(TabSize));
        if (!double.IsFinite(FontSize) || FontSize < 8 || FontSize > 40) throw new ArgumentOutOfRangeException(nameof(FontSize));
        if (RulerColumn is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(RulerColumn));
    }
}
