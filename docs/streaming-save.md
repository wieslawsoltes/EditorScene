# Save All and prepared snapshot writing

This increment follows the native tab-close workflow merged as `3eb2433e03f4a960a295c58f24ce874544816977`. It adds a real Save All command and removes whole-document/whole-output allocations from the application's ordinary file-save path. It does not establish complete Actipro feature, API or pixel parity.

## User interface and batch semantics

**File → Save All**, the original overlapping-save vector toolbar button, and the File toolbar-overflow group save modified documents in their current tab order. All three are disabled when every document is clean or another coordinated save/close operation is in progress. The icon dims with availability. **Ctrl+Shift+F12** invokes Save All; the existing Ctrl+S and Ctrl+Shift+S Save/Save As behavior is unchanged. A nontext shortcut was chosen instead of Ctrl+Alt+letter to avoid adding an AltGr text-input collision. Platform/browser shortcut interception still applies; the visible commands remain alternatives.

The batch freezes dirty document IDs before its first asynchronous operation. Clean documents are not needlessly saved or prompted. Documents added afterward are not included. Each identity is resolved again immediately before saving; removed or already-clean targets are skipped. The active document and view are not changed simply to save another document. Successful earlier saves remain successful if a later picker is cancelled, a provider fails, or the current target changes during writing. Later targets are not attempted in that case. This is a sequential batch, not an atomic multi-file transaction.

Save All uses the same nonqueuing `DocumentOperationCoordinator` as Save, Save As and tab closing. A second save/close request is rejected while the first is outstanding, rather than queued against a different future active document. The batch preserves existing native Save/Discard/Cancel close semantics. Under WebAssembly, the app still treats uploaded files as private temporary copies and requests an explicit save/download destination; it does not silently overwrite the user's original uploaded file. Browser download permissions and file-picker behavior are owned by the platform.

## Reusable prepared content

`EditorScene.Text.SnapshotSaveContent` separates immutable text preparation from a host-owned destination:

```csharp
using System.Text;
using EditorScene.Text;

// Capture the snapshot on its document's owning thread.
var snapshot = document.Snapshot();
var content = SnapshotSaveContent.Create(snapshot, new UTF8Encoding(false, true));

// Open or replace the destination only after preparation has succeeded.
// The caller owns the stream and controls seeking/truncation/atomic replacement.
await content.WriteToAsync(destinationStream, cancellationToken);
```

Preparation clones the encoding and validates the entire snapshot using an independent incremental encoder. It computes a 64-bit encoded byte length including the selected encoding's preamble. A strict UTF-8 failure near the end of a file is therefore detected **before** the application opens/truncates its destination, just as the preceding whole-string GetBytes path detected it before writing.

Writing uses a second encoder pass through the frozen snapshot. It preserves surrogate pairs that cross reader chunks, fallback state and CR/LF/CRLF code units. The configured preamble is emitted exactly once, including for an empty snapshot. Each writer has independent encoder/reader state, so prepared content can be written to independent streams concurrently and remains usable after the live document changes or is disposed. The helper never marks a document saved.

The encoder rents 4,096 UTF-16 code units and 16,384 bytes per active pass. It passes at most those fixed sizes to the encoder and destination, awaits each write before reusing its buffer, and clears both buffers when returning them to the pools. It does not materialize `snapshot.Text` or retain an output byte array proportional to file size. The prepared object does retain the immutable rope snapshot, encoding configuration and preamble. Host-supplied deterministic encodings are required; an encoder that cannot make progress within the bounded buffer fails explicitly. Encoding-specific state, unusual fallback behavior and host/provider buffering are not subject to the two buffer bounds.

Preparation and writing check cancellation between chunks and before/after completion. The reusable writer does not seek, truncate or dispose the caller's stream. It flushes only after all bytes have been written and does not attempt another write/flush after an I/O failure. Cancellation or I/O failure may leave a **partial destination**. This API is not atomic file replacement, a rollback service or a guarantee against arbitrary custom stream behavior.

## File-provider completion and saved state

The sample prepares content before opening the `StorageFile` stream, truncates that stream to remove any old tail, then writes/flushes the prepared bytes. It attempts `CachedFileManager.CompleteUpdatesAsync` after the deferred update even if writing failed, so the provider is not left deferred. The original write error is rethrown; simultaneous completion failure is retained alongside it. A successful provider status must be Complete or CompleteAndRenamed.

Only after successful writing and provider completion is the live document eligible to be marked saved. Its revision and encoding must still match the captured values. A changed document is not incorrectly marked clean just because an older snapshot was written. The save's identity is the selected batch/close target, not whichever view is active after the asynchronous operation. Existing filename updates and LSP save notifications remain in place.

