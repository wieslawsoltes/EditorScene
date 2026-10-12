# Soft-tab Backspace and responsive editor options

This increment extends the options/tab-column work in PR #9. It implements leading-space Backspace, removes an inactive macro-recorder limit from normal text insertion, and improves the native options layout. It is not complete Actipro API or pixel parity.

## Input behavior

With `ConvertTabsToSpaces = true`, `CanBackspaceOverSpacesToTabStop` defaults to `true`: Backspace within the leading ASCII space/tab prefix removes spaces to the preceding logical tab stop. The option is exposed in **View → Editor Options… → Tabs and input**. Turning it off restores ordinary backward deletion; the checkbox is disabled, without discarding its value, when spaces mode is off.

At width four, six leading spaces become four. At width eight, a literal tab followed by three spaces becomes the original tab. The soft-space operation never consumes that tab; a subsequent Backspace immediately after it uses ordinary native deletion. Spaces after content, nonempty selections, Unicode text and line joins retain the native behavior. Non-ASCII whitespace is not part of this implementation's soft indentation prefix. CRLF joins remove the whole delimiter rather than leaving a carriage return behind.

Plain Backspace and Shift+Backspace share the policy, matching the pinned native keymap's backward-deletion alias. Word-deletion modifiers are not redefined. Active snippet/completion sessions retain their existing input handlers. This is not a new virtual-space or rectangular-selection engine.

```csharp
editor.ApplyViewOptions(editor.GetViewOptions() with
{
    TabSize = 4,
    ConvertTabsToSpaces = true,
    CanBackspaceOverSpacesToTabStop = true
});
editor.Backspace();
```

`Backspace()` is a public owner-thread operation. Read-only views do nothing; a protected soft-deletion range or synchronous target change rejects the planned operation. The input adapter reports exceptions through the existing operation-failure channel. Current and imported `native.Backspace` macro actions use the same policy at playback time; a recording records one semantic step, not one step per deleted space. Macro recording still does not capture arbitrary view-option changes or multi-selection setup.

View snapshots, validated application, private dialog drafts and split inheritance include the new flag. Later changes to one split do not change the other. Settings remain view-owned and are not persisted across restarts.

## Reusable text APIs

```csharp
using var document = new EditorDocument("      value");
var map = new TabColumnMap(document.Snapshot(), lineIndex: 0, tabSize: 4);
int leading = map.LeadingWhitespaceLength;          // 6 UTF-16 units
int removal = map.GetSoftTabBackspaceLength(6);     // 2 spaces

var selections = new MultiSelection(document);
selections.Set([new SelectionRange(6, 6)]);
selections.DeleteToPreviousTabStop(4);
```

`TabColumnMap` is immutable, retains no source snapshot/live document, and maps one zero-based snapshot line. Its query accepts a line-local UTF-16 offset; zero means that the caller should use normal Backspace. It rejects coordinates outside the line, excludes line delimiters and respects literal-tab boundaries. Logical columns remain tab-expanded UTF-16 positions, not shaped glyph, bidi, grapheme-cell or soft-wrap coordinates.

`MultiSelection.DeleteToPreviousTabStop` computes each empty caret's deletion separately, reuses one map per affected line, and retains the ordinary selection/grapheme/CRLF fallback. Overlapping deletion spans are normalized before permission queries. All planned edits use one undo group and preserve the primary caret. A denied range prevents partial application. Inputs and selection generations are checked before application; newer host selections published during document notifications are not overwritten by the headless operation.

The native wrapper additionally captures document, revision, language, options, selection object and caret endpoints before permission callbacks. Changed options, caret, provider or read-only state prevent the old soft-deletion plan from editing. Single-caret deletion relies on native anchor tracking instead of assigning an old caret after document notifications. Multi-caret permission delegates live in a separate method so ordinary single-caret input does not allocate their captured context. This is not a guarantee against every side effect of arbitrary external native command handlers.

## Normal input versus macro limits

Previously `OnTextEntering` constructed `MacroStep.InsertText` even while recording was idle. The constructor both allocated a discarded step and validated the macro's 1,048,576-UTF-16-unit limit. Consequently an ordinary host insertion exceeding that limit was rejected despite no macro being recorded.

