using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace EditorScene.App;

public sealed partial class EditorPage
{
    private readonly Dictionary<string, MenuFlyoutSubItem> _toolbarGroups = new(StringComparer.Ordinal);

    private void AddToolbarOverflow(MenuFlyoutItem item)
    {
        // A single list of all toolbar buttons exceeds normal window height.
        // Preserve the actual actions but group them into native submenus: seven
        // top-level choices, at most six commands in any submenu. No off-screen
        // "overflow" commands, custom input simulation or duplicated execution.
        var group = item.Text switch
        {
            "New" or "Open" or "Save" or "Save All" => "File",
            "Cut" or "Copy" or "Paste" or "Delete" or "Undo" or "Redo" => "Edit",
            "Complete word" or "Signature help" or "Quick info" or "Insert snippet" => "IntelliPrompt",
            "Outdent" or "Indent" or "Comment lines" or "Uncomment lines" or "Format document" => "Formatting",
            "Find" or "Replace" => "Search",
            "Run last macro" or "Record or stop macro" or "Pause or resume recording" => "Macros",
            "Toggle bookmark" or "Previous bookmark" or "Next bookmark" or "Collapse all" or "Expand all" or "Split editor" => "Navigation",
            _ => throw new InvalidOperationException("A toolbar command needs an explicit overflow group: " + item.Text)
        };
        if (!_toolbarGroups.TryGetValue(group, out var submenu))
        {
            submenu = new MenuFlyoutSubItem { Text = group, FontSize = 12 };
            // Native popup processing may mark Escape handled before it tunnels
            // to its focused group. Still observe it, but only dismiss our root
            // when no separate owned child popup exists at the start of the key.
            submenu.AddHandler(UIElement.PreviewKeyDownEvent,
                new KeyEventHandler(OnToolbarGroupPreviewKeyDown), handledEventsToo: true);
            _toolbarGroups.Add(group, submenu); _toolbarOverflow.Items.Add(submenu);
        }
        submenu.Items.Add(item);
    }

    private void OnToolbarGroupPreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Escape || sender is not MenuFlyoutSubItem group
            || !_toolbarOverflow.Items.Contains(group) || XamlRoot is not { } root
            || !ReferenceEquals(FocusManager.GetFocusedElement(root), group)) return;

        var rootOpen = false; var childOpen = false;
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
        {
            if (!popup.IsOpen || popup.Child is not FrameworkElement child) continue;
            var containsOwner = false; var containsChildItem = false;
            foreach (var element in Descendants(child).Prepend(child))
            {
                if (ReferenceEquals(element, group)) containsOwner = true;
                else if (element is MenuFlyoutItemBase item && group.Items.Contains(item)) containsChildItem = true;
            }
            if (containsOwner) rootOpen = true;
            // A retained child in the root's visual/logical subtree is not proof
            // of an open submenu. Require a distinct, actually open popup.
            else if (containsChildItem) childOpen = true;
        }
        if (!rootOpen || childOpen) return;
        args.Handled = true;
        _toolbarOverflow.Hide();
        QueueDiagnostics();
    }
}
