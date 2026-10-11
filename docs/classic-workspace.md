# Classic editor workspace

This increment continues from merge `a62fe1871b1bd7f122f674e986de5b44f7c6dc27`. It uses the **SDI Code Editor** as the layout/interaction reference, while retaining EditorScene's identity and independent implementation. It does not establish full Actipro API, feature, or pixel parity.

## Reference and visible changes

Actipro's public SDI sample defines compact File / Edit / Advanced / IntelliPrompt / View / Outlining / Language menus, grouped small toolbar commands, paired symbol selectors, bottom Error List and Events windows, a Document Outline side pane, horizontal split views, and status-bar position/overwrite/zoom controls.

EditorScene now uses compact, square-edged native chrome, expanded grouped command icons, a real toolbar overflow menu, a rotated Document Outline tab instead of letter-by-letter vertical text, tool-window captions and close buttons, a resizable bottom tool pane, a neutral status bar with interactive zoom, and both top/bottom and side-by-side split layouts. Existing document tabs, file operations, provider connections and editing features remain available. Unsupported print and print-preview commands are not represented by nonfunctional buttons.

The references are design/behavior evidence, not incorporated control implementation or artwork:

- Public sample, inspected `MainControl.xaml` blob `33d47bf4cc8f6598dfbc4d67a9989000ed71034b`: https://github.com/Actipro/WPF-Controls/blob/develop/Samples/SampleBrowser/ProductSamples/SyntaxEditorSamples/Demo/SdiCodeEditor/MainControl.xaml
- Product/reference features: https://www.actiprosoftware.com/docs/controls/wpf/syntaxeditor/index

No proprietary Actipro binaries, extracted icons or redistributed font files are included. The glyphs remain original vectors. Theme colors and native control templates are independently implemented. Pixel-difference comparison against a running Actipro installation is not established by these changes.

## Reusable native controls

### ResizableSplitView

`EditorScene.Controls.ResizableSplitView` hosts arbitrary UI elements in two content presenters. `Orientation.Vertical` stacks the views top/bottom; `Orientation.Horizontal` arranges them side by side. The application labels these **Split Horizontally** and **Split Vertically**, describing the separator orientation.

```csharp
var split = new ResizableSplitView
{
    First = primaryEditor,
    Second = secondaryEditor,
    Orientation = Orientation.Vertical,
    Ratio = 0.5
};
```

The six-unit separator supports dragging, arrow-key resizing, Home/End and double-click centering. The preferred ratio survives orientation/size changes; effective layout keeps approximately 48 units for each pane when space permits. Very small hosts divide the available space rather than forcing content outside their bounds. Keyboard focus uses the native system focus visual. Assigning the same UI element to both panes is rejected before either content assignment changes.

Both presenters explicitly stretch their content. Omitting content alignment can leave otherwise stretchable native editors with only their desired size, so this behavior has direct native layout assertions. The control does not own or dispose externally supplied view content when panes are replaced.

The app creates the second editor with a shared document and provider, copies captured view options, and retains independent caret/selection/viewport state. Copying read-only/wrapping/line-number/whitespace/ruler/guide/font/completion preferences avoids opening an unexpectedly writable or differently formatted second view. Subsequent option changes remain view-local.

`SyntaxEditor.CopyViewSettingsFrom` checks context between synchronous setters. Host callbacks that replace the target document/provider, change target settings or dispose the view stop the remaining copy. Completed settings are not transactionally rolled back. `IsDisposed` allows reusable host controls to reject released views before subscribing.

### DiagnosticList and DiagnosticTable

`DiagnosticList` is a reusable current-document error-list control with Code / Line / Char / Description columns, severity toggles, text filtering, a count summary, and mouse/Enter navigation. The `ListView` receives data objects rather than one preconstructed control per diagnostic; visible row containers are handled by the native virtualizing control.

The UI-independent `EditorScene.LanguageServices.DiagnosticTable` projects an immutable analysis against its exact snapshot. It validates metadata/ranges, caches one-based UTF-16 positions, preserves input order and diagnostic object identity, and computes severity totals. Filtering combines a severity mask and ordinal-ignore-case matching against message, code, severity or filename. The common unfiltered request returns the original immutable array without copying.

```csharp
var table = new DiagnosticTable(snapshot, analysis);
var errors = table.Filter(DiagnosticVisibility.Error, query: "CS0029");
var view = new DiagnosticList { Editor = editor };
```

The control rebuilds its projection only when analysis identity or filename changes, not on unchanged caret/selection events. Stale or foreign revisions cannot navigate old offsets. Invalid immutable provider results are rejected once rather than repeatedly retried on every caret movement. Snapshot-based projection can run after the live document is disposed.

