# Sortable native diagnostic grid

This increment continues from the SDI-style workspace merged as `c30d547498d6c1a9a513b2c7d1912af3a677344c`. It replaces the fixed diagnostic table presentation with independently reusable native grid behavior. It is not a full DataGrid implementation or a claim of Actipro feature/API/pixel parity.

## Visible behavior

The Error List displays original severity vectors, Code, Line, Char, File and Description. Clicking a header selects ascending order; clicking the same header again selects descending order. An arrow and tooltip identify the current ordering. Equal keys retain the provider's original sequence in both directions. Line and Char compare numerically, not lexically. Ascending severity shows errors, warnings, then information. Code, Description and File use ordinal case-insensitive comparisons.

Each header's right-edge grip supports dragging and keyboard Left/Right resizing. Home or double-click resets that column to its default. Reset restores all preferred widths and the original provider ordering; it deliberately leaves the severity and text filters unchanged. Clear empties only the text filter, including whitespace-only input. The header and row columns share one horizontal scrolling surface while the ListView retains its own finite-height vertical viewport.

Defaults in device-independent pixels are 28, 82, 46, 46, 160 and 420 respectively. Minimums are 24 for severity, 64 for File, 96 for Description and 32 for other columns. Preferred widths are capped at 2,048. Description expands to fill unused viewport width; its effective width can therefore exceed its preferred width. Widths/order are view-local and survive document rebinding, but are not persisted across application restarts.

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

`GetColumnWidth` returns the preferred width; `GetEffectiveColumnWidth` includes Description's viewport fill. `GetSortButton` and `GetColumnGrip` expose the actual native controls. `ColumnLayoutChanged` observes preferred-width changes, and `RowsChanged` observes data-result publication. `ProjectionBuildCount`, `FilterBuildCount`, `SortBuildCount` and `RealizedRowCount` expose work counters for hosts/tests. SortBuildCount belongs to the current immutable table and resets when a new table replaces it.

`ProviderOrder` has no descending variant: requesting one throws before mutation. Invalid enum values, non-finite widths and out-of-range widths fail explicitly. No-op sort/width assignments avoid publication. Methods which mutate a disposed grid throw `ObjectDisposedException`.

Rows preserve their immutable object identity through ordering. The control retains a selected diagnostic when that same row remains in the result. A new analysis or different document must not inherit a same-offset row selection from the old table. Navigation requires reference membership in the displayed result and the exact current document, revision and analysis; stale rows are not translated speculatively. Sorting, filtering and column resizing never edit source text or create document undo units.

## Immutable query engine

`EditorScene.LanguageServices.DiagnosticTableView` adds ordering and bounded query reuse to the existing `DiagnosticTable`, without adding a native UI dependency:

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

The view retains one ordering and one filtered result, rather than a cache for every typed string. Exact repeated queries return the same immutable array. Provider order without filtering returns the original table array. Changing a filter reuses the current ordering; changing a sort key/direction rebuilds that ordering. A None visibility mask does not force an unnecessary sort.

Sorting is a stable, iterative merge sort. It uses two temporary arrays of row references and publishes one owned immutable array, with no changes to row objects or source diagnostics. Equal keys retain input order; descending comparisons swap operands instead of negating arbitrary comparer results. Filtering scans the ordered rows and creates a new immutable result when the query differs. Filename matching is computed once for the current-document table.

A per-view lock serializes construction. Cancellation is checked before and after acquiring it, between merge passes, periodically during merge output, and for every filtered row. Waiting for the lock itself is not interruptible; the token is checked immediately after the lock is acquired. Cancelled operations do not publish partial results. An ordering completed before cancellation during filtering may remain cached. Construction and retained arrays remain proportional to diagnostic count; bounded cache entries do not mean constant total memory.

## Native rendering and lifetime

`DiagnosticTemplates` is a compiled resource dictionary. Its `DiagnosticRowPresenter` binds the whole immutable row and sets native text cells directly. This removes reliance on individual reflection-bound row properties in trimmed applications. Original cached `EditorIcon` vectors replace font-dependent severity characters while preserving the existing `DiagnosticTableRow.Glyph` API for consumers.

