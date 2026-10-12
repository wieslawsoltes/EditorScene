using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace EditorScene.LanguageServices;

/// <summary>A single-owner cursor over an immutable diagnostic result. Navigation follows
/// the supplied ordering and filtering, includes duplicate positions, and preserves row identity.
/// Construction validates row references once; movement and selection are allocation-free O(1).
/// The host remains responsible for checking document/revision before source navigation.</summary>
public sealed class DiagnosticNavigationCursor
{
    private readonly ImmutableArray<DiagnosticTableRow> _rows;
    private int _index = -1;
    public DiagnosticNavigationCursor(ImmutableArray<DiagnosticTableRow> rows)
    {
        if (rows.IsDefault) throw new ArgumentException("The diagnostic result is uninitialized.", nameof(rows));
        foreach (var row in rows)
            if (row is null) throw new ArgumentException("The diagnostic result contains a null row.", nameof(rows));
        _rows = rows;
    }
    public int Count => _rows.Length;
    /// <summary>-1 means there is no current row. Valid row indices are zero-based.</summary>
    public int CurrentIndex => _index;
    public DiagnosticTableRow? Current => _index >= 0 ? _rows[_index] : null;
    public void SelectIndex(int index)
    {
        if (index < -1 || index >= _rows.Length) throw new ArgumentOutOfRangeException(nameof(index));
        _index = index;
    }
    /// <summary>Without a current row, starts at the first/last row. At an end,
    /// optional wrapping returns to the opposite end. Failed movement leaves the cursor unchanged.</summary>
    public bool TryMove([NotNullWhen(true)] out DiagnosticTableRow? row, bool backwards = false, bool wrap = true)
    {
        if (_rows.IsEmpty) { row = null; return false; }
        int next;
        if (_index < 0) next = backwards ? _rows.Length - 1 : 0;
        else if (backwards && _index > 0) next = _index - 1;
        else if (!backwards && _index < _rows.Length - 1) next = _index + 1;
        else if (wrap) next = backwards ? _rows.Length - 1 : 0;
        else { row = null; return false; }
        _index = next; row = _rows[next]; return true;
    }
}
