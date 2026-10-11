using System.Collections.Immutable;
using EditorScene.LanguageServices;
using EditorScene.Text;

namespace EditorScene.Controls;

public sealed partial class SyntaxEditor
{
    private SelectionExpansion? _selectionExpansion;
    private CancellationTokenSource? _selectionRequest;
    private long _selectionGeneration;
    private bool _changingStructuralSelection;
    public bool IsSelectionExpansionPending => _selectionRequest is not null;
    public bool CanContractSelection => _selectionExpansion?.CanContract == true;
    public int SelectionExpansionDepth => _selectionExpansion?.Depth ?? 0;
    private SelectionRange NativeSelection => new(_editor.TextArea.Session.AnchorOffset, CaretOffset);
    private void ResetSelectionExpansion()
    {
        _selectionGeneration++; _selectionExpansion = null;
        var pending = _selectionRequest; _selectionRequest = null;
        pending?.Cancel(); // The request owns disposal until all provider work has unwound.
    }
    /// <summary>Expand the active selection through syntax ancestors. Delayed responses never move a newer caret or document.</summary>
    public async Task<bool> ExpandSelectionAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); Document.VerifyAccess(); cancellationToken.ThrowIfCancellationRequested();
        if (Selections.IsActive) throw new NotSupportedException("Structural selection uses one selection; clear extra carets first.");
        if (_selectionRequest is not null) return false;
        var document = Document; var language = SyntaxLanguage; var snapshot = document.Snapshot(); var selected = NativeSelection;
        if (_selectionExpansion is { } cached && cached.IsCurrent(snapshot, selected))
        {
            if (!cached.TryExpand(snapshot, selected, out var next)) return false;
            ApplyStructuralSelection(next); return true;
        }
        ResetSelectionExpansion();
        var generation = _selectionGeneration;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); _selectionRequest = request;
        try
        {
            ImmutableArray<SourceRange> ranges;
            if (language is ISelectionRangeProvider provider && language.Capabilities.HasFlag(LanguageCapabilities.SelectionRanges))
                ranges = await provider.GetSelectionRangesAsync(snapshot, selected.Active, request.Token);
            else ranges = SelectionCandidates.FromAnalysis(snapshot, selected.Active, Analysis, request.Token);
            request.Token.ThrowIfCancellationRequested();
            if (_disposed || generation != _selectionGeneration || !ReferenceEquals(Document, document) || !ReferenceEquals(SyntaxLanguage, language)
                || document.Revision != snapshot.Revision || NativeSelection != selected) return false;
            if (ranges.IsDefault) throw new InvalidDataException("The selection provider returned an uninitialized range array.");
            if (ranges.IsEmpty) ranges = SelectionCandidates.Basic(snapshot, selected.Active, request.Token);
            var expansion = new SelectionExpansion(snapshot, selected, ranges);
            _selectionExpansion = expansion;
            if (!expansion.TryExpand(snapshot, selected, out var next)) return false;
            ApplyStructuralSelection(next); return true;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { return false; }
        catch (Exception) when (_disposed || generation != _selectionGeneration) { return false; }
        finally { if (ReferenceEquals(_selectionRequest, request)) _selectionRequest = null; }
    }
    /// <summary>Restore the preceding exact selection, including its original direction or empty caret.</summary>
    public bool ContractSelection()
    {
        ObjectDisposedException.ThrowIf(_disposed, this); Document.VerifyAccess();
        if (_selectionRequest is not null) { ResetSelectionExpansion(); return false; }
        if (_selectionExpansion is not { } expansion) return false;
        var snapshot = Document.Snapshot(); var selected = NativeSelection;
        if (!expansion.IsCurrent(snapshot, selected)) { ResetSelectionExpansion(); return false; }
        if (!expansion.TryContract(snapshot, selected, out var previous)) return false;
        ApplyStructuralSelection(previous); return true;
    }
    private void ApplyStructuralSelection(SelectionRange range)
    {
        _changingStructuralSelection = true;
        try
        {
            EndSnippet(false); ClearMultiSelection();
            _editor.TextArea.Session.SetSelection(range.Anchor, range.Active);
            _editor.TextArea.TextView.EnsureCaretVisible();
        }
        finally { _changingStructuralSelection = false; }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
