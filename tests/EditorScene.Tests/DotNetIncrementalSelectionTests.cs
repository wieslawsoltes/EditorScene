using System.Reflection;
using EditorScene.LanguageServices;
using EditorScene.Languages.DotNet;
using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class DotNetIncrementalSelectionTests
{
    [TestCase(false, "class C { int M() => 1 + 2; }", "1 + 2")]
    [TestCase(true, "Class C\n Function M() As Integer\n Return 1 + 2\n End Function\nEnd Class", "1 + 2")]
    public async Task SyntaxSelectionIncludesTokenExpressionAndWholeDocument(bool vb, string text, string expression)
    {
        using var doc = new EditorDocument(text, vb ? "test.vb" : "test.cs"); var s = doc.Snapshot(); var language = new DotNetLanguage(vb);
        var offset = text.IndexOf(expression, StringComparison.Ordinal);
        var ranges = await language.GetSelectionRangesAsync(s, offset);
        Assert.That(ranges, Does.Contain(new SourceRange(offset, 1))); Assert.That(ranges, Does.Contain(new SourceRange(offset, expression.Length)));
        Assert.That(ranges, Does.Contain(new SourceRange(0, s.Length))); Assert.That(IsFlattened(s), Is.False);
    }
    [Test]
    public async Task CommentsAndDelimiterInteriorsAreSelectable()
    {
        const string text = "class C { /* comment */ int M() { return (1 + 2); } }";
        using var doc = new EditorDocument(text); var s = doc.Snapshot(); var language = new DotNetLanguage();
        var comment = await language.GetSelectionRangesAsync(s, text.IndexOf("comment", StringComparison.Ordinal));
        Assert.That(comment.Any(r => s.GetText(r.Offset, r.Length) == "/* comment */"), Is.True);
        var expression = await language.GetSelectionRangesAsync(s, text.IndexOf("1 + 2", StringComparison.Ordinal));
        Assert.That(expression.Any(r => s.GetText(r.Offset, r.Length) == "(1 + 2)"), Is.True);
        Assert.That(expression.Any(r => s.GetText(r.Offset, r.Length) == "1 + 2"), Is.True);
    }
    [TestCase(false, "class C { int M() => 1; }", "\"bad\"")]
    [TestCase(true, "Class C\n Function M() As Integer\n Return 1\n End Function\nEnd Class", "\"bad\"")]
    public async Task SmallEditReusesCompilerAndMatchesFreshSemanticResults(bool vb, string text, string replacement)
    {
        using var doc = new EditorDocument(text, vb ? "test.vb" : "test.cs"); var before = doc.Snapshot();
        doc.Buffer.Replace(text.IndexOf('1'), 1, replacement); var after = doc.Snapshot();
        var language = new DotNetLanguage(vb); await language.AnalyzeAsync(before);
        var incremental = await language.AnalyzeAsync(after); var fresh = await new DotNetLanguage(vb).AnalyzeAsync(after);
        Assert.That(incremental.Diagnostics, Is.EqualTo(fresh.Diagnostics)); Assert.That(incremental.Classifications, Is.EqualTo(fresh.Classifications));
        Assert.That(incremental.Diagnostics.Any(d => d.Severity == IssueSeverity.Error), Is.True);
        Assert.That(language.CacheStatistics.ColdParses, Is.EqualTo(1)); Assert.That(language.CacheStatistics.IncrementalUpdates, Is.EqualTo(1));
        Assert.That(language.CacheStatistics.CompilationReuses, Is.EqualTo(1)); Assert.That(IsFlattened(before), Is.False); Assert.That(IsFlattened(after), Is.False);
    }
    [Test]
    public async Task ConcurrentViewsShareOneModelAndOneCompletedAnalysis()
    {
        using var doc = new EditorDocument("class C { int Value = 1; }"); var s = doc.Snapshot(); var language = new DotNetLanguage();
        var analyses = Enumerable.Range(0, 12).Select(_ => language.AnalyzeAsync(s)).ToArray();
        var selections = Enumerable.Range(0, 12).Select(_ => language.GetSelectionRangesAsync(s, 7)).ToArray();
        await Task.WhenAll(analyses.Cast<Task>().Concat(selections));
        Assert.That(language.CacheStatistics.ModelBuilds, Is.EqualTo(1)); Assert.That(language.CacheStatistics.AnalysisBuilds, Is.EqualTo(1));
        foreach (var task in analyses) Assert.That(task.Result, Is.SameAs(analyses[0].Result));
    }
    [Test]
    public async Task OlderRequestsCannotDisplaceNewerModelsAndFileRenameInvalidates()
    {
        using var doc = new EditorDocument("class C {}", "first.cs"); var old = doc.Snapshot();
        doc.Buffer.Insert(8, "int X; "); var newer = doc.Snapshot(); doc.FileName = "renamed.cs"; var renamed = doc.Snapshot();
        var language = new DotNetLanguage(); var result = await language.AnalyzeAsync(newer);
        await language.AnalyzeAsync(old); Assert.That(await language.AnalyzeAsync(newer), Is.SameAs(result));
        await language.AnalyzeAsync(renamed); var builds = language.CacheStatistics.ModelBuilds;
        await language.GetSelectionRangesAsync(renamed, 0); Assert.That(language.CacheStatistics.ModelBuilds, Is.EqualTo(builds));
    }
    [Test]
    public async Task RetainedCacheIsBoundedAcrossManyDocuments()
    {
        var language = new DotNetLanguage();
        for (var i = 0; i < 12; i++)
        {
            using var doc = new EditorDocument("class C {}", $"file{i}.cs"); var snapshot = doc.Snapshot();
            await language.GetSelectionRangesAsync(snapshot, 0);
        }
        Assert.That(language.CacheStatistics.CachedDocuments, Is.EqualTo(8));
    }
    [Test]
    public void CancelledModelRequestDoesNotPopulateCache()
    {
        using var doc = new EditorDocument("class C {}"); var snapshot = doc.Snapshot(); var language = new DotNetLanguage();
        Assert.ThrowsAsync<TaskCanceledException>(() => language.GetSelectionRangesAsync(snapshot, 0, new CancellationToken(true)));
        Assert.That(language.CacheStatistics.ModelBuilds, Is.Zero);
    }
    private static bool IsFlattened(DocumentSnapshot snapshot) => ((Lazy<string>)typeof(DocumentSnapshot).GetField("_text", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(snapshot)!).IsValueCreated;
}
