using System.Reflection;
using EditorScene.Languages.DotNet;
using EditorScene.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class DotNetLazyModelTests
{
    [Test]
    public async Task SyntaxOnlyRequestsDoNotConstructCompilationOrSemanticModels()
    {
        using var doc = new EditorDocument("class C { int M() => 1; }"); var first = doc.Snapshot();
        doc.Buffer.Replace(doc.Buffer.Text.IndexOf('1'), 1, "2"); var second = doc.Snapshot();
        var language = new DotNetLanguage();
        await language.GetSelectionRangesAsync(first, 6); await language.GetSelectionRangesAsync(second, 6);
        Assert.That(language.CacheStatistics.ModelBuilds, Is.EqualTo(2));
        Assert.That(language.CacheStatistics.CompilationBuilds, Is.Zero); Assert.That(language.CacheStatistics.SemanticModelBuilds, Is.Zero);
        await language.AnalyzeAsync(second);
        Assert.That(language.CacheStatistics.CompilationBuilds, Is.EqualTo(1)); Assert.That(language.CacheStatistics.SemanticModelBuilds, Is.EqualTo(1));
        var analysis = await language.AnalyzeAsync(second);
        Assert.That(await language.AnalyzeAsync(second), Is.SameAs(analysis));
        Assert.That(language.CacheStatistics.CompilationBuilds, Is.EqualTo(1)); Assert.That(language.CacheStatistics.AnalysisBuilds, Is.EqualTo(1));
    }
    [Test]
    public async Task ConcurrentSemanticRequestsForceTheLazyStateOnlyOnce()
    {
        using var doc = new EditorDocument("class C { int M() => 1; }"); var snapshot = doc.Snapshot(); var language = new DotNetLanguage();
        await language.GetSelectionRangesAsync(snapshot, 6);
        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => language.AnalyzeAsync(snapshot)));
        Assert.That(language.CacheStatistics.CompilationBuilds, Is.EqualTo(1)); Assert.That(language.CacheStatistics.SemanticModelBuilds, Is.EqualTo(1));
        Assert.That(language.CacheStatistics.AnalysisBuilds, Is.EqualTo(1));
    }
    [Test]
    public async Task SmallForwardEditActuallyReusesUnchangedSyntaxNodes()
    {
        var text = "class C {\n" + string.Concat(Enumerable.Range(0, 100).Select(i => $"int Method{i}() => {i};\n")) + "}";
        using var doc = new EditorDocument(text); var first = doc.Snapshot(); doc.Buffer.Replace(text.LastIndexOf("99;", StringComparison.Ordinal), 2, "98"); var second = doc.Snapshot();
        var language = new DotNetLanguage(); await language.GetSelectionRangesAsync(first, 6); var firstTree = CachedTree(language, first);
        await language.GetSelectionRangesAsync(second, 6); var secondTree = CachedTree(language, second);
        var before = firstTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
        var after = secondTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
        Assert.That(before.Length, Is.EqualTo(after.Length));
        Assert.That(before[0].IsIncrementallyIdenticalTo(after[0]), Is.True, "The incremental parser must reuse unchanged green syntax nodes, not just report a counter.");
        Assert.That(before[^1].IsIncrementallyIdenticalTo(after[^1]), Is.False);
        Assert.That(secondTree.GetText().ToString(), Is.EqualTo(text.Remove(text.LastIndexOf("99;", StringComparison.Ordinal), 2).Insert(text.LastIndexOf("99;", StringComparison.Ordinal), "98")));
    }
    private static SyntaxTree CachedTree(DotNetLanguage language, DocumentSnapshot snapshot)
    {
        var model = typeof(DotNetLanguage).GetMethod("GetModel", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(language, [snapshot, CancellationToken.None])!;
        return (SyntaxTree)model.GetType().GetProperty("Tree", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(model)!;
    }
}
