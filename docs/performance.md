# Snapshot and language-service performance

This increment adds persistent line indexing and semantic-token delta requests while retaining the native Uno/Skia editor. The measurements below concern CPU work and managed allocation, not physical-GPU throughput, frame rate, input-to-presentation latency or all Actipro functionality.

## Immutable line API and cached snapshots

`DocumentSnapshot.GetLine(int)` and `GetLineFromOffset(int)` return an immutable `SnapshotLine` with a **zero-based** line number, offset, content length and original delimiter length. `EndOffset` excludes the delimiter; `EndIncludingDelimiter` includes it. Only asking for the line's `Text` allocates its content string. Position/line queries do not materialize the full snapshot string.

The index is a persistent AVL tree of bounded 4,096-character chunks. Leaves retain packed line-break endpoints rather than a copy of the text. Internal summaries merge CR/LF pairs even when edits split a pair across nodes. An initial index requires an O(document length) streaming scan. Once available, edits path-copy tree paths, scan inserted text and coalesce bounded leaves instead of rebuilding a document-sized array. Lookups descend the balanced tree. Old snapshots and lines remain independent and readable after edits, undo/redo or disposal of the live document.

Repeated `EditorDocument.Snapshot()` calls at the same revision and file name return the same immutable snapshot object and allocate nothing. Changing the file name creates a new snapshot with the new metadata. The editor reuses only a completed index: it does not block the editing thread on a worker that is still building the old snapshot's lazy index. In that case a newer snapshot may require another cold scan; this is not an unconditional logarithmic guarantee for every call.

`GetOffset` keeps the existing clamp-to-content-end behavior for oversized columns. `GetPosition` keeps the existing behavior for offsets inside CRLF: such a point belongs to the preceding line, even when its character index extends into that delimiter. Consequently these two APIs are not exact inverses for a point inside CRLF. All positions remain UTF-16 code-unit positions.

## Bounded snapshot differencing

`SnapshotTextChanges.GetMinimalChange(before, after, cancellationToken)` returns one contiguous replacement for forward snapshots of the same document. Shared version history bounds the comparison to the affected region; small edits no longer scan an unchanged multi-megabyte prefix and suffix. The output can be wider when several distant edits are combined, since this API returns one replacement rather than a list of separate differences.

The history walk is capped at 2,048 entries. Longer histories use bounded-window text comparisons instead of retaining an unbounded edit list. Comparisons observe cancellation, do not flatten either snapshot, and expand endpoints around valid surrogate pairs and CRLF. LSP incremental synchronization uses this helper and the new snapshot line index. A cold line index can still require the initial full scan.

## Semantic-token deltas and allocation-light decoding

The LSP client advertises delta support and requests `textDocument/semanticTokens/full/delta` only after the server negotiates it and supplies a result ID. The reusable immutable `SemanticTokenData` validates unordered integer-array edits against the same previous array. Deltas may change individual integers rather than entire five-integer token records. Bounds, overlap, integer values, result IDs and the reconstructed length are checked before replacing cached state.

A delta request may return a full replacement or null. An empty delta for the same snapshot reuses both the immutable data and decoded spans. New-revision decoding remains O(token count), and a non-empty delta reconstructs the integer array; this implementation does not claim incremental rendering or sublinear decoding for every token update.

Malformed deltas and expired bases permit **one** full replacement request. A server that reports method-not-found has deltas disabled for that session. Invalid full recovery fails rather than retrying forever. Cancellation does not cause recovery and does not install a partially parsed cache. A response without a result ID prevents use of that response as a future delta base.

The decoder consumes typed integers, reuses the current line descriptor for tokens on the same line, avoids allocating temporary JSON position objects, and preallocates the final immutable result array. Method tokens map to the existing `function` theme classification. Range requests, multiline/overlapping tokens and modifier-specific styling remain separate work. Limits remain 500,000 integers (100,000 tokens), 100,000 delta edits and a 4,096-character result ID.

## Reproduce the measurements

```sh
dotnet run --project tools/EditorScene.Benchmarks -c Release -- artifacts/performance.json
# Also run automatically after the regression suites:
python3 tools/build.py test
```

The report includes source revision in CI, runtime, operating system, architecture, processor count, input sizes, warmups, sample count, p50/p95 elapsed time and median current-thread managed bytes. It is uploaded with `regression-tests` as `performance.json` for the exact CI revision.

Four comparisons are retained: cold line lookup, one middle edit followed by position lookup, a small LSP change and decoding 10,000 semantic tokens. The first three use the same 4,200,000-character / 100,001-line document. Token decoding uses 10,000 tokens in a 20,000-character document. Baseline helpers reproduce the previous hot-path algorithms on valid fixtures, not every prior validation branch or the entire previous application. In the LSP change comparison, **both** versions start with their previous snapshot's line index already built.

Each variant has three warmups and eleven measured samples. Setup and disposal are outside the measurement; operation allocations are measured with `GC.GetAllocatedBytesForCurrentThread`. p50 is the sixth sorted sample and nearest-rank p95 is the maximum of eleven samples. JIT compilation, scheduling and garbage collection can produce outliers; all rows and tail samples are retained. These descriptive timings are not machine-independent pass/fail assertions.

Separate executable regression guards enforce bounded allocations for fifty edits on a large indexed document, zero allocation for unchanged snapshot capture, bounded small-change allocation, and bounded dense-token decoding. Randomized edit/undo tests compare every line descriptor with a reference scan. Browser tests drive real editor keystrokes through a synthetic WebSocket peer, verify reduced token-integer payloads and inject a malformed delta to verify single-request recovery. This payload check is not a measurement of complete WebSocket byte savings.

## Remaining performance work

Built-in Roslyn analysis still requests full document text, and full LSP synchronization must transmit full text by protocol design. The initial snapshot index, new-revision semantic decoding, bracket analysis, broader language parsing and other view/application operations have not all been made incremental. These changes do not establish physical-GPU, Windows/macOS, IME, accessibility or pixel-identical Actipro conformance. See [compatibility](compatibility.md) and [LSP scope](lsp.md).

Primary protocol reference: [LSP 3.17 semantic-token encoding and deltas](https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/#textDocument_semanticTokens).
