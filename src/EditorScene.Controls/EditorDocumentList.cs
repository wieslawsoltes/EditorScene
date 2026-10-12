using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace EditorScene.Controls;

/// <summary>Data-only document identity and caption. No document, file handle or editor is retained.</summary>
public sealed record EditorDocumentListEntry(Guid Id, string Title, bool IsModified = false);

public sealed class EditorDocumentInvokedEventArgs(EditorDocumentListEntry document) : EventArgs
{
    public EditorDocumentListEntry Document { get; } = document;
}

/// <summary>A caller-owned native document menu. SetDocuments owns the Items collection;
/// callers own the documents and decide how to activate them in DocumentInvoked.
/// Existing native items are retained by identity across additions, removals and renames.</summary>
public sealed class EditorDocumentList : MenuFlyout, IDisposable
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private Dictionary<Guid, Registration> _entries = [];
    private Guid? _selected;
    private bool _disposed;
    public int DocumentCount => _entries.Count;
    public long ItemBuildCount { get; private set; }
    public event EventHandler<EditorDocumentInvokedEventArgs>? DocumentInvoked;

    public Guid? SelectedDocumentId
    {
        get => _selected;
        set
        {
            Verify();
            if (value is { } id && !_entries.ContainsKey(id)) throw new ArgumentException("The selected document is not in this menu.", nameof(value));
            if (_selected == value) return;
            var old = _selected; _selected = value;
            if (old is { } previous && _entries.TryGetValue(previous, out var before)) before.Item.IsChecked = false;
            if (!_disposed && _selected == value && value is { } current && _entries.TryGetValue(current, out var after))
                after.Item.IsChecked = true;
        }
    }

    /// <summary>Validate the entire input before changing native menu state. IDs must be unique
    /// and nonempty. Membership/order changes reuse controls rather than recreating them.</summary>
    public void SetDocuments(IEnumerable<EditorDocumentListEntry> documents)
    {
        Verify(); ArgumentNullException.ThrowIfNull(documents);
        var snapshot = documents.ToArray(); Verify(); var ids = new HashSet<Guid>();
        foreach (var document in snapshot)
        {
            ArgumentNullException.ThrowIfNull(document);
            if (document.Id == Guid.Empty || !ids.Add(document.Id)) throw new ArgumentException("Document IDs must be nonempty and unique.", nameof(documents));
            if (string.IsNullOrWhiteSpace(document.Title)) throw new ArgumentException("Document titles must not be blank.", nameof(documents));
        }
        var next = new Dictionary<Guid, Registration>(snapshot.Length);
        foreach (var document in snapshot)
        {
            if (!_entries.TryGetValue(document.Id, out var entry)) { entry = new(this, document); ItemBuildCount++; }
            next.Add(document.Id, entry);
        }
        foreach (var (id, entry) in _entries) if (!next.ContainsKey(id)) entry.Detach();
        _entries = next;
        if (_selected is { } selected && !next.ContainsKey(selected)) _selected = null;
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (Items[i] is not ToggleMenuFlyoutItem item || item.Tag is not Guid id || !next.TryGetValue(id, out var entry) || !ReferenceEquals(item, entry.Item)) Items.RemoveAt(i);
            if (!IsCurrent(next)) return;
        }
        for (var i = 0; i < snapshot.Length; i++)
        {
            var entry = next[snapshot[i].Id]; entry.Update(snapshot[i]);
            if (!IsCurrent(next)) return;
            var isSelected = _selected == snapshot[i].Id;
            if (entry.Item.IsChecked != isSelected) entry.Item.IsChecked = isSelected;
            if (!IsCurrent(next)) return;
            if (i < Items.Count && ReferenceEquals(Items[i], entry.Item)) continue;
            var oldIndex = Items.IndexOf(entry.Item); if (oldIndex >= 0) Items.RemoveAt(oldIndex);
            if (!IsCurrent(next)) return;
            Items.Insert(i, entry.Item);
            if (!IsCurrent(next)) return;
        }
        if (snapshot.Length == 0) Hide();
    }
    private bool IsCurrent(Dictionary<Guid, Registration> entries) => !_disposed && ReferenceEquals(entries, _entries);

    /// <summary>Update only one caption/dirty marker. False means the identity is no longer present.</summary>
    public bool UpdateDocument(EditorDocumentListEntry document)
    {
        Verify(); ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(document.Title)) throw new ArgumentException("Document titles must not be blank.", nameof(document));
        if (!_entries.TryGetValue(document.Id, out var entry)) return false;
        entry.Update(document); return true;
    }

    /// <summary>Request activation of a currently listed identity. The host sets SelectedDocumentId
    /// after accepting the request; a rejected request cannot silently change the check mark.</summary>
    public bool TryInvoke(Guid id)
    {
        Verify();
        if (!_entries.TryGetValue(id, out var entry)) return false;
        try { DocumentInvoked?.Invoke(this, new(entry.Document)); }
        finally
        {
            if (!_disposed && _entries.TryGetValue(id, out var current)) current.Item.IsChecked = _selected == id;
        }
        return true;
    }
    private void Verify()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("The document menu belongs to its creating UI thread.");
    }
    public void Dispose()
    {
        if (_disposed) return;
        Verify(); _disposed = true; DocumentInvoked = null; Hide();
        foreach (var entry in _entries.Values) entry.Detach();
        _entries.Clear(); Items.Clear(); _selected = null;
    }
    private sealed class Registration
    {
        private EditorDocumentList? _owner;
        internal EditorDocumentListEntry Document { get; private set; }
        internal ToggleMenuFlyoutItem Item { get; }
        internal Registration(EditorDocumentList owner, EditorDocumentListEntry document)
        {
            _owner = owner; Document = document;
            Item = new() { FontSize = 12, Tag = document.Id };
            Item.Click += OnClick; UpdateCaption();
        }
        internal void Update(EditorDocumentListEntry document)
        {
            if (Document == document) return;
            Document = document; UpdateCaption();
        }
        private void UpdateCaption()
        {
            var document = Document;
            Item.Text = document.Title + (document.IsModified ? " *" : "");
            if (_owner is null || _owner._disposed || !ReferenceEquals(Document, document)) return;
            AutomationProperties.SetName(Item, "Activate document " + document.Title);
            if (_owner is null || _owner._disposed || !ReferenceEquals(Document, document)) return;
            ToolTipService.SetToolTip(Item, document.Title);
        }
        private void OnClick(object sender, RoutedEventArgs args)
        {
            var owner = _owner;
            if (owner is not null && !owner._disposed) owner.TryInvoke(Document.Id);
        }
        internal void Detach() { Item.Click -= OnClick; _owner = null; }
    }
}
