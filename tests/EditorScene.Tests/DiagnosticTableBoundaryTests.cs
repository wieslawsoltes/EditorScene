using EditorScene.LanguageServices;
using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class DiagnosticTableBoundaryTests
{
    [Test]
    public void UninitializedDiagnosticArraysAndNullSnapshotChecksFailExplicitly()
    {
        using var document = new EditorDocument("text"); var snapshot = document.Snapshot();
        Assert.Throws<ArgumentException>(() => new DiagnosticTable(snapshot, LanguageAnalysis.Empty(snapshot) with { Diagnostics = default }));
        var table = new DiagnosticTable(snapshot, LanguageAnalysis.Empty(snapshot));
        Assert.Throws<ArgumentNullException>(() => table.IsCurrent(null!));
    }
    [Test]
    public async Task ImmutableProjectionCanRunAfterTheLiveDocumentIsDisposed()
    {
        var document = new EditorDocument("first\r\nsecond", "worker.txt"); var snapshot = document.Snapshot();
        var analysis = LanguageAnalysis.Empty(snapshot) with { Diagnostics = [new("E", "message", IssueSeverity.Error, new(7, 6))] };
        document.Dispose();
        var table = await Task.Run(() => new DiagnosticTable(snapshot, analysis));
        Assert.That(table.Rows.Single().Line, Is.EqualTo(2)); Assert.That(table.Rows.Single().Character, Is.EqualTo(1));
        Assert.That(table.Filter(query: "WORKER.TXT").Length, Is.EqualTo(1)); Assert.That(table.IsCurrent(snapshot), Is.True);
    }
}
