using EditorScene.Controls;
using EditorScene.LanguageServices;
using Microsoft.UI.Xaml.Controls;

namespace EditorScene.App;

public sealed partial class EditorPage
{
    // Observation only, used exclusively by the existing opt-in diagnostic bridge.
    private DiagnosticGridState CaptureDiagnosticGrid() => new()
    {
        SortColumn = _errors.SortColumn.ToString(), SortDirection = _errors.SortDirection.ToString(),
        SortBuilds = _errors.SortBuildCount, RealizedRows = _errors.RealizedRowCount,
        FilterText = _errors.FilterBox.Text, ClearEnabled = _errors.ClearFilterButton.IsEnabled,
        Widths = DiagnosticList.Columns.Select(_errors.GetEffectiveColumnWidth).ToArray(),
        RowWidths = Descendants(_errors.RowsView).OfType<DiagnosticRowPresenter>().Where(r => r.Row is not null)
            .Take(16).Select(r => ((Grid)r.Content).ColumnDefinitions.Select(c => c.Width.Value).ToArray()).ToArray(),
        RowIcons = Descendants(_errors.RowsView).OfType<EditorIcon>().Take(64).Select(i => i.Kind.ToString()).ToArray(),
        VisibleLines = _errors.VisibleRows.Take(512).Select(r => r.Line).ToArray(),
        VisibleOffsets = _errors.VisibleRows.Take(512).Select(r => r.Diagnostic.Range.Offset).ToArray(),
        SelectedOffset = (_errors.RowsView.SelectedItem as DiagnosticTableRow)?.Diagnostic.Range.Offset ?? -1,
        HorizontalOffset = _errors.HorizontalScroller.HorizontalOffset,
        HorizontalExtent = _errors.HorizontalScroller.ScrollableWidth
    };
}
internal sealed class DiagnosticGridState
{
    public string SortColumn { get; set; } = "";
    public string SortDirection { get; set; } = "";
    public long SortBuilds { get; set; }
    public int RealizedRows { get; set; }
    public string FilterText { get; set; } = "";
    public bool ClearEnabled { get; set; }
    public double[] Widths { get; set; } = [];
    public double[][] RowWidths { get; set; } = [];
    public string[] RowIcons { get; set; } = [];
    public int[] VisibleLines { get; set; } = [];
    public int[] VisibleOffsets { get; set; } = [];
    public int SelectedOffset { get; set; }
    public double HorizontalOffset { get; set; }
    public double HorizontalExtent { get; set; }
}
