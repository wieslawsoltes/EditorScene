using UnoEdit.Document;

namespace EditorScene.Text;

/// <summary>Whether a point stays before or follows text inserted at its exact position.</summary>
public enum PointTrackingMode { Negative, Positive }
/// <summary>Boundary behavior for insertions at the start and end of a tracked range.</summary>
public enum SpanTrackingMode { EdgeExclusive, EdgeInclusive, EdgeNegative, EdgePositive }

/// <summary>A validated immutable UTF-16 range in a particular document snapshot.</summary>
public sealed record SnapshotSpan
{
    public SnapshotSpan(DocumentSnapshot snapshot, int offset, int length)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (offset < 0 || length < 0 || offset > snapshot.Length || length > snapshot.Length - offset)
            throw new ArgumentOutOfRangeException(nameof(offset), "The range is outside the snapshot.");
        Snapshot = snapshot; Offset = offset; Length = length;
    }
    public DocumentSnapshot Snapshot { get; }
    public int Offset { get; }
    public int Length { get; }
    public int End => Offset + Length;
    public bool IsEmpty => Length == 0;
    public string Text => Snapshot.GetText(Offset, Length);
    /// <summary>Ranges are half-open; an empty range contains no characters.</summary>
    public bool Contains(int offset) => offset >= Offset && offset < End;
    public SnapshotSpan TranslateTo(DocumentSnapshot target, SpanTrackingMode mode = SpanTrackingMode.EdgeExclusive) => new TrackingSpan(this, mode).GetSpan(target);
    public TrackingSpan Track(SpanTrackingMode mode = SpanTrackingMode.EdgeExclusive) => new(this, mode);
}

/// <summary>
/// Retains a version checkpoint, not a mutable document or UI control. Translation
/// can run on language-service workers, forwards or backwards through edit history.
/// </summary>
public sealed class TrackingPoint
{
    private readonly Guid _documentId;
    private readonly ITextSourceVersion _version;
    private readonly int _offset;
    public TrackingPoint(DocumentSnapshot snapshot, int offset, PointTrackingMode mode = PointTrackingMode.Positive)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if ((uint)offset > (uint)snapshot.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        _documentId = snapshot.DocumentId; _version = SnapshotTranslation.VersionOf(snapshot); _offset = offset; Mode = mode;
    }
    public PointTrackingMode Mode { get; }
    public int GetOffset(DocumentSnapshot target)
    {
        var version = SnapshotTranslation.CompatibleVersion(target, _documentId, _version);
        return _version.MoveOffsetTo(version, _offset, Mode == PointTrackingMode.Negative ? AnchorMovementType.BeforeInsertion : AnchorMovementType.AfterInsertion);
    }
    public TextPosition GetPosition(DocumentSnapshot target) => target.GetPosition(GetOffset(target));
}

/// <summary>
/// Range tracking across edits with explicit boundary affinity. Insertions inside
/// a non-empty range are included. Deleting the complete range collapses it to a
/// point. An empty EdgeExclusive range remains empty after a boundary insertion,
/// on its positive side; EdgeInclusive includes the inserted text instead.
/// </summary>
public sealed class TrackingSpan
{
    private readonly Guid _documentId;
    private readonly ITextSourceVersion _version;
    private readonly int _start, _end;
    public TrackingSpan(SnapshotSpan span, SpanTrackingMode mode = SpanTrackingMode.EdgeExclusive)
    {
        ArgumentNullException.ThrowIfNull(span);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        _documentId = span.Snapshot.DocumentId; _version = SnapshotTranslation.VersionOf(span.Snapshot);
        _start = span.Offset; _end = span.End; Mode = mode;
    }
    public SpanTrackingMode Mode { get; }
    public SnapshotSpan GetSpan(DocumentSnapshot target)
    {
        var version = SnapshotTranslation.CompatibleVersion(target, _documentId, _version);
        var startMovement = Mode is SpanTrackingMode.EdgeInclusive or SpanTrackingMode.EdgeNegative ? AnchorMovementType.BeforeInsertion : AnchorMovementType.AfterInsertion;
        var endMovement = Mode is SpanTrackingMode.EdgeInclusive or SpanTrackingMode.EdgePositive ? AnchorMovementType.AfterInsertion : AnchorMovementType.BeforeInsertion;
        var start = _version.MoveOffsetTo(version, _start, startMovement);
        var end = _version.MoveOffsetTo(version, _end, endMovement);
        return new SnapshotSpan(target, start, Math.Max(0, end - start));
    }
}

public static class SnapshotTranslation
{
    public static int TranslateOffsetTo(this DocumentSnapshot source, DocumentSnapshot target, int offset, PointTrackingMode mode = PointTrackingMode.Positive) => new TrackingPoint(source, offset, mode).GetOffset(target);
    public static TrackingPoint CreateTrackingPoint(this DocumentSnapshot source, int offset, PointTrackingMode mode = PointTrackingMode.Positive) => new(source, offset, mode);
    public static SnapshotSpan GetSpan(this DocumentSnapshot source, int offset, int length) => new(source, offset, length);
    internal static ITextSourceVersion VersionOf(DocumentSnapshot snapshot) => snapshot.TextSource.Version ?? throw new InvalidOperationException("The snapshot has no version checkpoint.");
    internal static ITextSourceVersion CompatibleVersion(DocumentSnapshot target, Guid documentId, ITextSourceVersion source)
    {
        ArgumentNullException.ThrowIfNull(target);
        var version = VersionOf(target);
        if (target.DocumentId != documentId || !source.BelongsToSameDocumentAs(version)) throw new ArgumentException("Snapshots must belong to the same document.", nameof(target));
        return version;
    }
}
