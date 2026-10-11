using System.Globalization;
using UnoEdit.Document;

namespace EditorScene.Text;

public readonly record struct SelectionRange(int Anchor, int Active)
{
    public int Start => Math.Min(Anchor, Active);
    public int Length => Math.Abs(Anchor - Active);
    public int End => Math.Max(Anchor, Active);
}

/// <summary>Disjoint multi-caret edits in one undo group, independently reusable from the UI.</summary>
public sealed class MultiSelection
{
    private readonly EditorDocument _document;
    private SelectionRange[] _ranges = [];
    public MultiSelection(EditorDocument document) => _document = document;
    public IReadOnlyList<SelectionRange> Ranges => Array.AsReadOnly(_ranges);
    public bool IsActive => _ranges.Length > 1;
    public void Set(IEnumerable<SelectionRange> ranges)
    {
        var ordered = ranges.OrderBy(x => x.Start).ThenBy(x => x.End).ToArray();
        var merged = new List<SelectionRange>();
        foreach (var range in ordered)
        {
            if (range.Start < 0 || range.End > _document.Buffer.TextLength) throw new ArgumentOutOfRangeException(nameof(ranges));
            if (merged.Count != 0 && range.Start <= merged[^1].End)
                merged[^1] = new SelectionRange(merged[^1].Start, Math.Max(merged[^1].End, range.End));
            else merged.Add(range);
        }
        _ranges = merged.ToArray();
    }
    public void Clear() => _ranges = [];
    public void Replace(string text, Func<TextEdit, bool>? canEdit = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var revision = _document.Revision;
        var edits = _ranges.Select(r => new TextEdit(r.Start, r.Length, text)).ToArray();
        if (canEdit is not null && edits.Any(e => !canEdit(e))) throw new InvalidOperationException("A selection intersects protected text.");
        var next = new List<SelectionRange>(); var delta = 0;
        foreach (var edit in edits)
        {
            var caret = edit.Offset + delta + edit.NewText.Length;
            next.Add(new SelectionRange(caret, caret)); delta += edit.NewText.Length - edit.Length;
        }
        _document.ApplyEdits(edits, revision); _ranges = next.ToArray();
    }
    public void Delete(bool backwards, Func<TextEdit, bool>? canEdit = null)
    {
        var doc = _document.Buffer;
        Set(_ranges.Select(range =>
        {
            if (range.Length != 0) return range;
            var offset = range.Start;
            if (backwards && offset == 0 || !backwards && offset == doc.TextLength) return range;
            // Read at most the two affected lines, not the whole document.
            var line = doc.GetLineByOffset(offset);
            if (backwards && offset == line.Offset)
            {
                var previous = line.PreviousLine; return new SelectionRange(previous.EndOffset, offset);
            }
            if (!backwards && offset == line.EndOffset)
                return new SelectionRange(offset, offset + line.DelimiterLength);
            var text = doc.GetText(line.Offset, line.Length);
            var starts = StringInfo.ParseCombiningCharacters(text); var local = offset - line.Offset;
            if (backwards)
            {
                var start = starts.LastOrDefault(x => x < local); return new SelectionRange(line.Offset + start, offset);
            }
            var end = starts.FirstOrDefault(x => x > local, text.Length); return new SelectionRange(offset, line.Offset + end);
        }));
        Replace("", canEdit);
    }
}
