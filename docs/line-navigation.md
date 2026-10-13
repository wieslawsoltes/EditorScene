# Validated Go To Line

This increment follows Save All and strict native validation merged as `8e245f43d09606d2c6d2cf7a8ffbc360bb66704e`. It replaces the sample's generic navigation prompt with a reusable native panel, strict snapshot-position parsing and guarded caret application. It does not establish full Actipro UI/API parity.

## Native interaction and repaired defects

**Edit → Go To Line…**, the existing toolbar command and **Ctrl+G** open a compact native dialog. It displays the document's line range, current line/character, a single line-or-line:character input and an inline validation area. Enter accepts the default Go action; Cancel restores focus without changing the original caret or selection. The input is initially selected for replacement.

Previously the dialog closed before input validation. Negative columns were silently coerced to one, oversized columns were clamped by the underlying navigation method, and the active view was resolved after the await. Invalid input now leaves the same dialog open for correction. The dialog captures its original document, revision, provider, view and selection; if these change, the request is invalidated instead of reinterpreted in another document. Closed input controls are removed from the observation registry so the page does not retain each old dialog.

The input is a positive one-based line number, optionally followed by a positive one-based UTF-16 character position. Surrounding whitespace on each token is allowed. Signs, grouping separators, decimal/exponent forms, extra separators, integer overflow and out-of-range positions are rejected. The character range includes the line's end position but not its delimiter. Positions splitting a UTF-16 surrogate pair are rejected. Tabs count as one UTF-16 unit here, not their visual width. Grapheme clusters, bidi display order and soft-wrapped view lines are separate concepts. An empty document has the single valid position 1:1.

The public behavioral reference is Actipro's GoToLine and GoToLineOverlay sample: compact line-range labels and direct native input. EditorScene keeps its existing modal command surface and adds inline errors; this is not a copy of the vendor's overlay control or its complete behavior. Reference source:

https://github.com/Actipro/WPF-Controls/tree/29f5e6d82d843d1a587e7933a140ceeaa775ca19/Samples/SampleBrowser/ProductSamples/SyntaxEditorSamples/QuickStart/GoToLineOverlay

No proprietary source, icon or font file is included. Existing Uno theme resources style the panel; tests cover dark and narrow light windows.

## Reusable text and native APIs

```csharp
using EditorScene.Text;
using EditorScene.Controls;

var snapshot = document.Snapshot();
if (LineNavigationTarget.TryParse(snapshot, "12:4", out var target, out var error))
{
    // Caller resolves the correct view and its lifetime. No text is edited.
    if (editor.TryNavigateTo(target)) editor.FocusEditor();
}

using var draft = new EditorGoToLineView(snapshot, editor.CaretOffset);
// Host shows this native control in its own dialog/container.
// On acceptance, obtain draft.TryGetTarget(out target) and recheck the live target.
```

`LineNavigationTarget` is an immutable value type retaining document identity, revision, one-based line/character and resolved UTF-16 offset. It holds no snapshot or live document. The default value is invalid. `IsCurrent` checks exact document identity and revision, so undoing back to identical text does not make an old target current. A rename without text changes need not invalidate a position. Requests are bounded to 64 input UTF-16 units.

Parsing reads the existing immutable line index and at most two source code units around the requested boundary. It never flattens source text. The valid warm path uses spans and value types, with no per-query string splitting. Cold line-index construction can still scan the document and allocate the existing index; it is not included in the warm-path claim. Snapshots may be queried on workers after their live document is disposed.

`EditorGoToLineView` owns its draft, not an editor. It validates visible input and reports messages through ValidationMessage. SetUnavailable invalidates a draft when its host context changes. Disposal detaches input handlers and releases its snapshot; hosts retain document ownership. The host is responsible for displaying the panel, accepting or rejecting a target and arranging focus.

`SyntaxEditor.TryNavigateTo` refuses foreign/default/stale targets, active snippet sessions, and macro recording/playback. Paused capture and read-only views permit navigation. It uses native caret/selection and visibility operations but does not request focus itself. After synchronous host callbacks it checks document/revision/provider/session and target selection before any remaining scroll work. A callback that changes the document or caret wins; the old request returns false rather than focusing a replacement view. Completed caret callbacks are not transactional and cannot be rolled back safely.

The original public `GoToLine(int, int)` API retains its historical clamping behavior for existing consumers. This increment adds a strict alternative and uses it in the sample's dialog; it does not silently change all callers' existing API semantics.

## Validation and performance

New NUnit cases cover exact coordinates, CRLF boundaries, tabs, surrogate pairs, empty documents, invalid formats, stale/foreign targets, undo/revision identity, old worker snapshots, bounded input and non-flattening. A warm 10,000-query test on a 100,000-line snapshot enforces zero current-thread managed allocations after the line index is built.

Actual Uno-host tests exercise the draft and native caret APIs: inline error correction, negative/Unicode input, no source edits/undo changes, read-only navigation, stale targets, macro pause policy, host document/caret interference, invalidation and disposal. The strict native gate retains its single-start, single-completion and unique-assertion checks and raises its minimum from 385 to 401.

Browser tests use only real menus, keyboard and text input. They enter invalid ranges and a split-surrogate position, click Go without closing the dialog, correct the input, accept with Enter, cancel while preserving a source selection, type/undo immediately afterward, and navigate in a read-only narrow window. The optional state bridge observes the actual panel; it has no mutation API. All existing full-suite scenarios remain in each of three fresh browser contexts. All eleven isolated package consumers retain exact restored-byte checks and compile/execute the relevant new APIs.

`LineNavigationBenchmarks` compares 10,000 successful line:character requests on the same warm 100,000-line index. The baseline reproduces the preceding dialog's Split/TryParse/Parse sequence but uses the same immutable lookup as the strict path to isolate parsing costs. It is not an exact old whole-dialog/native-buffer measurement. The new path includes range and UTF-16-boundary checks and identity/revision capture.

Three warmups and eleven sequential samples report median, observed p95 and current-thread managed allocation with all raw values. Source/index setup, invalid-error rendering, modal/native layout, focus, scroll, painting and physical GPU are excluded. No end-to-end latency, Actipro performance or hardware speedup is inferred. Existing unfavorable saving/compiler/tab-map workloads remain in the complete report. Actual workflow outcomes, not the presence of test descriptions or artifacts, establish success.

## Review scope and remaining work

Review covers new parsing, native draft/target application, modal lifetime/focus, observation, native/browser tests and package/benchmark integration, plus the previous dialog replaced by this change. It is not an exhaustive audit of every unchanged source file or device. Local filesystem/Python attempts failed in this continuation, so publication and execution use GitHub and Actions; unknown local-only files and independent local archive validation were not inspected.

Overlay hosting, relative navigation, navigation history, virtual-space/grapheme policies, localized input grammars, complete original-product UI/API equivalence and all-device IME/accessibility remain separate work. Workspace persistence, docking, printing and remaining advanced language/refactoring/LSP features are not resolved by this increment. Rendering remains actual Uno/Skia WebGL2 in Chromium CI with software SwiftShader, not measured physical-GPU throughput or direct WebGPU rendering.
