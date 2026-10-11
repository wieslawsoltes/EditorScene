using System.Collections.Concurrent;
using EditorScene.LanguageServices;
using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class AnalysisReentrancyTests
{
    [Test]
    public void SynchronousPublicationCannotOverwriteANewerPendingTask()
    {
        using var document = new EditorDocument("text"); var slow = new DeferredLanguage();
        using var coordinator = new AnalysisCoordinator(document, new PlainTextLanguage(), action => action());
        coordinator.Published += (_, _) => coordinator.Language = slow;
        coordinator.RequestAnalysis(true);
        Assert.That(slow.Requests.Count, Is.EqualTo(1)); Assert.That(coordinator.Pending.IsCompleted, Is.False);
        var pending = coordinator.Pending; slow.Requests[0].Source.SetResult(LanguageAnalysis.Empty(slow.Requests[0].Snapshot));
        pending.GetAwaiter().GetResult(); Assert.That(coordinator.Pending, Is.SameAs(pending));
    }
    [Test]
    public void CancellationCallbackEditKeepsTheLatestSnapshotRequest()
    {
        using var document = new EditorDocument("old"); var provider = new DeferredLanguage(); var dispatch = new ConcurrentQueue<Action>();
        var first = true;
        provider.OnRequest = token =>
        {
            if (!first) return; first = false;
            token.Register(() => document.Buffer.Insert(0, "new "));
        };
        using var coordinator = new AnalysisCoordinator(document, provider, dispatch.Enqueue) { Debounce = TimeSpan.Zero };
        coordinator.RequestAnalysis(true); var originalPending = coordinator.Pending;
        coordinator.RequestAnalysis(true);
        Assert.That(provider.Requests.Count, Is.EqualTo(2)); Assert.That(provider.Requests[1].Snapshot.Text, Is.EqualTo("new old"));
        var latestPending = coordinator.Pending;
        provider.Requests[1].Source.SetResult(LanguageAnalysis.Empty(provider.Requests[1].Snapshot)); latestPending.GetAwaiter().GetResult();
        provider.Requests[0].Source.SetResult(LanguageAnalysis.Empty(provider.Requests[0].Snapshot)); originalPending.GetAwaiter().GetResult();
        while (dispatch.TryDequeue(out var action)) action();
        Assert.That(coordinator.Current!.Revision, Is.EqualTo(document.Revision)); Assert.That(coordinator.Pending, Is.SameAs(latestPending));
    }
    [Test]
    public void CancellationCallbackDisposalCannotScheduleAnotherOperation()
    {
        using var document = new EditorDocument("text"); var provider = new DeferredLanguage();
        using var coordinator = new AnalysisCoordinator(document, provider, action => action());
        provider.OnRequest = token => token.Register(coordinator.Dispose);
        coordinator.RequestAnalysis(true); var pending = coordinator.Pending;
        coordinator.RequestAnalysis(true);
        Assert.That(provider.Requests.Count, Is.EqualTo(1));
        provider.Requests[0].Source.SetResult(LanguageAnalysis.Empty(provider.Requests[0].Snapshot)); pending.GetAwaiter().GetResult();
        Assert.That(coordinator.Current, Is.Null);
    }
    [Test]
    public void EditingImmediatelyClearsPublishedAnalysisDuringDebounce()
    {
        using var document = new EditorDocument("text");
        using var coordinator = new AnalysisCoordinator(document, new PlainTextLanguage(), action => action()) { Debounce = TimeSpan.FromSeconds(10) };
        coordinator.RequestAnalysis(true); Assert.That(coordinator.Current, Is.Not.Null);
        document.Buffer.Insert(0, "new "); Assert.That(coordinator.Current, Is.Null);
    }
    [Test]
    public void DisposalReleasesTheLastPublishedResult()
    {
        using var document = new EditorDocument("text");
        using var coordinator = new AnalysisCoordinator(document, new PlainTextLanguage(), action => action());
        coordinator.RequestAnalysis(true); Assert.That(coordinator.Current, Is.Not.Null);
        coordinator.Dispose(); Assert.That(coordinator.Current, Is.Null);
    }
    [Test]
    public void SameProviderAssignmentDoesNotCancelOrRestartPendingWork()
    {
        using var document = new EditorDocument(); var provider = new DeferredLanguage();
        using var coordinator = new AnalysisCoordinator(document, provider, action => action());
        coordinator.RequestAnalysis(true); var pending = coordinator.Pending; coordinator.Language = provider;
        Assert.That(provider.Requests.Count, Is.EqualTo(1)); Assert.That(coordinator.Pending, Is.SameAs(pending));
        provider.Requests[0].Source.SetResult(LanguageAnalysis.Empty(provider.Requests[0].Snapshot)); pending.GetAwaiter().GetResult();
    }
    [TestCase(-1)]
    [TestCase(60001)]
    public void InvalidDebounceIsRejectedBeforeScheduling(int milliseconds)
    {
        using var document = new EditorDocument();
        using var coordinator = new AnalysisCoordinator(document, new PlainTextLanguage(), action => action());
        Assert.Throws<ArgumentOutOfRangeException>(() => coordinator.Debounce = TimeSpan.FromMilliseconds(milliseconds));
        Assert.That(coordinator.Pending.IsCompletedSuccessfully, Is.True);
    }
    [Test]
    public void WrongThreadCallsDoNotReplaceTheCurrentProvider()
    {
        using var document = new EditorDocument(); var provider = new PlainTextLanguage();
        using var coordinator = new AnalysisCoordinator(document, provider, action => action());
        Task.Run(() =>
        {
            Assert.Throws<InvalidOperationException>(() => coordinator.RequestAnalysis());
            Assert.Throws<InvalidOperationException>(() => coordinator.Language = new PlainTextLanguage());
        }).GetAwaiter().GetResult();
        Assert.That(coordinator.Language, Is.SameAs(provider));
    }
    private sealed class DeferredLanguage : ISyntaxLanguage
    {
        internal sealed record Request(DocumentSnapshot Snapshot, TaskCompletionSource<LanguageAnalysis> Source);
        internal readonly List<Request> Requests = [];
        internal Action<CancellationToken>? OnRequest;
        public string Id => "deferred";
        public string DisplayName => Id;
        public IReadOnlyList<string> Extensions => [];
        public LanguageCapabilities Capabilities => LanguageCapabilities.Diagnostics;
        public Task<LanguageAnalysis> AnalyzeAsync(DocumentSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            var source = new TaskCompletionSource<LanguageAnalysis>(TaskCreationOptions.RunContinuationsAsynchronously);
            Requests.Add(new(snapshot, source)); OnRequest?.Invoke(cancellationToken); return source.Task;
        }
    }
}
