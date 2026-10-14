using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace EditorScene.App;

public sealed partial class EditorPage
{
    private readonly Dictionary<string, MenuFlyoutSubItem> _toolbarGroups = new(StringComparer.Ordinal);
    private MenuFlyoutPresenter? _toolbarRootPresenter;

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

    private static bool IsWithinToolbarElement(object? element, DependencyObject ancestor)
    {
        for (var current = element as DependencyObject; current is not null; current = VisualTreeHelper.GetParent(current))
            if (ReferenceEquals(current, ancestor)) return true;
        return false;
    }

    private void AttachToolbarRoot(MenuFlyoutSubItem group)
    {
        if (!_toolbarOverflow.Items.Contains(group)) return;
        for (var parent = VisualTreeHelper.GetParent(group); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is not MenuFlyoutPresenter presenter) continue;
            if (ReferenceEquals(presenter, _toolbarRootPresenter)) return;
            DetachToolbarRoot();
            _toolbarRootPresenter = presenter;
            // This app's desktop/browser hosts both use Uno Skia. Preview runs
            // before a native item/presenter can consume Escape or restore focus.
            presenter.AddHandler(PreviewKeyDownEvent, new KeyEventHandler(OnToolbarRootPreviewKeyDown), true);
            presenter.Unloaded += OnToolbarRootUnloaded;
            return;
        }
    }

    private void DetachToolbarRoot()
    {
        if (_toolbarRootPresenter is not { } presenter) return;
        _toolbarRootPresenter = null;
        presenter.RemoveHandler(PreviewKeyDownEvent, new KeyEventHandler(OnToolbarRootPreviewKeyDown));
        presenter.Unloaded -= OnToolbarRootUnloaded;
    }

    private void OnToolbarRootUnloaded(object sender, RoutedEventArgs args)
    { if (ReferenceEquals(sender, _toolbarRootPresenter)) DetachToolbarRoot(); }

    private void OnToolbarRootPreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Escape || !ReferenceEquals(sender, _toolbarRootPresenter) || XamlRoot is not { } root) return;
        var presenter = _toolbarRootPresenter!;
        var sourceInRoot = IsWithinToolbarElement(args.OriginalSource, presenter);
        var focused = FocusManager.GetFocusedElement(root);
        if (_diagnostics) Console.WriteLine($"OVERFLOW_PREVIEW handled={args.Handled} sourceInRoot={sourceInRoot} source={args.OriginalSource?.GetType().Name} focus={focused?.GetType().Name}");
        // Uno can reuse OriginalSource while focus changes between preview and
        // bubbling. A child-presenter event must never dismiss this parent too.
        if (!sourceInRoot || !IsWithinToolbarElement(focused, presenter)) return;
        if (focused is MenuFlyoutSubItem group) TryDismissToolbarRoot(group, args, "preview-presenter");
    }

    private bool TryDismissToolbarRoot(MenuFlyoutSubItem group, KeyRoutedEventArgs args, string route)
    {
        if (args.Key != VirtualKey.Escape || !_toolbarOverflow.Items.Contains(group) || XamlRoot is not { } root) return false;
        var focused = ReferenceEquals(FocusManager.GetFocusedElement(root), group);
        var rootOpen = false; var childOpen = false;
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
        {
            if (!popup.IsOpen || popup.Child is not FrameworkElement child) continue;
            var containsOwner = false; var containsChildItem = false;
            foreach (var element in Descendants(child).Prepend(child))
            {
                if (ReferenceEquals(element, group)) containsOwner = true;
                // Check all owned children, not just the focused group's items:
                // keyboard navigation can change owners while another child exits.
                else if (element is MenuFlyoutItemBase item)
                    foreach (var candidate in _toolbarGroups.Values)
                        if (candidate.Items.Contains(item)) { containsChildItem = true; break; }
            }
            if (containsOwner) rootOpen = true;
            else if (containsChildItem) childOpen = true;
        }
        if (_diagnostics) Console.WriteLine($"OVERFLOW_ESCAPE route={route} group={group.Text} handled={args.Handled} focused={focused} root={rootOpen} child={childOpen} source={args.OriginalSource?.GetType().Name}");
        if (!focused || !rootOpen || childOpen) return false;
        args.Handled = true;
        _toolbarOverflow.Hide();
        QueueDiagnostics();
        return true;
    }

    private sealed class ToolbarOverflowGroup : MenuFlyoutSubItem
    {
        private readonly EditorPage _page;
        private KeyRoutedEventArgs? _observedEscape;
        internal ToolbarOverflowGroup(EditorPage page)
        {
            _page = page;
            Loaded += (_, _) => _page.AttachToolbarRoot(this);
            AddHandler(KeyDownEvent, new KeyEventHandler(OnRoutedKeyDown), true);
            Unloaded += (_, _) => _observedEscape = null;
        }
        protected override void OnKeyDown(KeyRoutedEventArgs args)
        {
            if (args.Key == VirtualKey.Escape) _observedEscape = args;
            // Capture BEFORE native processing can close a child. Never reconsider
            // this same event against its post-close state in the fallback below.
            if (!_page.TryDismissToolbarRoot(this, args, "override")) base.OnKeyDown(args);
        }
        private void OnRoutedKeyDown(object sender, KeyRoutedEventArgs args)
        {
            if (args.Key != VirtualKey.Escape) return;
            var observed = ReferenceEquals(args, _observedEscape); _observedEscape = null;
            if (observed) return;
            if (IsWithinToolbarElement(args.OriginalSource, this)) _page.TryDismissToolbarRoot(this, args, "handled-owner");
        }
        protected override void OnKeyUp(KeyRoutedEventArgs args)
        {
            _observedEscape = null;
            base.OnKeyUp(args);
        }
    }
}
