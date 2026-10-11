# Structural selection and incremental compiler analysis

This increment adds reversible structural selection, incremental C#/VB source updates, shared completed-analysis caching, deferred semantic state and analysis-scheduling fixes. These are independently implemented EditorScene capabilities, not a claim of full Actipro API/binary/feature/pixel parity or an exhaustive audit of every subsystem.

## Native commands and host API

**Alt+Shift+Right** expands the selection and **Alt+Shift+Left** contracts it. Both are also available under **Edit > Expand Selection / Contract Selection**.

```csharp
bool expanded = await editor.ExpandSelectionAsync(cancellationToken);
bool contracted = editor.ContractSelection();
```

C# and Visual Basic use compiler token/trivia and enclosing syntax-node spans, including paired-delimiter interiors. A numeric literal can expand to its binary expression, containing member/type and document. Duplicate ranges are omitted. Contraction restores each preceding selection, including reversed endpoints and the original empty caret.

The Uno engine permits the insertion caret to differ from the active selection endpoint. EditorScene captures all three values, positions the insertion caret at each expanded active endpoint, and restores its independent original position at depth zero. Native tests verify both ordinary reversed selections and deliberately independent insertion carets.

Providers without structural-selection capability use a bounded-memory Unicode word/line fallback supplemented by current classification, folding and navigation ranges. This is not a grammar-specific JSON/XML/XAML selection provider. Word boundaries include scalar letters, digits, combining marks and connector punctuation.

Structural selection operates on one selection. Multiple active carets are explicitly rejected rather than silently removed. Read-only documents support the commands because no text is changed. Macro recording/playback excludes these asynchronous, provider-dependent commands.

### Lifetime and cancellation

The view captures document identity, revision, language provider, anchor, active endpoint, insertion caret and a request generation. It accepts the response only while all remain current. Manual movement/selection, editing, provider changes, rebinding, unloading and disposal invalidate pending work/history. Providers that ignore cancellation cannot apply obsolete results. Contract cancels an outstanding expansion; repeated expansion while pending does not accumulate requests.

`IsSelectionExpansionPending`, `SelectionExpansionDepth` and `CanContractSelection` expose host observations. Superseded/canceled in-flight expansion returns `false`; an already-canceled supplied token is rejected at entry. Invalid current-provider ranges fail before changing the selection. View APIs run on the document owner thread; providers receive immutable snapshots. Menu focus is restored after closure only for successful current operations, without stealing focus from a newer popup.

## Reusable selection core

`EditorScene.LanguageServices.SelectionExpansion` stores document identity/revision and immutable selection endpoints, not a live document or its text:

```csharp
var snapshot = document.Snapshot();
var initial = new SelectionRange(anchor, active);
var history = new SelectionExpansion(snapshot, initial,
    SelectionCandidates.Basic(snapshot, active));
if (history.TryExpand(snapshot, initial, out var expanded))
    history.TryContract(snapshot, expanded, out var restored);
```

All candidates are validated, sorted by size/document position, and reduced to a strictly growing containment chain. Crossing alternatives are not parents. Whole-document selection is appended when absent. Foreign documents, new revisions and changed selections invalidate a history; undo to equal text does not resurrect an earlier revision's history.

There is a maximum of 4,096 local candidates. Boundaries cannot split valid surrogate pairs or CRLF, but this does not impose every grapheme-cluster rule on externally supplied ranges. Repeated core expansion/contraction is allocation-free after construction; native events, tasks, layout and painting are not covered by that guarantee.

A language advertises `LanguageCapabilities.SelectionRanges` and implements the optional extension:

```csharp
public interface ISelectionRangeProvider
{
    Task<ImmutableArray<SourceRange>> GetSelectionRangesAsync(
        DocumentSnapshot snapshot, int offset,
        CancellationToken cancellationToken = default);
}
```

The view reuses its returned chain until the captured document/provider/selection state changes.

## LSP selectionRange

The client advertises static `textDocument.selectionRange` support with dynamic registration disabled. The adapter enables requests only when the server advertises `selectionRangeProvider`. The existing document-snapshot lease serializes UTF-16 synchronization and the request, preventing a newer `didChange` from racing an older response.

`LanguageServerClient.SelectionRangesAsync` accepts 1–1,024 positions. `LspSelectionRanges.Decode` checks response cardinality, JSON object/range types, UTF-16 coordinates, containment and a maximum parent depth of 128. Equal parents are deduplicated; null results yield empty chains, while null entries in non-null result arrays are rejected. Malformed chains fail atomically. Response JSON is neither mutated nor interpreted as commands.

The native view requests one position and reuses the chain for expansion/contraction. Unsupported servers use the local fallback instead of receiving unadvertised requests. Dynamic registration, partial-result progress and full vendor provider contracts remain outside this increment.

## Compiler cache design

The preceding provider materialized `snapshot.Text` for every new model and created a fresh compilation. Repeated analysis of the same revision repeated diagnostics, classification and symbol walks.

The new provider creates cold Roslyn source from a snapshot reader. Forward edits use `SnapshotTextChanges.GetMinimalChange` and `SourceText.WithChanges`; `SyntaxTree.WithChangedText` receives actual change history. A regression test uses `SyntaxNode.IsIncrementallyIdenticalTo` to verify reuse of unchanged syntax nodes, not merely an incremented counter.

Compilation and semantic models are **lazy**. Syntax-only range requests do not construct either, including after an edit. When semantic work is needed, a previously completed compilation can replace its syntax tree; otherwise a new compilation is created. Factories retain only an already-built preceding compilation/tree, not a chain of unbuilt models or preceding document snapshots. Concurrent analysis requests force the lazy state once and share the completed immutable analysis for that model.

