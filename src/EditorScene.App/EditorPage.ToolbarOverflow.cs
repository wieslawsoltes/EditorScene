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
        // Keep the native commands in bounded submenus instead of an off-screen list.
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
            submenu = new ToolbarOverflowGroup(this) { Text = group, FontSize = 12 };
            _toolbarGroups.Add(group, submenu); _toolbarOverflow.Items.Add(submenu);
        }
        submenu.Items.Add(item);
    }

    private bool TryDismissToolbarRoot(MenuFlyoutSubItem group, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Escape || !_toolbarOverflow.Items.Contains(group)
            || XamlRoot is not { } root || !ReferenceEquals(FocusManager.GetFocusedElement(root), group)) return false;

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
            else if (containsChildItem) childOpen = true;
        }
        if (!rootOpen || childOpen) return false;
        args.Handled = true;
        _toolbarOverflow.Hide();
        QueueDiagnostics();
        return true;
    }

    private sealed class ToolbarOverflowGroup(EditorPage page) : MenuFlyoutSubItem
    {
        protected override void OnKeyDown(KeyRoutedEventArgs args)
        {
            // Popup-owned menu items do not reliably receive tunneled PreviewKeyDown.
            // Inspect the native popup state BEFORE the base control can close a child.
            // The first Escape remains native; only an already-closed child's focused
            // root owner dismisses the enclosing flyout. Other keys remain untouched.
            if (!page.TryDismissToolbarRoot(this, args)) base.OnKeyDown(args);
        }
    }
}
