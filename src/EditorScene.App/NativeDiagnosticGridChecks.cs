using EditorScene.Controls;
using EditorScene.LanguageServices;
using EditorScene.Rendering.Skia;
using EditorScene.Text;
using Microsoft.UI.Xaml.Automation;

namespace EditorScene.App;

internal static class NativeDiagnosticGridChecks
{
    internal static void Run(List<string> checks)
    {
        void Check(bool valid, string name) { if (!valid) throw new InvalidOperationException("Native diagnostic grid: " + name); checks.Add(name); }
        using var document = new EditorDocument("abcdefghijklmnop\nabcdefghijklmnop", "grid.cs");
        using var editor = new SyntaxEditor(); editor.SetDocumentAndLanguage(document, new FixtureLanguage());
        using var grid = new DiagnosticList { Editor = editor };
        var table = grid.Table;
        Check(grid.SortColumn == DiagnosticSortColumn.ProviderOrder && grid.VisibleRows.Select(r => r.Code).SequenceEqual(new[] { "Z", "A", "B" }), "Native diagnostic grid starts in provider order");
        grid.SortBy(DiagnosticSortColumn.Code);
        Check(ReferenceEquals(grid.Table, table) && grid.VisibleRows.Select(r => r.Code).SequenceEqual(new[] { "A", "B", "Z" }), "Native header sort reuses the immutable projection");
        var selected = grid.VisibleRows[1]; grid.RowsView.SelectedItem = selected;
        grid.SortBy(DiagnosticSortColumn.Code, DiagnosticSortDirection.Descending);
        Check(grid.VisibleRows.Select(r => r.Code).SequenceEqual(new[] { "Z", "B", "A" }) && ReferenceEquals(grid.RowsView.SelectedItem, selected), "Reordering diagnostic rows retains the selected diagnostic identity");
        grid.SortBy(DiagnosticSortColumn.Line);
        Check(grid.VisibleRows.Select(r => r.Code).SequenceEqual(new[] { "Z", "B", "A" }), "Equal native line sort keys preserve provider order");
        var projections = grid.ProjectionBuildCount; var filters = grid.FilterBuildCount; var sorts = grid.SortBuildCount;
        for (var i = 0; i < 100; i++) editor.CaretOffset = i % 2;
        Check(grid.ProjectionBuildCount == projections && grid.FilterBuildCount == filters && grid.SortBuildCount == sorts, "Caret movement does not rebuild sorted diagnostic results");
        var layoutEvents = 0; grid.ColumnLayoutChanged += (_, _) => layoutEvents++;
        grid.SetColumnWidth(DiagnosticSortColumn.Code, 144); grid.SetColumnWidth(DiagnosticSortColumn.Code, 144);
        Check(grid.GetColumnWidth(DiagnosticSortColumn.Code) == 144 && layoutEvents == 1 && grid.FilterBuildCount == filters, "Column resizing changes layout without rebuilding or sorting diagnostic data");
        var rejected = 0;
        foreach (var width in new[] { double.NaN, double.PositiveInfinity, -1d, 5000d })
            try { grid.SetColumnWidth(DiagnosticSortColumn.Code, width); } catch (ArgumentOutOfRangeException) { rejected++; }
        Check(rejected == 4 && grid.GetColumnWidth(DiagnosticSortColumn.Code) == 144, "Invalid diagnostic widths fail before mutating column state");
        grid.ResetColumnWidths();
        Check(grid.GetColumnWidth(DiagnosticSortColumn.Code) == 82 && grid.GetColumnWidth(DiagnosticSortColumn.File) == 160, "Default diagnostic column layout can be restored");
        Check(DiagnosticList.Columns.All(c => grid.GetColumnGrip(c).UseSystemFocusVisuals && AutomationProperties.GetName(grid.GetSortButton(c)).Contains(c.ToString())), "All native diagnostic headers and resize grips expose keyboard and automation labels");
        using var otherDocument = new EditorDocument("other", "other.txt"); using var other = new SyntaxEditor(); other.SetDocumentAndLanguage(otherDocument, new PlainTextLanguage());
        var rebound = false;
        void Rebind(object? sender, EventArgs args) { if (rebound) return; rebound = true; grid.Editor = other; }
        grid.RowsChanged += Rebind; grid.SortBy(DiagnosticSortColumn.Code); grid.RowsChanged -= Rebind;
        Check(rebound && ReferenceEquals(grid.Editor, other) && grid.VisibleCount == 0 && grid.Table?.FileName == "other.txt", "Reentrant diagnostic publication cannot restore a previously bound document");
        grid.Editor = editor; grid.SortBy(DiagnosticSortColumn.ProviderOrder);
        Check(grid.VisibleRows.Select(r => r.Code).SequenceEqual(new[] { "Z", "A", "B" }), "Provider ordering can be restored after sorting and rebinding");
        var calls = 0; grid.RowsChanged += (_, _) => calls++; grid.Dispose(); editor.CaretOffset = 2;
        var disposedGuards = 0;
        try { grid.SortBy(DiagnosticSortColumn.Code); } catch (ObjectDisposedException) { disposedGuards++; }
        try { grid.SetColumnWidth(DiagnosticSortColumn.Code, 120); } catch (ObjectDisposedException) { disposedGuards++; }
        try { grid.ApplyTheme(EditorTheme.Dark); } catch (ObjectDisposedException) { disposedGuards++; }
        Check(calls == 0 && disposedGuards == 3 && grid.VisibleCount == 0 && grid.RealizedRowCount == 0, "Disposed diagnostic grids release queries, rows and subscriptions and reject mutation");
    }
    private sealed class FixtureLanguage : ISyntaxLanguage
    {
        public string Id => "diagnostic-grid-fixture";
        public string DisplayName => Id;
        public IReadOnlyList<string> Extensions => [];
        public LanguageCapabilities Capabilities => LanguageCapabilities.Diagnostics;
        public Task<LanguageAnalysis> AnalyzeAsync(DocumentSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(LanguageAnalysis.Empty(snapshot) with { Diagnostics = [new("Z", "Last code", IssueSeverity.Information, new(0, 1)), new("A", "First code", IssueSeverity.Error, new(17, 1)), new("B", "Middle code", IssueSeverity.Warning, new(2, 1))] });
        }
    }
}
