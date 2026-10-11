namespace EditorScene.Text;

/// <summary>Snapshot differencing without flattening either snapshot.</summary>
public static class SnapshotTextChanges
{
    /// <summary>
    /// Return one minimal contiguous UTF-16 edit. Shared version history bounds the
    /// comparison to the affected region. After 2,048 history entries, a bounded-window
    /// scan is used instead. Endpoints never split CRLF or a valid surrogate pair.
    /// </summary>
    public static TextEdit GetMinimalChange(DocumentSnapshot before, DocumentSnapshot after, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(before); ArgumentNullException.ThrowIfNull(after);
        cancellationToken.ThrowIfCancellationRequested();
        if (before.DocumentId != after.DocumentId || after.Revision < before.Revision)
            throw new ArgumentException("Changes require forward snapshots of the same document.", nameof(after));
        var start = 0; var oldEnd = before.Length; var newEnd = after.Length;
        var first = before.TextSource.Version; var last = after.TextSource.Version;
        if (first is not null && last is not null && first.BelongsToSameDocumentAs(last))
        {
            var count = 0; var prefix = before.Length; var end = 0; var delta = 0; var complete = true;
            foreach (var change in first.GetChangesTo(last))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++count > 2048) { complete = false; break; }
                prefix = Math.Min(prefix, change.Offset);
                end = Math.Max(end, checked(change.Offset + change.RemovalLength - delta));
                delta = checked(delta + change.InsertionLength - change.RemovalLength);
            }
            if (count == 0) return new(before.Length, 0, "");
            if (complete) { start = prefix; oldEnd = end; newEnd = checked(end + delta); }
        }
        var common = CommonPrefix(before, after, start, Math.Min(oldEnd, newEnd) - start, cancellationToken);
        start += common;
        while (start > 0 && (SplitsSequence(before, start) || SplitsSequence(after, start))) start--;
        var suffix = CommonSuffix(before, after, oldEnd, newEnd, Math.Min(oldEnd, newEnd) - start, cancellationToken);
        oldEnd -= suffix; newEnd -= suffix;
        while (oldEnd < before.Length && newEnd < after.Length && (SplitsSequence(before, oldEnd) || SplitsSequence(after, newEnd)))
        { oldEnd++; newEnd++; }
        cancellationToken.ThrowIfCancellationRequested();
        return new(start, oldEnd - start, after.GetText(start, newEnd - start));
    }
    private static int CommonPrefix(DocumentSnapshot before, DocumentSnapshot after, int start, int limit, CancellationToken token)
    {
        if (limit == 0) return 0;
        using var a = before.TextSource.CreateReader(start, limit); using var b = after.TextSource.CreateReader(start, limit);
        Span<char> left = stackalloc char[4096]; Span<char> right = stackalloc char[4096]; var matched = 0;
        while (matched < limit)
        {
            token.ThrowIfCancellationRequested(); var count = Math.Min(left.Length, limit - matched);
            if (a.ReadBlock(left[..count]) != count || b.ReadBlock(right[..count]) != count) throw new EndOfStreamException();
            if (!left[..count].SequenceEqual(right[..count]))
            { for (var i = 0; i < count; i++) if (left[i] != right[i]) return matched + i; }
            matched += count;
        }
        return matched;
    }
    private static int CommonSuffix(DocumentSnapshot before, DocumentSnapshot after, int oldEnd, int newEnd, int limit, CancellationToken token)
    {
        var matched = 0;
        while (matched < limit)
        {
            token.ThrowIfCancellationRequested(); var count = Math.Min(4096, limit - matched);
            var left = before.TextSource.GetTextAsMemory(oldEnd - matched - count, count).Span;
            var right = after.TextSource.GetTextAsMemory(newEnd - matched - count, count).Span;
            if (!left.SequenceEqual(right))
            { for (var i = count - 1; i >= 0; i--) if (left[i] != right[i]) return matched + count - 1 - i; }
            matched += count;
        }
        return matched;
    }
    private static bool SplitsSequence(DocumentSnapshot snapshot, int offset) => offset > 0 && offset < snapshot.Length &&
        ((char.IsHighSurrogate(snapshot.TextSource.GetCharAt(offset - 1)) && char.IsLowSurrogate(snapshot.TextSource.GetCharAt(offset))) ||
         (snapshot.TextSource.GetCharAt(offset - 1) == '\r' && snapshot.TextSource.GetCharAt(offset) == '\n'));
}
