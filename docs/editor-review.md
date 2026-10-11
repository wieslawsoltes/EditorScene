# Editor correctness and performance review

This increment builds on main commit `dd8b5650a3d9620fe6d4c926406153664f459d32` (PR #3). It adds independent editor capabilities and fixes verified defects; it does not establish full Actipro API, feature or pixel parity.

## Reviewed paths and fixes

| Path | Defect or cost | Change and regression evidence |
| --- | --- | --- |
| Multi-selection normalization | Adjacent selections were merged; sorting lost the newly added primary caret; deleting neighboring selections could leave duplicate carets. | Adjacent spans stay distinct, the last supplied range remains primary after sorting/wrapping, and coincident output carets are coalesced. Direct tests plus actual native/browser typing and undo. |
| Multi-selection edit preflight | Delete published expanded ranges before read-only permission checks; reentrant callbacks could overwrite newer selection state. | Build and validate the complete candidate edit/selection state first. Revision and selection-generation checks reject reentrant changes. Denied deletion preserves the original view and primary caret. |
| Unicode multi-caret deletion | Interior offsets could leave partial combining sequences, surrogate pairs or CRLF delimiters; each caret repeatedly segmented its entire line. | Delete complete affected graphemes/delimiters and reuse one grapheme map per affected line per operation. |
| Snapshot literal search | Occurrence selection called `snapshot.Text.IndexOf`, flattening the whole document even when searching only near its end. | A pooled sliding-window reader supports bounded scope, ordinal case rules, Unicode word boundaries and cross-window matches. Randomized results are checked against .NET string search. |
| Bracket matching | Every analysis flattened the document; repeated caret queries allocated arrays. | Pooled rope scanning, cancellation, validated exclusions, allocation-free pair lookup and reuse when the snapshot/classification array is unchanged. |
| Diagnostic adornments | Every paint scanned diagnostics from offset zero; long spans could request offscreen geometry. | Immutable interval index, viewport query pruning and range clipping before geometry generation. Actual Skia surface tests use 100,000 diagnostics plus a whole-document span. |
| Indicators and whitespace | Repeated indicator sorting/read-only wrapper creation and per-paint whitespace sets. | Cached indicator ordering with revision invalidation, binary-search viewport enumeration, stable read-only collection and reusable whitespace buffer. |
| Indentation guides | Mixed tabs/spaces aligned guides by UTF-16 offset, and indentation was scanned twice. | One pass uses visual tab columns. |
| Native symbol navigation | Caret changes scanned/sorted all symbol items; old results could navigate stale offsets. | Indexed container/member lookup and revision-safe analysis exposure. Outline and diagnostics navigation reject obsolete results. |
| Document/provider binding | A new document could briefly be analyzed by the preceding document's remote provider. | `SetDocumentAndLanguage` binds both together; direct native tests use providers that reject foreign documents. |
| View lifecycle | Whitespace, ruler and guide preferences reset during document rebinding. | View-owned settings persist and are applied to newly attached adornments. |
| Workspace UI ownership | Every tab rebuild retained another close icon and old registered tab controls; every caret update recreated tab brushes and captions. | Reuse the close control, remove obsolete registrations, cache tab caption/theme state. Repeated browser new/close cycles assert retained counts remain stable. |
| Document I/O | Stream callbacks could edit a live document while Save wrote it, then incorrectly mark newer edits saved; Load could overwrite intervening edits. | Save a captured immutable snapshot, mark saved only if its revision still matches; reject Load if the revision changes during decoding. Verify owner-thread access before touching caller streams. |
| XML/XAML formatting | Global encoding-string replacement could rewrite user data; discarded or inserted whitespace changed mixed content. | Repair only the XML declaration; preserve existing text nodes and carriage-return references; indent only element-only containers outside inherited `xml:space="preserve"`. Bound nesting and added indentation. Processing instructions end at `?>`, not an embedded `>`. |

Manual review concentrated on the text, rendering, control, workspace and structured-language paths above. The complete regression suite still exercises the existing compiler, protocol, snippet, macro, line-index and packaging paths. This is not a claim that every unmodified implementation or device behavior has received an exhaustive independent audit.

## Streaming literal search

`EditorScene.Text.SnapshotLiteralSearch` is independent of Uno controls:

```csharp
var snapshot = document.Snapshot(); // capture on the document owner thread
var search = new SnapshotLiteralSearch("needle", matchCase: false, wholeWord: true);
var next = search.FindNext(snapshot, offset: 0, wrap: true);
var scope = snapshot.GetSpan(0, snapshot.Length);
var edits = search.CreateReplaceAllEdits(scope, "replacement", maxEdits: 100_000);
// Apply later only on the owner thread and against the same revision:
document.ApplyEdits(edits, snapshot.Revision);
```

Matching is ordinal, with optional .NET ordinal-ignore-case rules, rather than culture-sensitive casing. Whole-word edges examine neighboring Unicode scalar categories in the complete snapshot, including combining marks and connector punctuation. A restricted scope does not turn the middle of a word into a boundary.

`FindAll` is lazy, ascending and non-overlapping by default. Set `allowOverlap` explicitly when needed. It supports result limits and cancellation; the pooled buffer is returned and cleared when enumeration ends or is disposed. `CreateReplaceAllEdits` rejects an exceeded limit instead of returning an incomplete edit plan. Empty patterns are rejected. Replacement strings are literal, not regular-expression substitutions.

The reader buffer is proportional to `4096 + pattern.Length - 1`, not document length; patterns are capped at 1,048,576 UTF-16 units. Reverse lookup scans the specified preceding region and retains its last match, so it remains linear in that region. This API does not replace the native regex search panel or implement Actipro's complete search-provider contracts.

## New native commands

The Edit menu adds **Select All Occurrences**, **Go To Matching Bracket** and **Select To Matching Bracket**. Ctrl+D continues selecting the next occurrence, now retaining the actual wrapped primary selection. Select All Occurrences limits output to 10,000 carets and rejects larger requests rather than truncating them.

Hosts can call `SelectAllOccurrences()` and `NavigateToMatchingBracket(extendSelection: true)` directly. Bracket navigation requires analysis for the current revision and leaves text unchanged. These selection/analysis-dependent commands are excluded from macro recording; existing macro XML cannot silently activate unsupported behavior.

`SetDocumentAndLanguage(document, language)` avoids an intermediate foreign-provider binding. An unchanged language assignment no longer destroys/recreates the current coordinator and lexical styles. External documents and providers remain caller-owned as documented by their existing APIs.

## Reusable interval queries

`EditorScene.LanguageServices.RangeIndex<T>` copies and validates range coordinates at construction. It supports overlapping and nested tags and zero-length point tags, with stable document ordering.

```csharp
var index = new RangeIndex<EditorDiagnostic>(analysis.Diagnostics, d => d.Range, snapshot.Length);
var visible = new List<EditorDiagnostic>();
RangeQueryStatistics statistics = index.CopyOverlaps(new SourceRange(start, length), visible);
```

Queries append to a caller-owned list. Clear/reuse it to avoid per-query allocations after its capacity is warm. Positive query ranges are half-open; zero-length queries test one point. `CopyIntersecting(first, lastInclusive, list)` also supports inclusive endpoint queries for EOF diagnostics. The index prunes subtrees using maximum ends; building it still requires sorting and memory proportional to tag count.

## Performance evidence and limits

The benchmark executable now includes `tail_literal_search`, `bracket_scan_sparse` and `deep_diagnostic_queries_1000`, in addition to the PR #3 line-index/LSP measurements. Reproduce with:

```sh
dotnet run --project tools/EditorScene.Benchmarks -c Release -- artifacts/performance.json
```

Each row retains all 11 samples after three warmups, median and observed p95 times, and managed allocations. The previous paths reproduce the relevant code from `dd8b565`; the earlier PR #3 rows retain their own documented baseline. Setup/disposal are excluded. The interval-query benchmark uses a warm index, 100,000 ranges and 1,000 near-end queries per sample. Sparse bracket scanning uses 4,200,002 characters and one pair; tail search uses a fresh 4,200,006-character snapshot and a search starting at offset 4,200,000. The latter measures avoiding an unnecessary full-document copy, not faster whole-document matching.

CI stores the raw report in `artifacts/tests/performance.json`; use its source revision and environment fields when interpreting results. These microbenchmarks are not physical-GPU, FPS, input-latency, power or universal-complex-script measurements. Whole-document bracket construction is still linear and runs at analysis publication; dense pair storage remains proportional to pair count. Regex search, compilation, very large individual lines, and total rendering allocations are not eliminated by these changes. The renderer still uses native Uno/Skia with browser CI's software SwiftShader WebGL2 context.

## XML formatting policy

Formatting preserves existing whitespace instead of guessing which whitespace is semantically insignificant without a schema. It adds indentation only inside containers with child elements and no text nodes, honors inherited `xml:space`, and leaves inline text-bearing elements unchanged. Carriage returns originating from character references are entitized on output. Nesting deeper than 256 and more than 8 MiB of added indentation are explicitly rejected before returning edits. This is conservative XML formatting, not XAML type/schema assistance or byte-identical XML serialization.

The implementation uses the platform XML parser with DTDs and external resolution disabled. Relevant primary platform documentation:

- https://learn.microsoft.com/en-us/dotnet/standard/linq/preserve-white-space-serializing
- https://learn.microsoft.com/en-us/dotnet/api/system.xml.xmlwritersettings.newlinehandling

Remaining product compatibility and platform-validation work stays listed in [compatibility.md](compatibility.md).
