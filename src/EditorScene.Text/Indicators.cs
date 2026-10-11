using System.Collections.ObjectModel;
using UnoEdit.Document;

namespace EditorScene.Text;

public enum IndicatorKind { Bookmark, Breakpoint, ExecutionPoint, Error, Warning, Information }

/// <summary>Text-anchored indicator; unrelated insertions do not invalidate its location.</summary>
public sealed class TextIndicator
{
    internal TextIndicator(TextDocument document, int offset, int length, IndicatorKind kind, string? message)
    {
        Start = document.CreateAnchor(offset); Start.MovementType = AnchorMovementType.AfterInsertion;
        End = document.CreateAnchor(checked(offset + length)); End.MovementType = AnchorMovementType.BeforeInsertion;
        Start.SurviveDeletion = End.SurviveDeletion = true;
        Kind = kind; Message = message;
    }
    private TextAnchor Start { get; }
    private TextAnchor End { get; }
    public IndicatorKind Kind { get; }
    public string? Message { get; }
    public int Offset => Start.Offset;
    public int Length => Math.Max(0, End.Offset - Start.Offset);
    public int Line => Start.Line;
}

public sealed class IndicatorCollection
{
    private readonly EditorDocument _document;
    private readonly List<TextIndicator> _items = [];
    private readonly ReadOnlyCollection<TextIndicator> _view;
    private TextIndicator[]? _sorted;
    private long _sortedRevision = -1;
    public IndicatorCollection(EditorDocument document)
    { _document = document ?? throw new ArgumentNullException(nameof(document)); _view = _items.AsReadOnly(); }
    public IReadOnlyList<TextIndicator> Items => _view;
    public event EventHandler? Changed;
    public TextIndicator Add(int offset, int length, IndicatorKind kind, string? message = null)
    {
        _document.VerifyAccess();
        if (offset < 0 || length < 0 || offset > _document.Buffer.TextLength || length > _document.Buffer.TextLength - offset) throw new ArgumentOutOfRangeException(nameof(offset));
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        var indicator = new TextIndicator(_document.Buffer, offset, length, kind, message);
        _items.Add(indicator); Invalidate(); return indicator;
    }
    public bool Remove(TextIndicator indicator)
    {
        _document.VerifyAccess(); ArgumentNullException.ThrowIfNull(indicator);
        if (!_items.Remove(indicator)) return false;
        Invalidate(); return true;
    }
    public void Clear(IndicatorKind? kind = null)
    {
        _document.VerifyAccess();
        if (_items.RemoveAll(x => kind is null || x.Kind == kind) > 0) Invalidate();
    }
    private void Invalidate() { _sorted = null; Changed?.Invoke(this, EventArgs.Empty); }
    private TextIndicator[] Sorted()
    {
        _document.VerifyAccess();
        if (_sorted is null || _sortedRevision != _document.Revision)
        { _sorted = _items.OrderBy(x => x.Offset).ToArray(); _sortedRevision = _document.Revision; }
        return _sorted;
    }
    /// <summary>Enumerate indicator anchors inside inclusive endpoints. Cached ordering is rebuilt only after a document/collection change.</summary>
    public IEnumerable<TextIndicator> GetInRange(int offset, int length)
    {
        _document.VerifyAccess();
        if (offset < 0 || length < 0 || offset > _document.Buffer.TextLength || length > _document.Buffer.TextLength - offset) throw new ArgumentOutOfRangeException(nameof(offset));
        var items = Sorted(); var low = 0; var high = items.Length;
        while (low < high) { var middle = low + (high - low) / 2; if (items[middle].Offset < offset) low = middle + 1; else high = middle; }
        return Enumerate(items, low, offset + length);
    }
    private static IEnumerable<TextIndicator> Enumerate(TextIndicator[] items, int start, int end)
    { for (var i = start; i < items.Length && items[i].Offset <= end; i++) yield return items[i]; }
    public void ToggleLine(int line, IndicatorKind kind)
    {
        _document.VerifyAccess();
        var documentLine = _document.Buffer.GetLineByNumber(line);
        var existing = GetInRange(documentLine.Offset, documentLine.Length).FirstOrDefault(x => x.Kind == kind);
        if (existing is not null) Remove(existing); else Add(documentLine.Offset, 0, kind);
    }
    public TextIndicator? Next(int offset, IndicatorKind kind, bool backwards = false)
    {
        var items = Sorted(); TextIndicator? first = null, last = null, before = null, after = null;
        foreach (var item in items)
        {
            if (item.Kind != kind) continue; first ??= item; last = item;
            if (item.Offset < offset) before = item; else if (item.Offset > offset) after ??= item;
        }
        return backwards ? before ?? last : after ?? first;
    }
}
