using System.Buffers;
using EditorScene.Text;

namespace EditorScene.LanguageServices;

/// <summary>Immutable bracket pairs built from a pooled rope reader, excluding classified strings and comments.</summary>
public sealed class BracketMap
{
    private readonly Dictionary<int, int> _pairs = new();
    private readonly int _length;
    public Guid DocumentId { get; }
    public long Revision { get; }
    public int PairCount => _pairs.Count / 2;
    public BracketMap(DocumentSnapshot snapshot, IEnumerable<ClassificationSpan> classifications, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(classifications);
        cancellationToken.ThrowIfCancellationRequested();
        DocumentId = snapshot.DocumentId; Revision = snapshot.Revision; _length = snapshot.Length;
        var excluded = classifications.Where(c => c.Kind is "string" or "comment").Select(c => c.Range).OrderBy(r => r.Offset).ToArray();
        foreach (var range in excluded)
            if (range.Offset < 0 || range.Length < 0 || range.Offset > _length || range.Length > _length - range.Offset)
                throw new ArgumentOutOfRangeException(nameof(classifications), "An excluded classification is outside the snapshot.");
        var ignored = 0; var stack = new Stack<(char Character, int Offset)>();
        var buffer = ArrayPool<char>.Shared.Rent(4096);
        try
        {
            using var reader = snapshot.TextSource.CreateReader();
            var offset = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = reader.Read(buffer, 0, 4096); if (read == 0) break;
                for (var j = 0; j < read; j++, offset++)
                {
                    while (ignored < excluded.Length && excluded[ignored].End <= offset) ignored++;
                    if (ignored < excluded.Length && excluded[ignored].Offset <= offset) continue;
                    var c = buffer[j];
                    if (c is '(' or '[' or '{') stack.Push((c, offset));
                    else if (c is ')' or ']' or '}')
                    {
                        if (stack.TryPeek(out var open) && (open.Character, c) is ('(', ')') or ('[', ']') or ('{', '}'))
                        { stack.Pop(); _pairs.Add(open.Offset, offset); _pairs.Add(offset, open.Offset); }
                        else stack.Clear();
                    }
                }
            }
        }
        finally { ArrayPool<char>.Shared.Return(buffer, clearArray: true); }
    }
    /// <summary>Allocation-free lookup for repeated caret/render queries.</summary>
    public bool TryGetPair(int caret, out SourceRange atCaret, out SourceRange matching)
    {
        if ((uint)caret > (uint)_length) throw new ArgumentOutOfRangeException(nameof(caret));
        var offset = _pairs.ContainsKey(caret) ? caret : caret - 1;
        if (_pairs.TryGetValue(offset, out var other))
        { atCaret = new(offset, 1); matching = new(other, 1); return true; }
        atCaret = matching = default; return false;
    }
    public IReadOnlyList<SourceRange> AtCaret(int caret) => TryGetPair(caret, out var first, out var second) ? [first, second] : [];
}
