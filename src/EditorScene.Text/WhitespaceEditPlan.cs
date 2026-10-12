using System.Buffers;
using System.Collections.Immutable;

namespace EditorScene.Text;

public enum WhitespaceConversion { Spaces, Tabs, TrimTrailing }

/// <summary>Immutable, revision-bound edits of ASCII space/tab runs. Scans a bounded
/// snapshot reader, never materializes a document/line or retains a snapshot/live view.
/// Non-whitespace, line delimiters and text outside the scope are never replaced.</summary>
public sealed class WhitespaceEditPlan
{
    private static readonly SearchValues<char> Separators = SearchValues.Create(" \t\r\n");
    public Guid DocumentId { get; }
    public long Revision { get; }
    public ImmutableArray<TextEdit> Edits { get; }
    public int CharactersScanned { get; }
    public long ReplacementCharacters { get; }
    private WhitespaceEditPlan(DocumentSnapshot snapshot, ImmutableArray<TextEdit> edits, int scanned, long replacementCharacters)
    { DocumentId = snapshot.DocumentId; Revision = snapshot.Revision; Edits = edits; CharactersScanned = scanned; ReplacementCharacters = replacementCharacters; }

    /// <summary>Convert only the given scope. leadingOnly limits changes to line indentation.
    /// Columns use UTF-16 units and tab stops measured from the actual line start, including
    /// any unselected prefix. Tabs output canonical tabs followed by residual spaces.
    /// TrimTrailing removes only selected space/tab suffixes that reach a real line end.
    /// Limits fail before a plan is returned; there is no truncated/partially applied plan.</summary>
    public static WhitespaceEditPlan Create(SnapshotSpan scope, WhitespaceConversion conversion, int tabSize = 4,
        bool leadingOnly = false, int maxEdits = 100_000, int maxReplacementCharacters = 16_777_216,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (!Enum.IsDefined(conversion)) throw new ArgumentOutOfRangeException(nameof(conversion));
        if (tabSize is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(tabSize));
        if (maxEdits < 1) throw new ArgumentOutOfRangeException(nameof(maxEdits));
        if (maxReplacementCharacters < 0) throw new ArgumentOutOfRangeException(nameof(maxReplacementCharacters));
        if (leadingOnly && conversion == WhitespaceConversion.TrimTrailing) throw new ArgumentException("Trailing trim cannot be restricted to indentation.", nameof(leadingOnly));
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = scope.Snapshot;
        if (scope.Length == 0) return new(snapshot, [], 0, 0);
        var beginning = snapshot.GetLineFromOffset(scope.Offset).Offset;
        var edits = ImmutableArray.CreateBuilder<TextEdit>();
        var buffer = ArrayPool<char>.Shared.Rent(4096);
        var position = beginning; long column = 0, generated = 0, delta = 0;
        var leading = true;
        var runStart = -1; var runLength = 0; var tabs = 0;
        long runColumn = 0;
        var runEligible = false; var sawSpace = false; var canonical = true;
        try
        {
            using var reader = snapshot.TextSource.CreateReader(beginning, scope.End - beginning);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = reader.Read(buffer, 0, 4096); if (count == 0) break;
                for (var i = 0; i < count;)
                {
                    var c = buffer[i];
                    if (c is ' ' or '\t')
                    {
                        if (position >= scope.Offset)
                        {
                            if (runStart < 0) { runStart = position; runColumn = column; runEligible = !leadingOnly || leading; }
                            runLength++;
                            if (c == '\t') { tabs++; if (sawSpace) canonical = false; } else sawSpace = true;
                        }
                        column += c == '\t' ? tabSize - column % tabSize : 1;
                        i++; position++; continue;
                    }
                    Flush(c is '\r' or '\n');
                    if (c is '\r' or '\n') { column = 0; leading = true; i++; position++; continue; }
                    // Skip non-whitespace in bulk; code and long string content need no allocation.
                    var next = buffer.AsSpan(i, count - i).IndexOfAny(Separators);
                    var ordinary = next < 0 ? count - i : next;
                    column += ordinary; leading = false; i += ordinary; position += ordinary;
                }
            }
            var reachesLineEnd = scope.End == snapshot.Length || snapshot.TextSource.GetCharAt(scope.End) is '\r' or '\n';
            Flush(reachesLineEnd);
            cancellationToken.ThrowIfCancellationRequested();
            return new(snapshot, edits.ToImmutable(), position - beginning, generated);
        }
        finally { ArrayPool<char>.Shared.Return(buffer, clearArray: true); }

        void Flush(bool atLineEnd)
        {
            if (runStart < 0) return;
            if (runEligible)
            {
                long tabCount = 0, spaceCount = 0;
                var changed = false;
                if (conversion == WhitespaceConversion.TrimTrailing) changed = atLineEnd;
                else if (conversion == WhitespaceConversion.Spaces) { changed = tabs != 0; spaceCount = column - runColumn; }
                else
                {
                    var firstStop = runColumn + tabSize - runColumn % tabSize;
                    if (column >= firstStop) tabCount = 1 + (column - firstStop) / tabSize;
                    spaceCount = column - (tabCount == 0 ? runColumn : firstStop + (tabCount - 1) * tabSize);
                    changed = !canonical || tabs != tabCount || runLength != tabCount + spaceCount;
                }
                if (changed)
                {
                    var length = tabCount + spaceCount;
                    if (edits.Count == maxEdits || length > maxReplacementCharacters - generated)
                        throw new InvalidOperationException("Whitespace conversion exceeded its edit/output limit; no partial plan was returned.");
                    delta += length - runLength;
                    if (snapshot.Length + delta > int.MaxValue) throw new InvalidOperationException("Whitespace conversion would exceed the document length limit.");
                    cancellationToken.ThrowIfCancellationRequested();
                    var replacement = length == 0 ? string.Empty : string.Create(checked((int)length), checked((int)tabCount),
                        static (target, numberOfTabs) => { target[..numberOfTabs].Fill('\t'); target[numberOfTabs..].Fill(' '); });
                    edits.Add(new(runStart, runLength, replacement)); generated += length;
                }
            }
            runStart = -1; runLength = tabs = 0; sawSpace = false; canonical = true;
        }
    }
    /// <summary>Apply on the document owner thread. UI hosts must do their own read-only
    /// permission preflight before applying the edits. Snapshot identity is always checked.</summary>
    public void Apply(EditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document); document.VerifyAccess();
        if (document.Id != DocumentId) throw new ArgumentException("The plan belongs to another document.", nameof(document));
        document.ApplyEdits(Edits, Revision);
    }
}
