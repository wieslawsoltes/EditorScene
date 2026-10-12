# Editor options, tab stops and logical columns

This increment follows the merged diagnostic-navigation work in PR #8. It adds an independently reusable options panel and corrects tab-aware editing/status behavior. It does not claim a pixel-identical Actipro options dialog or complete vendor API parity.

## Native options workflow

**View → Editor Options…** opens **Text Editor Options**, a compact native Apply/Cancel dialog with two sections: Display and Tabs and input. It edits a private copy rather than writing into the view for every checkbox or numeric keystroke.

The panel includes font size, line numbers, word wrap, visible whitespace, indentation guides, ruler visibility/column, tab/indent size, spaces-versus-tabs, bracket completion and automatic IntelliPrompt. Restore defaults changes only that draft. Cancel preserves the editor and document. Apply validates every value before the first setter runs and rejects a replaced document/provider or changed options target.

At a section width below 480 device-independent pixels, the same Display and Tabs/input controls stack vertically in the existing scroll view. Check labels wrap instead of overflowing their column. Crossing the breakpoint changes grid placement rather than reconstructing controls, preserving numeric drafts and native focus. `EditorOptionsView.UsesStackedLayout` exposes the current layout choice. The width is an independent implementation choice, not an original-product dimension guarantee.

Cancel restores typing focus only to an unchanged, surviving editor target. The dialog also hides if its editor unloads. A new document/provider, changed caret/selection, changed options or active macro session must not be overwritten by the old dialog. Apply uses the existing post-menu focus helper after acceptance. The browser regression types and undoes text immediately after Cancel rather than merely observing that the dialog disappeared.

Settings are view-owned. Document rebinding preserves them, and a newly created split copies tab/space/ruler settings along with its existing display options; subsequent changes to either view remain independent. Existing text is never converted merely by changing settings. Use the existing Convert Indentation commands for text conversion.

The native dependency currently uses one width for both tab stops and indentation units. The options panel labels it **Tab and indent size** and does not pretend to support independent values. Allowed values are 1–1000 for that width, 8–40 for font size and 1–10000 for the ruler column. Settings are not persisted across application restarts in this increment.

## Reusable APIs

```csharp
var options = editor.GetViewOptions();
var accepted = editor.ApplyViewOptions(options with
{
    TabSize = 8,
    ConvertTabsToSpaces = true,
    ShowRuler = true,
    RulerColumn = 80
});

var panel = new EditorOptionsView(editor.GetViewOptions());
if (panel.TryGetOptions(out var proposed))
{
    // Apply only after the host explicitly accepts the panel.
    editor.ApplyViewOptions(proposed);
}
panel.Dispose();
```

`EditorViewOptions` is an immutable value snapshot; `new EditorViewOptions()` supplies defaults while `default(EditorViewOptions)` is intentionally rejected because its numeric fields are zero. `EditorOptionsView` owns native input controls, not an editor or document. `ShowViewOptionsAsync` composes the panel with a native dialog for a loaded view. A dialog is not opened during an active macro session.

`ApplyViewOptions` validates the entire input first, then checks document identity, revision, provider and expected options between synchronous setters. A host callback that changes these or disposes the editor stops remaining changes. The return value is false on such interference; already completed setters are not rolled back and arbitrary external side effects are not transactional. Equal options avoid redundant setter/event work. Native option changes are observed so assignments through the exposed native editor invalidate column/status state too.

## Correct tab-stop insertion

The preceding single-caret native Tab path calculated its next stop from the raw UTF-16 offset. That treats an existing tab as one column and inserts the wrong number of spaces. EditorScene's ordinary Tab and Indent paths now measure the tab-expanded logical column first.

For a tab width of eight, `\tX|` is at zero-based logical column nine. In spaces mode, Tab inserts seven spaces to reach sixteen, not six from raw offset two. In tabs mode it inserts one literal tab. Selected-line indentation continues to use the native protected-line implementation. Snippet/completion Tab handlers remain ahead of ordinary indentation. Macro replay, including older imported `native.Tab` steps, uses the same ordinary indentation semantics. Read-only Tab remains a no-op rather than reporting an editing exception.

For multiple carets, each caret computes its required insertion string. `MultiSelection.ReplaceEach` accepts one string per normalized range, validates and copies input before permission callbacks, applies one atomic edit group and retains primary-caret identity. A denied range prevents the entire planned edit. A synchronous edit notification replacing selection state is no longer overwritten by the old operation's final publication. The API is reusable for other headless per-range replacements.

The increment does not redefine all rectangular selection, multi-caret backward-indentation, language-specific smart indentation or soft-tab Backspace behavior. These remain separate compatibility boundaries.

## Sparse tab-column mapping

