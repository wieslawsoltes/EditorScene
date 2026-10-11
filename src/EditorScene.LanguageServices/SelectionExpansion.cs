using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using EditorScene.Text;

namespace EditorScene.LanguageServices;

/// <summary>Optional syntax-aware selection service. Ranges must use the requested immutable UTF-16 snapshot.</summary>
public interface ISelectionRangeProvider
{
    Task<ImmutableArray<SourceRange>> GetSelectionRangesAsync(DocumentSnapshot snapshot, int offset, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reversible structural selection history, independent of the UI. Captures document
/// identity/revision and selection direction, but retains neither a live document nor
/// its text. Repeated expansion/contraction performs no allocations.
/// </summary>
public sealed class SelectionExpansion
{
    public const int MaximumCandidates = 4096;
    private readonly Guid _documentId;
    private readonly long _revision;
    private readonly ImmutableArray<SelectionRange> _steps;
    private int _index;
    public SelectionExpansion(DocumentSnapshot snapshot, SelectionRange initialSelection, IEnumerable<SourceRange> candidates)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(candidates);
        ValidateRange(snapshot, new(initialSelection.Start, checked(initialSelection.End - initialSelection.Start)));
        _documentId = snapshot.DocumentId; _revision = snapshot.Revision;
        var values = new List<SourceRange>();
        foreach (var range in candidates)
        {
            if (values.Count == MaximumCandidates) throw new ArgumentException("The selection candidate limit was exceeded.", nameof(candidates));
            ValidateRange(snapshot, range); values.Add(range);
        }
        values.Sort(static (a, b) => a.Length != b.Length ? a.Length.CompareTo(b.Length) : a.Offset.CompareTo(b.Offset));
        var steps = ImmutableArray.CreateBuilder<SelectionRange>(); steps.Add(initialSelection);
        var previous = new SourceRange(initialSelection.Start, initialSelection.Length);
        var reverse = initialSelection.Active < initialSelection.Anchor;
        foreach (var range in values)
        {
            // Crossing alternatives are not parents. Equal ranges add no new step.
            if (range == previous || !Encloses(range, previous)) continue;
            steps.Add(reverse ? new(range.End, range.Offset) : new(range.Offset, range.End)); previous = range;
        }
        if (previous.Offset != 0 || previous.Length != snapshot.Length)
            steps.Add(reverse ? new(snapshot.Length, 0) : new(0, snapshot.Length));
        _steps = steps.ToImmutable();
    }
    public int Depth => _index;
    public int StepCount => _steps.Length;
    public bool CanExpand => _index + 1 < _steps.Length;
    public bool CanContract => _index > 0;
    public SelectionRange Current => _steps[_index];
    public bool IsCurrent(DocumentSnapshot snapshot, SelectionRange selection)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.DocumentId == _documentId && snapshot.Revision == _revision && selection == Current;
    }
    public bool TryExpand(DocumentSnapshot snapshot, SelectionRange selection, out SelectionRange result)
    {
        VerifyCurrent(snapshot, selection); if (CanExpand) { result = _steps[++_index]; return true; }
        result = selection; return false;
    }
    public bool TryContract(DocumentSnapshot snapshot, SelectionRange selection, out SelectionRange result)
    {
        VerifyCurrent(snapshot, selection); if (CanContract) { result = _steps[--_index]; return true; }
        result = selection; return false;
    }
    private void VerifyCurrent(DocumentSnapshot snapshot, SelectionRange selection)
    { if (!IsCurrent(snapshot, selection)) throw new InvalidOperationException("The selection history belongs to another document, revision or caret selection."); }
    public static bool Encloses(SourceRange outer, SourceRange inner) => outer.Offset <= inner.Offset && outer.End >= inner.End;
    public static void ValidateRange(DocumentSnapshot snapshot, SourceRange range)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (range.Offset < 0 || range.Length < 0 || range.Offset > snapshot.Length || range.Length > snapshot.Length - range.Offset)
            throw new ArgumentOutOfRangeException(nameof(range), "The selection is outside the snapshot.");
        ValidateBoundary(snapshot, range.Offset); ValidateBoundary(snapshot, range.End);
    }
    public static void ValidateBoundary(DocumentSnapshot snapshot, int offset)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if ((uint)offset > (uint)snapshot.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        if (offset == 0 || offset == snapshot.Length) return;
        var before = snapshot.TextSource.GetCharAt(offset - 1); var after = snapshot.TextSource.GetCharAt(offset);
        if (before == '\r' && after == '\n' || char.IsHighSurrogate(before) && char.IsLowSurrogate(after))
            throw new ArgumentException("A structural selection boundary splits a surrogate pair or CRLF.", nameof(offset));
    }
}

