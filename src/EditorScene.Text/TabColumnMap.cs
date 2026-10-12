using System.Buffers;

namespace EditorScene.Text;

/// <summary>Immutable mapping between line-local UTF-16 offsets and tab-expanded logical columns.
/// Only tabs need stored entries. No live document or snapshot text is retained.
/// This is not glyph-width, bidi-order or grapheme-cell measurement.</summary>
public sealed class TabColumnMap
{
    private readonly int[] _tabs;
    private readonly long[] _after;
    public Guid DocumentId { get; }
    public long Revision { get; }
    public int LineIndex { get; }
    public int LineOffset { get; }
    public int Length { get; }
    public int TabSize { get; }
    public int TabCount => _tabs.Length;
    public long ColumnLength { get; }

    public TabColumnMap(DocumentSnapshot snapshot, int lineIndex, int tabSize = 4, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (tabSize is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(tabSize));
        cancellationToken.ThrowIfCancellationRequested();
        var line = snapshot.GetLine(lineIndex);
        DocumentId = snapshot.DocumentId; Revision = snapshot.Revision; LineIndex = lineIndex;
        LineOffset = line.Offset; Length = line.Length; TabSize = tabSize;
        var tabs = new List<int>(); var after = new List<long>(); long column = 0;
        var buffer = ArrayPool<char>.Shared.Rent(4096);
        try
        {
            using var reader = snapshot.TextSource.CreateReader(line.Offset, line.Length);
            var offset = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = reader.Read(buffer, 0, 4096); if (count == 0) break;
                for (var i = 0; i < count; i++, offset++)
                {
                    if (buffer[i] == '\t') { column += tabSize - column % tabSize; tabs.Add(offset); after.Add(column); }
                    else column++;
                }
            }
        }
        finally { ArrayPool<char>.Shared.Return(buffer, clearArray: true); }
        _tabs = tabs.ToArray(); _after = after.ToArray(); ColumnLength = column;
    }
    /// <summary>Zero-based logical column at a UTF-16 offset within the line, including end of line.</summary>
    public long GetColumn(int offset)
    {
        if ((uint)offset > (uint)Length) throw new ArgumentOutOfRangeException(nameof(offset));
        // Last tab strictly before the requested offset. Direct comparisons avoid
        // generic comparer/helper initialization on this repeated UI query path.
        var low = 0; var high = _tabs.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (_tabs[middle] < offset) low = middle + 1; else high = middle;
        }
        var index = low - 1;
        return index < 0 ? offset : offset + _after[index] - (_tabs[index] + 1L);
    }
    /// <summary>Map a nonnegative logical column to an offset; columns inside a tab snap to its
    /// leading edge unless afterTab is true. Columns beyond the line clamp to its end.</summary>
    public int GetOffset(long column, bool afterTab = false)
    {
        if (column < 0) throw new ArgumentOutOfRangeException(nameof(column));
        if (column >= ColumnLength) return Length;
        var low = 0; var high = _after.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (_after[middle] < column) low = middle + 1; else high = middle;
        }
        if (low < _after.Length && _after[low] == column) return _tabs[low] + 1;
        var next = low; var previous = next - 1;
        var extra = previous < 0 ? 0 : _after[previous] - (_tabs[previous] + 1L);
        var offset = column - extra;
        if (next < _tabs.Length && offset > _tabs[next]) return _tabs[next] + (afterTab ? 1 : 0);
        return checked((int)offset);
    }
    public string GetIndentation(int offset, bool convertTabsToSpaces)
        => convertTabsToSpaces ? new string(' ', (int)(TabSize - GetColumn(offset) % TabSize)) : ValidateTab(offset);
    private string ValidateTab(int offset) { _ = GetColumn(offset); return "\t"; }
}
