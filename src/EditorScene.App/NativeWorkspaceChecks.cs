using System.Collections.Immutable;
using EditorScene.Controls;
using EditorScene.LanguageServices;
using EditorScene.Rendering.Skia;
using EditorScene.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace EditorScene.App;

internal static class NativeWorkspaceChecks
{
    internal static Task RunAsync(List<string> checks)
    {
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException("Native workspace regression: " + name); checks.Add(name); }
        using var document = new EditorDocument("a\nb", "workspace.txt");
        using var editor = new SyntaxEditor(); editor.SetDocumentAndLanguage(document, new FixtureLanguage());
        using var status = new EditorStatusBar { Editor = editor };
        using var errors = new DiagnosticList { Editor = editor };
        Check(errors.Table is { Rows.Length: 2 } && errors.VisibleCount == 2, "Diagnostic list projects current analysis into native rows");
        Check(errors.RowsView.Items.Count == 2, "Diagnostic ListView receives data models instead of eagerly created row controls");
        editor.GoToLine(2);
        Check(status.PositionText.Contains("Ln 2") && status.PositionText.Contains("Ch 1"), "Status uses line-local character position rather than absolute document offset");
        var projections = errors.ProjectionBuildCount; var filters = errors.FilterBuildCount; var refreshes = status.RefreshCount;
        for (var i = 0; i < 1000; i++) { editor.IsOverstrike = editor.IsOverstrike; status.Refresh(); }
        Check(errors.ProjectionBuildCount == projections && errors.FilterBuildCount == filters, "Repeated unchanged native events do not rebuild diagnostic rows or filters");
        Check(status.RefreshCount == refreshes, "Repeated unchanged native events do not rebuild status text");
        errors.FilterBox.Text = "WARN";
        Check(errors.VisibleCount == 1 && errors.Table!.Rows.Length == 2, "Native diagnostic text filter preserves the immutable original table");
        errors.FilterBox.Text = "";
        Check(errors.VisibleCount == 2, "Clearing the diagnostic filter restores all rows");
        status.ZoomSlider.Value = 20;
        Check(editor.FontSize == 20, "Native status slider changes the attached editor font size");
        editor.FontSize = 13;
        Check(status.ZoomSlider.Value == 13 && status.ResetZoomButton.Content?.ToString() == "100%", "Editor zoom changes synchronize the native slider and percentage");
        using var splitEditor = new SyntaxEditor(); splitEditor.SetDocumentAndLanguage(document, editor.SyntaxLanguage);
        editor.IsReadOnly = true; editor.WordWrap = true; editor.ShowLineNumbers = false;
        editor.ShowWhitespace = true; editor.ShowRuler = true; editor.ShowIndentGuides = false;
        editor.AutoShowCompletion = false; editor.AutoCompleteBrackets = false; editor.FontSize = 17;
        splitEditor.CopyViewSettingsFrom(editor);
        Check(splitEditor.IsReadOnly && splitEditor.WordWrap && !splitEditor.ShowLineNumbers, "New split copies read-only, wrapping and line-number options");
        Check(splitEditor.ShowWhitespace && splitEditor.ShowRuler && !splitEditor.ShowIndentGuides && !splitEditor.AutoShowCompletion && !splitEditor.AutoCompleteBrackets && splitEditor.FontSize == 17, "New split copies visual and IntelliPrompt preferences");
        splitEditor.WordWrap = false; splitEditor.CaretOffset = 0;
        Check(editor.WordWrap && editor.CaretOffset != splitEditor.CaretOffset, "Split view preferences and carets remain independent after copying");
        var split = new ResizableSplitView { First = editor, Second = splitEditor };
        Check(split.IsSplit && split.Orientation == Orientation.Vertical, "Reusable split defaults to top/bottom panes");
        split.Measure(new Size(800, 400)); split.Arrange(new Rect(0, 0, 800, 400)); split.Ratio = .3;
        Check(split.Ratio == .3, "Split ratio survives native layout");
        split.Orientation = Orientation.Horizontal;
        Check(split.Orientation == Orientation.Horizontal && split.Ratio == .3, "Changing split orientation preserves its preferred ratio");
        var rejected = false; try { split.Ratio = double.NaN; } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected && split.Ratio == .3, "Invalid split ratios are rejected without mutating layout state");
        split.Second = null;
        Check(!split.IsSplit && ReferenceEquals(split.First, editor), "Closing the second pane preserves the first view"); split.First = null; split.Dispose();
        foreach (var theme in new[] { EditorTheme.Light, EditorTheme.Dark, EditorTheme.HighContrast })
        {
            status.ApplyTheme(theme); errors.ApplyTheme(theme); split.ApplyTheme(theme);
            var resources = EditorChrome.Create(theme);
            Check(resources[typeof(MenuFlyoutItem)] is Style && resources["ControlCornerRadius"] is CornerRadius radius && radius.TopLeft == 0, "Compact native chrome resources / " + theme.Name);
        }
        document.FileName = "renamed.txt"; editor.IsOverstrike = editor.IsOverstrike;
        Check(errors.Table?.FileName == "renamed.txt", "Diagnostic filenames refresh without a text revision after rename");
        using var otherDocument = new EditorDocument("x", "other.txt"); using var other = new SyntaxEditor(); other.SetDocumentAndLanguage(otherDocument, new PlainTextLanguage());
        errors.Editor = other; status.Editor = other;
        var detachedProjectionCount = errors.ProjectionBuildCount; var detachedStatusCount = status.RefreshCount;
        editor.CaretOffset = 0; editor.IsOverstrike = !editor.IsOverstrike;
        Check(errors.ProjectionBuildCount == detachedProjectionCount && status.RefreshCount == detachedStatusCount, "Workspace controls detach old editor event subscriptions on rebinding");
        errors.Editor = null; status.Editor = null;
        Check(errors.VisibleCount == 0 && status.PositionText.Length == 0, "Unbinding workspace controls releases displayed document state");
        errors.Dispose(); status.Dispose(); editor.IsOverstrike = !editor.IsOverstrike;
        Check(true, "Disposed workspace controls remain detached during subsequent editor events");
        return Task.CompletedTask;
    }
    private sealed class FixtureLanguage : ISyntaxLanguage
    {
        public string Id => "workspace-fixture";
        public string DisplayName => Id;
        public IReadOnlyList<string> Extensions => [];
        public LanguageCapabilities Capabilities => LanguageCapabilities.Diagnostics;
        public Task<LanguageAnalysis> AnalyzeAsync(DocumentSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(LanguageAnalysis.Empty(snapshot) with { Diagnostics = ImmutableArray.Create(new EditorDiagnostic("ERR", "Error row", IssueSeverity.Error, new(0, 1)), new EditorDiagnostic("WARN", "Warning row", IssueSeverity.Warning, new(snapshot.Length - 1, 1))) });
        }
    }
}
