using System.Collections.Immutable;

namespace EditorScene.Text;

public readonly record struct DocumentSaveCandidate(Guid Id, bool IsModified);

/// <summary>Frozen Save All membership in tab order. Only dirty identities are included;
/// filenames, live documents and callbacks are not retained. Hosts re-resolve identities
/// and skip already saved or removed documents before every asynchronous write.</summary>
public sealed class DocumentSavePlan
{
    public ImmutableArray<Guid> DocumentIds { get; }
    private DocumentSavePlan(ImmutableArray<Guid> documentIds) => DocumentIds = documentIds;

    public static DocumentSavePlan Create(IEnumerable<DocumentSaveCandidate> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var seen = new HashSet<Guid>(); var targets = ImmutableArray.CreateBuilder<Guid>();
        foreach (var document in documents)
        {
            if (document.Id == Guid.Empty || !seen.Add(document.Id))
                throw new ArgumentException("Document identities must be nonempty and unique, including clean entries.", nameof(documents));
            if (document.IsModified) targets.Add(document.Id);
        }
        return new(targets.ToImmutable());
    }
}
