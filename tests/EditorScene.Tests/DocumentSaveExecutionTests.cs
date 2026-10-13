using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class DocumentSaveExecutionTests
{
    private static DocumentSavePlan Plan() => DocumentSavePlan.Create(Enumerable.Range(0, 3).Select(_ => new DocumentSaveCandidate(Guid.NewGuid(), true)));
    [Test]
    public async Task FalseStopsTheBatchWithoutRepeatingEarlierAcceptedSaves()
    {
        var plan = Plan(); var called = new List<Guid>();
        var result = await plan.ExecuteAsync((id, _) => { called.Add(id); return Task.FromResult(called.Count != 2); });
        Assert.That(result, Is.False); Assert.That(called, Is.EqualTo(plan.DocumentIds.Take(2)));
    }
    [Test]
    public async Task CallbacksAreAwaitedInOrderBeforeTheNextTarget()
    {
        var plan = Plan(); var called = new List<Guid>(); var active = 0;
        var result = await plan.ExecuteAsync(async (id, _) =>
        {
            Assert.That(Interlocked.Increment(ref active), Is.EqualTo(1));
            await Task.Yield(); called.Add(id); Interlocked.Decrement(ref active); return true;
        });
        Assert.That(result, Is.True); Assert.That(called, Is.EqualTo(plan.DocumentIds));
    }
    [Test]
    public void CancellationAfterASaveStopsLaterTargetsEvenWhenTheCallbackReturnsTrue()
    {
        var plan = Plan(); using var cancel = new CancellationTokenSource(); var called = new List<Guid>();
        Assert.ThrowsAsync<OperationCanceledException>(async () => await plan.ExecuteAsync((id, token) =>
        { Assert.That(token, Is.EqualTo(cancel.Token)); called.Add(id); cancel.Cancel(); return Task.FromResult(true); }, cancel.Token));
        Assert.That(called, Is.EqualTo(plan.DocumentIds.Take(1)));
    }
    [Test]
    public async Task FailedExecutionDoesNotMutateThePlanOrPreventLaterExplicitUse()
    {
        var plan = Plan(); var called = 0;
        Assert.ThrowsAsync<IOException>(async () => await plan.ExecuteAsync((_, _) => { called++; throw new IOException("provider failed"); }));
        Assert.That(called, Is.EqualTo(1));
        var retried = new List<Guid>();
        Assert.That(await plan.ExecuteAsync((id, _) => { retried.Add(id); return Task.FromResult(true); }), Is.True);
        Assert.That(retried, Is.EqualTo(plan.DocumentIds));
    }
    [Test]
    public void PreCancelledEmptyPlansAndNullCallbacksAreRejected()
    {
        var empty = DocumentSavePlan.Create([]);
        Assert.ThrowsAsync<OperationCanceledException>(async () => await empty.ExecuteAsync((_, _) => Task.FromResult(true), new CancellationToken(true)));
        Assert.ThrowsAsync<ArgumentNullException>(async () => await empty.ExecuteAsync(null!));
    }
    [Test]
    public async Task ExistingCoordinatorRejectsAnOverlappingSaveAllWithoutInvokingItsCallback()
    {
        var plan = Plan(); var coordinator = new DocumentOperationCoordinator();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); var calls = 0;
        var first = coordinator.TryRunAsync(token => plan.ExecuteAsync(async (_, _) => { calls++; await release.Task; return true; }, token));
        var second = await coordinator.TryRunAsync(token => plan.ExecuteAsync((_, _) => { calls += 100; return Task.FromResult(true); }, token));
        Assert.That(second, Is.False); Assert.That(calls, Is.EqualTo(1)); release.SetResult(true);
        Assert.That(await first, Is.True); Assert.That(calls, Is.EqualTo(3)); Assert.That(coordinator.IsBusy, Is.False);
    }
}
