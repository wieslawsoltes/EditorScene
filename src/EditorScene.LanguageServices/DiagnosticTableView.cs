using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace EditorScene.LanguageServices;

/// <summary>ProviderOrder preserves the language provider's original diagnostic sequence.</summary>
public enum DiagnosticSortColumn { ProviderOrder, Severity, Code, Line, Character, Description, File }
public enum DiagnosticSortDirection { Ascending, Descending }

/// <summary>A thread-safe, bounded query cache over one immutable diagnostic table.
/// Retains only the last ordering and the last filtered result; never a live document or UI control.
/// Equal keys preserve provider order in both directions. Strings use ordinal-ignore-case order;
/// ascending severity means Error, Warning, Information. Line and Character compare numerically.</summary>
public sealed class DiagnosticTableView
{
    private readonly object _gate = new();
    private ImmutableArray<DiagnosticTableRow> _ordered;
    private DiagnosticSortColumn _orderedColumn;
    private DiagnosticSortDirection _orderedDirection;
    private QueryKey? _lastQuery;
    private ImmutableArray<DiagnosticTableRow> _result;
    private long _sortBuilds, _queryBuilds;
    public DiagnosticTable Table { get; }
    public long SortBuildCount { get { lock (_gate) return _sortBuilds; } }
    public long QueryBuildCount { get { lock (_gate) return _queryBuilds; } }
    public DiagnosticTableView(DiagnosticTable table) => Table = table ?? throw new ArgumentNullException(nameof(table));

    /// <summary>Returns retained immutable arrays on exact repeat queries and unfiltered provider order.
    /// Filtering reuses the current ordering rather than sorting each keystroke. Cache construction is
    /// serialized per view; cancellation is checked before/after waiting, during merge passes and filtering.
    /// Cancelled queries never publish partial arrays. ProviderOrder has no descending variant.</summary>
    public ImmutableArray<DiagnosticTableRow> Query(DiagnosticVisibility visibility = DiagnosticVisibility.All,
        string? query = null, DiagnosticSortColumn column = DiagnosticSortColumn.ProviderOrder,
        DiagnosticSortDirection direction = DiagnosticSortDirection.Ascending, CancellationToken cancellationToken = default)
    {
        if ((visibility & ~DiagnosticVisibility.All) != 0) throw new ArgumentOutOfRangeException(nameof(visibility));
        if (!Enum.IsDefined(column)) throw new ArgumentOutOfRangeException(nameof(column));
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        if (column == DiagnosticSortColumn.ProviderOrder && direction != DiagnosticSortDirection.Ascending)
            throw new ArgumentException("Provider order does not have a descending variant.", nameof(direction));
        cancellationToken.ThrowIfCancellationRequested();
        var key = new QueryKey(visibility, query?.Trim() ?? string.Empty, column, direction);
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_lastQuery == key) return _result;
            ImmutableArray<DiagnosticTableRow> result;
            if (visibility == DiagnosticVisibility.None || Table.Rows.IsEmpty) result = [];
            else
            {
                var ordered = OrderedRows(column, direction, cancellationToken);
                if (visibility == DiagnosticVisibility.All && key.Text.Length == 0) result = ordered;
                else
                {
                    var filtered = ImmutableArray.CreateBuilder<DiagnosticTableRow>();
                    var fileMatches = key.Text.Length != 0 && Contains(Table.FileName, key.Text);
                    foreach (var row in ordered)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var flag = row.Diagnostic.Severity switch
                        {
                            IssueSeverity.Error => DiagnosticVisibility.Error,
                            IssueSeverity.Warning => DiagnosticVisibility.Warning,
                            _ => DiagnosticVisibility.Information
                        };
                        if ((visibility & flag) == 0) continue;
                        if (key.Text.Length != 0 && !fileMatches && !Contains(row.Code, key.Text)
                            && !Contains(row.Message, key.Text) && !Contains(row.Severity, key.Text)) continue;
                        filtered.Add(row);
                    }
                    result = filtered.ToImmutable();
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            _result = result; _lastQuery = key; _queryBuilds++;
            return result;
        }
    }

    private ImmutableArray<DiagnosticTableRow> OrderedRows(DiagnosticSortColumn column, DiagnosticSortDirection direction, CancellationToken token)
    {
        if (column == DiagnosticSortColumn.ProviderOrder) return Table.Rows;
        if (!_ordered.IsDefault && _orderedColumn == column && _orderedDirection == direction) return _ordered;
        var source = Table.Rows.ToArray();
        var scratch = new DiagnosticTableRow[source.Length];
        // Bottom-up stable merge sort avoids recursive depth and Array.Sort's wrapping
        // of comparer exceptions. Cancellation is surfaced as OperationCanceledException.
        for (long run = 1; run < source.Length; run *= 2)
        {
            token.ThrowIfCancellationRequested();
            for (long start = 0; start < source.Length; start += run * 2)
            {
                var left = (int)start;
                var middle = (int)Math.Min(start + run, source.Length);
                var end = (int)Math.Min(start + run * 2, source.Length);
                var right = middle;
                for (var output = left; output < end; output++)
                {
                    if ((output & 255) == 0) token.ThrowIfCancellationRequested();
                    if (left < middle && (right >= end || Compare(source[left], source[right], column, direction) <= 0))
                        scratch[output] = source[left++];
                    else scratch[output] = source[right++];
                }
            }
            (source, scratch) = (scratch, source);
        }
        token.ThrowIfCancellationRequested();
        // Neither this array nor its elements are subsequently mutated or exposed as mutable arrays.
        var ordered = ImmutableCollectionsMarshal.AsImmutableArray(source);
        _ordered = ordered; _orderedColumn = column; _orderedDirection = direction; _sortBuilds++;
        return ordered;
    }

    private static int Compare(DiagnosticTableRow left, DiagnosticTableRow right, DiagnosticSortColumn column, DiagnosticSortDirection direction)
    {
        // Swap operands instead of negating a comparer result (which could overflow).
        if (direction == DiagnosticSortDirection.Descending) (left, right) = (right, left);
        return column switch
        {
            DiagnosticSortColumn.Severity => Rank(left.Diagnostic.Severity).CompareTo(Rank(right.Diagnostic.Severity)),
            DiagnosticSortColumn.Code => StringComparer.OrdinalIgnoreCase.Compare(left.Code, right.Code),
            DiagnosticSortColumn.Line => left.Line.CompareTo(right.Line),
            DiagnosticSortColumn.Character => left.Character.CompareTo(right.Character),
            DiagnosticSortColumn.Description => StringComparer.OrdinalIgnoreCase.Compare(left.Message, right.Message),
            DiagnosticSortColumn.File => StringComparer.OrdinalIgnoreCase.Compare(left.FileName, right.FileName),
            _ => 0
        };
    }
    private static int Rank(IssueSeverity severity) => severity switch { IssueSeverity.Error => 0, IssueSeverity.Warning => 1, _ => 2 };
    private static bool Contains(string value, string text) => value.Contains(text, StringComparison.OrdinalIgnoreCase);
    private readonly record struct QueryKey(DiagnosticVisibility Visibility, string Text, DiagnosticSortColumn Column, DiagnosticSortDirection Direction);
}
