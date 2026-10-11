using System.Collections.Immutable;
using EditorScene.Text;

namespace EditorScene.LanguageServices;

/// <summary>Atomic completion edits against an immutable snapshot, including additional imports.</summary>
public sealed class CompletionEditPlan
{
    private readonly DocumentSnapshot _source;
    private readonly SourceRange _range;
    private readonly string _insertText;
    private readonly ImmutableArray<TextEdit> _additional;
    public CompletionEditPlan(DocumentSnapshot source, int caret, SourceRange range, string insertText, IEnumerable<TextEdit>? additional = null)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(insertText);
        ValidateRange(source, range);
        if (caret < range.Offset || caret > range.End) throw new ArgumentException("The completion range must contain the request caret.", nameof(range));
        _source = source; _range = range; _insertText = insertText; _additional = (additional ?? []).ToImmutableArray();
        ValidateEdits(source, _additional.Prepend(new TextEdit(range.Offset, range.Length, insertText)));
    }
    public CompletionCommit Prepare(DocumentSnapshot target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (_source.DocumentId != target.DocumentId) throw new InvalidOperationException("The completion belongs to another document.");
        var oldVersion = _source.TextSource.Version ?? throw new InvalidOperationException("A versioned snapshot is required.");
        var newVersion = target.TextSource.Version ?? throw new InvalidOperationException("A versioned snapshot is required.");
        if (!oldVersion.BelongsToSameDocumentAs(newVersion) || oldVersion.CompareAge(newVersion) > 0)
            throw new InvalidOperationException("Completion cannot be applied to an earlier snapshot.");
        var start = _range.Offset; var end = _range.End;
        // Only filtering edits inside the original completion span are permitted.
        // An unrelated edit must invalidate this plan, even if it is later undone.
        foreach (var change in oldVersion.GetChangesTo(newVersion))
        {
            if (change.Offset < start || change.Offset > end || change.RemovalLength > end - change.Offset)
                throw new InvalidOperationException("The document changed outside the completion range.");
            end = checked(end + change.InsertionLength - change.RemovalLength);
        }
        var primary = new TextEdit(start, end - start, _insertText);
        var edits = ImmutableArray.CreateBuilder<TextEdit>(); edits.Add(primary);
        var caret = checked(start + _insertText.Length);
        foreach (var additional in _additional)
        {
            var mapped = _source.GetSpan(additional.Offset, additional.Length).TranslateTo(target, SpanTrackingMode.EdgeExclusive);
            if (mapped.Length != additional.Length || mapped.Text != _source.GetText(additional.Offset, additional.Length))
                throw new InvalidOperationException("An additional completion edit became stale.");
            edits.Add(new(mapped.Offset, mapped.Length, additional.NewText));
            if (mapped.Offset < start) caret = checked(caret + additional.NewText.Length - mapped.Length);
        }
        ValidateEdits(target, edits);
        return new(edits.OrderBy(e => e.Offset).ToImmutableArray(), caret);
    }
    private static void ValidateRange(DocumentSnapshot source, SourceRange range)
    {
        if (range.Offset < 0 || range.Length < 0 || range.Offset > source.Length || range.Length > source.Length - range.Offset)
            throw new ArgumentOutOfRangeException(nameof(range));
        foreach (var offset in new[] { range.Offset, range.End })
            if (offset > 0 && offset < source.Length && char.IsHighSurrogate(source.TextSource.GetCharAt(offset - 1)) && char.IsLowSurrogate(source.TextSource.GetCharAt(offset)))
                throw new ArgumentException("An edit boundary splits a surrogate pair.", nameof(range));
    }
    private static void ValidateEdits(DocumentSnapshot source, IEnumerable<TextEdit> changes)
    {
        var edits = changes.OrderBy(e => e.Offset).ThenBy(e => e.Length).ToArray();
        foreach (var edit in edits) { ArgumentNullException.ThrowIfNull(edit.NewText); ValidateRange(source, new(edit.Offset, edit.Length)); }
        for (var i = 1; i < edits.Length; i++)
            if (edits[i - 1].End > edits[i].Offset || edits[i - 1].Offset == edits[i].Offset)
                throw new ArgumentException("Completion edits overlap or share insertion points.", nameof(changes));
    }
}
public sealed record CompletionCommit(ImmutableArray<TextEdit> Edits, int CaretOffset);

/// <summary>Optional provider extension for diagnostics arriving after an analysis request completed.</summary>
public interface INotifyLanguageAnalysisChanged
{
    event EventHandler<LanguageAnalysisInvalidated>? AnalysisInvalidated;
}
public sealed record LanguageAnalysisInvalidated(Guid DocumentId, long Revision);
