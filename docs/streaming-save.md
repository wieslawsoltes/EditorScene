# Save All and prepared snapshot writing

This increment follows the native tab-close workflow merged as `3eb2433e03f4a960a295c58f24ce874544816977`. It adds a real Save All command and removes whole-document/whole-output allocations from the application's ordinary file-save encoder path. It does not establish complete Actipro feature, API or pixel parity.

## Native commands and batch behavior

**File → Save All**, the original overlapping-save vector toolbar button, and the File toolbar-overflow group save modified documents in tab order. All three are disabled when every document is clean or another coordinated save/close is in progress; the toolbar icon dims. **Ctrl+Shift+F12** invokes Save All. Existing Ctrl+S and Ctrl+Shift+S Save/Save As behavior remains unchanged. The nontext gesture avoids introducing a Ctrl+Alt+letter/AltGr collision. Browsers and operating systems may intercept shortcuts; visible native commands remain alternatives.

The batch freezes dirty document IDs before its first asynchronous operation. Clean files are not needlessly saved or prompted, and newly added documents are not included. Each ID is resolved again before saving; removed or already-clean entries are skipped. Saving an inactive document does not activate it. Cancelling a picker, a failed provider or a changed target stops later saves. Earlier completed writes are not rolled back: this is a sequential batch, not an atomic multi-file transaction.

Save All shares the nonqueuing DocumentOperationCoordinator with Save, Save As and tab closing. Concurrent requests are rejected rather than queued against a future active document. Under WebAssembly uploaded files remain private temporary copies; saving requests an explicit destination/download instead of silently overwriting the original upload. Browser permissions and picker behavior remain platform-owned.

## Filename regression and repair

Browser workflow `38353889124` successfully downloaded two inactive dirty documents, then exposed a naming defect: `Untitled31.cs` became `Untitled31.cs.cs`. The same duplication could recur on subsequent saves. The application supplied the full name as SuggestedFileName while Uno's download picker also appended the first FileTypeChoices extension.

CreateDocumentSavePicker now supplies the basename and extension separately. Compound names, Unicode and extension case are preserved; directory components are excluded. Names without a suffix retain the text-file fallback. Dot-only-prefix names such as `.editorconfig` currently become `.editorconfig.txt`; fully extensionless/dotfile save policy is not introduced here. Invalid empty/blank/directory-only names fail before showing a picker.

Native tests verify repeated suggestion round trips, compound/Unicode names, fallback and invalid input. Browser tests check the actual download's suggestedFilename, exact saved bytes, unchanged tab captions and a second save after Undo. The old assertion was not changed to accept duplicated suffixes. Download listener failures and unexpected extra files remain failures even after the expected number of download events has arrived.

Primary implementation reference for the cause:
https://github.com/unoplatform/uno/blob/56e430f5c56af07e86ea4b3fe782c83d2a94ac64/src/Uno.WinRT/Storage/Pickers/FileSavePicker.wasm.cs

## Reusable prepared content

```csharp
using System.Text;
using EditorScene.Text;

// Capture on the document owner thread. Preparation can use the immutable snapshot.
var snapshot = document.Snapshot();
var content = SnapshotSaveContent.Create(snapshot, new UTF8Encoding(false, true));
// The host opens/truncates/replaces the destination only after preparation succeeds.
await content.WriteToAsync(destinationStream, cancellationToken);
```

SnapshotSaveContent clones the encoding and validates the entire snapshot through an incremental encoder before the application's destination writer opens/truncates its stream. ByteLength is a 64-bit length including the configured preamble. Strict encoding failures near the end are detected before that write begins, as with the previous whole-string GetBytes path. A platform file picker may independently create its selected file; preflight does not reverse picker side effects.

The second pass encodes and writes the frozen snapshot. Surrogate pairs crossing reader chunks, fallback state and CR/LF/CRLF are preserved. The preamble is emitted exactly once, including for an empty snapshot. Each write has independent encoder/reader state; prepared content supports independent concurrent destinations and survives live-document changes/disposal. It does not mark any document saved.

Each pass rents 4,096 UTF-16 code units and 16,384 output bytes, uses at most those sizes for conversion/writes, and clears both rented buffers on return. A write is awaited before its buffer is reused. The new path never requests snapshot.Text or a file-sized output byte array. It retains the immutable rope snapshot, encoding configuration and preamble. Deterministic host encodings are required; an encoder unable to progress within the bounded buffer fails explicitly. Custom encoder state/fallback objects and provider buffering are not bounded by these two buffer sizes.

Cancellation is checked between chunks and around completion. The reusable writer neither seeks, truncates nor disposes the destination. It flushes after all output and attempts no further write/flush after an I/O failure. Partial output can remain on cancellation/failure. Hosts own atomic replacement and rollback; this is not a transactional stream API. The synchronous EditorDocument.Save(Stream) API is unchanged.

## Provider completion and saved-state checks

