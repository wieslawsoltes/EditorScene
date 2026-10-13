using EditorScene.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace EditorScene.Controls;

public sealed class EditorDocumentCloseRequestedEventArgs(Guid documentId, DocumentCloseScope scope) : EventArgs
{
    public Guid DocumentId { get; } = documentId;
    public DocumentCloseScope Scope { get; } = scope;
}

/// <summary>One reusable native tab context menu. It owns four menu items, not the
/// host's documents. Configure with the target's current position before showing;
/// hosts resolve the stable ID again and apply their own save/cancel policy.</summary>
public sealed class EditorDocumentTabMenu : MenuFlyout, IDisposable
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly Dictionary<DocumentCloseScope, MenuFlyoutItem> _commands = [];
    private Guid? _target;
    private int _index, _count;
    private bool _enabled, _disposed;
    private long _generation;
    public Guid? TargetDocumentId => _target;
    public int ItemBuildCount { get; private set; }
    public event EventHandler<EditorDocumentCloseRequestedEventArgs>? CloseRequested;

    public EditorDocumentTabMenu()
    {
        Add(DocumentCloseScope.Current, "Close", "Ctrl+F4");
        Add(DocumentCloseScope.Others, "Close All But This");
        Add(DocumentCloseScope.All, "Close All");
        Add(DocumentCloseScope.ToRight, "Close Tabs to the Right");
    }
    private void Add(DocumentCloseScope scope, string text, string shortcut = "")
    {
        var item = new MenuFlyoutItem { Text = text, Tag = scope, FontSize = 12, IsEnabled = false, KeyboardAcceleratorTextOverride = shortcut };
        AutomationProperties.SetName(item, text);
        item.Click += OnClick; _commands.Add(scope, item); Items.Add(item); ItemBuildCount++;
    }
    public void Configure(Guid documentId, int index, int documentCount, bool enabled = true)
    {
        Verify();
        if (documentId == Guid.Empty) throw new ArgumentException("A nonempty document ID is required.", nameof(documentId));
        if (documentCount <= 0) throw new ArgumentOutOfRangeException(nameof(documentCount));
        if ((uint)index >= (uint)documentCount) throw new ArgumentOutOfRangeException(nameof(index));
        _target = documentId; _index = index; _count = documentCount; _enabled = enabled;
        Refresh(++_generation);
    }
    public void ClearContext()
    {
        Verify(); _target = null; _enabled = false; _count = 0; _index = 0;
        var generation = ++_generation; Refresh(generation);
        if (!_disposed && generation == _generation) Hide();
    }
    public bool CanInvoke(DocumentCloseScope scope)
    {
        Verify();
        return _target is not null && _enabled && (scope switch
        {
            DocumentCloseScope.Current or DocumentCloseScope.All => true,
            DocumentCloseScope.Others => _count > 1,
            DocumentCloseScope.ToRight => _index < _count - 1,
            _ => false
        });
    }
    public bool TryInvoke(DocumentCloseScope scope)
    {
        if (!CanInvoke(scope)) return false;
        var request = new EditorDocumentCloseRequestedEventArgs(_target!.Value, scope);
        CloseRequested?.Invoke(this, request);
        return true;
    }
    private void Refresh(long generation)
    {
        // Native property observers may synchronously reconfigure or dispose.
        foreach (var (scope, item) in _commands)
        {
            if (_disposed || generation != _generation) return;
            var enabled = CanInvoke(scope); if (item.IsEnabled != enabled) item.IsEnabled = enabled;
        }
    }
    private void OnClick(object sender, RoutedEventArgs args)
    { if (!_disposed && sender is MenuFlyoutItem { Tag: DocumentCloseScope scope }) TryInvoke(scope); }
    private void Verify()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("The tab menu belongs to its creating UI thread.");
    }
    public void Dispose()
    {
        if (_disposed) return;
        Verify(); _disposed = true; _generation++; _target = null; CloseRequested = null;
        foreach (var item in _commands.Values) item.Click -= OnClick;
        // Do not clear the private command dictionary while a native callback may
        // have an outer Refresh enumerator. The four detached controls die with this menu.
        Items.Clear(); Hide();
    }
}
