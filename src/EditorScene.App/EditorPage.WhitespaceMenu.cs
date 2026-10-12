using EditorScene.Controls;
using Microsoft.UI.Xaml.Controls;

namespace EditorScene.App;

public sealed partial class EditorPage
{
    private void AddWhitespaceMenu(MenuBarItem advanced)
    {
        var whitespace = new MenuFlyoutSubItem { Text = "Whitespace", FontSize = 12 };
        advanced.Items.Add(whitespace);
        Add("Tabify Selected Lines", EditorCommand.TabifySelectedLines);
        Add("Untabify Selected Lines", EditorCommand.UntabifySelectedLines);
        whitespace.Items.Add(new MenuFlyoutSeparator());
        Add("Convert Selected Spaces to Tabs", EditorCommand.ConvertSelectedSpacesToTabs);
        Add("Convert Selected Tabs to Spaces", EditorCommand.ConvertSelectedTabsToSpaces);
        whitespace.Items.Add(new MenuFlyoutSeparator());
        Add("Trim Trailing Whitespace", EditorCommand.TrimTrailingWhitespace);
        whitespace.Items.Add(new MenuFlyoutSeparator());
        // Keep legacy whole-document-without-selection actions separately named.
        Add("Convert Leading Tabs to Spaces", EditorCommand.TabsToSpaces);
        Add("Convert Leading Spaces to Tabs", EditorCommand.SpacesToTabs);

        void Add(string label, EditorCommand command)
        {
            var item = new MenuFlyoutItem { Text = label, FontSize = 12 };
            item.Click += (_, _) => _ = GuardAsync(async () => { await _active.ExecuteAsync(command); AddEvent(label); });
            whitespace.Items.Add(item); Register("Command " + label, item);
        }
    }
}