/// <summary>Bounded-memory fallback for providers without structural selection: word, line, analysis ranges and document.</summary>
public static class SelectionCandidates
{
    public static ImmutableArray<SourceRange> Basic(DocumentSnapshot snapshot, int offset, CancellationToken cancellationToken = default)
    {
        SelectionExpansion.ValidateBoundary(snapshot, offset); cancellationToken.ThrowIfCancellationRequested();
        var values = ImmutableArray.CreateBuilder<SourceRange>();
        using var reader = snapshot.CreateReader(offset);
        var start = offset; var end = offset;
        while (reader.TryReadPreviousRune(out var rune))
        {
            cancellationToken.ThrowIfCancellationRequested(); if (!IsWord(rune)) break; start = reader.Position;
        }
        reader.Position = offset;
        while (reader.TryReadRune(out var rune))
        {
            cancellationToken.ThrowIfCancellationRequested(); if (!IsWord(rune)) break; end = reader.Position;
        }
        if (end > start) values.Add(new(start, end - start));
        var line = snapshot.GetLineFromOffset(offset);
        if (line.Length > 0) values.Add(new(line.Offset, line.Length));
        if (line.DelimiterLength > 0) values.Add(new(line.Offset, line.TotalLength));
        values.Add(new(0, snapshot.Length)); return values.ToImmutable();
    }
    public static ImmutableArray<SourceRange> FromAnalysis(DocumentSnapshot snapshot, int offset, LanguageAnalysis? analysis, CancellationToken cancellationToken = default)
    {
        var values = Basic(snapshot, offset, cancellationToken).ToBuilder();
        if (analysis is null || analysis.DocumentId != snapshot.DocumentId || analysis.Revision != snapshot.Revision) return values.ToImmutable();
        void Add(SourceRange range)
        {
            if (range.Offset <= offset && range.End >= offset && range.Length > 0)
            {
                if (values.Count == SelectionExpansion.MaximumCandidates) throw new InvalidOperationException("The selection candidate limit was exceeded.");
                values.Add(range);
            }
        }
        foreach (var span in analysis.Classifications) { cancellationToken.ThrowIfCancellationRequested(); Add(span.Range); }
        foreach (var fold in analysis.Foldings) { cancellationToken.ThrowIfCancellationRequested(); Add(fold.Range); }
        // Explicit stack, not recursive traversal of server-supplied symbol trees.
        var stack = new Stack<NavigationSymbol>();
        for (var i = analysis.Symbols.Length - 1; i >= 0; i--) stack.Push(analysis.Symbols[i]);
        while (stack.TryPop(out var symbol))
        {
            cancellationToken.ThrowIfCancellationRequested(); Add(symbol.Range); Add(symbol.SelectionRange);
            for (var i = symbol.Children.Length - 1; i >= 0; i--) stack.Push(symbol.Children[i]);
        }
        return values.ToImmutable();
    }
    private static bool IsWord(Rune rune) => Rune.IsLetterOrDigit(rune) || Rune.GetUnicodeCategory(rune) is
        UnicodeCategory.LetterNumber or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark or UnicodeCategory.ConnectorPunctuation;
}
