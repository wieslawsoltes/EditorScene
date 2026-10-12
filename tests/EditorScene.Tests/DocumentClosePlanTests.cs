using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class DocumentClosePlanTests
{
    private static readonly Guid A = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid B = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid C = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [TestCase(DocumentCloseScope.Current, "B")]
    [TestCase(DocumentCloseScope.Others, "AC")]
    [TestCase(DocumentCloseScope.All, "ABC")]
    [TestCase(DocumentCloseScope.ToRight, "C")]
    public void ScopeSelectsStableIdsInOriginalOrder(DocumentCloseScope scope, string expected)
    {
        var plan = DocumentClosePlan.Create([A, B, C], B, scope);
        Assert.That(plan.TargetDocumentId, Is.EqualTo(B)); Assert.That(plan.Scope, Is.EqualTo(scope));
        Assert.That(plan.DocumentIds, Is.EqualTo(expected.Select(ch => ch == 'A' ? A : ch == 'B' ? B : C)));
    }

    [TestCase(DocumentCloseScope.Others)]
    [TestCase(DocumentCloseScope.ToRight)]
    public void SingleDocumentCanProduceAnEmptyPlan(DocumentCloseScope scope)
        => Assert.That(DocumentClosePlan.Create([A], A, scope).DocumentIds, Is.Empty);

    [Test]
    public void InputMutationsCannotRetargetAnExistingPlan()
    {
        var source = new List<Guid> { A, B, C }; var plan = DocumentClosePlan.Create(source, B, DocumentCloseScope.All);
        source.Clear(); source.Add(Guid.NewGuid());
        Assert.That(plan.DocumentIds, Is.EqualTo(new[] { A, B, C }));
    }

    [Test]
    public void EnumerationHappensOnceBeforeThePlanIsReturned()
    {
        var reads = 0;
        IEnumerable<Guid> Source() { reads++; yield return A; yield return B; }
        var plan = DocumentClosePlan.Create(Source(), A, DocumentCloseScope.Others);
        Assert.That(reads, Is.EqualTo(1)); Assert.That(plan.DocumentIds, Is.EqualTo(new[] { B }));
        _ = plan.DocumentIds.ToArray(); Assert.That(reads, Is.EqualTo(1));
    }

    [TestCase(DocumentCloseScope.Current)]
    [TestCase(DocumentCloseScope.Others)]
    [TestCase(DocumentCloseScope.All)]
    [TestCase(DocumentCloseScope.ToRight)]
    public void StaleTargetNeverProducesDestructiveWork(DocumentCloseScope scope)
        => Assert.Throws<ArgumentException>(() => DocumentClosePlan.Create([A, B], C, scope));

    [Test]
    public void RejectsMalformedMembershipAndScope()
    {
        Assert.Throws<ArgumentNullException>(() => DocumentClosePlan.Create(null!, A, DocumentCloseScope.All));
        Assert.Throws<ArgumentException>(() => DocumentClosePlan.Create([A, A], A, DocumentCloseScope.All));
        Assert.Throws<ArgumentException>(() => DocumentClosePlan.Create([A, Guid.Empty], A, DocumentCloseScope.Current));
        Assert.Throws<ArgumentException>(() => DocumentClosePlan.Create([], A, DocumentCloseScope.All));
        Assert.Throws<ArgumentException>(() => DocumentClosePlan.Create([A], Guid.Empty, DocumentCloseScope.All));
        Assert.Throws<ArgumentOutOfRangeException>(() => DocumentClosePlan.Create([A], A, (DocumentCloseScope)999));
    }

    [Test]
    public async Task OverlappingRequestsAreRejectedWithoutCallingOrQueuingTheirCallbacks()
    {
        var coordinator = new DocumentOperationCoordinator();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = coordinator.TryRunAsync(async _ => { await release.Task; return true; });
        Assert.That(coordinator.IsBusy, Is.True); var invoked = 0;
        Assert.That(await coordinator.TryRunAsync(_ => { invoked++; return Task.FromResult(true); }), Is.False);
        Assert.That(invoked, Is.Zero); Assert.That(coordinator.IsBusy, Is.True);
        release.SetResult(true); Assert.That(await first, Is.True);
        Assert.That(coordinator.IsBusy, Is.False); Assert.That(invoked, Is.Zero);
        Assert.That(await coordinator.TryRunAsync(_ => { invoked++; return Task.FromResult(true); }), Is.True);
        Assert.That(invoked, Is.EqualTo(1));
    }

    [Test]
    public async Task ReentrantCallbackDoesNotDeadlockOrExecuteNestedWork()
    {
        var coordinator = new DocumentOperationCoordinator(); var nested = false;
        var result = await coordinator.TryRunAsync(async _ =>
        {
            Assert.That(await coordinator.TryRunAsync(_ => { nested = true; return Task.FromResult(true); }), Is.False);
            return true;
        });
        Assert.That(result, Is.True); Assert.That(nested, Is.False); Assert.That(coordinator.IsBusy, Is.False);
    }

    [Test]
    public async Task FailureAndCancellationReleaseCoordinationWithoutSwallowingErrors()
    {
        var coordinator = new DocumentOperationCoordinator();
        Assert.ThrowsAsync<IOException>(async () => { await coordinator.TryRunAsync(_ => throw new IOException("write failed")); });
        Assert.That(coordinator.IsBusy, Is.False);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); var called = false;
        Assert.ThrowsAsync<OperationCanceledException>(async () =>
        { await coordinator.TryRunAsync(_ => { called = true; return Task.FromResult(true); }, cancelled.Token); });
        Assert.That(called, Is.False); Assert.That(coordinator.IsBusy, Is.False);
        Assert.ThrowsAsync<OperationCanceledException>(async () =>
        { await coordinator.TryRunAsync(_ => Task.FromCanceled<bool>(cancelled.Token)); });
        Assert.That(coordinator.IsBusy, Is.False);
        Assert.That(await coordinator.TryRunAsync(_ => Task.FromResult(false)), Is.False);
        Assert.That(await coordinator.TryRunAsync(_ => Task.FromResult(true)), Is.True);
    }
}
