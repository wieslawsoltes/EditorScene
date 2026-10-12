using EditorScene.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace EditorScene.App;

public sealed partial class EditorPage
{
    private readonly EditorCommandBindings _commandBindings = new();
    private readonly Dictionary<EditorCommand, string> _commandShortcuts = [];

    private void AddBoundMenuCommand(MenuBarItem menu, string label, EditorCommand command, string shortcut)
    {
        if (shortcut.Length != 0) _commandShortcuts[command] = shortcut;
        var item = Item(menu, label, async () =>
        {
            if (await _commandBindings.TryExecuteAsync(command)) AddEvent(label);
        }, shortcut);
        item.Icon = EditorCommandIcon.Create(command);
        _commandBindings.Bind(item, command, executeOnClick: false);
    }
    private void AddBoundToolCommand(string label, EditorIconKind kind, EditorCommand command)
    {
        var glyph = NewIcon(kind);
        var shortcut = _commandShortcuts.GetValueOrDefault(command) ?? string.Empty;
        var button = new Button
        {
            Content = glyph, Width = 26, Height = 25, MinHeight = 25,
            Padding = new Thickness(4), BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent)
        };
        ToolTipService.SetToolTip(button, shortcut.Length == 0 ? label : label + " (" + shortcut + ")");
        _commandBindings.Bind(button, command, glyph);
        _toolbar.Children.Add(button); Register("Toolbar " + label, button);
        var overflow = new MenuFlyoutItem
        {
            Text = label, FontSize = 12, Icon = EditorCommandIcon.Create(command),
            KeyboardAcceleratorTextOverride = shortcut
        };
        _commandBindings.Bind(overflow, command); AddToolbarOverflow(overflow);
    }
}