The input path now creates steps only while actively recording. Plain insertion does not inherit a macro-specific size limit; active recording still rejects an oversized step before editing or adding it. This does not remove the document engine's own limits or promise low latency for arbitrarily large pastes. Backspace and overstrike also avoid discarded idle steps. Existing macro playback/recording tests remain part of the full regression suite.

## Native options layout

The options dialog now scopes its width resources locally and adapts its content width to the current XAML root. On a wide window, Display and Tabs/input appear side by side, with a separate IntelliPrompt heading. Narrow windows stack the same controls; numeric drafts are not reconstructed. Unchanged root notifications do not reset width, and the root-size subscription is removed when the dialog closes. Restore Defaults remains in a fixed footer outside the scroller.

This also addresses PR #9's post-merge browser failure in run `38233665994`. Its narrow-layout test could accept an already-stacked pre-resize layout, then compare old desktop bounds with a 440-pixel target. The later captured failure state fitted the new width. The revised check requires the requested native root width, containment, expected section placement and stable geometry across distinct native observations. It retains the horizontal-overflow assertion; it does not retry failed workflows or mutate the application through a test bridge.

Browser coverage repeatedly crosses desktop/narrow breakpoints, edits and reads the actual numeric input draft, scrolls real controls, and exercises Apply/Cancel/Restore Defaults. It tests both themes, ordinary/Shift Backspace, mixed indentation, the disabled preference, content-space fallback, CRLF joins, and keyboard-created multiple carets with undo. Screenshots remain in the browser validation artifact.

## Performance measurement

The constructor now searches spans for tabs instead of branching on every ordinary character. Tab arrays are still sparse, and their temporary lists are allocated only after a tab is found. The leading whitespace prefix is recorded in the same reader pass. Initial construction still scans the line; rebuilding an edited long line is not constant time, and dense tab storage remains proportional to tab count.

`TabColumnBenchmarks` retains the prior scalar constructor from `75a81960a1bbd6cb1c7d88179b1d639af3af4d5b` as a source-level baseline. Both versions use the same immutable snapshot, pooled 4096-character reader, cleared buffer return, tab width and result checks. Workloads cover a 100,003-unit sparse line, 100,000 units without tabs, 100,000 indentation spaces and a 100,000-unit dense-tab line. Dense cases are retained even when slower. A separate prebuilt-map workload measures 10,000 soft-stop queries.

Reports include all eleven samples after three warmups, median, observed p95 and current-thread managed allocation. Baseline/current groups run sequentially, not as a controlled hardware experiment. Setup is excluded and the shared pool is warm; construction and cached queries are separate workloads. These results do not measure UI latency, full frames, total/native memory, physical GPUs or the Actipro implementation. Earlier compiler regressions remain in the complete report and are not solved by these changes.

## Validation and boundaries

The headless tests cover direct/random oracles, mixed tabs, reader boundaries, invalid offsets, non-ASCII content, overlapping ranges, primary selection, denial/interference and atomic undo. Warm soft-stop queries require exactly zero allocated bytes in each of eleven samples. Native checks exercise actual Uno controls, options/split state, fallback input, macro recording/playback, protected edits and the inactive-versus-active macro text limit. The isolated package script executes the new text APIs and compiles native Backspace/options APIs without source project references.

Test descriptions are not evidence that a pending workflow passed. Check its source revision and actual results in `artifacts/tests`, `artifacts/native-checks.json`, `artifacts/packages/validation.json` and all three `artifacts/browser-pass-*` folders. Pages deployment remains gated on successful main validation. Native execution is Linux/X11; browser graphics use Uno/Skia WebGL2 through software SwiftShader.

Review covers the changed source, pinned input integration, lifetime handlers, tests and package boundaries, not every unchanged subsystem or device. Independent tab/indent widths, smart indentation/outdent policies, persistent settings/workspaces, full project/refactoring/schema/LSP capabilities, printing/docking and complete API/pixel/IME/accessibility parity remain open in the [compatibility matrix](compatibility.md).

## Primary references

- Actipro whitespace and Backspace policy: https://www.actiprosoftware.com/docs/controls/wpf/syntaxeditor/user-interface/editor-view/whitespace
- Pinned UnoEdit backward-deletion keymap: https://github.com/wieslawsoltes/AvaloniaEdit/blob/1a2df473f157a0127d2559f241dc712d1ce60188/src/UnoEdit.Uno/Editing/TextArea.cs
- Native editor options contract: [editor-options.md](editor-options.md)
