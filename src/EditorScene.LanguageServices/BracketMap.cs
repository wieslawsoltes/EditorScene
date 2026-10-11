using EditorScene.Text;

namespace EditorScene.LanguageServices;

/// <summary>Matched bracket lookup built once per analyzed snapshot, excluding strings and comments.</summary>
public sealed class BracketMap
{
    private readonly Dictionary<int, int> _pairs = new();
    public long Revision { get; }
    public BracketMap(DocumentSnapshot snapshot, IEnumerable<ClassificationSpan> classifications)
    {
        Revision = snapshot.Revision;
        var excluded = classifications.Where(c => c.Kind is "string" or "comment").OrderBy(c => c.Range.Offset).ToArray();
        var ignored = 0; var stack = new Stack<(char Character, int Offset)>();
        for (var i = 0; i < snapshot.Length; i++)
        {
            while (ignored < excluded.Length && excluded[ignored].Range.End <= i) ignored++;
            if (ignored < excluded.Length && excluded[ignored].Range.Offset <= i) { i = excluded[ignored].Range.End - 1; continue; }
            var c = snapshot.Text[i];
            if (c is '(' or '[' or '{') stack.Push((c, i));
            else if (c is ')' or ']' or '}')
            {
                if (stack.TryPeek(out var open) && (open.Character, c) is ('(', ')') or ('[', ']') or ('{', '}'))
                { stack.Pop(); _pairs.Add(open.Offset, i); _pairs.Add(i, open.Offset); }
                else stack.Clear(); // Do not pair across syntactically mismatched brackets.
            }
        }
    }
    public IReadOnlyList<SourceRange> AtCaret(int caret)
    {
        var offset = _pairs.ContainsKey(caret) ? caret : caret - 1;
        return _pairs.TryGetValue(offset, out var other) ? [new(offset, 1), new(other, 1)] : [];
    }
}
