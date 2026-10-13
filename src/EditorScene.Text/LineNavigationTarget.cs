using System.Globalization;

namespace EditorScene.Text;

public enum LineNavigationError { None, InvalidFormat, LineOutOfRange, CharacterOutOfRange, SplitSurrogate }

/// <summary>A validated, revision-bound navigation position. User line/character numbers
/// are one-based UTF-16 positions, not tab-expanded, shaped, grapheme or wrapped columns.
/// The value retains no snapshot or live document. Default is never a valid target.</summary>
public readonly record struct LineNavigationTarget
{
    public Guid DocumentId { get; }
    public long Revision { get; }
    public int LineNumber { get; }
    public int Character { get; }
    public int Offset { get; }
    private LineNavigationTarget(DocumentSnapshot snapshot, int line, int character, int offset)
    { DocumentId = snapshot.DocumentId; Revision = snapshot.Revision; LineNumber = line; Character = character; Offset = offset; }

    public bool IsCurrent(DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return DocumentId != Guid.Empty && snapshot.DocumentId == DocumentId && snapshot.Revision == Revision;
    }

    /// <summary>Parse line or line:character, allowing surrounding token whitespace but
    /// rejecting signs, grouping separators, overflow, out-of-range coordinates and positions
    /// inside a UTF-16 surrogate pair. Uses the immutable line index without flattening source.
    /// Warm valid queries allocate no managed objects; initial line-index construction may scan.</summary>
    public static bool TryParse(DocumentSnapshot snapshot, ReadOnlySpan<char> input,
        out LineNavigationTarget target, out LineNavigationError error)
    {
        ArgumentNullException.ThrowIfNull(snapshot); target = default; error = LineNavigationError.InvalidFormat;
        if (input.Length > 64) return false;
        input = input.Trim();
        var separator = input.IndexOf(':');
        var lineText = separator < 0 ? input : input[..separator].Trim();
        var character = 1;
        if (!int.TryParse(lineText, NumberStyles.None, CultureInfo.InvariantCulture, out var number)) return false;
        if (separator >= 0 && !int.TryParse(input[(separator + 1)..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out character)) return false;
        if (number < 1 || number > snapshot.LineCount) { error = LineNavigationError.LineOutOfRange; return false; }
        var line = snapshot.GetLine(number - 1);
        if (character < 1 || character - 1 > line.Length) { error = LineNavigationError.CharacterOutOfRange; return false; }
        var offset = line.Offset + character - 1;
        if (offset > line.Offset && offset < line.Offset + line.Length
            && char.IsHighSurrogate(snapshot.TextSource.GetCharAt(offset - 1)) && char.IsLowSurrogate(snapshot.TextSource.GetCharAt(offset)))
        { error = LineNavigationError.SplitSurrogate; return false; }
        target = new(snapshot, number, character, offset); error = LineNavigationError.None; return true;
    }
}
