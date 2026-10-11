using System.Collections.Immutable;
using System.Reflection;
using EditorScene.LanguageServices;
using EditorScene.Text;
using NUnit.Framework;

namespace EditorScene.Tests;

[TestFixture]
public sealed class DiagnosticTableTests
{
    private static LanguageAnalysis Analysis(DocumentSnapshot snapshot, params EditorDiagnostic[] diagnostics) => LanguageAnalysis.Empty(snapshot) with { Diagnostics = diagnostics.ToImmutableArray() };
    private static EditorDiagnostic D(string code, IssueSeverity severity, int offset, int length = 0) => new(code, "Message " + code, severity, new(offset, length));

    [Test]
    public void ProjectionPreservesOrderReferencesAndUtf16PositionsWithoutFlattening()
    {
        using var document = new EditorDocument("A🙂\r\nB\rC\n", "unicode.cs"); var snapshot = document.Snapshot();
        var diagnostics = new[] { D("E1", IssueSeverity.Error, 5), D("W1", IssueSeverity.Warning, 1, 2), D("I1", IssueSeverity.Information, snapshot.Length) };
        var table = new DiagnosticTable(snapshot, Analysis(snapshot, diagnostics));
        Assert.That(table.Rows.Select(r => (r.Line, r.Character)), Is.EqualTo(new[] { (2, 1), (1, 2), (4, 1) }));
        Assert.That(table.Rows[0].Diagnostic, Is.SameAs(diagnostics[0]));
        Assert.That((table.ErrorCount, table.WarningCount, table.InformationCount), Is.EqualTo((1, 1, 1)));
        Assert.That(table.Rows.All(r => r.FileName == "unicode.cs"), Is.True);
        Assert.That(((Lazy<string>)typeof(DocumentSnapshot).GetField("_text", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(snapshot)!).IsValueCreated, Is.False);
    }
    [Test]
    public void FilteringCombinesSeverityAndOrdinalSearchAndReusesRows()
    {
        using var document = new EditorDocument("abc", "Test.cs"); var snapshot = document.Snapshot();
        var table = new DiagnosticTable(snapshot, Analysis(snapshot, D("E1", IssueSeverity.Error, 0), D("W1", IssueSeverity.Warning, 1), D("I1", IssueSeverity.Information, 2)));
        var filtered = table.Filter(DiagnosticVisibility.Error | DiagnosticVisibility.Warning, "message w1");
        Assert.That(filtered.Length, Is.EqualTo(1)); Assert.That(filtered[0], Is.SameAs(table.Rows[1]));
        Assert.That(table.Filter(query: "TEST.CS").Length, Is.EqualTo(3));
        Assert.That(table.Filter(query: "error")[0], Is.SameAs(table.Rows[0]));
        Assert.That(table.Filter(query: "absent"), Is.Empty); Assert.That(table.Filter(DiagnosticVisibility.None), Is.Empty);
        Assert.That(table.Rows.Length, Is.EqualTo(3));
    }
    [Test]
    public void UnfilteredWarmRequestsReuseTheExactImmutableArrayWithoutAllocating()
    {
        using var document = new EditorDocument("abc"); var snapshot = document.Snapshot();
        var table = new DiagnosticTable(snapshot, Analysis(snapshot, D("E", IssueSeverity.Error, 0)));
        for (var i = 0; i < 20; i++) _ = table.Filter();
        var bytes = GC.GetAllocatedBytesForCurrentThread(); var shared = true;
        for (var i = 0; i < 1000; i++) shared &= table.Filter() == table.Rows;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
        Assert.That(shared, Is.True); Assert.That(allocated, Is.Zero);
    }
    [Test]
    public void StaleForeignAndRenamedSnapshotsCannotNavigateOldRows()
    {
        using var document = new EditorDocument("abc", "first.cs"); using var foreign = new EditorDocument("abc", "first.cs");
        var snapshot = document.Snapshot(); var analysis = Analysis(snapshot, D("E", IssueSeverity.Error, 0)); var table = new DiagnosticTable(snapshot, analysis);
        Assert.That(table.IsCurrent(snapshot), Is.True); Assert.That(table.IsCurrent(foreign.Snapshot()), Is.False);
        Assert.Throws<ArgumentException>(() => new DiagnosticTable(foreign.Snapshot(), analysis));
        document.FileName = "second.cs"; Assert.That(table.IsCurrent(document.Snapshot()), Is.False);
        document.Buffer.Insert(0, "x"); var edited = document.Snapshot(); Assert.That(table.IsCurrent(edited), Is.False);
        Assert.Throws<ArgumentException>(() => new DiagnosticTable(edited, analysis));
        document.Buffer.UndoStack.Undo(); Assert.That(table.IsCurrent(document.Snapshot()), Is.False);
    }
    [TestCase(-1, 0)]
    [TestCase(0, -1)]
    [TestCase(4, 0)]
    [TestCase(1, int.MaxValue)]
    public void OutOfRangeDiagnosticsAreRejectedBeforePublishingARow(int offset, int length)
    {
        using var document = new EditorDocument("abc"); var snapshot = document.Snapshot();
        Assert.Throws<ArgumentException>(() => new DiagnosticTable(snapshot, Analysis(snapshot, D("ok", IssueSeverity.Error, 0), D("invalid", IssueSeverity.Error, offset, length))));
    }
    [Test]
    public void InvalidMetadataAndFilterFlagsAreRejected()
    {
        using var document = new EditorDocument(); var snapshot = document.Snapshot();
        Assert.Throws<ArgumentException>(() => new DiagnosticTable(snapshot, Analysis(snapshot, D("E", (IssueSeverity)17, 0))));
        Assert.Throws<ArgumentException>(() => new DiagnosticTable(snapshot, Analysis(snapshot, new EditorDiagnostic(null!, "message", IssueSeverity.Error, new(0, 0)))));
        Assert.Throws<ArgumentException>(() => new DiagnosticTable(snapshot, Analysis(snapshot, new EditorDiagnostic("E", null!, IssueSeverity.Error, new(0, 0)))));
        var table = new DiagnosticTable(snapshot, LanguageAnalysis.Empty(snapshot));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.Filter((DiagnosticVisibility)8)); Assert.That(table.Rows, Is.Empty);
    }
    [Test]
    public void CancellationIsObservedEvenOnTheCachedFastPath()
    {
        using var document = new EditorDocument(); var snapshot = document.Snapshot(); var analysis = LanguageAnalysis.Empty(snapshot);
        var token = new CancellationToken(true);
        Assert.Throws<OperationCanceledException>(() => new DiagnosticTable(snapshot, analysis, token));
        var table = new DiagnosticTable(snapshot, analysis);
        Assert.Throws<OperationCanceledException>(() => table.Filter(cancellationToken: token));
    }
}
