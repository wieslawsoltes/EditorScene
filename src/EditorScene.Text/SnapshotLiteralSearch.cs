using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace EditorScene.Text;

/// <summary>
/// Reusable ordinal literal search over immutable rope snapshots. A pooled sliding
/// window preserves cross-window matches without materializing the document.
/// Case-insensitive matching uses .NET ordinal rules, not current-culture casing.
/// </summary>
public sealed class SnapshotLiteralSearch
{
    private const int ReadSize = 4096;
    public const int MaximumPatternLength = 1_048_576;
    public SnapshotLiteralSearch(string pattern, bool matchCase = true, bool wholeWord = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(pattern);
        if (pattern.Length > MaximumPatternLength) throw new ArgumentOutOfRangeException(nameof(pattern));
        Pattern = pattern; MatchCase = matchCase; WholeWord = wholeWord;
    }
    public string Pattern { get; }
    public bool MatchCase { get; }
    public bool WholeWord { get; }
    /// <summary>Ascending matches. Whole-word boundaries use the full snapshot, not artificial scope edges.</summary>
    public IEnumerable<SnapshotSpan> FindAll(SnapshotSpan scope, int maxResults = int.MaxValue, bool allowOverlap = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (maxResults < 0) throw new ArgumentOutOfRangeException(nameof(maxResults));
        cancellationToken.ThrowIfCancellationRequested();
        return Enumerate(scope, maxResults, allowOverlap, cancellationToken);
    }
    private IEnumerable<SnapshotSpan> Enumerate(SnapshotSpan scope, int maxResults, bool allowOverlap, CancellationToken token)
    {
        if (scope.Length < Pattern.Length || maxResults == 0) yield break;
        var buffer = ArrayPool<char>.Shared.Rent(checked(ReadSize + Pattern.Length - 1));
        try
        {
            using var reader = scope.Snapshot.TextSource.CreateReader(scope.Offset, scope.Length);
            var position = scope.Offset; var carry = 0; var nextCandidate = scope.Offset; var emitted = 0;
            var comparison = MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var read = reader.Read(buffer, carry, ReadSize);
                if (read == 0) yield break;
                var windowStart = position - carry; position += read;
                var length = carry + read; var cursor = Math.Max(0, nextCandidate - windowStart);
                while (cursor <= length - Pattern.Length)
                {
                    token.ThrowIfCancellationRequested();
                    var found = buffer.AsSpan(cursor, length - cursor).IndexOf(Pattern.AsSpan(), comparison);
                    if (found < 0) break;
                    var offset = windowStart + cursor + found;
                    var accepted = !WholeWord || (!WordBefore(scope.Snapshot, offset) && !WordAt(scope.Snapshot, offset + Pattern.Length));
                    nextCandidate = checked(offset + (accepted && !allowOverlap ? Pattern.Length : 1));
                    if (accepted)
                    {
                        yield return new SnapshotSpan(scope.Snapshot, offset, Pattern.Length);
                        if (++emitted == maxResults) yield break;
                    }
                    cursor = nextCandidate - windowStart;
                }
                carry = Math.Min(Pattern.Length - 1, length);
                Array.Copy(buffer, length - carry, buffer, 0, carry);
            }
        }
        finally { ArrayPool<char>.Shared.Return(buffer, clearArray: true); }
    }
    /// <summary>Find a match beginning at or after offset; optional wrapping searches only the preceding region.</summary>
    public SnapshotSpan? FindNext(DocumentSnapshot snapshot, int offset, bool wrap = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if ((uint)offset > (uint)snapshot.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        var result = FindAll(new(snapshot, offset, snapshot.Length - offset), 1, cancellationToken: cancellationToken).FirstOrDefault();
        return result ?? (wrap && offset > 0 ? FindAll(new(snapshot, 0, offset), 1, cancellationToken: cancellationToken).FirstOrDefault() : null);
    }
    /// <summary>Find the last match ending at or before offset. Reverse lookup scans its bounded region without flattening.</summary>
    public SnapshotSpan? FindPrevious(DocumentSnapshot snapshot, int offset, bool wrap = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if ((uint)offset > (uint)snapshot.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        var result = FindAll(new(snapshot, 0, offset), allowOverlap: true, cancellationToken: cancellationToken).LastOrDefault();
        return result ?? (wrap && offset < snapshot.Length ? FindAll(new(snapshot, offset, snapshot.Length - offset), allowOverlap: true, cancellationToken: cancellationToken).LastOrDefault() : null);
    }
    /// <summary>Preview non-overlapping literal replacements. Exceeding the limit fails instead of returning a partial edit set.</summary>
    public ImmutableArray<TextEdit> CreateReplaceAllEdits(SnapshotSpan scope, string replacement, int maxEdits = 100_000, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (maxEdits < 1 || maxEdits == int.MaxValue) throw new ArgumentOutOfRangeException(nameof(maxEdits));
        var edits = ImmutableArray.CreateBuilder<TextEdit>();
        foreach (var match in FindAll(scope, maxEdits + 1, cancellationToken: cancellationToken))
        {
            if (edits.Count == maxEdits) throw new InvalidOperationException("The replacement limit was exceeded; no partial edit plan is returned.");
            edits.Add(new(match.Offset, match.Length, replacement));
        }
        return edits.ToImmutable();
    }
    private static bool WordBefore(DocumentSnapshot snapshot, int offset)
    {
        if (offset == 0) return false;
        var c = snapshot.TextSource.GetCharAt(offset - 1);
        if (char.IsLowSurrogate(c) && offset > 1 && char.IsHighSurrogate(snapshot.TextSource.GetCharAt(offset - 2)))
            return WordRune(new Rune(snapshot.TextSource.GetCharAt(offset - 2), c));
        return !char.IsSurrogate(c) && WordRune(new Rune(c));
    }
    private static bool WordAt(DocumentSnapshot snapshot, int offset)
    {
        if (offset == snapshot.Length) return false;
        var c = snapshot.TextSource.GetCharAt(offset);
        if (char.IsHighSurrogate(c) && offset + 1 < snapshot.Length && char.IsLowSurrogate(snapshot.TextSource.GetCharAt(offset + 1)))
            return WordRune(new Rune(c, snapshot.TextSource.GetCharAt(offset + 1)));
        return !char.IsSurrogate(c) && WordRune(new Rune(c));
    }
    private static bool WordRune(Rune rune) => Rune.IsLetterOrDigit(rune) || Rune.GetUnicodeCategory(rune) is UnicodeCategory.LetterNumber or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark or UnicodeCategory.ConnectorPunctuation;
}
