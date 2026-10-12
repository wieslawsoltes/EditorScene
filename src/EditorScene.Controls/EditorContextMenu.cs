using EditorScene.LanguageServices;
using EditorScene.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;

namespace EditorScene.Controls;

/// <summary>Allows a host to append native items or cancel an editor menu request.
/// Menu and editor are not transferred to the event subscriber.</summary>
public sealed class EditorContextMenuEventArgs(MenuFlyout menu, int? targetOffset) : EventArgs
{
    public MenuFlyout Menu { get; } = menu;
    public int? TargetOffset { get; } = targetOffset;
    public bool IsKeyboardRequest => TargetOffset is null;
    public bool Cancel { get; set; }
}

/// <summary>Reusable, lazily built native command menu bound to one caller-owned editor.
/// One native menu is reused; only open menus subscribe to editor state changes.</summary>
public sealed class EditorContextMenu : IDisposable
{
    private readonly SyntaxEditor _editor;
    private readonly Dictionary<EditorCommand, MenuFlyoutItem> _items = [];
    private MenuFlyout? _menu;
    private Lease? _lease;
    private bool _disposed, _opening, _subscribed;
    private long _generation;
    public bool IsOpen { get; private set; }
    public long BuildCount { get; private set; }
    public EditorContextMenu(SyntaxEditor editor) => _editor = editor ?? throw new ArgumentNullException(nameof(editor));
    public event EventHandler<EditorContextMenuEventArgs>? Requested;
    public MenuFlyout Flyout { get { ObjectDisposedException.ThrowIf(_disposed, this); return _menu ??= Build(); } }
    public MenuFlyoutItem? GetCommandItem(EditorCommand command) { _ = Flyout; return _items.GetValueOrDefault(command); }

