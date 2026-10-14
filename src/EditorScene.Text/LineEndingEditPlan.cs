using System.Buffers;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace EditorScene.Text;

/// <summary>Immutable, revision-bound edits for CR, LF and CRLF conversion. Only the
/// code units which actually differ are inserted, removed or replaced. Correct
/// delimiters and all ordinary text remain untouched. No snapshot is retained.</summary>
public sealed class LineEndingEditPlan
{
    private const int BufferLength = 4096;
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

    /// <summary>Use completed snapshot metadata when available. Otherwise count
    /// delimiters with a bounded cleared reader buffer. Noncanonical text uses one
    /// exact owned result array and an emitting scan, without builder growth/copy.
    /// Canonical cached metadata needs no reader or source scan. An unobserved
    /// snapshot still uses one counting scan, plus one emitting scan if needed.</summary>
    public static LineEndingEditPlan Create(DocumentSnapshot snapshot, string delimiter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (delimiter is not ("\n" or "\r" or "\r\n")) throw new ArgumentException("Choose LF, CRLF or CR.", nameof(delimiter));
        cancellationToken.ThrowIfCancellationRequested();
        var known = snapshot.BuiltLineEndings;
        (int Cr, int Lf, int CrLf) counts = known is null ? default : (known.CrCount, known.LfCount, known.CrLfCount);
        if (known is not null && Changes(counts, delimiter) == 0)
            return new(snapshot, delimiter, ImmutableArray<TextEdit>.Empty, counts.Cr, counts.Lf, counts.CrLf);
        var buffer = ArrayPool<char>.Shared.Rent(BufferLength);
        try
        {
            if (known is null) counts = Scan(snapshot, delimiter, null, buffer, cancellationToken);
            var changed = Changes(counts, delimiter);
            cancellationToken.ThrowIfCancellationRequested();
            if (changed == 0) return new(snapshot, delimiter, ImmutableArray<TextEdit>.Empty, counts.Cr, counts.Lf, counts.CrLf);
            var edits = new TextEdit[changed];
            Scan(snapshot, delimiter, edits, buffer, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            // This method owns the array. Scan retains nothing. Never mutate or
            // pool the result after publishing it through the immutable wrapper.
            return new(snapshot, delimiter, ImmutableCollectionsMarshal.AsImmutableArray(edits), counts.Cr, counts.Lf, counts.CrLf);
        }
        finally { ArrayPool<char>.Shared.Return(buffer, clearArray: true); }
    }
    private static int Changes((int Cr, int Lf, int CrLf) counts, string delimiter) => delimiter switch
    { "\n" => counts.Cr + counts.CrLf, "\r" => counts.Lf + counts.CrLf, _ => counts.Cr + counts.Lf };

    private static (int Cr, int Lf, int CrLf) Scan(DocumentSnapshot snapshot, string delimiter,
        TextEdit[]? edits, char[] buffer, CancellationToken cancellationToken)
    {
        var cr = 0; var lf = 0; var crlf = 0; var pendingCr = -1; var editIndex = 0;
        using var reader = snapshot.TextSource.CreateReader(0, snapshot.Length);
        var offset = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = reader.Read(buffer, 0, BufferLength);
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
        if (edits is not null && editIndex != edits.Length) throw new InvalidOperationException("The immutable line-ending scan produced inconsistent counts.");
        return (cr, lf, crlf);
        void Add(int offset, string source)
        {
            if (source == "\r\n") crlf++; else if (source == "\r") cr++; else lf++;
            if (edits is null || source == delimiter) return;
            edits[editIndex++] = (source, delimiter) switch
            {
                ("\r\n", "\n") => new TextEdit(offset, 1, ""),
                ("\r\n", "\r") => new TextEdit(offset + 1, 1, ""),
                ("\n", "\r\n") => new TextEdit(offset, 0, "\r"),
                ("\r", "\r\n") => new TextEdit(offset + 1, 0, "\n"),
                _ => new TextEdit(offset, 1, delimiter)
            };
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
