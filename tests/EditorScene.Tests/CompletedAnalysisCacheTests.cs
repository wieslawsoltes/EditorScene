using EditorScene.Languages.DotNet;
using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class CompletedAnalysisCacheTests
{
    [Test]
    public async Task CompletedExactSnapshotRequestsReuseOneTaskWithoutAllocating()
    {
        using var document = new EditorDocument("class C { int M() => 1; }"); var snapshot = document.Snapshot(); var language = new DotNetLanguage();
        var expected = await language.AnalyzeAsync(snapshot); var task = language.AnalyzeAsync(snapshot);
        Assert.That(task.IsCompletedSuccessfully, Is.True); Assert.That(task.Result, Is.SameAs(expected));
        for (var i = 0; i < 20; i++) _ = language.AnalyzeAsync(snapshot);
        var before = GC.GetAllocatedBytesForCurrentThread(); var reused = true;
        for (var i = 0; i < 1000; i++) reused &= ReferenceEquals(task, language.AnalyzeAsync(snapshot));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(reused, Is.True); Assert.That(allocated, Is.Zero);
        Assert.That(language.CacheStatistics.AnalysisBuilds, Is.EqualTo(1)); Assert.That(language.CacheStatistics.ModelBuilds, Is.EqualTo(1));
    }
    [Test]
    public async Task PreCancelledTokenCannotReturnACachedSuccessfulAnalysis()
    {
        using var document = new EditorDocument("class C {}"); var snapshot = document.Snapshot(); var language = new DotNetLanguage();
        await language.AnalyzeAsync(snapshot);
        var cancelled = language.AnalyzeAsync(snapshot, new CancellationToken(true));
        Assert.That(cancelled.IsCanceled, Is.True);
        Assert.ThrowsAsync<TaskCanceledException>(async () => await cancelled);
        Assert.That(language.AnalyzeAsync(snapshot).IsCompletedSuccessfully, Is.True);
        Assert.That(language.CacheStatistics.AnalysisBuilds, Is.EqualTo(1));
    }
    [Test]
    public async Task NewRevisionAndFileNameNeverReceiveTheOldCompletedTask()
    {
        using var document = new EditorDocument("class C {}", "first.cs"); var before = document.Snapshot();
        document.Buffer.Insert(9, "int X; "); var after = document.Snapshot(); document.FileName = "second.cs"; var renamed = document.Snapshot();
        var language = new DotNetLanguage(); await language.AnalyzeAsync(before); var first = language.AnalyzeAsync(before);
        var updated = await language.AnalyzeAsync(after); var second = language.AnalyzeAsync(after);
        Assert.That(second, Is.Not.SameAs(first)); Assert.That(updated.Revision, Is.EqualTo(after.Revision));
        await language.AnalyzeAsync(renamed); var third = language.AnalyzeAsync(renamed);
        Assert.That(third, Is.Not.SameAs(second)); Assert.That(language.CacheStatistics.AnalysisBuilds, Is.EqualTo(3));
    }
    [Test]
    public async Task ConcurrentWarmCallersSharePublishedTaskAndImmutableResult()
    {
        using var document = new EditorDocument("class C {}"); var snapshot = document.Snapshot(); var language = new DotNetLanguage();
        await language.AnalyzeAsync(snapshot); var shared = language.AnalyzeAsync(snapshot);
        var tasks = await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => Task.Run(() =>
        {
            var task = language.AnalyzeAsync(snapshot);
            return ReferenceEquals(task, shared) && task.IsCompletedSuccessfully;
        })));
        Assert.That(tasks.All(value => value), Is.True); Assert.That(language.CacheStatistics.AnalysisBuilds, Is.EqualTo(1));
    }
}
