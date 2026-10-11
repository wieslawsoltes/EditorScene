# Structural selection and incremental compiler analysis

This increment adds reversible selection expansion/contraction and incremental C#/VB model construction. It also repairs analysis-coordinator reentrancy and stale-result lifetime defects. These are independent EditorScene features targeting documented editor capabilities, not an Actipro API/binary/pixel compatibility claim.

## Native editor commands

**Alt+Shift+Right** expands the active selection; **Alt+Shift+Left** contracts it. Both commands are also available as **Edit > Expand Selection** and **Edit > Contract Selection**. Hosts can call:

```csharp
bool expanded = await editor.ExpandSelectionAsync(cancellationToken);
bool contracted = editor.ContractSelection();
```

C# and Visual Basic use compiler token/trivia and enclosing syntax-node spans, including the interior of paired delimiters. A caret on a numeric literal can expand to the literal, its binary expression, enclosing statement/member/type, and the document. Duplicate ranges are omitted. Contracting restores each exact preceding selection, including reversed anchor/active endpoints and the original zero-length caret.

Providers without structural-selection capability use a bounded-memory word/line fallback supplemented by current classification, folding and navigation ranges. Word edges account for Unicode scalar letters, digits, combining marks and connector punctuation. This fallback is not a full grammar-specific selection service for JSON, XML, XAML or every lexical language.

Structural selection is a single-selection operation. More than one active caret is explicitly rejected; it does not silently discard a multi-selection. Read-only documents support navigation because text is not changed. The commands are excluded from macro recording/playback because they depend on asynchronous language analysis rather than a fixed semantic edit sequence.

### Pending work and selection lifetime

The native view captures the document, revision, provider, anchor/active selection and a generation before requesting ranges. It applies a response only if all still match. Manual selection/caret changes, document edits, provider changes, rebinding, unloading and disposal cancel/invalidate the request and clear old history. A provider that ignores cancellation still cannot apply a stale result. Contract cancels an outstanding expansion. Repeated expansion while a request is pending does not enqueue an unbounded sequence of requests.

`IsSelectionExpansionPending`, `SelectionExpansionDepth` and `CanContractSelection` expose observations for hosts. A canceled or superseded in-flight `ExpandSelectionAsync` returns `false`; an already-canceled supplied token is rejected at entry. Invalid current-provider ranges fail before the selection is modified. These APIs are called on the view/document owner thread; provider work consumes immutable snapshots.

Menu-originated commands use the existing deferred focus restoration after menu closure. A discarded stale response does not focus the editor or overwrite the status of a newer interaction.

## Reusable selection model

`EditorScene.LanguageServices.SelectionExpansion` contains only document identity, revision, immutable selection endpoints and a current index. It retains neither a live document nor the snapshot's text:

```csharp
var snapshot = document.Snapshot();
var initial = new SelectionRange(anchor, active);
var candidates = SelectionCandidates.Basic(snapshot, active);
var history = new SelectionExpansion(snapshot, initial, candidates);
if (history.TryExpand(snapshot, initial, out var expanded))
{
    // Use expanded.Anchor and expanded.Active in a host-specific view.
    history.TryContract(snapshot, expanded, out var restored);
}
```

Candidates are validated before use, sorted by size and document position, and reduced to a strictly growing containment chain. Crossing alternatives are not parents. The full document is the final step when it is not already present. Wrong-document, wrong-revision and manually changed selection histories are rejected. Undoing the text to equal contents does not revive a history from an earlier revision.

The maximum candidate count is 4,096. Boundaries cannot split a valid UTF-16 surrogate pair or CRLF. This is not a claim that every externally supplied range is constrained to every Unicode grapheme boundary. Repeated model expansion/contraction is allocation-free; native control layout, events, task scheduling and painting can still allocate.

An optional language extension separates this service from ordinary analysis:

```csharp
public interface ISelectionRangeProvider
{
    Task<ImmutableArray<SourceRange>> GetSelectionRangesAsync(
        DocumentSnapshot snapshot, int offset,
        CancellationToken cancellationToken = default);
}
```

A provider advertises `LanguageCapabilities.SelectionRanges` and returns ranges against the exact requested immutable snapshot. The native view caches the resulting expansion history only while document/provider/selection state remains valid.

## LSP selectionRange

The client advertises static `textDocument.selectionRange` support with dynamic registration disabled. `LspSyntaxLanguage` enables the capability only when the server advertises `selectionRangeProvider`. Requests use the existing serialized document-snapshot lease and negotiated UTF-16 synchronization; a newer `didChange` cannot race the response for an older request.

`LanguageServerClient.SelectionRangesAsync` supports 1–1,024 requested positions. `LspSelectionRanges.Decode` verifies response cardinality, object/range types, UTF-16 positions, containing parents and a maximum of 128 parent nodes per position. Equal parent ranges are deduplicated. A null result produces empty chains; null entries inside a non-null response array are rejected. No incomplete chain is returned after a validation failure, and response JSON is never mutated or interpreted as commands.