These changes bound the application's encoder buffers, **not total process or provider memory**. A WebAssembly download provider may buffer complete files. Completion after a partial failed write may expose partial data; the source remains unsaved and errors are reported. Cross-platform atomic replacement requires separate provider-specific work. The synchronous `EditorDocument.Save(Stream)` API is unchanged by this increment.

## Reusable Save All plan

`DocumentSavePlan.Create` validates every input ID, including clean entries, and freezes only modified IDs in source order. It enumerates input once and retains no live documents, filenames, file handles or callbacks.

```csharp
var plan = DocumentSavePlan.Create(openDocuments.Select(document =>
    new DocumentSaveCandidate(document.Id, document.IsModified)));

bool completed = await plan.ExecuteAsync(async (id, cancellationToken) =>
{
    // Host resolves identity, handles removed/clean entries, chooses a destination,
    // authorizes I/O and checks the saved revision. False stops remaining targets.
    return await SaveHostDocumentAsync(id, cancellationToken);
}, cancellationToken);
```

Execution awaits each host callback before starting the next and checks cancellation again afterward, even when that callback returned true. False stops the batch; exceptions and cancellation propagate. An explicit later use of the same immutable plan is permitted, but there is no automatic retry. Callbacks retain the caller's synchronization context for UI-owned work. `DocumentOperationCoordinator` is a separate host policy; the immutable plan does not own a lock or dispatch callbacks to another thread.

Planning uses O(document count) identity validation and target storage. The sample resolves each target from its existing list, and Save All availability currently scans open documents on relevant state updates. Large-workspace indexing and progress/cancellation UI are separate concerns; no constant-time or zero-allocation batch claim is made.

## Validation and performance methodology

Headless tests compare exact bytes against whole-string encoding across UTF-8 with/without BOM, UTF-16 LE/BE, UTF-32 and fallback encodings. They cover boundary surrogates, empty files, late strict failures, pending fallback flush, mutable encoding capture, concurrent asynchronous writes, cancellation, destination failure, stream ownership, snapshot independence and non-flattening. Save-plan tests cover order, single enumeration, malformed later entries, interruption, callback sequencing, explicit recovery and rejection of overlapping batches.

Actual Uno-host tests write through real `StorageFile` and provider completion, verify shorter-file truncation, captured old revisions, BOMs, unchanged destinations after invalid preflight, original vector independence and shortcut classification. Browser coverage uses actual menu/toolbar/keyboard input and real file downloads. It saves two inactive dirty documents while a clean document is active, verifies exact downloaded bytes, undoes a saved document to an empty-but-dirty revision, invokes Save All by its real shortcut, checks disabled controls in the native overflow, and closes saved documents without dirty prompts. Existing close tests continue checking an inactive Save through the new writer.

All preceding regression, actual native-host, isolated-package and three-fresh-browser gates remain. All eleven packages retain exact restored-byte verification and source-independent consumers. Describing these tests is not a pass claim; exact workflow results and source revisions establish completion.

`SnapshotSaveBenchmarks` compares the prior app's preamble + GetBytes(snapshot.Text) + Concat.ToArray path against **preparation plus streaming**, including both encoder passes. Fixtures use four million ASCII characters and about one million UTF-16 units of Japanese/emoji/mixed line endings. Both write to Stream.Null and flush. Byte-for-byte equivalence is checked outside timing.

The old snapshot's full text is deliberately warm; its first flattening allocation is excluded, conservatively favoring the old implementation. Shared buffers are warm. Three warmups and eleven sequential samples report median, observed p95, current-thread managed allocations and all raw values. Actual file I/O, provider buffering, native UI, physical GPU and complete editor latency are excluded. Stream.Null completes synchronously, so allocation during asynchronously completing providers is not represented. Two passes may trade CPU cost for lower peak allocation; any unfavorable timings and pre-existing compiler/tab-map results remain in the complete report rather than being hidden.

## Review and remaining scope

Review covers the new encoder/state machine, immutable batch executor, app save/provider/dirty-state paths, native command surfaces, input gesture and test/package/benchmark integration. It is not an exhaustive audit of every unchanged subsystem, platform or device. Local container/Python execution was unavailable in this continuation, so publication and validation use connected GitHub and Actions; unknown local-only files and independent local archive checks were not inspected.

Full original-product API/pixel parity, transactional multi-file replacement, provider-specific memory limits, save-progress cancellation UI, persisted workspaces, docking, printing, remaining advanced language/LSP/refactoring services and complete IME/accessibility/platform qualification remain separate work. Rendering continues to use Uno/Skia; browser CI exercises actual WebGL2 through software SwiftShader, not measured physical-GPU throughput.
