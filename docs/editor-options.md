# Editor options, tab stops and logical columns

This increment follows the merged diagnostic-navigation work in PR #8. It adds an independently reusable options panel and corrects tab-aware editing/status behavior. It does not claim a pixel-identical Actipro options dialog or complete vendor API parity.

## Native options workflow

**View → Editor Options…** opens **Text Editor Options**, a compact native Apply/Cancel dialog with two sections: Display and Tabs and input. It edits a private copy rather than writing into the view for every checkbox or numeric keystroke.

The panel includes font size, line numbers, word wrap, visible whitespace, indentation guides, ruler visibility/column, tab/indent size, spaces-versus-tabs, bracket completion and automatic IntelliPrompt. Restore defaults changes only that draft. Cancel preserves the editor and document. Apply validates every value before the first setter runs and rejects a replaced document/provider or changed options target.

Settings are view-owned. Document rebinding preserves them, and a newly created split copies tab/space/ruler settings along with its existing display options; subsequent changes to either view remain independent. Existing text is never converted merely by changing the settings. Use the existing Convert Indentation commands for text conversion.

The native dependency currently uses one width for both tab stops and indentation units. The options panel labels it **Tab and indent size** and does not pretend to support independent values. Allowed values are 1–1000 for that width, 8–40 for font size and 1–10000 for the ruler column. These limits are explicit implementation choices, not a promise of every original-product setting or zoom policy. Settings are not persisted across application restarts in this increment.

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

`EditorViewOptions` is an immutable value snapshot; `new EditorViewOptions()` supplies defaults while `default(EditorViewOptions)` is intentionally rejected by validation because its numeric fields are zero. `EditorOptionsView` owns native input controls, not an editor or its document. `ShowViewOptionsAsync` composes the panel with a native dialog for a loaded view. A dialog is not opened during an active macro session.

`ApplyViewOptions` validates the entire input first, then checks document identity, revision, provider and the expected current options between synchronous setters. A host callback that changes these or disposes the editor stops remaining changes. The return value is false on such interference; already completed setters are not rolled back and arbitrary external side effects are not transactional. Reapplying equal options does not rebuild controls or emit a gratuitous state update. Native option changes are observed so changes through the exposed native editor can invalidate the column map/status too.

## Correct tab-stop insertion

The preceding single-caret native Tab path calculated its next stop from the raw UTF-16 offset. That treats an existing tab as one column and inserts the wrong number of spaces. EditorScene's Tab and Indent paths now measure the tab-expanded logical column first.

For a tab width of eight, `\tX|` is at zero-based logical column nine. In spaces mode, Tab inserts seven spaces to reach sixteen, not six spaces from raw offset two. In tabs mode it inserts one literal tab. Selected-line indentation continues to use the native protected-line implementation. Snippet/completion Tab handlers remain ahead of the ordinary indentation path. Macro replay, including older imported `native.Tab` steps, uses the same ordinary indentation semantics.

For multiple carets, each caret computes its own required insertion string. `MultiSelection.ReplaceEach` accepts one string per normalized range, validates and copies the input before permission callbacks, applies one atomic edit group and retains primary-caret identity. A denied range prevents the entire planned edit. A synchronous edit notification which replaces selection state is no longer overwritten by the old operation's final caret publication. This API is also available to headless hosts for other per-range replacement operations.

The increment does not redefine all rectangular selection, multi-caret backward-indentation, language-specific smart indentation or soft-tab Backspace behavior. These remain separately testable compatibility boundaries rather than inferred from the ordinary Tab fix.

## Sparse tab-column mapping

`EditorScene.Text.TabColumnMap` maps one immutable snapshot line between line-local UTF-16 offsets and zero-based tab-expanded logical columns:

```csharp
var snapshot = document.Snapshot();
var map = new TabColumnMap(snapshot, lineIndex: 0, tabSize: 8);
long column = map.GetColumn(lineRelativeOffset);
int offset = map.GetOffset(column, afterTab: false);
string insertion = map.GetIndentation(lineRelativeOffset, convertTabsToSpaces: true);
```

Construction scans only the requested line using a pooled 4096-character buffer. It records offsets and expanded positions only for tab characters; it neither materializes the whole document nor retains the snapshot text or a live document. Queries use binary search over the stored tabs without allocation. Reverse queries inside a tab snap to its leading edge unless the caller requests `afterTab`; columns beyond the line clamp to its end. End-of-line delimiters are not counted. Long expanded columns use 64-bit arithmetic.

The view retains one map for the current document/revision/line/tab width. Moving the caret within that unchanged line reuses it; changing the line, revision or width rebuilds it. Initial construction remains linear in line length and memory remains proportional to tab count. A very long edited line can still be expensive; this is not constant-time cold layout or an incremental grapheme-layout engine.

The status bar now distinguishes **Col**, a one-based tab-expanded UTF-16 logical column, from **Ch**, the one-based raw UTF-16 position within the line. This deliberately does not claim shaped glyph width, bidi visual order, grapheme-cell width, East Asian character cell widths or soft-wrap-relative columns. Surrogates and combining marks count as their UTF-16 units except for tab expansion. A full visual-coordinate API requires the rendering engine's layout data and remains separate scope.

## Validation and measurement scope

Headless tests compare tab maps to a direct oracle, check reader-boundary cases, reverse affinity, CRLF exclusion, sparse retention, cancellation, invalid coordinates and post-disposal use. Per-range replacement tests cover independent strings, primary identity, atomic undo, invalid counts/nulls, permission denial, captured arrays and selection interference.

Actual Uno-host checks cover status columns, unchanged-line cache counters, application/no-op validation, mixed-tab insertion, per-caret strings, undo, macro replay, split inheritance, dialog draft behavior and synchronous callbacks that change documents/providers/options/lifetime. The published-browser tests use actual View-menu activation, NumberBox text input, checkboxes and Apply/Cancel buttons; screenshots are captured in both light and dark themes. The test bridge only observes state and coordinates. All prior editing, snippets, macros, LSP, diagnostic-grid and large-document scenarios remain in each complete browser run.

Isolated package consumers execute the text mapping/replacement APIs and compile the native options/control APIs from built packages without source project references.

`TabColumnBenchmarks` records both constructing a map for a 100003-unit line with two tabs and 10000 lookups on the preconstructed map. Each has three warmups and eleven samples with median, observed p95 and current-thread managed allocations. The lookup timing excludes construction; construction uses a warm shared buffer pool. These are new workloads without a previous-version performance baseline, not physical-GPU, total-frame or end-to-end input-latency claims. Raw report provenance and actual workflow conclusions determine validation; this document is not itself evidence that an unfinished run passed.

The review covers changed source, tests and integration. Existing tests cover unchanged subsystems, but this is not an exhaustive manual audit of every repository file, device or assistive technology. Full API/visual parity, project/refactoring/schema features, grammar tooling, printing, docking/persistence, wider LSP and complete IME/accessibility remain in the compatibility matrix.

## Primary references

- Actipro SyntaxEditor public feature documentation: https://www.actiprosoftware.com/docs/controls/wpf/syntaxeditor/index
- Actipro zoom behavior (reference, not full parity): https://www.actiprosoftware.com/docs/controls/wpf/syntaxeditor/user-interface/editor-view/zooming
- Native NumberBox input and commit behavior: https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/number-box