The view currently requests one position and caches the chain for expansion/contraction. Unsupported servers use the local fallback; they are not sent unadvertised selection requests. Dynamic registration, partial-result progress and full vendor selection-provider contracts remain outside this increment.

## Incremental .NET model construction

Before this increment, every new document revision converted `snapshot.Text` into a complete source string and constructed a fresh compilation, even when only one character changed. Repeated analysis on the same revision also repeated diagnostics, classification and symbol walks.

The provider now:

1. Creates a cold Roslyn `SourceText` from a snapshot reader rather than materializing `DocumentSnapshot.Text`.
2. Derives a forward change with the existing history-bounded `SnapshotTextChanges.GetMinimalChange` and applies it to the preceding `SourceText.WithChanges`.
3. Updates the syntax tree with `WithChangedText` and replaces that tree in the preceding compilation, preserving reusable compiler/reference state.
4. Shares one cached model-construction gate per retained document and caches the completed immutable `LanguageAnalysis` for each model. Concurrent split-view requests can reuse the same completed analysis object.

The cache retains at most eight document slots and evicts the least recently used idle slot. If all eight model-construction slots are busy, additional documents use transient uncached slots. This bounds retained slots, not total memory of arbitrary concurrent requests. Cancellation is checked while waiting and before cache publication. Older snapshots can be analyzed without displacing a newer cached revision. File-name changes invalidate model identity and the syntax-tree path.

`DotNetLanguage.CacheStatistics` exposes actual model builds, hits, cold parses, incremental updates, compilation replacements, analysis builds and retained slot counts. The counters report work, not elapsed time or performance guarantees.

### Limits

This does not make all semantic analysis incremental: diagnostics, classification and navigation construction for a new revision still do substantive compiler work. Initial parsing scans the document. Long/unrelated histories can require broader differencing; widely separated edits can produce a larger contiguous replacement. Completion/formatting paths may still request full text where their existing implementation does so. The cache is not a project system, multi-project compilation graph, refactoring engine or shared multi-document LSP workspace.

Cold reader construction and compiler calls can have cancellation latency inside platform APIs. The model cache does not expose hardware timing, physical-GPU throughput or overall editor frame-rate claims.

## Analysis-coordinator fixes

Review identified two reentrancy failures in the existing reusable coordinator. Canceling a provider could synchronously trigger a new document edit/request, only for the outer request to overwrite it. A synchronously published analysis could also start a new request before the outer `ExecuteAsync` returned, after which the outer assignment overwrote `Pending` with the wrong task.

The coordinator now detaches the prior cancellation source and advances the generation before invoking cancellation callbacks. It checks that generation before starting work and again before assigning `Pending`. Publication verifies the captured provider as well as generation and revision. Editing clears obsolete `Current` immediately rather than exposing an old result until debounce completes; disposal releases the published result. Reassigning the same provider no longer restarts work. Mutation/request APIs verify the document owner thread, and debounce is explicitly limited to zero through one minute.

The host must dispatch publication/invalidation callbacks onto the document owner thread. `Pending` observes the most recently scheduled provider operation; separately queued UI publication can complete later. Arbitrary throwing/blocking external callbacks are not promised transactional rollback.

## Validation and measurement methodology

New unit coverage includes containment/direction, Unicode and CRLF boundaries, cancellation, malformed LSP parent chains, exact wire capabilities and synchronization, concurrent same-snapshot model/analysis reuse, bounded cache retention, old-revision and file-name behavior, and reentrant analysis callbacks. Native-host checks exercise real selection state, read-only behavior, delayed providers that ignore cancellation, document/provider changes and disposal. Browser tests use real keys, menus and the WebSocket fixture; the diagnostics bridge remains observation-only.

The benchmark executable adds:

- `csharp_small_edit_model`: 4,000 C# methods and one four-character edit, comparing the preceding full-source/new-compilation hot path with changed-source/reused-compilation construction.
- `structural_selection_cycles_10000`: 10,000 cached model expansion/contraction pairs, without native UI/layout/painting.

Both retain three warmups and eleven samples with p50, observed p95 and current-thread managed allocations. Compiler setup, prior-model warmup and final produced-text verification are excluded; full semantic analysis, Task scheduling and native rendering are not measured by this microbenchmark. Raw outputs are retained in `artifacts/tests/performance.json` with the exact source revision/environment. Read the corresponding successful job results before treating a benchmark or test as validated.

## Primary references

- Actipro selection and editing feature target: https://www.actiprosoftware.com/docs/controls/wpf/syntaxeditor/index
- LSP selectionRange: https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/#textDocument_selectionRange
- Roslyn SourceText reader construction: https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.text.sourcetext.from
- Roslyn immutable source changes: https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.text.sourcetext.withchanges

See [compatibility.md](compatibility.md) for the remaining product, platform and visual-parity boundaries.
