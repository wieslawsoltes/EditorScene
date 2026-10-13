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

    /// <summary>Visit frozen targets sequentially. False stops before later targets; cancellation
    /// and exceptions propagate. Earlier successful saves are not rolled back. The host resolves
    /// each identity, authorizes I/O and returns true for safely skipped clean/removed documents.
    /// Preserves the caller's synchronization context for UI-owned callbacks. Use an external
    /// operation coordinator to reject overlapping invocations; the immutable plan owns no lock.</summary>
    public async Task<bool> ExecuteAsync(Func<Guid, CancellationToken, Task<bool>> save, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(save);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var id in DocumentIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var completed = await save(id, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!completed) return false;
        }
        return true;
    }
}
