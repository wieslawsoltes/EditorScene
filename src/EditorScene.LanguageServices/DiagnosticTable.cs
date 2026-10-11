using System.Collections.Immutable;
using EditorScene.Text;

namespace EditorScene.LanguageServices;

[Flags]
public enum DiagnosticVisibility { None = 0, Information = 1, Warning = 2, Error = 4, All = Information | Warning | Error }

/// <summary>One row in a revision-bound diagnostic table. Line and Character are one-based UTF-16 positions.</summary>
public sealed record DiagnosticTableRow(EditorDiagnostic Diagnostic, int Line, int Character, string FileName)
{
    public string Code => Diagnostic.Code;
    public string Message => Diagnostic.Message;
    public string Severity => Diagnostic.Severity switch { IssueSeverity.Error => "Error", IssueSeverity.Warning => "Warning", _ => "Information" };
    public string Glyph => Diagnostic.Severity switch { IssueSeverity.Error => "×", IssueSeverity.Warning => "!", _ => "i" };
    public override string ToString() => $"{Severity} {Code}: {Message} ({Line},{Character})";
}

/// <summary>Projects immutable analysis once, not on every caret/selection event. No full-text copy or live-buffer access.</summary>
public sealed class DiagnosticTable
{
    public Guid DocumentId { get; }
    public long Revision { get; }
    public string FileName { get; }
    public ImmutableArray<DiagnosticTableRow> Rows { get; }
    public int ErrorCount { get; }
    public int WarningCount { get; }
    public int InformationCount { get; }

    public DiagnosticTable(DocumentSnapshot snapshot, LanguageAnalysis analysis, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(analysis);
        cancellationToken.ThrowIfCancellationRequested();
        if (snapshot.DocumentId != analysis.DocumentId || snapshot.Revision != analysis.Revision)
            throw new ArgumentException("Diagnostics must belong to the captured snapshot.", nameof(analysis));
        DocumentId = snapshot.DocumentId; Revision = snapshot.Revision; FileName = snapshot.FileName;
        var rows = ImmutableArray.CreateBuilder<DiagnosticTableRow>(analysis.Diagnostics.Length);
        foreach (var diagnostic in analysis.Diagnostics)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (diagnostic is null || diagnostic.Code is null || diagnostic.Message is null || !Enum.IsDefined(diagnostic.Severity))
                throw new ArgumentException("A diagnostic has invalid metadata.", nameof(analysis));
            var range = diagnostic.Range;
            if (range.Offset < 0 || range.Length < 0 || range.Offset > snapshot.Length || range.Length > snapshot.Length - range.Offset)
                throw new ArgumentException("A diagnostic is outside its snapshot.", nameof(analysis));
            var position = snapshot.GetPosition(range.Offset);
            rows.Add(new(diagnostic, position.Line + 1, position.Character + 1, FileName));
            switch (diagnostic.Severity)
            {
                case IssueSeverity.Error: ErrorCount++; break;
                case IssueSeverity.Warning: WarningCount++; break;
                case IssueSeverity.Information: InformationCount++; break;
            }
        }
        Rows = rows.MoveToImmutable();
    }

    /// <summary>Returns the original immutable rows for the common unfiltered path.
    /// Query matches message, code, severity or file name using ordinal case-insensitive matching.</summary>
    public ImmutableArray<DiagnosticTableRow> Filter(DiagnosticVisibility visibility = DiagnosticVisibility.All, string? query = null, CancellationToken cancellationToken = default)
    {
        if ((visibility & ~DiagnosticVisibility.All) != 0) throw new ArgumentOutOfRangeException(nameof(visibility));
        cancellationToken.ThrowIfCancellationRequested();
        query = query?.Trim();
        if (visibility == DiagnosticVisibility.None) return [];
        if (visibility == DiagnosticVisibility.All && string.IsNullOrEmpty(query)) return Rows;
        var rows = ImmutableArray.CreateBuilder<DiagnosticTableRow>();
        foreach (var row in Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var flag = row.Diagnostic.Severity switch { IssueSeverity.Error => DiagnosticVisibility.Error, IssueSeverity.Warning => DiagnosticVisibility.Warning, _ => DiagnosticVisibility.Information };
            if ((visibility & flag) == 0) continue;
            if (!string.IsNullOrEmpty(query) && !Matches(row.Message, query) && !Matches(row.Code, query) && !Matches(row.FileName, query) && !Matches(row.Severity, query)) continue;
            rows.Add(row);
        }
        return rows.ToImmutable();
    }
    public bool IsCurrent(DocumentSnapshot snapshot) => snapshot.DocumentId == DocumentId && snapshot.Revision == Revision && snapshot.FileName == FileName;
    private static bool Matches(string value, string query) => value.Contains(query, StringComparison.OrdinalIgnoreCase);
}
