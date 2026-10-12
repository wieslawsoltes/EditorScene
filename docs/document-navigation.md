# Document navigation and adaptive tab mapping

This increment follows PR #13, merged as `5dab80efcf3859922d1066ca87ed5ee36caeeb8b`. It adds a native document-list menu, keeps tab-strip controls reachable at narrow widths, and retains existing native tab controls across membership changes. It also introduces an adaptive scanner for tab-column maps. These are incremental features of the independent Uno implementation, not complete Actipro Docking or SyntaxEditor parity.

## Visible document navigation

The compact document strip has three regions: a horizontally scrolling tab area, an anchored **All documents** button, and the existing **Close document** button. The latter two are outside the scrolled content, so they do not move off-screen with a long document list. Close retains the existing Save/Discard/Cancel behavior; selecting another document does not close or discard the previous document.

**All documents** displays open captions with dirty markers and the current selection check mark. Selecting an entry requests activation by its stable document ID rather than filename. Duplicate filenames remain distinct documents, although the sample does not add full-path disambiguation. **Window → Next Document / Previous Document**, **Ctrl+PageDown / Ctrl+PageUp** traverse insertion order with wraparound. **Ctrl+Alt+Down** opens the document list; the Window menu also exposes Documents. Preview key routing handles these workspace shortcuts before the native text editor consumes page-navigation keys. Shift-modified source-selection gestures remain untouched, and a browser/OS may intercept a shortcut before the application receives it.

Activating an already active document preserves the active split, caret, analysis and undo history instead of rebinding unnecessarily. Activation and resize queue one native bring-into-view request for the active tab. Opening, closing, renaming or saving reuses surviving tab controls. Caption/dirty changes update existing text and tooltip properties rather than recreating tabs or menu items.

The public visual/behavior reference is Actipro's tabbed MDI documentation, particularly its all-document dropdown, tooltips and Ctrl+Alt+Down invocation:

https://www.actiprosoftware.com/docs/controls/wpf/docking/workspace-mdi-features/tabbed-mdi

The reference has additional pin/preview/drag/docking/close-button modes not provided here. The sample retains its append order and shared-document split model rather than claiming all vendor MDI policies. Original project icons and native theme resources remain; no proprietary source or artwork is copied.

## Reusable native menu

`EditorDocumentList` belongs to `EditorScene.Controls` and derives from the actual Uno `MenuFlyout`. Its input is data-only metadata; it retains no live editor documents or file handles.

```csharp
var menu = new EditorDocumentList();
var first = new EditorDocumentListEntry(Guid.NewGuid(), "Document.cs");
var second = new EditorDocumentListEntry(Guid.NewGuid(), "Notes.txt", IsModified: true);
menu.SetDocuments([first, second]);
menu.SelectedDocumentId = first.Id;
menu.DocumentInvoked += (_, args) =>
{
    // Resolve the identity and activate your host-owned document before accepting.
    ActivateHostDocument(args.Document.Id);
    menu.SelectedDocumentId = args.Document.Id;
};
var button = new Button { Content = "Documents", Flyout = menu };
menu.UpdateDocument(second with { IsModified = false });
// At teardown, detach the button's Flyout and dispose the menu.
```

Hosts own activation and set SelectedDocumentId only after accepting it. TryInvoke returns false for stale/unknown IDs; a rejected request does not silently change selection. DocumentInvoked exceptions propagate; the component does not swallow errors or retry activation. Removed menu items detach their click handlers.

SetDocuments snapshots and validates the complete input before replacing membership. IDs must be nonempty/unique; null entries and blank titles fail without partially replacing the menu. Titles need not be unique. Existing ToggleMenuFlyoutItem objects are retained by ID across caption changes and reordering. The component owns inherited Items and identity tags during reconciliation; hosts should use this API instead of mutating them directly.

Native property callbacks may replace membership or dispose the menu synchronously. Selection/reconciliation stops after its context is superseded. Activation callbacks may remove their requested entry without reviving it. This does not roll back arbitrary host callbacks or guarantee deliberately recursive host code. The menu belongs to its creating UI thread. Disposal rejects further mutation without disposing caller-owned documents.

## Retention and cost

ItemBuildCount counts constructed native menu items; opt-in app diagnostics similarly count tab builds. Six new documents construct six tabs and six menu items, not replacements for every prior item. Ordinary dirty/caption updates and document switches retain those controls. Browser regressions assert the counters across additions, activation, themes and removal.