At most eight document slots are retained, with least-recently-used idle-slot eviction. When all retained construction gates are busy, additional documents use transient slots. This bounds retained slots, not total memory for arbitrary simultaneous work. Older snapshots remain analyzable but cannot displace newer revisions. File-name changes invalidate the syntax-tree path. Cancellation is checked while waiting and before model publication.

`CacheStatistics` reports model builds/hits, cold parses, incremental updates, actual compilation replacements, completed analyses, compilation builds and semantic-model builds. These are work counters, not time estimates.

### Performance findings — not a universal speedup

The retained initial probe in [compiler-initial-a186.json](performance/compiler-initial-a186.json) found a real unfavorable result: cold-semantic model construction rose from 6.4099 ms to 24.0116 ms median, with more temporary allocations. That evidence is preserved rather than relabeled as an improvement.

The expanded probe at revision `2bb1153e7c24dd2e2324dab30cd30ac64b06d686` separates the work:

| Workload | Previous p50 ms | New p50 ms | Previous / new managed bytes |
| --- | ---: | ---: | ---: |
| Model construction, prior diagnostics not warmed | 7.4657 | 24.6904 | 1,333,776 / 1,948,872 |
| Updated model plus full diagnostics, prior diagnostics warmed | 71.2639 | 61.2599 | 5,240,608 / 4,676,736 |
| Source-change construction only | 0.0257 | 0.0138 | 235,688 / 3,664 |
| Syntax-tree update only | 3.2804 | 3.3724 | 1,094,312 / 1,943,136 |
| Compilation replacement/construction only | 0.0032 | 0.0335 | 1,904 / 1,784 |
| 10,000 cached structural-selection cycles | not applicable | 0.1335 | not applicable / 0 |

The semantic-warm p95 **worsened** from 98.9569 to 122.8566 ms in this run. The isolated syntax-update path also allocates more despite actual syntax-node reuse. Phase samples run separately and must not be summed as though they were one execution. These data support reduced source-copy allocation and a workload-specific warm semantic median improvement, **not uniformly faster compiler construction or improved tail latency**.

The direct model cache tests additionally establish one model/analysis for concurrent same-snapshot views and zero compilation/semantic builds for syntax-only requests. The table reproduces old/new compiler hot paths; it does not measure the entire provider, its cache gate, Task scheduling, native UI or rendering.

Each row has three warmups and eleven samples. Compiler fixtures have 117,792 UTF-16 units, 4,000 methods and one four-character numeric edit. Setup, prior-model warmup and output verification are excluded. The semantic-warm row forces diagnostics before the edit during setup and for the new model during measurement. Current-thread allocations do not capture every worker allocation from compiler internals. Raw outputs, exact revisions and environment data are in `artifacts/tests/performance.json` in the corresponding CI run. No absolute machine-independent timing gate is used.

### Remaining compiler limits

Initial parsing scans the document. New-revision diagnostics/classification/navigation still perform substantial compiler work; this is not a fully incremental semantic pipeline. Long or unrelated histories can require broad differencing, and distant edits can produce one larger contiguous replacement. Completion and formatting paths may still request full text. The cache is not a project system, refactoring engine or multi-document LSP workspace. Platform parser/compiler calls can delay cancellation internally. Physical-GPU, frame-rate, power and end-to-end input latency have not been measured by these probes.

## Scheduling review and fixes

Canceling the previous provider can invoke callbacks that edit the document, switch providers, request new analysis or dispose the coordinator. Previously, the outer request could overwrite the reentrant newer request. A synchronous publication handler could likewise start work before the outer call assigned `Pending`, causing that assignment to replace the newer task.

The coordinator now advances its generation and detaches the old cancellation source before invoking callbacks. It checks the generation before starting work and before assigning `Pending`. Publication checks provider identity as well as generation/revision. Editing clears obsolete `Current` immediately during debounce; disposal releases the last published result and subscriptions. Reassigning the same provider avoids a redundant restart. Mutating/request APIs verify the owner thread, and debounce accepts zero through one minute.

Hosts must dispatch publication/invalidation callbacks onto the document owner thread. `Pending` represents the most recently scheduled provider operation; separately queued UI publication may finish later. Arbitrary blocking or throwing external callbacks are not given transactional rollback guarantees.

## Validation scope

Unit coverage includes containment/direction, UTF-16/CRLF, malformed protocol chains, cancellation and snapshot leases, actual parser-node reuse, deferred semantic state, same-snapshot concurrency, bounded retention, old revisions/file names, and reentrant scheduling callbacks. Native checks cover real selection/caret state, read-only documents, providers that ignore cancellation, document/provider changes and disposal. Browser tests use actual keys, menus and WebSocket transport; the diagnostic bridge remains observation-only. Existing regression/package validation is retained, not replaced by the new tests.

This review specifically inspects selection, compiler-cache and coordinator paths and their control/protocol integration. Other subsystems remain covered by their existing tests, not an assertion that every file received exhaustive manual review. See [compatibility.md](compatibility.md) for remaining work.

## Primary references

- Actipro SyntaxEditor target documentation: https://www.actiprosoftware.com/docs/controls/wpf/syntaxeditor/index
- LSP selectionRange: https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/#textDocument_selectionRange
- Roslyn reader construction: https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.text.sourcetext.from
- Roslyn source changes: https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.text.sourcetext.withchanges
- Actual incremental syntax identity: https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.syntaxnode.isincrementallyidenticalto
