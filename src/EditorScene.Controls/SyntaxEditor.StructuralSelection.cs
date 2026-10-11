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
    private int _selectionOriginCaret, _selectionCurrentCaret;
    public bool IsSelectionExpansionPending => _selectionRequest is not null;
    public bool CanContractSelection => _selectionExpansion?.CanContract == true;
    public int SelectionExpansionDepth => _selectionExpansion?.Depth ?? 0;
    // The insertion caret and active selection endpoint are independent in UnoEdit.
    private SelectionRange NativeSelection => new(_editor.TextArea.Session.AnchorOffset, _editor.TextArea.Session.SelectionEndOffset);
    private void ResetSelectionExpansion()
    {
        _selectionGeneration++; _selectionExpansion = null;
        var pending = _selectionRequest; _selectionRequest = null;
        pending?.Cancel(); // The request owns disposal until provider work unwinds.
    }
    /// <summary>Expand through syntax ancestors without overwriting a newer caret, document or host callback.</summary>
    public async Task<bool> ExpandSelectionAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); Document.VerifyAccess(); cancellationToken.ThrowIfCancellationRequested();
        if (_changingStructuralSelection) return false;
        if (Selections.IsActive) throw new NotSupportedException("Structural selection uses one selection; clear extra carets first.");
        if (_selectionRequest is not null) return false;
        var document = Document; var language = SyntaxLanguage; var snapshot = document.Snapshot();
        var selected = NativeSelection; var caret = CaretOffset;
        if (_selectionExpansion is { } cached && cached.IsCurrent(snapshot, selected) && caret == _selectionCurrentCaret)
        {
            if (!cached.TryExpand(snapshot, selected, out var next)) return false;
            return ApplyStructuralSelection(next);
        }
        ResetSelectionExpansion(); _selectionOriginCaret = _selectionCurrentCaret = caret;
        var generation = _selectionGeneration;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); _selectionRequest = request;
        try
        {
            ImmutableArray<SourceRange> ranges;
            if (language is ISelectionRangeProvider provider && language.Capabilities.HasFlag(LanguageCapabilities.SelectionRanges))
                ranges = await provider.GetSelectionRangesAsync(snapshot, caret, request.Token);
            else ranges = SelectionCandidates.FromAnalysis(snapshot, caret, Analysis, request.Token);
            request.Token.ThrowIfCancellationRequested();
            if (_disposed || generation != _selectionGeneration || !ReferenceEquals(Document, document) || !ReferenceEquals(SyntaxLanguage, language)
                || document.Revision != snapshot.Revision || NativeSelection != selected || CaretOffset != caret || Selections.IsActive) return false;
            if (ranges.IsDefault) throw new InvalidDataException("The selection provider returned an uninitialized range array.");
            if (ranges.IsEmpty) ranges = SelectionCandidates.Basic(snapshot, caret, request.Token);
            var expansion = new SelectionExpansion(snapshot, selected, ranges); _selectionExpansion = expansion;
            if (!expansion.TryExpand(snapshot, selected, out var next)) return false;
            return ApplyStructuralSelection(next);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { return false; }
        catch (Exception) when (_disposed || generation != _selectionGeneration) { return false; }
        finally { if (ReferenceEquals(_selectionRequest, request)) _selectionRequest = null; }
    }
    /// <summary>Restore the preceding selection and its independent original insertion caret.</summary>
    public bool ContractSelection()
    {
        ObjectDisposedException.ThrowIf(_disposed, this); Document.VerifyAccess();
        if (_changingStructuralSelection) return false;
        if (_selectionRequest is not null) { ResetSelectionExpansion(); return false; }
        if (_selectionExpansion is not { } expansion) return false;
        var snapshot = Document.Snapshot(); var selected = NativeSelection;
        if (!expansion.IsCurrent(snapshot, selected) || CaretOffset != _selectionCurrentCaret || Selections.IsActive)
        { ResetSelectionExpansion(); return false; }
        if (!expansion.TryContract(snapshot, selected, out var previous)) return false;
        return ApplyStructuralSelection(previous);
    }
    private bool IsStructuralContextCurrent(EditorDocument document, long revision, ISyntaxLanguage language, SelectionExpansion expansion, long generation)
        => !_disposed && generation == _selectionGeneration && ReferenceEquals(Document, document) && document.Revision == revision
            && ReferenceEquals(SyntaxLanguage, language) && ReferenceEquals(_selectionExpansion, expansion) && !Selections.IsActive;
    private bool AbandonStructuralSelection(SelectionExpansion expansion)
    {
        // A callback may already have created a new history: never clear that one.
        if (ReferenceEquals(_selectionExpansion, expansion)) ResetSelectionExpansion();
        return false;
    }
    private bool ApplyStructuralSelection(SelectionRange range)
    {
        var expansion = _selectionExpansion ?? throw new InvalidOperationException("No structural selection history is active.");
        var document = Document; var revision = document.Revision; var language = SyntaxLanguage; var generation = _selectionGeneration;
        var initial = NativeSelection; var initialCaret = CaretOffset;
        var targetCaret = expansion.Depth == 0 ? _selectionOriginCaret : range.Active;
        _changingStructuralSelection = true;
        try
        {
            EndSnippet(false);
            if (!IsStructuralContextCurrent(document, revision, language, expansion, generation) || NativeSelection != initial || CaretOffset != initialCaret)
                return AbandonStructuralSelection(expansion);
            ClearMultiSelection();
            if (!IsStructuralContextCurrent(document, revision, language, expansion, generation) || NativeSelection != initial || CaretOffset != initialCaret)
                return AbandonStructuralSelection(expansion);
            _selectionCurrentCaret = targetCaret;
            var session = _editor.TextArea.Session;
            // Each setter notifies native and host listeners synchronously. Check
            // its expected output before the next setter can overwrite a callback.
            var afterCaret = initial.Length == 0 ? new SelectionRange(targetCaret, targetCaret) : initial;
            session.SetCaretOffset(targetCaret);
            if (!IsStructuralContextCurrent(document, revision, language, expansion, generation) || NativeSelection != afterCaret || CaretOffset != targetCaret)
                return AbandonStructuralSelection(expansion);
            session.SetSelection(range.Anchor, range.Active);
            if (!IsStructuralContextCurrent(document, revision, language, expansion, generation) || NativeSelection != range || CaretOffset != targetCaret)
                return AbandonStructuralSelection(expansion);
            _editor.TextArea.TextView.EnsureCaretVisible();
        }
        finally { _changingStructuralSelection = false; }
        if (!IsStructuralContextCurrent(document, revision, language, expansion, generation) || NativeSelection != range || CaretOffset != targetCaret)
            return AbandonStructuralSelection(expansion);
        StateChanged?.Invoke(this, EventArgs.Empty);
        // A final observer may redirect selection or start a newer operation. Do
        // not report success and restore keyboard focus over that interaction.
        if (!IsStructuralContextCurrent(document, revision, language, expansion, generation) || NativeSelection != range || CaretOffset != targetCaret)
            return AbandonStructuralSelection(expansion);
        return true;
    }
}
