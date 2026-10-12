using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace EditorScene.Controls;

public sealed partial class SyntaxEditor
{
    private EditorContextMenu? _contextMenu;
    private bool _defaultContextMenu = true, _moveCaretForSecondaryPointer = true;
    public bool IsDefaultContextMenuEnabled
    {
        get => _defaultContextMenu;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this); Document.VerifyAccess();
            if (_defaultContextMenu == value) return; _defaultContextMenu = value;
            if (!value) _contextMenu?.Close();
            if (!_disposed) StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public bool CanMoveCaretForSecondaryPointerButton
    {
        get => _moveCaretForSecondaryPointer;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this); Document.VerifyAccess();
            if (_moveCaretForSecondaryPointer == value) return; _moveCaretForSecondaryPointer = value;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public EditorContextMenu EditorContextMenu { get { ObjectDisposedException.ThrowIf(_disposed, this); return _contextMenu ??= new EditorContextMenu(this); } }
    public bool IsEditorContextMenuOpen => _contextMenu?.IsOpen == true;
    public long ContextMenuBuildCount => _contextMenu?.BuildCount ?? 0;
    public bool ShowEditorContextMenu(Point? position = null) => EditorContextMenu.Open(position);
    private void InitializeContextMenu()
    {
        // Replace the pinned editor's unconditional six-command menu; never leave
        // it as an ancestor fallback when our default menu has been disabled.
        _editor.TextArea.ContextFlyout = null;
        _editor.TextArea.TextView.ContextRequested += OnEditorContextRequested;
    }
    private void OnEditorContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (_disposed || !IsDefaultContextMenuEnabled) return;
        args.Handled = true;
        try { ShowEditorContextMenu(args.TryGetPosition(_editor.TextArea.TextView, out var position) ? position : null); }
        catch (Exception error) { if (!_disposed) ReportError(error); }
    }
    private void DisposeContextMenu()
    { _editor.TextArea.TextView.ContextRequested -= OnEditorContextRequested; _contextMenu?.Dispose(); _contextMenu = null; }
}