Reflection-bound row properties are explicitly preserved for trimmed desktop/WebAssembly consumers. Browser coverage inspects the actual rendered cell text as well as row counts; an ItemsSource count alone is not proof that bindings displayed anything. The diagnostic observation bridge excludes controls under collapsed ancestors and never mutates application state.

Filtered requests still scan the diagnostic rows and allocate a new filtered array. Fresh table construction remains proportional to diagnostic count and is currently performed on the UI thread by the control. This is not asynchronous filtering, a multi-file error list, a sortable full DataGrid or complete vendor error-list API compatibility.

### EditorStatusBar and EditorChrome

`EditorStatusBar` shows status, encoding/line endings, line-local character position, insert/overwrite state and zoom. Its slider adjusts the active view's existing 8–40 font-size range; the percentage button resets to 13 (100%). Smaller widths collapse the slider/encoding fields while retaining the reset button and position display. The insertion-mode button changes a real editor setting.

A value-type state key skips unchanged updates, and line-ending lookup is reused within a revision. `Ch` now means a one-based position within the line rather than the absolute document offset. The current `Col` value is also a UTF-16 character column, not a full grapheme-/tab-expanded visual-column model. This distinction remains a compatibility boundary.

`EditorChrome.Create(theme)` returns a scoped resource dictionary for compact native menus, buttons and selectors. It does not mutate application-global styles or replace the native input/focus/automation machinery. Hosts may apply it through their own control/page resources. Light, dark and high-contrast palettes are supported; individual device/assistive-technology qualification remains separate validation.

## Cached original glyph rendering

`EditorScene.Rendering.Skia.EditorGlyphRenderer` records original 16-unit geometry into one disposable `SKPicture` per renderer instance. Warm draws at different output sizes replay that picture; kind/palette changes replace it. Existing `EditorIconKind` numeric values are preserved and additional indent/comment/snippet/signature/macro/split glyphs are appended.

`EditorIcon` owns its renderer, releases it on unload/disposal, and recreates it when loaded/drawn again. This avoids per-draw paint/path/coordinate-array construction while avoiding an unbounded process-global native cache. The high-contrast palette is explicit. Draws preserve the caller's canvas transform/save stack.

## Review fixes and validation scope

The review covers all changed workspace/control/model/rendering files and their app, package and test integrations. Concrete fixes include missing renderer references in the benchmark project; explicit content stretching; read-only/display-option inheritance for new splits; callback-safe setting copies; disposed-binding rejection; stale diagnostic navigation; actual row-binding preservation; cached title/status/projection updates; and palette/lifetime handling for native pictures. Unchanged compiler, protocol, snippet, macro and editing paths retain the existing full regression suite, not a claim that every repository file/device received exhaustive manual review.

The suite retains regression tests, actual Linux/X11 Uno-host assertions, three independent browser runs against the published WebAssembly artifact, and isolated consumers for all eleven packages. New browser paths exercise rendered diagnostic filtering/navigation, document switching, split dragging and keyboard resizing, read-only input, status zoom/mode buttons, theme changes and toolbar overflow. Native checks verify layout, event detachment, same-state work counters and callback reentrancy. Main deployment stays gated on the normal validation jobs.

Use the actual workflow results/artifact manifests and source revision to establish pass/fail. Failed jobs also upload diagnostic artifacts; this document's description of test intent is not itself a successful-run claim.

## Reproducible performance evidence

`tools/EditorScene.Benchmarks/WorkspaceBenchmarks.cs` adds raw samples to `artifacts/tests/performance.json`: three warmups, eleven samples, p50, observed p95 and current-thread managed allocations.

- `diagnostic_rows_10000`: rebuilding a 10,000-row projection versus returning its existing unfiltered array. The difference measures avoiding unnecessary work; it does not claim faster construction of a new table. The line index is warm, and UI binding/rendering is excluded.
- `glyph_draws_1000`: recording/replaying the same current geometry for every draw versus replaying a cached picture 1,000 times onto a 16×16 CPU bitmap. The uncached control experiment is **not an exact reproduction of the previous EditorIcon implementation**. Initial cached recording is excluded.

Native picture retention and allocations on other threads are not included in managed allocation measurements. These are CPU microbenchmarks, not physical-GPU, compositor frame-rate, power or end-to-end input-latency evidence. Existing unfavorable compiler measurements remain documented; the workspace benchmark does not supersede or hide them.

## Publication and local-work boundary

All recoverable work used for this increment is committed to the GitHub feature branch. Local filesystem/execution tools failed in the session, so unpushed files outside the connected repository cannot be certified as recovered or merged. Source compilation/execution and artifact checks occur in GitHub Actions; no unavailable local execution or independent live-site verification is claimed.

Remaining product scope includes printing/preview, complete floating/docking/auto-hide behavior, persistent workspaces, project-wide refactoring, schema/grammar tooling, additional LSP capabilities, complete IME/accessibility behavior and original-product API/visual parity. See [compatibility.md](compatibility.md).
