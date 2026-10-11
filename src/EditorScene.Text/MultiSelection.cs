using System.Collections.ObjectModel;
using System.Globalization;

namespace EditorScene.Text;

public readonly record struct SelectionRange(int Anchor, int Active)
{
    public int Start => Math.Min(Anchor, Active);
    public int Length => checked(End - Start);
    public int End => Math.Max(Anchor, Active);
}

/// <summary>Disjoint multi-caret edits in one undo group. The last supplied range is the primary selection.</summary>
public sealed class MultiSelection
{
    private readonly EditorDocument _document;
    private SelectionRange[] _ranges = [];
    private ReadOnlyCollection<SelectionRange> _view = Array.AsReadOnly(Array.Empty<SelectionRange>());
    private long _generation;
    public MultiSelection(EditorDocument document) => _document = document ?? throw new ArgumentNullException(nameof(document));
    public IReadOnlyList<SelectionRange> Ranges => _view;
    public int PrimaryIndex { get; private set; } = -1;
    public SelectionRange? PrimaryRange => PrimaryIndex >= 0 ? _ranges[PrimaryIndex] : null;
    public bool IsActive => _ranges.Length > 1;
    public void Set(IEnumerable<SelectionRange> ranges)
    {
        _document.VerifyAccess(); ArgumentNullException.ThrowIfNull(ranges);
        var revision = _document.Revision; var generation = _generation; var source = ranges.ToArray();
        EnsureUnchanged(revision, generation);
        var normalized = Normalize(source, source.Length - 1, _document.Buffer.TextLength);
        Publish(normalized.Ranges, normalized.Primary);
    }
    public void Clear()
    {
        _document.VerifyAccess();
        if (_ranges.Length == 0) return;
        Publish([], -1);
    }
    public void Replace(string text, Func<TextEdit, bool>? canEdit = null)
    {
        _document.VerifyAccess(); ArgumentNullException.ThrowIfNull(text);
        ReplaceCore(_ranges, PrimaryIndex, text, canEdit, _document.Revision, _generation);
    }
    private void ReplaceCore(SelectionRange[] ranges, int primary, string text, Func<TextEdit, bool>? canEdit, long revision, long generation)
    {
        var edits = ranges.Select(r => new TextEdit(r.Start, r.Length, text)).Where(e => e.Length != 0 || e.NewText.Length != 0).ToArray();
        if (canEdit is not null)
            foreach (var edit in edits)
            {
                var allowed = canEdit(edit);
                EnsureUnchanged(revision, generation);
                if (!allowed) throw new InvalidOperationException("A selection intersects protected text.");
            }
        var next = new SelectionRange[ranges.Length]; var delta = 0;
        for (var i = 0; i < ranges.Length; i++)
        {
            var caret = checked(ranges[i].Start + delta + text.Length);
            next[i] = new(caret, caret); delta = checked(delta + text.Length - ranges[i].Length);
        }
        var nextLength = checked(_document.Buffer.TextLength + delta);
        var normalized = Normalize(next, primary, nextLength);
        EnsureUnchanged(revision, generation);
        _document.ApplyEdits(edits, revision);
        Publish(normalized.Ranges, normalized.Primary);
    }
    public void Delete(bool backwards, Func<TextEdit, bool>? canEdit = null)
    {
        _document.VerifyAccess();
        var revision = _document.Revision; var generation = _generation;
        var doc = _document.Buffer;
        // Several carets commonly share a line. Segment its graphemes once per
        // operation, not once per caret. Delimiters are handled separately.
        var lines = new Dictionary<int, (string Text, int[] Starts)>();
        var candidates = new SelectionRange[_ranges.Length];
        for (var i = 0; i < _ranges.Length; i++)
        {
            var range = _ranges[i]; var offset = range.Start;
            if (range.Length != 0 || (backwards && offset == 0) || (!backwards && offset == doc.TextLength)) { candidates[i] = range; continue; }
            var line = doc.GetLineByOffset(offset); var local = offset - line.Offset;
            if (backwards && local == 0)
            { candidates[i] = new(line.PreviousLine.EndOffset, offset); continue; }
            if (local > line.Length || (!backwards && local == line.Length && line.DelimiterLength > 0))
            { candidates[i] = new(line.EndOffset, line.Offset + line.TotalLength); continue; }
            if (!lines.TryGetValue(line.LineNumber, out var data))
            {
                var text = doc.GetText(line.Offset, line.Length);
                data = (text, StringInfo.ParseCombiningCharacters(text)); lines.Add(line.LineNumber, data);
            }
            var index = Array.BinarySearch(data.Starts, local);
            int start, end;
            if (index >= 0)
            {
                start = backwards ? data.Starts[Math.Max(0, index - 1)] : local;
                end = backwards ? local : index + 1 < data.Starts.Length ? data.Starts[index + 1] : data.Text.Length;
            }
            else
            {
                var next = ~index;
                start = next == 0 ? 0 : data.Starts[next - 1];
                end = next < data.Starts.Length ? data.Starts[next] : data.Text.Length;
            }
            candidates[i] = new(line.Offset + start, line.Offset + end);
        }
        // Do not publish expanded deletion ranges before the read-only preflight.
        var normalized = Normalize(candidates, PrimaryIndex, doc.TextLength);
        ReplaceCore(normalized.Ranges, normalized.Primary, "", canEdit, revision, generation);
    }
    private void EnsureUnchanged(long revision, long generation)
    {
        _document.VerifyAccess();
        if (_document.Revision != revision || _generation != generation)
            throw new InvalidOperationException("The document or selections changed during edit preflight.");
    }
    private void Publish(SelectionRange[] ranges, int primary)
    { _ranges = ranges; _view = Array.AsReadOnly(ranges); PrimaryIndex = primary; _generation++; }
    private static (SelectionRange[] Ranges, int Primary) Normalize(SelectionRange[] source, int primary, int textLength)
    {
        foreach (var range in source)
            if (range.Start < 0 || range.End > textLength) throw new ArgumentOutOfRangeException(nameof(source));
        var ordered = source.Select((range, index) => (Range: range, Index: index)).OrderBy(x => x.Range.Start).ThenBy(x => x.Range.End);
        var result = new List<SelectionRange>(source.Length); var primaryIndex = -1;
        foreach (var item in ordered)
        {
            var range = item.Range;
            // Adjacency is not overlap. Duplicate carets and shared starts must
            // merge, but two neighboring word selections remain independent.
            if (result.Count > 0 && (range.Start < result[^1].End || range.Start == result[^1].Start))
            {
                var start = result[^1].Start; var end = Math.Max(result[^1].End, range.End);
                var reverse = item.Index == primary ? range.Active < range.Anchor : result[^1].Active < result[^1].Anchor;
                result[^1] = reverse ? new(end, start) : new(start, end);
                if (item.Index == primary) primaryIndex = result.Count - 1;
            }
            else
            {
                result.Add(range); if (item.Index == primary) primaryIndex = result.Count - 1;
            }
        }
        return (result.ToArray(), primaryIndex);
    }
}
