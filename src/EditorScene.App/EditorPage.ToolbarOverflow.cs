using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
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
            if (_toolbarGroups.Count == 0) _toolbarOverflow.Closing += (_, _) => CloseToolbarGroups();
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

    private static IExpandCollapseProvider ToolbarExpansion(MenuFlyoutSubItem group)
        => FrameworkElementAutomationPeer.CreatePeerForElement(group)?.GetPattern(PatternInterface.ExpandCollapse) as IExpandCollapseProvider
            ?? throw new InvalidOperationException("The native overflow item does not expose expand/collapse.");

    private void CancelToolbarHover()
    {
        foreach (var group in _toolbarGroups.Values)
            if (group is ToolbarOverflowGroup native) native.CancelHover();
    }

    private void CloseToolbarGroups(MenuFlyoutSubItem? except = null)
    {
        CancelToolbarHover();
        foreach (var group in _toolbarGroups.Values)
        {
            if (ReferenceEquals(group, except)) continue;
            var expansion = ToolbarExpansion(group);
            // An unopened native item has not created its popup. Never ask the
            // framework to collapse it; only close owners which are truly open.
            if (expansion.ExpandCollapseState == ExpandCollapseState.Expanded) expansion.Collapse();
        }
    }

    private bool PrepareToolbarGroup(MenuFlyoutSubItem group)
    {
        if (_toolbarRootPresenter is not { IsLoaded: true } presenter || !group.IsLoaded || !group.IsEnabled ||
            !IsWithinToolbarElement(group, presenter)) return false;
        // A native presenter's cached child link can be cleared while an older
        // submenu finishes closing. Reconcile all actual owners before opening
        // a peer, through their public native pattern rather than popup flags.
        CloseToolbarGroups(group);
        return group.IsLoaded && ReferenceEquals(presenter, _toolbarRootPresenter);
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
        CancelToolbarHover();
        if (_toolbarRootPresenter is not { } presenter) return;
        _toolbarRootPresenter = null;
        presenter.RemoveHandler(PreviewKeyDownEvent, new KeyEventHandler(OnToolbarRootPreviewKeyDown));
        presenter.Unloaded -= OnToolbarRootUnloaded;
    }

    private void OnToolbarRootUnloaded(object sender, RoutedEventArgs args)
    { if (ReferenceEquals(sender, _toolbarRootPresenter)) DetachToolbarRoot(); }

    private void OnToolbarRootPreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (!ReferenceEquals(sender, _toolbarRootPresenter)) return;
        CancelToolbarHover();
        if (args.Key != VirtualKey.Escape || XamlRoot is not { } root) return;
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
        CancelToolbarHover();
        _toolbarOverflow.Hide();
        QueueDiagnostics();
        return true;
    }

    private sealed class ToolbarOverflowGroup : MenuFlyoutSubItem
    {
        private readonly EditorPage _page;
        private KeyRoutedEventArgs? _observedEscape;
        private DispatcherTimer? _hoverTimer;
        private bool _hoverPending;
        private long _hoverDue;
        internal ToolbarOverflowGroup(EditorPage page)
        {
            _page = page;
            Loaded += (_, _) => _page.AttachToolbarRoot(this);
            AddHandler(KeyDownEvent, new KeyEventHandler(OnRoutedKeyDown), true);
            Unloaded += (_, _) => { CancelHover(); _observedEscape = null; };
        }
        internal void CancelHover()
        {
            _hoverPending = false;
            _hoverTimer?.Stop();
        }
        protected override void OnPointerEntered(PointerRoutedEventArgs args)
        {
            var wasHandled = args.Handled;
            // Retain native parent/hover state, but do not start the framework's
            // uncancelled delayed-open callback. That callback can reopen an
            // unloaded item after Escape, creating orphaned sibling popups.
            args.Handled = true;
            base.OnPointerEntered(args);
            if (wasHandled || !IsLoaded || !IsEnabled || args.Pointer.PointerDeviceType == PointerDeviceType.Touch) return;
            _page.CancelToolbarHover();
            if (_hoverTimer is null)
            {
                _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                _hoverTimer.Tick += OnHoverTick;
            }
            _hoverPending = true; _hoverDue = Environment.TickCount64 + 400;
            _hoverTimer.Start();
            VisualStateManager.GoToState(this, "PointerOver", true);
        }
        protected override void OnPointerExited(PointerRoutedEventArgs args)
        {
            CancelHover();
            base.OnPointerExited(args);
        }
        private void OnHoverTick(object? sender, object args)
        {
            if (!_hoverPending) { _hoverTimer?.Stop(); return; }
            // A queued tick from an earlier hover cannot open a newly scheduled
            // item early, even if the same retained timer has been restarted.
            if (Environment.TickCount64 < _hoverDue) return;
            CancelHover();
            if (_page.PrepareToolbarGroup(this)) ToolbarExpansion(this).Expand();
        }
        protected override void OnPointerReleased(PointerRoutedEventArgs args)
        {
            CancelHover();
            if (_page.PrepareToolbarGroup(this)) base.OnPointerReleased(args);
            else args.Handled = true;
        }
        protected override void OnKeyDown(KeyRoutedEventArgs args)
        {
            if (args.Key == VirtualKey.Escape) _observedEscape = args;
            if (!args.Handled && args.Key is VirtualKey.Enter or VirtualKey.Right or VirtualKey.Space)
                if (!_page.PrepareToolbarGroup(this)) { args.Handled = true; return; }
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
