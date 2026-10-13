using System.Globalization;
using EditorScene.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace EditorScene.Controls;

/// <summary>Native private-draft navigation panel. It never moves a caret or edits a document.
/// The host accepts a validated target and rechecks its revision before navigation.</summary>
public sealed class EditorGoToLineView : UserControl, IDisposable
{
    private DocumentSnapshot? _snapshot;
    private readonly TextBox _input = new()
    {
        Name = "GoToPositionInput", MaxLength = 64, MinWidth = 180, FontSize = 12,
        MinHeight = 28, Padding = new Thickness(6, 3, 6, 3), PlaceholderText = "line or line:character"
    };
    private readonly TextBlock _validation = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, MinHeight = 32 };
    private LineNavigationTarget _target;
    private bool _disposed;
    private string? _unavailable;
    public TextBox PositionInput => _input;
    public bool IsValid { get; private set; }
    public string ValidationMessage => _validation.Text;
    public int LineCount { get; }

    public EditorGoToLineView(DocumentSnapshot snapshot, int currentOffset)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if ((uint)currentOffset > (uint)snapshot.Length) throw new ArgumentOutOfRangeException(nameof(currentOffset));
        _snapshot = snapshot; LineCount = snapshot.LineCount; IsTabStop = false;
        var current = snapshot.GetPosition(currentOffset);
        var panel = new StackPanel { Spacing = 6, MinWidth = 220, MaxWidth = 380 };
        panel.Children.Add(new TextBlock { Text = $"Line number (1–{LineCount:N0})", FontSize = 12, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(_input);
        panel.Children.Add(new TextBlock
        {
            Text = $"Current line {current.Line + 1:N0}, character {current.Character + 1:N0}\nAdd :character for a position within the line.",
            FontSize = 11, TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(_validation); Content = panel;
        AutomationProperties.SetName(_input, "Go to line input");
        AutomationProperties.SetName(_validation, "Go to line validation");
        _input.Text = (current.Line + 1).ToString(CultureInfo.InvariantCulture);
        _input.TextChanged += OnTextChanged; Validate();
    }
    public bool TryGetTarget(out LineNavigationTarget target)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); Validate(); target = _target; return IsValid;
    }
    /// <summary>Invalidate a dialog whose original document/view is no longer current.</summary>
    public void SetUnavailable(string message)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(message); _unavailable = message; Validate();
    }
    private void OnTextChanged(object sender, TextChangedEventArgs args) => Validate();
    private void Validate()
    {
        if (_disposed) return;
        _target = default;
        if (_unavailable is { } unavailable) { IsValid = false; _validation.Text = unavailable; return; }
        IsValid = LineNavigationTarget.TryParse(_snapshot!, _input.Text.AsSpan(), out _target, out var error);
        _validation.Text = error switch
        {
            LineNavigationError.None => string.Empty,
            LineNavigationError.LineOutOfRange => $"Enter a line between 1 and {LineCount:N0}.",
            LineNavigationError.CharacterOutOfRange => "Enter a character within that line, including its end position.",
            LineNavigationError.SplitSurrogate => "That position splits a Unicode character. Choose the position before or after it.",
            _ => "Enter a positive whole line number, optionally followed by :character."
        };
    }
    public new void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _input.TextChanged -= OnTextChanged; _snapshot = null; _target = default; IsValid = false; base.Dispose();
    }
}
