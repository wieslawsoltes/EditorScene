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
    /// <summary>Number of leading space/tab UTF-16 units, excluding the line delimiter.</summary>
    public int LeadingWhitespaceLength { get; }

    public TabColumnMap(DocumentSnapshot snapshot, int lineIndex, int tabSize = 4, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (tabSize is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(tabSize));
        cancellationToken.ThrowIfCancellationRequested();
        var line = snapshot.GetLine(lineIndex);
        DocumentId = snapshot.DocumentId; Revision = snapshot.Revision; LineIndex = lineIndex;
        LineOffset = line.Offset; Length = line.Length; TabSize = tabSize;
        List<int>? tabs = null; List<long>? after = null; long column = 0;
        var leading = 0; var inIndentation = true;
        var buffer = ArrayPool<char>.Shared.Rent(4096);
        try
        {
            using var reader = snapshot.TextSource.CreateReader(line.Offset, line.Length);
            var offset = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = reader.Read(buffer, 0, 4096); if (count == 0) break;
                var chunk = buffer.AsSpan(0, count);
                if (inIndentation)
                {
                    var firstContent = chunk.IndexOfAnyExcept(' ', '\t');
                    leading += firstContent < 0 ? count : firstContent;
                    inIndentation = firstContent < 0;
                }
                // Search non-tab runs using the runtime's span search rather than
                // branching once for every ordinary character. Only tabs need entries.
                var consumed = 0;
                while (consumed < count)
                {
                    var next = chunk[consumed..].IndexOf('\t');
                    if (next < 0) { column += count - consumed; break; }
                    column += next;
                    column += tabSize - column % tabSize;
                    (tabs ??= new()).Add(offset + consumed + next);
                    (after ??= new()).Add(column);
                    consumed += next + 1;
                }
                offset += count;
            }
        }
        finally { ArrayPool<char>.Shared.Return(buffer, clearArray: true); }
        _tabs = tabs?.ToArray() ?? []; _after = after?.ToArray() ?? [];
        ColumnLength = column; LeadingWhitespaceLength = leading;
    }
    /// <summary>Zero-based logical column at a UTF-16 offset within the line, including end of line.</summary>
    public long GetColumn(int offset)
    {
        if ((uint)offset > (uint)Length) throw new ArgumentOutOfRangeException(nameof(offset));
        var index = FindPreviousTab(offset);
        return index < 0 ? offset : offset + _after[index] - (_tabs[index] + 1L);
    }
    private int FindPreviousTab(int offset)
    {
        var low = 0; var high = _tabs.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (_tabs[middle] < offset) low = middle + 1; else high = middle;
        }
        return low - 1;
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
    /// <summary>Number of leading spaces to remove to reach the previous tab stop.
    /// Returns zero at line start, after non-indentation content or immediately after a
    /// literal tab, where the caller should retain its ordinary Backspace behavior.
    /// Never consumes a tab or a line delimiter. Does not modify the source.</summary>
    public int GetSoftTabBackspaceLength(int offset)
    {
        if ((uint)offset > (uint)Length) throw new ArgumentOutOfRangeException(nameof(offset));
        if (offset == 0 || offset > LeadingWhitespaceLength) return 0;
        var previous = FindPreviousTab(offset);
        if (previous >= 0 && _tabs[previous] == offset - 1) return 0;
        var column = previous < 0 ? offset : offset + _after[previous] - (_tabs[previous] + 1L);
        var distance = (int)((column - 1) % TabSize + 1);
        var spaces = previous < 0 ? offset : offset - _tabs[previous] - 1;
        return Math.Min(distance, spaces);
    }
    public string GetIndentation(int offset, bool convertTabsToSpaces)
        => convertTabsToSpaces ? new string(' ', (int)(TabSize - GetColumn(offset) % TabSize)) : ValidateTab(offset);
    private string ValidateTab(int offset) { _ = GetColumn(offset); return "\t"; }
}