The ListView instantiates presenters only for realized rows. A presenter registers with its enclosing grid on load and unregisters on unload. Width/theme changes update those presenters instead of constructing a control for every diagnostic. Presenters have no document subscriptions. Their icons use the existing per-instance disposable Skia picture cache.

Data publication checks a generation after ItemsSource and selected-item changes so a synchronous host callback cannot restore an earlier document after rebinding/disposal. Post-navigation focus is checked against the surviving editor and selection. Disposal detaches editor, filter, row-input and layout subscriptions, clears external events and cached results, and detaches realized row ownership. Hosts continue to own their editors/documents.

The review also fixed a normalized-query/input-state mismatch: whitespace-only input did not change the filter key, so a Clear button updated only after query publication could remain disabled. Input chrome now follows raw text, while the data cache still uses trimmed text. The browser regression verifies this without accepting extra projection/filter/sort work.

## Validation and review scope

The added model tests cover six keys in both directions, stable equal keys, numeric positions, exact array reuse, filtering, cancellation/invalid arguments, concurrency, table isolation and randomized odd-length inputs compared with stable LINQ ordering. Native host checks cover real control APIs, retained selection, unchanged-state counters, widths, labels, rebinding and disposal.

The published WebAssembly browser suite adds a real compiler-produced 300-error document. It checks actual rendered filenames/severity vectors, bounded realized presenter counts, pointer sorting, filter reuse, whitespace clearing, actual grip dragging/arrow keys/Home, shared row/header widths, sorted-row navigation, narrow horizontal scrolling, light/dark screenshots and row release after switching documents. The observation bridge is opt-in and does not mutate the app. These paths remain part of each of the three fresh full-suite runs, not a replacement for the previous tests.

The package script still packs all eleven libraries and validates one executed headless consumer plus compiled desktop and browser consumers using exact built package bytes in an isolated cache. It now exercises the new ordering/column/template APIs and retains the consumer source alongside its project examples.

Review covers the changed query, control, template, application observation, test and build-integration code. Unchanged editor/language/LSP/snippet/macro/rendering code retains its regression coverage; this is not an exhaustive manual audit of every source file, driver, platform or assistive technology. Read workflow results and their source revisions for actual pass/fail. Describing a test here is not proof that an unfinished run passed.

## Reproducible performance measurements

`WorkspaceBenchmarks` adds `diagnostic_order_10000_requests_10` to `artifacts/tests/performance.json`, retaining three warmups, eleven raw samples, median, observed p95 and current-thread managed allocation counts.

`fresh_stable_sort` creates ten new query views and sorts the same preprojected 10,000 rows on each sample. It includes view, scratch and ordering allocations. `cached_order_lookup` performs ten identical requests against one already ordered view and asserts array identity and a single sort build. Initial projection and the cached ordering setup are excluded.

This compares uncached versus cached use of the **same new implementation**, not an older implementation, Actipro, total editor latency or physical-GPU throughput. It measures avoiding repeated work, not making new sorts free. Native retained memory, other-thread allocations, collection binding, layout, rasterization and presentation are excluded. Prior unfavorable compiler benchmark results remain in the complete report and documentation.

## Remaining scope

Fresh projection, sorting and changed filtering are still synchronous when invoked by DiagnosticList. The reusable engine can be used on immutable data by a host worker, but asynchronous control publication/debouncing is not implemented here. Multi-file aggregation, grouping, multi-key sorts, column reordering, persisted layouts, full DataGrid automation and complete original-product error-list/API/visual behavior remain separate work. Full printing, docking/auto-hide and the other boundaries in [compatibility](compatibility.md) are not resolved by this diagnostic increment.

The broader workspace reference remains Actipro's public SDI Code Editor sample, documented in [classic workspace](classic-workspace.md). No proprietary binaries, extracted artwork or font files were added.
