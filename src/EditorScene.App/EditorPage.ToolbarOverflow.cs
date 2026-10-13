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
            submenu.PreviewKeyDown += OnToolbarGroupPreviewKeyDown;
            _toolbarGroups.Add(group, submenu); _toolbarOverflow.Items.Add(submenu);
        }
        submenu.Items.Add(item);
    }

    private void OnToolbarGroupPreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Handled || args.Key != VirtualKey.Escape || sender is not MenuFlyoutSubItem group
            || !group.IsLoaded || group.XamlRoot is not { } root
            || !ReferenceEquals(FocusManager.GetFocusedElement(root), group)) return;

        // Uno can leave the root menu open when Escape reaches the focused owner
        // of an already closed submenu. Handle only that root-level case. The
        // first Escape belongs to the native child presenter, even if its owner
        // still has focus. Inspect actual open popups, not delayed diagnostics or
        // cached menu state; never hide a parent while an owned child is open.
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
        {
            if (!popup.IsOpen || popup.Child is not FrameworkElement child) continue;
            foreach (var element in Descendants(child).Prepend(child))
                if (element is MenuFlyoutItemBase item && group.Items.Contains(item)) return;
        }
        args.Handled = true;
        _toolbarOverflow.Hide();
        QueueDiagnostics();
    }
}
