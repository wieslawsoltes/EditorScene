# Diagnostic keyboard and toolbar navigation

This increment follows the sortable/resizable [diagnostic grid](diagnostic-grid.md). It adds an independent EditorScene keyboard workflow, not a claim that every Actipro key binding or diagnostic API has been reproduced.

## User interaction

**F8** selects the next visible diagnostic; **Shift+F8** selects the previous one. The keys work while the bound editor or the Error List has keyboard focus. The Error List also exposes two compact native arrow buttons, with tooltips identifying the shortcuts. These controls use the existing original cached vector icons and the current light/dark/high-contrast palette.

Traversal follows the **current sorted and filtered result order**, not document-offset order. Every row is visited, including distinct diagnostics at the same source position. With no selected row, Next starts at the first visible row and Previous starts at the last. Navigation wraps by default. The selected diagnostic remains visible in the list, and its source range is selected in the editor. No text edit or undo unit is produced; read-only documents permit navigation.

Empty or obsolete results do not move the source caret. Navigation buttons are disabled for an empty/stale result and during active macro recording or playback. Paused recording permits navigation without recording it. Ctrl/Alt/Windows-modified F8 combinations are left for the host; these are not overwritten by the result-navigation shortcut. The existing editor's playback input shield remains in effect.

The key handler is attached to the bound editor and detached when the Error List is rebound/disposed. A still-bound hidden Error List can support F8 from the editor; an unbound list cannot navigate the preceding editor. If multiple hosts attach lists to the same editor, the first handler which handles the event prevents duplicate routing; this is not a generalized workspace-wide command arbitration system.

## Reusable APIs

```csharp
var list = new DiagnosticList { Editor = editor };
bool next = list.NavigateNextDiagnostic();
bool previous = list.NavigateNextDiagnostic(backwards: true);
bool noWrap = list.NavigateNextDiagnostic(wrap: false);

Button nextButton = list.NextDiagnosticButton;
Button previousButton = list.PreviousDiagnosticButton;
bool available = list.CanNavigateDiagnostics;
```

The public navigation operation runs on the document/view owner thread. It returns false for unavailable results, a non-wrapping boundary, disallowed macro state, or synchronous host interference. A disposed grid rejects navigation with `ObjectDisposedException`. Earlier completed caret/row changes are not transactionally rolled back when a callback supersedes an operation.

`EditorScene.LanguageServices.DiagnosticNavigationCursor` is a separate headless single-owner component:

```csharp
var cursor = new DiagnosticNavigationCursor(visibleRows);
if (cursor.TryMove(out DiagnosticTableRow? row))
{
    // Verify the current document/revision before selecting row.Diagnostic.Range.
}
cursor.SelectIndex(-1); // no current row; the next move starts at an end
```

The cursor retains the supplied immutable row array and a zero-based current index (`-1` means none). Construction validates the array and non-null references once. Movement and index selection use constant time and do not allocate. Distinct equal-valued rows, duplicate offsets and repeated references remain separate positions. Caller arrays/diagnostic objects are never mutated. Replacing a result requires a new cursor; the old cursor continues to reference its original result. The cursor intentionally does not own a live document, perform automatic revision tracking, navigate files or manage synchronization for multiple mutating threads.

## Correctness review

The native view recreates the cursor only when its immutable visible result changes. Before movement it synchronizes the cursor with the native selected index, avoiding a search through all rows for ordinary Next/Previous commands. Sorting and filtering retain the existing query cache; traversal does not rebuild projections or orderings.

Navigation captures the editor, document, analysis, visible result, publication generation and source selection before selecting the next row. Native SelectedIndex changes can invoke host callbacks, so those captured values and the chosen row are checked again before touching source selection. A recursion guard covers the entire request, including Refresh and result publication, not just the final caret change.

Source navigation is staged: clear extra carets, set the insertion caret, set the selected source range, scroll and focus. Current-result and native endpoint checks run between stages. A synchronous callback which changes the document, provider, caret, selection, multi-selection, source text or lifetime stops the remaining operation. Macro state is checked as well. This protects both new cursor navigation and existing row-click/Enter navigation from bypassing macro recording or overwriting a newer host selection.

The first native regression run found that the new guard assumed selection endpoints always remained unchanged during caret movement. The pinned UnoEdit session actually moves empty selection endpoints with a changed insertion caret; nonempty selections retain their endpoints. The guard now computes the expected endpoints before the caret change, preserving this behavior without removing the independent host-callback checks. Regressions for both ordinary navigation and callback interference remain enforced.

These guards do not promise rollback of arbitrary external callback side effects, and callback code which throws remains host code. Navigation remains current-document only. It does not aggregate projects or resolve stale diagnostic offsets speculatively.

## Validation

Headless tests cover forward/backward starts, wrapping, non-wrapping failure, empty/single-row results, duplicate offsets/references, equal-valued row identity, array replacement isolation, invalid indices and warm allocation-free movement. Native checks exercise real selection and sorted/filter order, read-only documents, empty results, macro recording/pause, rebinding/disposal, row-selection callbacks and callbacks at source-caret changes.

Browser coverage uses actual F8/Shift+F8 and native arrow buttons against compiler-produced diagnostics, changes native sort/filter controls, verifies wrap behavior and tests empty-result recovery. It runs alongside the previous real 300-row grid, column resizing, navigation, split, editor, LSP, macro, snippet and file tests. The diagnostics bridge remains read-only.

Isolated package consumers execute the cursor API and compile the native navigation controls from built packages. All ordinary regression, native, package and three-pass browser gates remain required before main deployment. Workflow results and source revisions determine actual pass/fail; describing intended coverage here does not certify an unfinished run.

Generated Python bytecode briefly introduced by integration validation was removed from the final source, and `.gitignore` now excludes `__pycache__` and Python bytecode. Temporary integration workflows are removed. Existing source dependency pins and the normal CI/deployment gates are unchanged.

## Performance boundaries

`DiagnosticNavigationBenchmarks` records `diagnostic_cursor_cycles_10000`: 10,000 Next/Previous pairs over a preconstructed cursor containing 10,000 rows, with three warmups and eleven samples. It retains raw timings and current-thread managed allocations in `artifacts/tests/performance.json`.

[Retained measurements at f39c1d49](performance/diagnostic-navigation-f39c1d4.json) report **0.0261 ms median, 0.0291 ms observed p95 and zero managed bytes** for that whole batch of 10,000 pairs. It is a new operation without a previous-version baseline, not a speedup comparison.

Initial table projection and cursor construction/validation are excluded. This measures the reusable cursor only, **not** native ListView selection, callbacks, source navigation, scrolling, painting, input latency or physical-GPU work. Native navigation may allocate while selecting/laying out controls. Prior fresh-sort costs and the documented compiler construction regressions are not eliminated by this navigation increment; the complete report retains their measurements, including unfavorable allocation and tail-latency results.

See [compatibility.md](compatibility.md) for remaining product scope: full project language/refactoring services, schemas/grammar tooling, printing, docking/persistence, broader protocol capabilities, full DataGrid automation and complete original-product API/visual/IME/accessibility parity.
