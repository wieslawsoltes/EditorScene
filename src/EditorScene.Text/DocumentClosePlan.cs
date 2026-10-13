using System.Collections.Immutable;

namespace EditorScene.Text;

public enum DocumentCloseScope { Current, Others, All, ToRight }

/// <summary>A stable, insertion-ordered snapshot of document identities to close.
/// Contains no documents, editors, native controls or file handles. The host still
/// decides whether each document may close and must stop if that decision is declined.</summary>
public sealed class DocumentClosePlan
{
    public Guid TargetDocumentId { get; }
    public DocumentCloseScope Scope { get; }
    public ImmutableArray<Guid> DocumentIds { get; }

    private DocumentClosePlan(Guid targetDocumentId, DocumentCloseScope scope, ImmutableArray<Guid> ids)
    { TargetDocumentId = targetDocumentId; Scope = scope; DocumentIds = ids; }

    /// <summary>Enumerates membership once and validates every ID before selecting any.
    /// A missing target is rejected even for All, so a stale tab cannot close a newer workspace.</summary>
    public static DocumentClosePlan Create(IEnumerable<Guid> documentIds, Guid targetDocumentId, DocumentCloseScope scope)
    {
        ArgumentNullException.ThrowIfNull(documentIds);
        if (!Enum.IsDefined(scope)) throw new ArgumentOutOfRangeException(nameof(scope));
        if (targetDocumentId == Guid.Empty) throw new ArgumentException("A nonempty target identity is required.", nameof(targetDocumentId));
        var snapshot = documentIds.ToImmutableArray();
        var seen = new HashSet<Guid>(); var targetIndex = -1;
        for (var i = 0; i < snapshot.Length; i++)
        {
            var id = snapshot[i];
            if (id == Guid.Empty || !seen.Add(id)) throw new ArgumentException("Document identities must be nonempty and unique.", nameof(documentIds));
            if (id == targetDocumentId) targetIndex = i;
        }
        if (targetIndex < 0) throw new ArgumentException("The target document is no longer present.", nameof(targetDocumentId));
        var selected = ImmutableArray.CreateBuilder<Guid>();
        for (var i = 0; i < snapshot.Length; i++)
        {
            var include = scope switch
            {
                DocumentCloseScope.Current => i == targetIndex,
                DocumentCloseScope.Others => i != targetIndex,
                DocumentCloseScope.All => true,
                DocumentCloseScope.ToRight => i > targetIndex,
                _ => false
            };
            if (include) selected.Add(snapshot[i]);
        }
        return new(targetDocumentId, scope, selected.ToImmutable());
    }
}

/// <summary>Runs at most one host operation at a time. Busy requests return false,
/// rather than being queued against a potentially different active document. This
/// is coordination, not a lock on document edits or a transaction/rollback service.</summary>
public sealed class DocumentOperationCoordinator
{
    private int _busy;
    public bool IsBusy => Volatile.Read(ref _busy) != 0;

    /// <summary>The operation starts on the caller's thread; its own awaits retain
    /// their normal context. Callers own UI dispatch, cancellation and error policy.
    /// False means either a busy rejection or the operation's own false result.
    /// Cancellation/failure propagates, and always releases the coordinator.</summary>
    public async Task<bool> TryRunAsync(Func<CancellationToken, Task<bool>> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return false;
        try { return await operation(cancellationToken).ConfigureAwait(false); }
        finally { Volatile.Write(ref _busy, 0); }
    }
}
