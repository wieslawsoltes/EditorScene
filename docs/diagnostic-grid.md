# Sortable native diagnostic grid

This increment continues from the SDI-style workspace merged as `c30d547498d6c1a9a513b2c7d1912af3a677344c`. It replaces the fixed diagnostic table presentation with independently reusable native grid behavior. It is not a full DataGrid implementation or a claim of Actipro feature/API/pixel parity.

## Visible behavior

The Error List displays original severity vectors, Code, Line, Char, File and Description. Clicking a header selects ascending order; clicking the same header again selects descending order. An arrow and tooltip identify the current ordering. Equal keys retain the provider's original sequence in both directions. Line and Char compare numerically, not lexically. Ascending severity shows errors, warnings, then information. Code, Description and File use ordinal case-insensitive comparisons.

Each header's right-edge grip supports dragging and keyboard Left/Right resizing. Home or double-click resets that column to its default. Reset restores all preferred widths and the original provider ordering; it deliberately leaves severity and text filters unchanged. Clear empties only the text filter, including whitespace-only input. The header and row columns share one horizontal scrolling surface while the ListView retains its own finite-height vertical viewport.

Defaults in device-independent pixels are 28, 82, 46, 46, 160 and 420 respectively. Minimums are 24 for severity, 64 for File, 96 for Description and 32 for other columns. Preferred widths are capped at 2,048. Description expands to fill unused viewport width; its effective width can exceed its preferred width. Widths/order are view-local and survive document rebinding, but are not persisted across application restarts.

## Reusable control API

```csharp
using EditorScene.Controls;
using EditorScene.LanguageServices;

var diagnostics = new DiagnosticList { Editor = editor };
diagnostics.SortBy(DiagnosticSortColumn.Line, DiagnosticSortDirection.Descending);
diagnostics.SetColumnWidth(DiagnosticSortColumn.File, 180);

var selected = diagnostics.RowsView.SelectedItem as DiagnosticTableRow;
var visible = diagnostics.VisibleRows;
diagnostics.ResetColumnWidths();
diagnostics.SortBy(DiagnosticSortColumn.ProviderOrder);
```

`GetColumnWidth` returns the preferred width; `GetEffectiveColumnWidth` includes Description's viewport fill. `GetSortButton` and `GetColumnGrip` expose the actual native controls. `ColumnLayoutChanged` observes preferred-width changes, and `RowsChanged` observes data-result publication. `ProjectionBuildCount`, `FilterBuildCount`, `SortBuildCount` and `RealizedRowCount` expose work counters. SortBuildCount belongs to the current immutable table and resets when a new table replaces it.

`ProviderOrder` has no descending variant: requesting one throws before mutation. Invalid enum values, non-finite widths and out-of-range widths fail explicitly. No-op sort/width assignments avoid publication. Methods which mutate a disposed grid throw `ObjectDisposedException`.

Rows preserve their immutable object identity through ordering. The control retains a selected diagnostic when that same row remains in the result. A new analysis or different document must not inherit a same-offset row selection from the old table. Navigation requires reference membership in the displayed result and the exact current document, revision and analysis; stale rows are not translated speculatively. Sorting, filtering and resizing never edit source text or create document undo units.

## Immutable query engine

`EditorScene.LanguageServices.DiagnosticTableView` adds ordering and bounded query reuse to `DiagnosticTable`, without a native UI dependency:

```csharp
var table = new DiagnosticTable(snapshot, analysis);
var view = new DiagnosticTableView(table);
var errors = view.Query(
    DiagnosticVisibility.Error,
    query: "CS0029",
    column: DiagnosticSortColumn.Line,
    direction: DiagnosticSortDirection.Ascending,
    cancellationToken: cancellationToken);
```

The view retains one ordering and one filtered result, rather than a cache for every typed string. Exact repeated queries return the same immutable array. Provider order without filtering returns the original table array. Changing a filter reuses the current ordering; changing the sort key/direction rebuilds it. A None visibility mask does not force a sort.

Sorting is a stable, iterative merge sort. Two temporary arrays contain row references; one owned immutable array is published without changing any row or source diagnostic. Equal keys retain input order; descending comparisons swap operands instead of negating arbitrary comparer results. Filtering scans ordered rows and creates a new immutable result when the query differs. Filename matching is computed once for the current-document table.

A per-view lock serializes construction. Cancellation is checked before/after acquiring it, between merge passes, periodically during merge output, and for every filtered row. Lock waiting itself is not interruptible. Cancelled operations do not publish partial results. An ordering completed before cancellation during filtering may remain cached. Construction and retained arrays remain proportional to diagnostic count; bounded cache entries do not mean constant total memory.

## Native rendering and lifetime

`DiagnosticTemplates` is a compiled resource dictionary. Its `DiagnosticRowPresenter` binds the whole immutable row and sets native text cells directly, avoiding individual reflection-bound row properties in trimmed applications. Original cached `EditorIcon` vectors replace font-dependent severity characters while preserving the existing `DiagnosticTableRow.Glyph` API for consumers.

