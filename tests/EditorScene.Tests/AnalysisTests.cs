using System.Collections.Concurrent;
using EditorScene.LanguageServices;
using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class AnalysisTests
{
    [Test]
    public void CoordinatorRejectsStaleResultsEvenWhenProviderIgnoresCancellation()
    {
        using var document = new EditorDocument("first"); var provider = new DeferredLanguage();
        var dispatch = new ConcurrentQueue<Action>();
        using var coordinator = new AnalysisCoordinator(document, provider, dispatch.Enqueue) { Debounce = TimeSpan.Zero };
        var published = new List<long>(); coordinator.Published += (_, result) => published.Add(result.Revision);
        coordinator.RequestAnalysis(true); var first = provider.Requests[0];
        document.Buffer.Text = "second"; var second = provider.Requests[1];
        second.Completion.SetResult(LanguageAnalysis.Empty(second.Snapshot)); coordinator.Pending.GetAwaiter().GetResult();
        while (dispatch.TryDequeue(out var action)) action();
        first.Completion.SetResult(LanguageAnalysis.Empty(first.Snapshot));
        Assert.That(published, Is.EqualTo(new[] { document.Revision }));
        Assert.That(coordinator.Current?.Revision, Is.EqualTo(document.Revision));
    }
    [Test]
    public void CoordinatorValidatesProviderIdentityAndDoesNotPublishAfterDisposal()
    {
        using var document = new EditorDocument(); var provider = new DeferredLanguage(); var dispatch = new ConcurrentQueue<Action>();
        using var coordinator = new AnalysisCoordinator(document, provider, dispatch.Enqueue);
        Exception? failure = null; coordinator.Failed += (_, error) => failure = error;
        coordinator.RequestAnalysis(true); var request = provider.Requests[0];
        request.Completion.SetResult(LanguageAnalysis.Empty(request.Snapshot) with { DocumentId = Guid.NewGuid() });
        coordinator.Pending.GetAwaiter().GetResult(); while (dispatch.TryDequeue(out var action)) action();
        Assert.That(failure, Is.TypeOf<InvalidOperationException>());
        coordinator.RequestAnalysis(true); request = provider.Requests[1];
        request.Completion.SetResult(LanguageAnalysis.Empty(request.Snapshot)); coordinator.Pending.GetAwaiter().GetResult();
        coordinator.Dispose(); while (dispatch.TryDequeue(out var action)) action();
        Assert.That(coordinator.Current, Is.Null);
    }
    [Test]
    public void BracketMatchingIgnoresStringsAndRejectsMismatchedNesting()
    {
        using var document = new EditorDocument("{\"}\" [()]} ([)]"); var snapshot = document.Snapshot();
        var map = new BracketMap(snapshot, [new(new(1, 3), "string")]);
        Assert.That(map.AtCaret(0), Is.EqualTo(new[] { new SourceRange(0, 1), new SourceRange(9, 1) }));
        Assert.That(map.AtCaret(12), Is.Empty);
    }
    private sealed class DeferredLanguage : ISyntaxLanguage
    {
        internal sealed record Request(DocumentSnapshot Snapshot, TaskCompletionSource<LanguageAnalysis> Completion);
        internal List<Request> Requests { get; } = [];
        public string Id => "deferred"; public string DisplayName => Id; public IReadOnlyList<string> Extensions => [];
        public LanguageCapabilities Capabilities => LanguageCapabilities.Diagnostics;
        public Task<LanguageAnalysis> AnalyzeAsync(DocumentSnapshot snapshot, CancellationToken cancellationToken = default)
        { var completion = new TaskCompletionSource<LanguageAnalysis>(TaskCreationOptions.RunContinuationsAsynchronously); Requests.Add(new(snapshot, completion)); return completion.Task; }
    }
}
