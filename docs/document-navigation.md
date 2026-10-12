# Document navigation and adaptive tab mapping

This increment follows PR #13, merged as `5dab80efcf3859922d1066ca87ed5ee36caeeb8b`. It adds a native document-list menu, keeps tab-strip controls reachable at narrow widths, and retains existing native tab controls across membership changes. It also introduces an adaptive scanner for tab-column maps. These are incremental features of the independent Uno implementation, not complete Actipro Docking or SyntaxEditor parity.

## Visible document navigation

The compact document strip now has three regions: a horizontally scrolling tab area, an anchored **All documents** button, and the existing **Close document** button. The latter two are outside the scrolled content, so they do not move off-screen with a long document list. Close retains the existing Save/Discard/Cancel behavior; selecting another document does not close or discard the previous document.

**All documents** displays open document captions with dirty markers and the current selection check mark. Selecting an entry requests activation by its stable document ID rather than by filename. Duplicate filenames are therefore distinct documents, even though the sample does not add full-path disambiguation to their captions. **Window → Next Document / Previous Document** traverse insertion order with wraparound. **Ctrl+Alt+Down** opens the document list; the Window menu also exposes Documents. Native keyboard/browser shortcut handling remains subject to the host intercepting a shortcut before the application receives it.

Activating an already active document preserves the active split, caret, analysis and undo history instead of rebinding the document unnecessarily. Activation and resize queue one native bring-into-view request for the active tab. Opening, closing, renaming or saving a document reuses surviving tab controls. Caption/dirty changes update existing text and tooltip properties; they do not recreate the tab or menu entry.

The visual reference is Actipro's public tabbed MDI documentation, particularly its all-document overflow menu, document tooltips and Ctrl+Alt+Down invocation:

https://www.actiprosoftware.com/docs/controls/wpf/docking/workspace-mdi-features/tabbed-mdi

The reference has additional pin/preview/drag/docking/close-button modes that are not provided by this increment. The sample keeps its existing append order and shared-document split model rather than pretending to implement all vendor MDI policies. Original project icons and native theme resources remain in use; no proprietary source or artwork is copied.

## Reusable native menu

`EditorDocumentList` is part of `EditorScene.Controls` and derives from the real Uno `MenuFlyout`. Its input is data-only metadata; it does not retain live editor documents or file handles.

```csharp
var menu = new EditorDocumentList();
var first = new EditorDocumentListEntry(Guid.NewGuid(), "Document.cs");
var second = new EditorDocumentListEntry(Guid.NewGuid(), "Notes.txt", IsModified: true);
menu.SetDocuments([first, second]);
menu.SelectedDocumentId = first.Id;
menu.DocumentInvoked += (_, args) =>
{
    // The host resolves this identity, activates its own document, then accepts selection.
    ActivateHostDocument(args.Document.Id);
    menu.SelectedDocumentId = args.Document.Id;
};
var button = new Button { Content = "Documents", Flyout = menu };

// Update a caption or dirty marker without replacing its native menu item.
menu.UpdateDocument(second with { IsModified = false });
// At host teardown, detach the Flyout from the button and dispose the menu.
```

The host owns activation and should set `SelectedDocumentId` only after accepting a request. `TryInvoke(id)` returns false for a stale or unknown identity. A rejected request does not silently change selection. `DocumentInvoked` exceptions propagate to the caller; this component does not swallow application errors or retry activation. Removed menu items detach their click handlers.

`SetDocuments` snapshots and validates its entire input before replacing membership. IDs must be nonempty and unique; null entries and blank titles are rejected. Titles need not be unique. Existing `ToggleMenuFlyoutItem` instances are retained by ID, including across reordering. The component owns the inherited Items collection while reconciling; hosts should change entries through this API rather than directly mutating Items or identity tags.

Native dependency-property callbacks can synchronously replace membership or dispose the menu. Selection checks and reconciliation stop when their target context is superseded. An activation callback can remove its own requested entry without reviving the old selection. This does not promise rollback of arbitrary host property callbacks, and deliberately recursive host code is not made safe merely by this API. The menu belongs to its creating UI thread; disposal rejects further API mutation without disposing caller-owned documents.

## Retention and cost