    /// <summary>Open at a view-relative pointer position, or the caret for a keyboard request.
    /// Pointer requests preserve a containing selection; optional relocation affects only this view.</summary>
    public bool Open(Point? position = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_opening || _editor.IsDisposed || !_editor.IsLoaded || _editor.IsOptionsDialogOpen || !_editor.IsDefaultContextMenuEnabled
            || _editor.MacroRecording.State != EditorScene.Text.Macros.MacroRecordingState.Idle) return false;
        var view = _editor.NativeEditor.TextArea.TextView;
        _opening = true; _generation++;
        try
        {
            var before = Capture();
            if (position is { } point && (!double.IsFinite(point.X) || !double.IsFinite(point.Y))) throw new ArgumentOutOfRangeException(nameof(position));
            int? offset = position is { } p ? view.GetOffsetFromPoint(p) : null;
            if (offset is { } target && _editor.CanMoveCaretForSecondaryPointerButton
                && !(_editor.SelectionLength > 0 && target >= _editor.SelectionStart && target < _editor.SelectionStart + _editor.SelectionLength))
            {
                _editor.Select(target, 0);
                if (_disposed || _editor.IsDisposed || !ReferenceEquals(_editor.Document, before.Document)
                    || _editor.Document.Revision != before.Revision || !ReferenceEquals(_editor.SyntaxLanguage, before.Language)
                    || _editor.CaretOffset != target || _editor.SelectionLength != 0) return false;
            }
            var menu = Flyout;
            _lease = Capture();
            _editor.FocusEditor();
            if (!Current()) return false;
            var args = new EditorContextMenuEventArgs(menu, offset);
            Requested?.Invoke(this, args);
            if (args.Cancel || !Current()) { Close(); return false; }
            Refresh();
            if (!Current()) return false;
            menu.XamlRoot = _editor.XamlRoot;
            var rectangle = view.Viewport.GetCaretRectangle(_editor.CaretOffset);
            var origin = position ?? new Point(rectangle.Left, rectangle.Bottom);
            origin = new Point(Math.Clamp(origin.X, 0, Math.Max(0, view.ActualWidth - 1)), Math.Clamp(origin.Y, 0, Math.Max(0, view.ActualHeight - 1)));
            if (!_subscribed) { _editor.StateChanged += OnStateChanged; _editor.Unloaded += OnUnloaded; _subscribed = true; }
            menu.ShowAt(view, new FlyoutShowOptions { Position = origin });
            return true;
        }
        catch { Close(); throw; }
        finally { _opening = false; }
    }
    private Lease Capture()
    {
        var session = _editor.NativeEditor.TextArea.Session;
        return new(_editor.Document, _editor.Document.Revision, _editor.SyntaxLanguage, _editor.CaretOffset,
            session.AnchorOffset, session.SelectionEndOffset, _editor.Selections.Ranges);
    }
    private bool Current() => !_disposed && !_editor.IsDisposed && _editor.IsDefaultContextMenuEnabled && !_editor.IsOptionsDialogOpen
        && _editor.MacroRecording.State == EditorScene.Text.Macros.MacroRecordingState.Idle && _lease == Capture();
    private void Refresh()
    {
        foreach (var (command, item) in _items)
        {
            var enabled = _editor.GetCommandState(command).IsEnabled;
            if (item.IsEnabled != enabled) item.IsEnabled = enabled;
        }
    }
    private void OnStateChanged(object? sender, EventArgs args) { if (!Current()) Close(); else Refresh(); }
    private void OnUnloaded(object sender, RoutedEventArgs args) => Close();
    private void OnOpened(object? sender, object args) { IsOpen = true; if (!Current()) Close(); }
    private void OnClosed(object? sender, object args)
    {
        IsOpen = false; Detach();
        // MenuFlyout may close before raising its item's Click. Keep the lease
        // through this input dispatch only, then release the old document.
        var generation = _generation;
        if (!_editor.DispatcherQueue.TryEnqueue(() => { if (_generation == generation && !IsOpen) _lease = null; })) _lease = null;
    }
    private void Detach()
    {
        if (!_subscribed) return;
        _subscribed = false; _editor.StateChanged -= OnStateChanged; _editor.Unloaded -= OnUnloaded;
    }
    public void Close() { _generation++; _lease = null; _menu?.Hide(); IsOpen = false; Detach(); }
    private async void OnClick(object sender, RoutedEventArgs args)
    {
        if (sender is not MenuFlyoutItem { Tag: EditorCommand command } || !Current() || !_editor.GetCommandState(command).IsEnabled) return;
        // ExecuteAsync contains the real protection checks and error publication.
        await _editor.ExecuteAsync(command);
    }
    private MenuFlyout Build()
    {
        BuildCount++;
        var menu = new MenuFlyout();
        menu.Opened += OnOpened; menu.Closed += OnClosed;
        Add(menu.Items, "Undo", EditorCommand.Undo, "Ctrl+Z"); Add(menu.Items, "Redo", EditorCommand.Redo, "Ctrl+Y");
        menu.Items.Add(new MenuFlyoutSeparator());
        Add(menu.Items, "Cut", EditorCommand.Cut, "Ctrl+X"); Add(menu.Items, "Copy", EditorCommand.Copy, "Ctrl+C");
        Add(menu.Items, "Paste", EditorCommand.Paste, "Ctrl+V"); Add(menu.Items, "Delete", EditorCommand.Delete, "Del");
        menu.Items.Add(new MenuFlyoutSeparator()); Add(menu.Items, "Select All", EditorCommand.SelectAll, "Ctrl+A");
        menu.Items.Add(new MenuFlyoutSeparator());
        var editing = Group(menu, "Advanced");
        Add(editing.Items, "Increase Line Indent", EditorCommand.Indent, "Tab"); Add(editing.Items, "Decrease Line Indent", EditorCommand.Outdent, "Shift+Tab");
        Add(editing.Items, "Comment Lines", EditorCommand.Comment, "Ctrl+K, Ctrl+C"); Add(editing.Items, "Uncomment Lines", EditorCommand.Uncomment, "Ctrl+K, Ctrl+U");
        Add(editing.Items, "Format Document", EditorCommand.FormatDocument, "Ctrl+K, Ctrl+D");
        var prompt = Group(menu, "IntelliPrompt");
        Add(prompt.Items, "Complete Word", EditorCommand.CompleteWord, "Ctrl+Space"); Add(prompt.Items, "Quick Info", EditorCommand.QuickInfo);
        Add(prompt.Items, "Signature Help", EditorCommand.SignatureHelp, "Ctrl+Shift+Space");
        var outlining = Group(menu, "Outlining");
        Add(outlining.Items, "Toggle Current Region", EditorCommand.ToggleFold); Add(outlining.Items, "Collapse All", EditorCommand.CollapseAll); Add(outlining.Items, "Expand All", EditorCommand.ExpandAll);
        return menu;
    }
    private static MenuFlyoutSubItem Group(MenuFlyout menu, string title)
    { var group = new MenuFlyoutSubItem { Text = title, FontSize = 12 }; menu.Items.Add(group); return group; }
    private void Add(IList<MenuFlyoutItemBase> items, string text, EditorCommand command, string shortcut = "")
    {
        var item = new MenuFlyoutItem { Text = text, Tag = command, FontSize = 12, KeyboardAcceleratorTextOverride = shortcut };
        AutomationProperties.SetName(item, "Editor context " + text); item.Click += OnClick; _items.Add(command, item); items.Add(item);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Close(); Requested = null; _lease = null;
        if (_menu is not null) { _menu.Opened -= OnOpened; _menu.Closed -= OnClosed; }
        foreach (var item in _items.Values) item.Click -= OnClick;
        _items.Clear(); _menu?.Items.Clear(); _menu = null;
    }
    private readonly record struct Lease(EditorDocument Document, long Revision, ISyntaxLanguage Language, int Caret, int Anchor, int Active,
        IReadOnlyList<SelectionRange> Ranges);
}
