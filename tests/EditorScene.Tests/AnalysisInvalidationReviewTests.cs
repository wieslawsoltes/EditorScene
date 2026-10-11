using System.Collections.Concurrent;
using EditorScene.LanguageServices;
using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class AnalysisInvalidationReviewTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void SameRevisionInvalidationClearsOldResultsUntilReplacementCompletes(bool fail)
    {
        using var document = new EditorDocument("text"); var snapshot = document.Snapshot();
        var queue = new ConcurrentQueue<Action>(); var provider = new RefreshingLanguage();
        using var coordinator = new AnalysisCoordinator(document, provider, queue.Enqueue);
        Exception? failure = null; coordinator.Failed += (_, error) => failure = error;
        coordinator.RequestAnalysis(true); Drain(queue);
        Assert.That(coordinator.Current, Is.Not.Null);
        provider.Invalidate(snapshot.DocumentId, snapshot.Revision); Drain(queue);
        Assert.That(coordinator.Current, Is.Null); Assert.That(coordinator.Pending.IsCompleted, Is.False);
        var expected = LanguageAnalysis.Empty(snapshot);
        if (fail) provider.Next.SetException(new InvalidOperationException("refresh failed")); else provider.Next.SetResult(expected);
        coordinator.Pending.GetAwaiter().GetResult(); Drain(queue);
        if (fail) { Assert.That(coordinator.Current, Is.Null); Assert.That(failure?.Message, Is.EqualTo("refresh failed")); }
        else { Assert.That(coordinator.Current, Is.SameAs(expected)); Assert.That(failure, Is.Null); }
        Assert.That(document.Revision, Is.Zero); Assert.That(provider.Calls, Is.EqualTo(2));
    }
    [Test]
    public void ForeignAndWrongRevisionNotificationsDoNotClearValidResults()
    {
        using var document = new EditorDocument("text"); var snapshot = document.Snapshot();
        var provider = new RefreshingLanguage(); var queue = new ConcurrentQueue<Action>();
        using var coordinator = new AnalysisCoordinator(document, provider, queue.Enqueue);
        coordinator.RequestAnalysis(true); Drain(queue); var expected = coordinator.Current; var pending = coordinator.Pending;
        provider.Invalidate(Guid.NewGuid(), snapshot.Revision);
        provider.Invalidate(snapshot.DocumentId, snapshot.Revision - 1); provider.Invalidate(snapshot.DocumentId, snapshot.Revision + 1); Drain(queue);
        Assert.That(coordinator.Current, Is.SameAs(expected)); Assert.That(coordinator.Pending, Is.SameAs(pending)); Assert.That(provider.Calls, Is.EqualTo(1));
    }
    [Test]
    public void QueuedNotificationFromReplacedProviderCannotInvalidateItsReplacement()
    {
        using var document = new EditorDocument("text"); var snapshot = document.Snapshot();
        var provider = new RefreshingLanguage(); var queue = new ConcurrentQueue<Action>();
        using var coordinator = new AnalysisCoordinator(document, provider, queue.Enqueue);
        coordinator.RequestAnalysis(true); Drain(queue);
        provider.Invalidate(snapshot.DocumentId, snapshot.Revision);
        coordinator.Language = new PlainTextLanguage(); var newest = coordinator.Pending; Drain(queue);
        Assert.That(coordinator.Current, Is.Not.Null); Assert.That(coordinator.Current!.Revision, Is.EqualTo(snapshot.Revision));
        Assert.That(coordinator.Pending, Is.SameAs(newest)); Assert.That(provider.Calls, Is.EqualTo(1));
    }
    private static void Drain(ConcurrentQueue<Action> queue) { while (queue.TryDequeue(out var action)) action(); }
    private sealed class RefreshingLanguage : ISyntaxLanguage, INotifyLanguageAnalysisChanged
    {
        internal readonly TaskCompletionSource<LanguageAnalysis> Next = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Calls;
        public string Id => "refresh";
        public string DisplayName => Id;
        public IReadOnlyList<string> Extensions => [];
        public LanguageCapabilities Capabilities => LanguageCapabilities.Diagnostics;
        public event EventHandler<LanguageAnalysisInvalidated>? AnalysisInvalidated;
        internal void Invalidate(Guid id, long revision) => AnalysisInvalidated?.Invoke(this, new(id, revision));
        public Task<LanguageAnalysis> AnalyzeAsync(DocumentSnapshot snapshot, CancellationToken cancellationToken = default)
            => ++Calls == 1 ? Task.FromResult(LanguageAnalysis.Empty(snapshot)) : Next.Task;
    }
}
