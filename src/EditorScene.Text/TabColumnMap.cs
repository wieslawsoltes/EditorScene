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
        var index = Array.BinarySearch(_tabs, offset);
        index = index >= 0 ? index - 1 : ~index - 1;
        return index < 0 ? offset : offset + _after[index] - (_tabs[index] + 1L);
    }
    /// <summary>Map a nonnegative logical column to an offset; columns inside a tab snap to its
    /// leading edge unless afterTab is true. Columns beyond the line clamp to its end.</summary>
    public int GetOffset(long column, bool afterTab = false)
    {
        if (column < 0) throw new ArgumentOutOfRangeException(nameof(column));
        if (column >= ColumnLength) return Length;
        var index = Array.BinarySearch(_after, column);
        if (index >= 0) return _tabs[index] + 1;
        var next = ~index; var previous = next - 1;
        var extra = previous < 0 ? 0 : _after[previous] - (_tabs[previous] + 1L);
        var offset = column - extra;
        if (next < _tabs.Length && offset > _tabs[next]) return _tabs[next] + (afterTab ? 1 : 0);
        return checked((int)offset);
    }
    public string GetIndentation(int offset, bool convertTabsToSpaces)
        => convertTabsToSpaces ? new string(' ', (int)(TabSize - GetColumn(offset) % TabSize)) : ValidateTab(offset);
    private string ValidateTab(int offset) { _ = GetColumn(offset); return "\t"; }
}
