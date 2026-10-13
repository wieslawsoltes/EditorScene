using System.Buffers;
using System.Collections.Immutable;

namespace EditorScene.Text;

/// <summary>Immutable, revision-bound edits for CR, LF and CRLF conversion. Only the
/// code units which actually differ are inserted, removed or replaced. Correct
/// delimiters and all ordinary text remain untouched. No snapshot is retained.</summary>
public sealed class LineEndingEditPlan
{
    public Guid DocumentId { get; }
    public long Revision { get; }
    public string Delimiter { get; }
    public ImmutableArray<TextEdit> Edits { get; }
    public int CrCount { get; }
    public int LfCount { get; }
    public int CrLfCount { get; }
    public long DelimiterCount => (long)CrCount + LfCount + CrLfCount;
    public int ChangedDelimiterCount => Edits.Length;
    private LineEndingEditPlan(DocumentSnapshot snapshot, string delimiter, ImmutableArray<TextEdit> edits, int cr, int lf, int crlf)
    { DocumentId = snapshot.DocumentId; Revision = snapshot.Revision; Delimiter = delimiter; Edits = edits; CrCount = cr; LfCount = lf; CrLfCount = crlf; }

    /// <summary>Scan immutable source with a bounded cleared reader buffer. CRLF is
    /// one delimiter even across reader boundaries. Cost is O(source length + changes);
    /// canonical input creates no per-line objects or full line index.</summary>
    public static LineEndingEditPlan Create(DocumentSnapshot snapshot, string delimiter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (delimiter is not ("\n" or "\r" or "\r\n")) throw new ArgumentException("Choose LF, CRLF or CR.", nameof(delimiter));
        cancellationToken.ThrowIfCancellationRequested();
        var edits = ImmutableArray.CreateBuilder<TextEdit>();
        var cr = 0; var lf = 0; var crlf = 0; var pendingCr = -1;
        var buffer = ArrayPool<char>.Shared.Rent(4096);
        try
        {
            using var reader = snapshot.TextSource.CreateReader(0, snapshot.Length);
            var offset = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = reader.Read(buffer, 0, 4096);
                if (count == 0) break;
                var chunk = buffer.AsSpan(0, count); var consumed = 0;
                if (pendingCr >= 0)
                {
                    var pair = chunk[0] == '\n'; Add(pendingCr, pair ? "\r\n" : "\r");
                    pendingCr = -1; if (pair) consumed = 1;
                }
                while (consumed < count)
                {
                    var next = chunk[consumed..].IndexOfAny('\r', '\n');
                    if (next < 0) break;
                    var index = consumed + next;
                    if (chunk[index] == '\n') { Add(offset + index, "\n"); consumed = index + 1; }
                    else if (index + 1 == count) { pendingCr = offset + index; break; }
                    else
                    {
                        var pair = chunk[index + 1] == '\n'; Add(offset + index, pair ? "\r\n" : "\r");
                        consumed = index + (pair ? 2 : 1);
                    }
                }
                offset += count;
            }
            if (pendingCr >= 0) Add(pendingCr, "\r");
        }
        finally { ArrayPool<char>.Shared.Return(buffer, clearArray: true); }
        cancellationToken.ThrowIfCancellationRequested();
        return new(snapshot, delimiter, edits.ToImmutable(), cr, lf, crlf);

        void Add(int offset, string source)
        {
            if (source == "\r\n") crlf++; else if (source == "\r") cr++; else lf++;
            if (source == delimiter) return;
            // Preserve any shared code unit rather than replacing a whole delimiter.
            var edit = (source, delimiter) switch
            {
                ("\r\n", "\n") => new TextEdit(offset, 1, ""),
                ("\r\n", "\r") => new TextEdit(offset + 1, 1, ""),
                ("\n", "\r\n") => new TextEdit(offset, 0, "\r"),
                ("\r", "\r\n") => new TextEdit(offset + 1, 0, "\n"),
                _ => new TextEdit(offset, 1, delimiter)
            };
            edits.Add(edit);
        }
    }

    /// <summary>Apply once to a matching live document with one normal undo group.
    /// Even empty plans reject stale/foreign documents. Hosts with edit protection
    /// should authorize Edits themselves before applying through their editor API.</summary>
    public bool Apply(EditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document); document.VerifyAccess();
        if (document.Id != DocumentId || document.Revision != Revision) throw new InvalidOperationException("The line-ending target changed; rebuild its conversion plan.");
        if (Edits.IsEmpty) return false;
        document.ApplyEdits(Edits, Revision); return true;
    }
}
