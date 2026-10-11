namespace EditorScene.LanguageServices;

public readonly record struct RangeQueryStatistics(int Matches, int VisitedNodes);

/// <summary>Immutable balanced interval index with stable document ordering and zero-width point tags.</summary>
public sealed class RangeIndex<T>
{
    private readonly Entry[] _entries;
    private readonly int[] _maximumEnds;
    private readonly record struct Entry(SourceRange Range, T Value)
    { public int Last => Range.Offset + Math.Max(0, Range.Length - 1); }
    public RangeIndex(IEnumerable<T> items, Func<T, SourceRange> getRange, int textLength = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(items); ArgumentNullException.ThrowIfNull(getRange);
        if (textLength < 0) throw new ArgumentOutOfRangeException(nameof(textLength));
        _entries = items.Select(value =>
        {
            var range = getRange(value);
            if (range.Offset < 0 || range.Length < 0 || range.Offset > textLength || range.Length > textLength - range.Offset)
                throw new ArgumentOutOfRangeException(nameof(items), "A tag range is outside the indexed text.");
            return new Entry(range, value);
        }).OrderBy(e => e.Range.Offset).ToArray();
        _maximumEnds = new int[_entries.Length]; Build(0, _entries.Length);
    }
    public int Count => _entries.Length;
    private int Build(int first, int end)
    {
        if (first == end) return -1;
        var middle = first + (end - first) / 2;
        return _maximumEnds[middle] = Math.Max(_entries[middle].Last, Math.Max(Build(first, middle), Build(middle + 1, end)));
    }
    /// <summary>Append overlaps. Empty queries test one point; non-empty queries are half-open.</summary>
    public RangeQueryStatistics CopyOverlaps(SourceRange query, List<T> destination)
    {
        if (query.Offset < 0 || query.Length < 0 || query.Length > int.MaxValue - query.Offset) throw new ArgumentOutOfRangeException(nameof(query));
        return CopyIntersecting(query.Offset, query.Offset + Math.Max(0, query.Length - 1), destination);
    }
    /// <summary>Append tags intersecting inclusive endpoint coordinates, including zero-width EOF diagnostics.</summary>
    public RangeQueryStatistics CopyIntersecting(int firstOffset, int lastOffsetInclusive, List<T> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (firstOffset < 0 || lastOffsetInclusive < firstOffset) throw new ArgumentOutOfRangeException(nameof(firstOffset));
        var before = destination.Count; var visited = 0;
        Visit(0, _entries.Length, firstOffset, lastOffsetInclusive, destination, ref visited);
        return new(destination.Count - before, visited);
    }
    private void Visit(int first, int end, int start, int last, List<T> destination, ref int visited)
    {
        if (first == end) return;
        visited++;
        var middle = first + (end - first) / 2;
        if (_maximumEnds[middle] < start || _entries[first].Range.Offset > last) return;
        Visit(first, middle, start, last, destination, ref visited);
        var entry = _entries[middle];
        if (entry.Range.Offset <= last && entry.Last >= start) destination.Add(entry.Value);
        if (entry.Range.Offset <= last) Visit(middle + 1, end, start, last, destination, ref visited);
    }
}