`ItemBuildCount` counts native menu items constructed; the opt-in application diagnostics similarly expose tab builds. Opening six additional documents should construct six tabs and six menu items, not recreate all prior controls. Ordinary caption updates and document switches reuse those controls. The browser regression asserts these exact counters across additions, activation, theme changes and removal.

Retention is not zero-cost virtualization. SetDocuments allocates a validated metadata snapshot, ID set and dictionary, then reconciles the native item sequence. Reordering can be quadratic in the item count due to native list lookups. The sample's tab reconciliation also builds a membership set and scans existing tabs. The native menu retains one item per open document and the horizontal strip retains one button per document. Normal dirty/caption updates are incremental, but existing sample status refreshes still inspect tab captions. Thousands of simultaneous documents require separate large-list and workspace optimization work.

## Adaptive text-tab scanning

Document tabs and text tab characters are separate features. `EditorScene.Text.TabColumnMap` still maps one immutable line's UTF-16 offsets to tab-expanded logical columns and back. Its arrays, metadata, public API, snapping behavior, leading-whitespace semantics and soft-tab Backspace queries are unchanged.

The preceding constructor efficiently skipped long non-tab runs using span searches, but dense input repeatedly searched very short spans. The current constructor samples at most the first 128 UTF-16 units of each 4,096-unit reader chunk. At least 16 tabs selects a single scalar pass for that chunk; other chunks retain the prior span-search loop. Lazy tab arrays, 64-bit logical columns, per-chunk cancellation and clearing pooled character buffers are preserved.

This is a bounded heuristic, not a universal speed guarantee. A sparse prefix followed by a dense tail can select the span path; a dense prefix followed by ordinary text can select the scalar path. Both produce identical mappings. Sampling adds work even on tab-free chunks, and the same O(tab count) arrays remain necessary. Neither path flattens the document or retains its snapshot after construction.

The NUnit oracle suite covers every offset, selected tab-interior columns, leading whitespace and soft stops for tab widths 1, 3, 4, 8 and 1,000. Fixtures include dense/sparse chunk transitions, reader boundaries, all-tab lines, UTF-16 surrogate/combining sequences, snapshot independence and pre-cancelled construction. These tests check logical UTF-16 positions, not shaped glyph, bidi or grapheme-cell measurements.

## Measurement and validation

`TabColumnBenchmarks` retains both historical scalar and immediately preceding span constructors with their original field shapes, reader usage, list behavior and cleared buffer return. It compares the current adaptive constructor against them on the same immutable snapshots and warmed shared pool. The directly preceding span baseline additionally has the same leading-whitespace metadata as the current implementation; the older scalar baseline does not.

Seven fixtures include sparse, tab-free, indentation-only, dense, all-tab, mixed-chunk and sparse-prefix/dense-tail lines. Three warmups and eleven sequential samples report median, observed p95 and current-thread managed allocations. Result counts, expanded lengths and available leading-whitespace values are checked inside measured runs. Setup, unrelated query methods, whole-app scheduling, native layout/painting, end-to-end input latency and physical GPU are excluded. Raw samples remain in the full regression performance artifact; unfavorable results and previous compiler regressions are not removed.

The actual Uno-host suite covers menu identity, dirty captions, control retention, validation, host-declined activation, removal and callback/disposal behavior. Browser coverage uses native pointer and keyboard input, real dirty documents, narrow overflow controls, active-tab bounds and light/dark menu screenshots. Isolated package consumers compile the new API for desktop and browser alongside all existing package-byte checks. All preceding regression/native/package/three-fresh-browser gates remain required. A test description is not itself a passing result; consult the exact revision's workflow before treating a build as validated.

Initial integration errors used an incorrect icon namespace and xUnit attributes in this NUnit project; both were repaired without introducing another test framework or removing assertions. Local filesystem/Python tools were unavailable in this continuation, so source changes were committed through GitHub and executed in Actions. No claim is made about unknown local-only files or independent local archive validation.

## Remaining parity

Per-document tab pin/preview modes, drag reordering and docking, rich switchers, workspace persistence, full filename/path policy, printing, remaining advanced language/refactoring/LSP services and complete IME/accessibility/platform qualification remain outside this increment. The controls continue to use Uno/Skia; browser CI exercises real WebGL2 through software SwiftShader, not physical-GPU throughput or direct WebGPU rendering.