Retention is not zero-cost virtualization. SetDocuments allocates a validated metadata snapshot, ID set and dictionary, then reconciles the native sequence. Reordering can be quadratic because of native list lookups. Tab reconciliation builds a membership set and scans existing tabs. Both surfaces retain one native item per open document; existing sample status refreshes still inspect captions. Thousands of simultaneous documents need separate large-list/workspace optimization.

## Adaptive text-tab scanning

Document tabs and text tab characters are distinct features. TabColumnMap maps one immutable line's UTF-16 offsets to expanded logical columns and back. Its arrays, metadata, public API, snapping, leading-whitespace and soft-tab Backspace semantics are unchanged.

The previous constructor skips long ordinary runs with span searches, but dense input repeatedly searches tiny spans. The initial adaptive version sampled the first 128 units of every 4096-unit reader chunk. Its measurements improved dense medians but showed sparse/tab-free overhead; those results remain in `performance/adaptive-tabs-4827e20.json` rather than being overwritten.

The refined constructor first performs the tab search required by either path. A tab-free chunk adds its length and exits without density sampling. The ordinary prefix before the first tab is skipped exactly once. Only when at least 16 units remain does the constructor sample up to 128 units, now starting at that first tab. At least 16 tabs selects a scalar traversal of the remainder; otherwise the existing span search strategy continues, reusing the first search result. This also avoids missing a dense region merely because it follows a long tab-free prefix.

It remains a bounded heuristic, not a universal timing guarantee. The first tab may be followed by sparse text and then a dense tail, or a dense prefix may be followed by ordinary text. Both paths produce identical mappings; all O(tab count) arrays remain necessary. Lazy lists, 64-bit columns, per-chunk cancellation and clearing pooled character buffers are preserved. Neither path flattens the document or retains its snapshot after construction. New measurements must be checked before treating removal of sampling work as a measured speedup.

The NUnit oracle covers every offset, selected tab-interior columns, leading whitespace and soft stops for widths 1, 3, 4, 8 and 1000. Fixtures include mixed-density reader boundaries, all-tab lines, UTF-16 surrogate/combining sequences, old snapshots after edits and pre-cancelled construction. These are logical UTF-16 positions, not shaped-glyph, bidi or grapheme-cell measurements.

## Measurement and validation

TabColumnBenchmarks retains both historical scalar and immediately preceding span constructors, including original field shapes, reader/list behavior and cleared pooled-buffer return. It compares current construction on identical immutable snapshots and a warmed shared pool. The immediately preceding span baseline has the same leading-whitespace metadata; the older scalar baseline does not. Neither baseline changes when the adaptive path is refined.

Seven fixtures include sparse, tab-free, indentation-only, dense, all-tab, mixed-chunk and sparse-prefix/dense-tail lines. Three warmups and eleven sequential samples report median, observed p95 and current-thread managed allocations. Counts, expanded lengths and available leading-whitespace metadata are checked inside measured runs. Source construction, unrelated query methods, whole-app scheduling, native layout/painting, input latency and physical GPU are excluded. Raw samples, unfavorable results and earlier compiler regressions remain in the complete report.

The actual Uno-host suite covers native identities, dirty captions, control retention, validation, host-declined activation, removal and callback/disposal interference. Browser coverage uses actual pointer/keyboard input, dirty documents, narrow overflow, active-tab bounds and both themes. Package consumers compile the API for desktop/browser alongside all prior package-byte checks. All regression/native/package/three-fresh-browser gates remain required; descriptions are not passing-result claims.

Run38291655974 passed regressions, native checks and packages, but the existing split command-binding scenario failed before the new navigation cases. Its failure state had an open menu during a direct, single-observation split click. The revised test requires the initiating menu to close and uses the established click helper, which waits for stable bounds across distinct native observations. Both active-view/read-only assertions remain. No retry, direct activation bridge or relaxed assertion was introduced.

Initial integration errors used a wrong icon namespace and xUnit attributes in this NUnit project. Both were repaired without adding another framework or removing assertions. Local filesystem/Python tools were unavailable, so changes were committed through GitHub and executed in Actions. Unknown local-only files and independent local ZIP verification are not claimed.

## Remaining parity

Tab pin/preview modes, drag reordering/docking, rich switchers, persisted workspaces, complete filename/path policies, printing, remaining advanced language/refactoring/LSP services and full IME/accessibility/platform qualification remain outside this increment. Rendering remains Uno/Skia; browser CI exercises real WebGL2 through software SwiftShader, not physical-GPU throughput or direct WebGPU rendering.
