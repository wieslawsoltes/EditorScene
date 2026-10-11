using System.Buffers;
using UnoEdit.Document;

namespace EditorScene.Text;

/// <summary>An immutable, zero-based snapshot line. Text excludes the original line terminator.</summary>
public readonly record struct SnapshotLine
{
    internal SnapshotLine(DocumentSnapshot snapshot, int number, int offset, int length, int delimiterLength)
    { Snapshot = snapshot; LineNumber = number; Offset = offset; Length = length; DelimiterLength = delimiterLength; }
    public DocumentSnapshot Snapshot { get; }
    public int LineNumber { get; }
    public int Offset { get; }
    public int Length { get; }
    public int DelimiterLength { get; }
    public int EndOffset => Offset + Length;
    public int EndIncludingDelimiter => EndOffset + DelimiterLength;
    public string Text => Snapshot.GetText(Offset, Length);
}

/// <summary>
/// Persistent AVL index of bounded text chunks. Leaves retain only packed line-break
/// endpoints, not text. Joining summaries combines CR/LF pairs across leaf boundaries.
/// Edits path-copy logarithmic tree paths and scan only inserted text.
/// </summary>
internal sealed class SnapshotLineIndex
{
    private const int ChunkSize = 4096;
    private readonly Node? _root;
    private SnapshotLineIndex(Node? root) => _root = root;
    public int Length => _root?.Length ?? 0;
    public int LineCount => (_root?.Breaks ?? 0) + 1;
    public static SnapshotLineIndex Build(ITextSource source)
    {
        using var reader = source.CreateReader();
        var buffer = ArrayPool<char>.Shared.Rent(ChunkSize);
        try
        {
            var nodes = new List<Node>(); var remaining = source.TextLength;
            while (remaining > 0)
            {
                var size = Math.Min(remaining, ChunkSize); if (reader.ReadBlock(buffer.AsSpan(0, size)) != size) throw new EndOfStreamException("Snapshot reader ended before its declared length.");
                nodes.Add(Scan(buffer.AsSpan(0, size))); remaining -= size;
            }
            return new(BuildBalanced(nodes, 0, nodes.Count));
        }
        finally { ArrayPool<char>.Shared.Return(buffer); }
    }
    public SnapshotLineIndex Replace(int offset, int length, ITextSource inserted)
    {
        if (offset < 0 || length < 0 || offset > Length || length > Length - offset) throw new ArgumentOutOfRangeException(nameof(offset));
        if (length == 0 && inserted.TextLength == 0) return this;
        var (prefix, tail) = Split(_root, offset); var (_, suffix) = Split(tail, length);
        return new(Join(Join(prefix, Build(inserted)._root), suffix));
    }
    public (int Start, int Length, int Delimiter) Line(int number)
    {
        if ((uint)number >= (uint)LineCount) throw new ArgumentOutOfRangeException(nameof(number));
        var start = number == 0 ? 0 : BreakAt(_root!, number - 1).End;
        var end = number == LineCount - 1 ? (Length, 0) : BreakAt(_root!, number);
        return (start, end.Item1 - end.Item2 - start, end.Item2);
    }
    public int LineAt(int offset)
    {
        if ((uint)offset > (uint)Length) throw new ArgumentOutOfRangeException(nameof(offset));
        return CountThrough(_root, offset);
    }
    private static Node Scan(ReadOnlySpan<char> text)
    {
        Span<int> breaks = stackalloc int[ChunkSize]; var count = 0; var cursor = 0;
        while (cursor < text.Length)
        {
            var next = text[cursor..].IndexOfAny('\r', '\n'); if (next < 0) break;
            cursor += next; var kind = text[cursor++] == '\r' ? 1 : 2;
            if (kind == 1 && cursor < text.Length && text[cursor] == '\n') { cursor++; kind = 3; }
            breaks[count++] = (cursor << 2) | kind;
        }
        return new(text.Length, breaks[..count].ToArray());
    }
    private static Node? BuildBalanced(List<Node> nodes, int start, int count)
    {
        if (count == 0) return null; if (count == 1) return nodes[start]; var half = count / 2;
        return new(BuildBalanced(nodes, start, half)!, BuildBalanced(nodes, start + half, count - half)!);
    }
    private static int Height(Node? node) => node?.Height ?? 0;
    private static Node? Join(Node? left, Node? right)
    {
        if (left is null) return right; if (right is null) return left;
        if (left.Marks is not null && right.Marks is not null && left.Length + right.Length <= ChunkSize)
        {
            var merge = left.LastCr && right.FirstLf; var count = left.Breaks + right.Breaks - (merge ? 1 : 0);
            var marks = new int[count]; var index = left.Breaks - (merge ? 1 : 0);
            left.Marks.AsSpan(0, index).CopyTo(marks);
            for (var i = 0; i < right.Marks.Length; i++)
                marks[index++] = right.Marks[i] + (left.Length << 2) + (merge && i == 0 ? 1 : 0); // LF kind 2 -> CRLF kind 3
            return new(left.Length + right.Length, marks);
        }
        if (left.Height > right.Height + 1) return Balance(left.Left!, Join(left.Right, right)!);
        if (right.Height > left.Height + 1) return Balance(Join(left, right.Left)!, right.Right!);
        return new(left, right);
    }
    private static Node Balance(Node left, Node right)
    {
        if (left.Height > right.Height + 1)
        {
            if (Height(left.Left) >= Height(left.Right)) return new(left.Left!, new Node(left.Right!, right));
            var middle = left.Right!; return new(new Node(left.Left!, middle.Left!), new Node(middle.Right!, right));
        }
        if (right.Height > left.Height + 1)
        {
            if (Height(right.Right) >= Height(right.Left)) return new(new Node(left, right.Left!), right.Right!);
            var middle = right.Left!; return new(new Node(left, middle.Left!), new Node(middle.Right!, right.Right!));
        }
        return new(left, right);
    }
    private static (Node? Left, Node? Right) Split(Node? node, int offset)
    {
        if (node is null || offset == 0) return (null, node);
        if (offset == node.Length) return (node, null);
        if (node.Marks is { } marks)
        {
            Span<int> first = stackalloc int[ChunkSize]; Span<int> second = stackalloc int[ChunkSize]; var a = 0; var b = 0;
            foreach (var mark in marks)
            {
                var end = mark >> 2; var kind = mark & 3; var start = end - (kind == 3 ? 2 : 1);
                if (end <= offset) first[a++] = mark;
                else if (start >= offset) second[b++] = mark - (offset << 2);
                else { first[a++] = (offset << 2) | 1; second[b++] = (1 << 2) | 2; } // split an existing CRLF
            }
            return (new(offset, first[..a].ToArray()), new(node.Length - offset, second[..b].ToArray()));
        }
        if (offset < node.Left!.Length)
        {
            var split = Split(node.Left, offset); return (split.Left, Join(split.Right, node.Right));
        }
        else
        {
            var split = Split(node.Right, offset - node.Left.Length); return (Join(node.Left, split.Left), split.Right);
        }
    }
    private static (int End, int Width) BreakAt(Node node, int ordinal)
    {
        if (node.Marks is { } marks) { var mark = marks[ordinal]; return (mark >> 2, (mark & 3) == 3 ? 2 : 1); }
        var left = node.Left!; var right = node.Right!; var merge = left.LastCr && right.FirstLf;
        var prefix = left.Breaks - (merge ? 1 : 0);
        if (ordinal < prefix) return BreakAt(left, ordinal);
        var result = BreakAt(right, ordinal - prefix);
        return (left.Length + result.End, merge && ordinal == prefix ? 2 : result.Width);
    }
    private static int CountThrough(Node? node, int offset)
    {
        if (node is null) return 0;
        if (node.Marks is { } marks)
        {
            var low = 0; var high = marks.Length;
            while (low < high) { var middle = low + (high - low) / 2; if ((marks[middle] >> 2) <= offset) low = middle + 1; else high = middle; }
            return low;
        }
        if (offset < node.Left!.Length) return CountThrough(node.Left, offset);
        return node.Left.Breaks - (node.Left.LastCr && node.Right!.FirstLf ? 1 : 0) + CountThrough(node.Right, offset - node.Left.Length);
    }
    private sealed class Node
    {
        public Node(int length, int[] marks)
        {
            Length = length; Marks = marks; Height = 1; Breaks = marks.Length;
            FirstLf = marks.Length > 0 && marks[0] == ((1 << 2) | 2);
            LastCr = marks.Length > 0 && marks[^1] == ((length << 2) | 1);
        }
        public Node(Node left, Node right)
        {
            Left = left; Right = right; Height = Math.Max(left.Height, right.Height) + 1; Length = checked(left.Length + right.Length);
            FirstLf = left.FirstLf; LastCr = right.LastCr; Breaks = left.Breaks + right.Breaks - (left.LastCr && right.FirstLf ? 1 : 0);
        }
        public readonly int Length, Height, Breaks;
        public readonly bool FirstLf, LastCr;
        public readonly int[]? Marks;
        public readonly Node? Left, Right;
    }
}
