using System.Buffers;
using UnoEdit.Document;

namespace EditorScene.Text;

public enum LineEndingKind { None, Cr, Lf, CrLf, Mixed }

/// <summary>Immutable CR/LF/CRLF counts. Contains no document, source or edit arrays.
/// A CRLF pair is one terminator. Other Unicode separators are not normalized.</summary>
public sealed class LineEndingInfo
{
    private static readonly LineEndingInfo Empty = new(0, 0, 0);
    private LineEndingInfo(int cr, int lf, int crlf)
    {
        if (cr < 0 || lf < 0 || crlf < 0) throw new InvalidOperationException("Invalid line-ending counts.");
        CrCount = cr; LfCount = lf; CrLfCount = crlf;
    }
    public int CrCount { get; }
    public int LfCount { get; }
    public int CrLfCount { get; }
    public long DelimiterCount => (long)CrCount + LfCount + CrLfCount;
    private int KindCount => (CrCount > 0 ? 1 : 0) + (LfCount > 0 ? 1 : 0) + (CrLfCount > 0 ? 1 : 0);
    public bool HasUniformLineTerminators => KindCount <= 1;
    public LineEndingKind Kind => KindCount > 1 ? LineEndingKind.Mixed : CrLfCount > 0 ? LineEndingKind.CrLf
        : LfCount > 0 ? LineEndingKind.Lf : CrCount > 0 ? LineEndingKind.Cr : LineEndingKind.None;
    /// <summary>CRLF takes priority over LF, then CR, regardless of frequency.
    /// A source with no terminators falls back to Environment.NewLine. This is
    /// distinct from EditorDocument.PreferredLineEnding's first-delimiter policy.</summary>
    public string InferredLineTerminator => CrLfCount > 0 ? "\r\n" : LfCount > 0 ? "\n" : CrCount > 0 ? "\r" : Environment.NewLine;
    internal (int Cr, int Lf, int CrLf) Counts => (CrCount, LfCount, CrLfCount);
    internal static LineEndingInfo FromCounts((int Cr, int Lf, int CrLf) counts) => counts == default ? Empty : new(counts.Cr, counts.Lf, counts.CrLf);
    internal static LineEndingInfo Scan(ITextSource source, CancellationToken cancellationToken)
    {
        var count = Count(source, null, null, cancellationToken);
        return FromCounts((count.Cr, count.Lf, count.CrLf));
    }
    internal static (int Cr, int Lf, int CrLf) ApplyChange((int Cr, int Lf, int CrLf) current,
        ITextSource removed, ITextSource inserted, char? prefix, char? suffix)
    {
        // The live document updates value-type counters, not a new immutable
        // object for every edit in a bulk conversion. Publish only at snapshot capture.
        // Count the same unchanged neighbors in both windows so outside pairs cancel.
        var before = Count(removed, prefix, suffix, default);
        var after = Count(inserted, prefix, suffix, default);
        return (checked(current.Cr - before.Cr + after.Cr), checked(current.Lf - before.Lf + after.Lf), checked(current.CrLf - before.CrLf + after.CrLf));
    }
    private static Counter Count(ITextSource source, char? prefix, char? suffix, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var count = new Counter();
        if (prefix is { } leading) count.Append(leading);
        // Ordinary keystrokes need no rented buffer, reader or temporary string.
        if (source.TextLength <= 128)
        {
            for (var i = 0; i < source.TextLength; i++) count.Append(source.GetCharAt(i));
        }
        else
        {
            var buffer = ArrayPool<char>.Shared.Rent(4096);
            try
            {
                using var reader = source.CreateReader(0, source.TextLength);
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var length = reader.Read(buffer, 0, 4096);
                    if (length == 0) break;
                    count.Append(buffer.AsSpan(0, length));
                }
            }
            finally { ArrayPool<char>.Shared.Return(buffer, clearArray: true); }
        }
        if (suffix is { } trailing) count.Append(trailing);
        count.Finish(); cancellationToken.ThrowIfCancellationRequested(); return count;
    }
    private struct Counter
    {
        internal int Cr, Lf, CrLf;
        private bool _pendingCr;
        internal void Append(char value)
        {
            if (_pendingCr)
            {
                _pendingCr = false;
                if (value == '\n') { CrLf++; return; }
                Cr++;
            }
            if (value == '\r') _pendingCr = true;
            else if (value == '\n') Lf++;
        }
        internal void Append(ReadOnlySpan<char> text)
        {
            var offset = 0;
            if (_pendingCr && text.Length > 0) { Append(text[0]); offset = 1; }
            while (offset < text.Length)
            {
                var next = text[offset..].IndexOfAny('\r', '\n');
                if (next < 0)
                {
                    if (_pendingCr) { Cr++; _pendingCr = false; }
                    return;
                }
                if (next > 0 && _pendingCr) { Cr++; _pendingCr = false; }
                offset += next; Append(text[offset++]);
            }
        }
        internal void Finish() { if (_pendingCr) { Cr++; _pendingCr = false; } }
    }
}