`EditorScene.Text.TabColumnMap` maps one immutable snapshot line between line-local UTF-16 offsets and zero-based tab-expanded logical columns:

```csharp
var snapshot = document.Snapshot();
var map = new TabColumnMap(snapshot, lineIndex: 0, tabSize: 8);
long column = map.GetColumn(lineRelativeOffset);
int offset = map.GetOffset(column, afterTab: false);
string insertion = map.GetIndentation(lineRelativeOffset, convertTabsToSpaces: true);
```

Construction scans only the requested line using a pooled 4096-character buffer. It records offsets and expanded positions only for tab characters; it neither materializes the whole document nor retains the snapshot text or live document. Queries use direct binary search over the stored tabs. Reverse queries inside a tab snap to its leading edge unless `afterTab` is requested; columns beyond the line clamp to its end. Line delimiters are excluded, and expanded columns use 64-bit arithmetic.

The view retains one map for the current document/revision/line/tab width. Moving within the unchanged line reuses it; changing line, revision or width rebuilds it. Initial construction remains linear in line length and memory proportional to tab count. A very long edited line can still be expensive; this is not constant-time cold layout or an incremental grapheme-layout engine.

The status bar distinguishes **Col**, a one-based tab-expanded UTF-16 logical column, from **Ch**, the one-based raw UTF-16 position within the line. It does not measure shaped glyph width, bidi visual order, grapheme cells, East Asian character cells or soft-wrap-relative columns. Surrogates/combining marks count as UTF-16 units except for tab expansion. Full visual coordinates require rendering-layout data and remain separate scope.

## Validation and measurement scope

Headless tests compare tab maps to a direct oracle, check reader-boundary cases, reverse affinity, CRLF exclusion, sparse storage, cancellation, invalid coordinates and detached use. Per-range replacement tests cover distinct strings, primary identity, atomic undo, invalid counts/nulls, permission denial, captured arrays and selection interference.

Actual Uno-host checks cover status columns, unchanged-line cache counters, apply/no-op validation, mixed-tab insertion, per-caret strings, undo, macro replay, split inheritance, private drafts and synchronous callbacks changing documents/providers/options/lifetime. Published-browser tests use actual View-menu activation, NumberBox input, checkboxes and Apply/Cancel. They exercise immediate typing after Cancel and narrow-window scrolling/input as well as screenshots in both themes. The bridge observes state/coordinates only. Prior editing, snippets, macros, LSP, diagnostic-grid and large-document cases remain in every complete browser run.

Isolated package consumers execute text mapping/replacement APIs and compile native options/control APIs from the built packages without source references. Describing intended test coverage is not proof of a pending workflow's success: inspect its source revision and job conclusions.

### Measured sparse-map costs

[Retained exact report at 25bbbd](performance/tab-columns-25bbbd.json) records three warmups and eleven samples on .NET 10.0.12, Linux x64:

| Workload | Median | Observed p95 | Managed bytes |
| --- | ---: | ---: | ---: |
| 10,000 lookups on a prebuilt 100,003-unit line with two tabs | 0.2422 ms | 0.2694 ms | 0 |
| Build the map for that line | 0.1228 ms | 0.1454 ms | 712 |

The lookup measurement excludes construction. Construction includes the reader and sparse arrays with a warm shared buffer pool, excluding document/snapshot creation. These are new workloads without a previous-version baseline, not overall-editor speedup, cold startup, physical-GPU or input-latency claims. Current-thread managed allocation does not account for all native memory or unrelated worker threads.

An earlier combined correctness/allocation test observed 24 bytes without identifying the allocation site. Its replacement isolates a non-inlined lookup loop from setup/assertions, uses three warmups, and requires exactly zero bytes in all eleven measured samples. No nonzero allocation allowance was added. This verifies steady-state lookup, not cold execution. The benchmark report independently measures both lookup and construction.

Existing compiler tradeoffs remain recorded: the same report's model-only probe increased from 10.6269 to 24.4859 ms median and from 1,333,776 to 1,948,872 bytes. Warm diagnostic p95 increased from 124.1272 to 154.3879 ms. Sparse-column and UI changes do not imply those regressions are fixed or every path is faster.

The review covers changed source, tests and integration. Existing tests cover unchanged subsystems, but this is not an exhaustive manual audit of every repository file, device or assistive technology. Full API/visual parity, project/refactoring/schema features, grammar tooling, printing, docking/persistence, wider LSP and complete IME/accessibility remain in the compatibility matrix.

## Primary references

- Actipro public feature documentation: https://www.actiprosoftware.com/docs/controls/wpf/syntaxeditor/index
- Actipro zoom behavior (reference, not full parity): https://www.actiprosoftware.com/docs/controls/wpf/syntaxeditor/user-interface/editor-view/zooming
- Native NumberBox input/commit behavior: https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/number-box