The sample prepares content, opens the StorageFile stream, truncates its old tail and writes/flushed encoded bytes. It attempts CachedFileManager.CompleteUpdatesAsync after a deferred update even when writing failed. The original error propagates; simultaneous completion failure is retained with it. Success requires Complete or CompleteAndRenamed.

Only successful writing/provider completion with matching live revision and encoding permits MarkSaved. A newer revision is not marked clean merely because an old snapshot was written. Filename updates and LSP save notifications still target the captured document. Encoding-only dirty tracking is not a new document-model feature of this change.

This bounds application encoding buffers, not total process/provider memory. Download providers can stage whole output outside these buffers. Completing after a partial write may expose partial data; the operation reports failure and does not mark that save successful. Cross-platform atomic replacement remains separate provider-specific work.

## Reusable Save All plan

```csharp
var plan = DocumentSavePlan.Create(openDocuments.Select(document =>
    new DocumentSaveCandidate(document.Id, document.IsModified)));

bool completed = await plan.ExecuteAsync(async (id, token) =>
{
    // Resolve current membership, authorize I/O, choose a destination, check revision.
    return await SaveHostDocumentAsync(id, token);
}, cancellationToken);
```

Creation validates every ID, including clean entries, enumerates once and freezes only dirty IDs in order. It retains no documents, filenames, file handles or callbacks. ExecuteAsync awaits callbacks sequentially, checks cancellation afterward even when a callback returns true, and stops on false. Exceptions/cancellation propagate. Earlier saves are not rolled back and there is no automatic retry; explicit later plan reuse is allowed. Callbacks preserve the caller's synchronization context. The immutable plan owns no UI dispatcher or lock; coordination is separate.

Planning uses O(document count) validation/target storage. The sample re-resolves IDs through its existing list, and Save All availability scans open documents. Large-workspace indexing and save-progress/cancellation UI remain separate work, not constant-time or zero-allocation batch claims.

## Validation and performance

Headless regressions compare exact UTF-8/BOM, UTF-16 LE/BE, UTF-32 and fallback output. They cover boundary surrogates, empty files, strict late failure, fallback flush, mutable encoding capture, concurrent delayed writes, cancellation, failed writes/flushes, caller-owned streams, old snapshots and non-flattening. Plan/executor cases cover ordering, single enumeration, malformed later entries, partial success, callback sequencing, cancellation, explicit recovery and overlap rejection.

Actual Uno-host checks use StorageFile/provider completion for shorter-file truncation, captured old content, unchanged files after invalid preflight, empty BOMs, icon independence and all virtual-key/modifier combinations. Filename checks increase the enforced native minimum to 385. Browser tests use real native menus/buttons/keys and downloads, preserve another active document/revision, save an empty dirty result after Undo, enforce exact names and clean controls, and close saved files without prompts. Existing inactive-tab Save tests also execute the new writer.

All prior regression, native, eleven-package isolated-consumer and three-fresh-browser gates remain required. Tests and thresholds are not pass claims; exact revision-specific workflows establish results.

SnapshotSaveBenchmarks compares the old preamble + GetBytes(snapshot.Text) + Concat.ToArray path with **both preflight and streaming**. Four-million-character ASCII and approximately one-million-unit Unicode fixtures write/flush Stream.Null. Output equality is checked outside timing. The baseline's full text is already warm, conservatively excluding its initial flattening allocation. Pools are warm; three warmups and eleven sequential samples retain raw timings and current-thread allocations. Real I/O/provider buffering, asynchronously completing provider allocations, native UI, GPU and full input latency are excluded.

The verified regression artifact for source `88181e262d33c08393573d4ee46169a4161a2b0d` measured ASCII allocation 8,000,144 → 1,248 bytes, but median CPU time 1.5521 → 2.5841 ms. Unicode allocation was 4,285,888 → 1,376 bytes, with median time 3.1735 → 6.5325 ms. This is a memory-allocation improvement with CPU cost, not an all-workload speedup. That source's complete browser workflow failed on filenames; its passed regression job is distinct evidence. Current-source results belong to their own run, and other unfavorable compiler/tab-map rows remain in the full reports.

## Review boundaries

Review covers the encoder/state machine, batch executor, file/provider/saved-state handling, native command/input integration and regression/package/benchmark changes. It is not an exhaustive audit of every unchanged subsystem or device. Initial local execution was unavailable; final continuation recovered Python filesystem/artifact inspection and found no working local checkout. GitHub Actions supplies application builds and native/browser execution; archive checks are separately identified.

Full original-product API/pixel parity, transactional multi-file replacement, provider-specific memory limits, progress/cancellation UI, workspace persistence, docking, printing, remaining advanced language/LSP/refactoring and complete IME/accessibility/platform qualification remain unfinished. Rendering uses Uno/Skia; browser CI exercises WebGL2 through software SwiftShader, not measured physical-GPU throughput.