The ListView instantiates presenters for realized rows. A presenter registers with its enclosing grid on load and unregisters on unload. Width/theme changes update realized presenters instead of constructing a control per diagnostic. Presenters have no document subscriptions; icons use the per-instance disposable Skia picture cache.

Data publication checks a generation after ItemsSource and selected-item changes so a synchronous host callback cannot restore an earlier document after rebinding/disposal. Post-navigation focus is checked against the surviving editor and selection. Disposal detaches editor, filter, row-input and layout subscriptions, clears external events/cached results, and detaches realized row ownership. Hosts continue to own editors/documents.

### Header ownership and layout review

Because hosts can retain the publicly exposed header buttons and resize grips, simply disposing the grid is not sufficient: a closure attached to those objects can retain the grid. All header Click, drag, keyboard, double-tap and handled-pointer callbacks now use named handlers which are explicitly removed. Header visual-tree links are cleared on disposal. Native tests retain those exposed controls and assert their parent links are detached after disposal.

The six grips share one parsed ControlTemplate, while each creates its own native visual instance. Unchanged effective column layout skips repeated width assignments and realized-row enumeration. Palette brushes are shared across headers in one update. These changes avoid redundant work; they are not a claim that all layout/theme operations allocate nothing or that every host callback is transactional.

### Input chrome and test-helper review

Whitespace-only input does not change the normalized filter query. Clear enablement therefore follows raw text independently of cached result publication; spaces remain clearable without new sorting/projection work.

The browser test also uncovered an input-helper error: selecting all text and typing an empty string leaves the old selection/text intact. The shared helper now sends an actual Backspace for an empty target, and the suite waits for the actual filter text/publication. This fixes the test's real keyboard input rather than calling a private filter method or mutating document state through the observation bridge. Assertions for the 300-error document, filtering, sorting and virtualization remain enforced.

## Validation and review scope

Model tests cover six keys in both directions, stable equal keys, numeric positions, exact array reuse, filtering, cancellation/invalid arguments, concurrency, table isolation and randomized odd-length inputs compared with stable LINQ ordering. Additional warm-query cases assert zero current-thread managed allocation for repeated filtered and unfiltered requests, and verify that later sorts cannot mutate arrays retained by callers.

Native-host checks cover actual control APIs, retained row selection, unchanged-state counters, widths, automation labels, rebinding/disposal, shared templates and retained header detachment. The published browser suite uses a compiler-produced 300-error document and checks rendered filenames/severity vectors, bounded realized presenter counts, header clicks, filter reuse, whitespace clearing, grip dragging/arrows/Home, shared widths, sorted-row navigation, narrow horizontal scrolling, screenshots and row release after document switching. These are included in each of the three complete fresh-context runs, not substituted for existing editor/LSP/snippet/macro/file tests.

The package script still validates all eleven libraries with one executed headless consumer and compiled desktop/browser consumers using the exact built package bytes in an isolated cache. It exercises query, column and compiled-template APIs and retains consumer source with the project examples.

Review covers changed query/control/template code and its app/test/build integration. Unchanged editor, language, protocol and rendering subsystems retain regression coverage, not an exhaustive independent manual audit. Read workflow results and source revisions for actual pass/fail: a description of test intent is not proof an unfinished browser run passed. Native execution is Linux/X11; browser CI uses actual Uno/Skia WebGL2 backed by software SwiftShader, not a physical-GPU benchmark.

## Reproducible performance measurements

`WorkspaceBenchmarks` adds `diagnostic_order_10000_requests_10` to `artifacts/tests/performance.json`: three warmups, eleven raw samples, median, observed p95 and current-thread managed allocation counts.

`fresh_stable_sort` creates ten query views and sorts the same preprojected 10,000 rows for each measured sample. `cached_order_lookup` performs ten identical requests against an already ordered view, asserting array identity and one sort build. Initial projection and cached-order setup are excluded.

[Retained samples at e60db4c](performance/diagnostic-grid-e60db4c.json) report 22.3498 ms median / 1,601,760 managed bytes for ten fresh constructions, versus 0.0006 ms / zero bytes for ten exact warm lookups. This compares uncached and cached use of the **same new implementation**, not an older implementation, Actipro, total editor latency or GPU throughput. It measures avoiding repeated work, not free new sorting. Native retained memory, other threads, collection binding, layout, rasterization and presentation are excluded. The complete report retains the pre-existing compiler regressions and their worse tail measurements.

## Remaining scope

Fresh projection, sorting and changed filtering remain synchronous when invoked by DiagnosticList. A host may run the immutable engine on a worker, but asynchronous control publication/debouncing is not implemented here. Multi-file aggregation, grouping, multi-key sorts, column reordering, persisted layouts, full DataGrid automation and complete original-product error-list/API/visual behavior remain separate work. Printing, docking/auto-hide and other [compatibility boundaries](compatibility.md) are not resolved by this increment.

The broader reference remains Actipro's public SDI Code Editor sample, documented in [classic workspace](classic-workspace.md). No proprietary binaries, extracted artwork or font files were added.
