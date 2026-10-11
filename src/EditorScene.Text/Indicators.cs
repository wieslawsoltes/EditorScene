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
    public IndicatorCollection(EditorDocument document) => _document = document ?? throw new ArgumentNullException(nameof(document));
    public IReadOnlyList<TextIndicator> Items => _items.AsReadOnly();
    public event EventHandler? Changed;
    public TextIndicator Add(int offset, int length, IndicatorKind kind, string? message = null)
    {
        if (offset < 0 || length < 0 || offset > _document.Buffer.TextLength || length > _document.Buffer.TextLength - offset)
            throw new ArgumentOutOfRangeException(nameof(offset));
        var indicator = new TextIndicator(_document.Buffer, offset, length, kind, message);
        _items.Add(indicator); Changed?.Invoke(this, EventArgs.Empty); return indicator;
    }
    public bool Remove(TextIndicator indicator)
    {
        if (!_items.Remove(indicator)) return false;
        Changed?.Invoke(this, EventArgs.Empty); return true;
    }
    public void Clear(IndicatorKind? kind = null)
    {
        _items.RemoveAll(x => kind is null || x.Kind == kind); Changed?.Invoke(this, EventArgs.Empty);
    }
    public void ToggleLine(int line, IndicatorKind kind)
    {
        var existing = _items.FirstOrDefault(x => x.Line == line && x.Kind == kind);
        if (existing is not null) Remove(existing); else Add(_document.Buffer.GetLineByNumber(line).Offset, 0, kind);
    }
    public TextIndicator? Next(int offset, IndicatorKind kind, bool backwards = false)
    {
        var list = _items.Where(x => x.Kind == kind).OrderBy(x => x.Offset).ToArray();
        if (list.Length == 0) return null;
        return backwards ? list.LastOrDefault(x => x.Offset < offset) ?? list[^1] : list.FirstOrDefault(x => x.Offset > offset) ?? list[0];
    }
}
