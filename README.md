# EditorScene

Native code-editor components for **Uno Platform desktop and WebAssembly**, with a shared rope document engine, Skia/HarfBuzz rendering, compiler-backed language services, and independently consumable NuGet packages.

**Status: functional alpha, not full Actipro SyntaxEditor parity.** Actipro's documented editing, language-service and extensibility behavior is the compatibility target. This is an independent implementation, not an Actipro product or a binary-compatible WPF replacement. No proprietary Actipro source code or artwork is included. See [compatibility](docs/compatibility.md) for explicit boundaries.

GitHub Pages deployment is configured for validated **`main`** builds only. Document/language/rendering regressions, native-host checks, isolated package consumers and three complete browser input passes must succeed before deployment. Check [Actions](https://github.com/wieslawsoltes/EditorScene/actions) and the deployed `build.json` before assuming a particular revision is published. [PR #1](https://github.com/wieslawsoltes/EditorScene/pull/1) records the initial implementation; [PR #2](https://github.com/wieslawsoltes/EditorScene/pull/2) records LSP integration.

## Build and run

```sh
git clone --recurse-submodules https://github.com/wieslawsoltes/EditorScene.git
cd EditorScene
python3 tools/build.py test
python3 tools/build.py desktop
# Linux native-host regression checks (requires Xvfb):
python3 tools/build.py native-checks
# WebAssembly publish, including workload installation:
python3 tools/build.py browser --install-workloads
# Complete local NuGet feed and external consumer validation:
python3 tools/package.py
```

The SDK and package versions are pinned in `global.json` and project files: .NET SDK 10.0.401, Uno.Sdk 6.7.30. Open `EditorScene.slnx` to work with all libraries and the sample. `tools/build.py browser` produces `artifacts/site`, configured for the `/EditorScene/` URL prefix. Serve the directory at that prefix; opening `index.html` with `file://` is not supported. Use `--base-path /` when publishing to a local web root instead.

The app supports new/open/save documents, tabs, independently resizable split views sharing one document, language selection, navigable symbols, document outline, filtered tabular diagnostics, event log, bookmarks, breakpoint indicators, themes, snippets, macro recording/playback, explicit language-server connections and text export. Its controls are native Uno controls, not a JavaScript editor embedded in a WebView.

## Reusable packages

| Package | Responsibility |
| --- | --- |
| `EditorScene.Text` | Versioned documents, immutable snapshots, bounded readers and literal search, atomic edits, multi-selection, indicators, tracked ranges, data-only macros, HTML/RTF export. |
| `EditorScene.LanguageServices` | Provider contracts, cancellable/debounced analysis, provider invalidation, completion edit plans, structural selection, bracket/range indexes and immutable diagnostic-table projections. |
| `EditorScene.Languages.DotNet` | Roslyn-backed C#/VB diagnostics, classifications, symbols, completion, quick info, signatures, formatting and structural ranges; bounded incremental model and completed-analysis caches. |
| `EditorScene.Languages.Structured` | Strict JSON and safe XML/XAML analysis, structural navigation, folding and conservative formatting. |
| `EditorScene.Protocol` | Bounded JSON-RPC stream/WebSocket transports, negotiated synchronization, validated result conversion and document-scoped `LspSyntaxLanguage`, including semantic-token deltas and selection ranges. |
| `EditorScene.Rendering.Skia` | Semantic/lexical styling, palettes, viewport adornments and disposable cached original vector-glyph rendering. |
| `EditorScene.Snippets` | UI-independent Visual Studio snippet XML, immutable declarations/tokens, folder catalog, atomic import and 16 built-in snippets. |
| `EditorScene.Controls` | `SyntaxEditor`, `NavigableSymbolSelector`, `DocumentOutline`, `SnippetPicker`, `DiagnosticList`, `EditorStatusBar`, `ResizableSplitView`, scoped `EditorChrome` resources and original `EditorIcon` controls. |

The feed also includes the pinned MIT dependencies `UnoEdit.Core`, `UnoEdit.Rendering.Skia` and `UnoEdit.Uno`; do not omit these when consuming a downloaded local feed. The package job builds all 11 packages and uses a fresh NuGet cache outside the repository to execute a headless consumer and compile desktop/browser control consumers. It verifies that no source `ProjectReference` was used and that restored package bytes match the built artifacts. Package artifacts are not a claim of publication to nuget.org.

## Embed a native editor

```csharp
using EditorScene.Controls;
using EditorScene.Languages.DotNet;
using EditorScene.Rendering.Skia;
using EditorScene.Text;

var document = new EditorDocument("class Example { }", "Example.cs");
var editor = new SyntaxEditor
{
    Document = document,
    SyntaxLanguage = new DotNetLanguage(),
    ShowLineNumbers = true,
    FontSize = 13,
    Theme = EditorTheme.Light
};
var navigation = new NavigableSymbolSelector { Editor = editor };
var outline = new DocumentOutline { Editor = editor };
```

The host owns externally supplied documents and should dispose views and documents when finished. Document edits and snapshot capture run on the owning thread. Language providers receive immutable snapshots; results and edits must be checked against the current document revision before publication or application. Two editors can share a document without sharing their caret or viewport.

## Classic workspace controls

The workspace uses Actipro's public **SDI Code Editor** layout as a reference: compact menus and grouped toolbar commands, paired symbol selectors, a readable Document Outline tab, bottom tool windows and neutral status/zoom controls. All icons are original vectors. The reference is not a claim of pixel-identical styling or proprietary control compatibility.

**Window → Split Horizontally / Split Vertically** creates top/bottom or side-by-side views. Drag the separator, use its arrow keys or double-click to center it. New splits copy read-only and visual preferences, then keep their caret/viewport and subsequent preference changes independent. The Error List exposes Code, Line, Char and Description columns with severity/text filters and current-revision row navigation. The status slider changes the active editor's zoom; the percentage resets it, and the INS/OVR button toggles insertion mode.

```csharp
using Microsoft.UI.Xaml.Controls;

var second = new SyntaxEditor();
second.SetDocumentAndLanguage(document, editor.SyntaxLanguage);
second.CopyViewSettingsFrom(editor);
var split = new ResizableSplitView
{
    First = editor,
    Second = second,
    Orientation = Orientation.Vertical, // top/bottom
    Ratio = 0.5
};
var diagnostics = new DiagnosticList { Editor = editor };
var status = new EditorStatusBar { Editor = editor };
// Scope to your page or control, not the entire application:
// Resources.MergedDictionaries.Add(EditorChrome.Create(EditorTheme.Light));
```

`ResizableSplitView` detaches, but does not dispose, externally supplied pane contents. `DiagnosticList` and `EditorStatusBar` detach prior editor subscriptions when rebound or disposed. The headless `DiagnosticTable` validates immutable diagnostics and caches positions once; its unfiltered path reuses the original row array. `EditorGlyphRenderer` records one native picture per instance/kind/palette and replays it without rebuilding paints or paths on each draw.

See [classic workspace APIs, reference and review](docs/classic-workspace.md). Full floating/docking/auto-hide behavior, workspace persistence, printing and pixel-level equivalence remain unfinished. Status `Ch` is line-local UTF-16; `Col` is not yet a full tab/grapheme-expanded display-column model.

## Language servers

**Language → Connect Language Server…** connects the selected document to an explicitly supplied trusted WebSocket endpoint. The dialog explains that the document and subsequent edits will be sent to that server. The sample accepts `wss://`, or loopback-only `ws://` for development, subject to browser security policies. It does not launch processes, persist connection settings, reconnect automatically or send other files. **Disconnect Language Server** restores the built-in provider.

`LspSyntaxLanguage` adapts the remote server to the existing language contract: completion, hover, signatures, symbols, folding, full/delta semantic tokens, structural selection, formatting and diagnostics. It negotiates UTF-16 full/incremental synchronization and save behavior. Per-document request leases prevent new changes from racing older responses; result conversion validates ranges before native controls consume them. Completion replacement and additional edits, including imports, are committed in a single undo group.

The adapter supports pull diagnostics and exact-version push diagnostics. Unversioned pushes are deliberately ignored. Remote commands, workspace edits, completion snippets/resolve, rename/refactoring, dynamic registration and full protocol parity are not implemented. GitHub Pages hosts only the client; a real language server must be provided separately. See [connection examples, supported capabilities and limitations](docs/lsp.md).

## Snippets

Type `for` followed by **Tab** in a C# editor to expand a loop. Repeated placeholders mirror edits. Tab/Shift+Tab navigate fields; Enter completes at the end marker; Escape exits snippet editing without removing the inserted text. Insertion and shortcut removal form one undo unit.

**Ctrl+K, Ctrl+X** opens Insert Snippet; **Ctrl+K, Ctrl+S** opens Surround With. The IntelliPrompt menu also exposes the picker and `.snippet` import/export. Snippet folders are logical catalog names, not filesystem paths.

```csharp
using EditorScene.Snippets;

editor.Snippets.Register(new CodeSnippet(
    title: "Log value", shortcut: "log", language: "csharp",
    code: "System.Console.WriteLine($value$);$end$",
    declarations: [new SnippetDeclaration("value", "\"message\"")]),
    folder: "Team snippets");
```

`CodeSnippetSerializer.Load/Save` leaves caller-owned streams open. DTDs and external entities are prohibited. Unsupported snippet functions, assembly references and imports are **not silently ignored**: insertion requires explicit trusted host callbacks when these are present. The control does not download URLs, resolve assemblies or execute code from snippet files.

## Macro recording

The **Macros** menu offers Start, Stop, Pause, Resume, Cancel Recording, Run Last Macro, Cancel Playback and XML import/export. Keyboard navigation, selection, typing, deletion, indentation and noninteractive editor commands are recorded as semantic actions. Empty or cancelled recordings preserve the previous saved macro. An editor-local pointer shield prevents mouse selection during active macro sessions; menus remain accessible.

```csharp
editor.MacroRecording.Record();
editor.InsertText("hello");
await editor.ExecuteAsync(EditorCommand.SelectAll);
await editor.ExecuteAsync(EditorCommand.Uppercase);
editor.MacroRecording.Stop();
// Move to the intended target, then explicitly replay:
await editor.RunMacroAsync(repeat: 1);
```

The reusable `EditorScene.Text.Macros` model has no UI dependency. Its versioned `.esmacro` XML stores command IDs and text, not activatable .NET types. Native playback preflights all command IDs before editing, observes cancellation and rejects recursive playback. Completed actions retain normal native undo behavior; playback is not one all-or-nothing edit. Interactive popups, snippets, file/workspace actions and arbitrary external handler effects are outside recording scope. See [macro behavior, API and boundaries](docs/macros.md).

## Snapshot tracking and reading

```csharp
var before = document.Snapshot();
var tracked = before.GetSpan(6, 4).Track(SpanTrackingMode.EdgeExclusive);
document.ApplyEdits([new TextEdit(0, 0, "prefix ")], before.Revision);
var after = document.Snapshot();
SnapshotSpan updated = tracked.GetSpan(after);
using var reader = updated.CreateReader();
string text = reader.ReadToEnd();
```

Point and span tracking have explicit insertion affinity, handle reverse history and undo/redo, and reject snapshots from different documents. Tracking tokens keep version checkpoints rather than the mutable control.

`SnapshotReader` is a bounded, seekable `TextReader` over a snapshot or span. It supports forward/backward UTF-16 and Unicode-scalar reads, CR/LF/CRLF lines, buffer reads, lookahead, checkpoints and independent clones. Sequential reads use a single 4,096-character rope window and do not flatten the snapshot. Readers remain valid after the live document changes or is disposed. See [snapshot reader semantics](docs/snapshot-reader.md) and [architecture](docs/architecture.md).

## Performance and immutable line queries

Snapshot position and line queries use a persistent, bounded-chunk AVL line index without flattening the snapshot. `GetLine` and `GetLineFromOffset` expose immutable zero-based line descriptors. Unchanged snapshot capture reuses the same object, and edits reuse completed indexes. `SnapshotTextChanges.GetMinimalChange` uses bounded edit history to avoid comparing unchanged multi-megabyte text for small LSP changes.

LSP semantic-token deltas retain validated result IDs and immutable data, recover once from an invalid base, and use allocation-light typed decoding. Timing/allocation comparisons run in the regression job and are retained as `performance.json`. See [performance architecture, reproducible benchmarks and limits](docs/performance.md), [editor hot-path review](docs/editor-review.md), [compiler cache tradeoffs](docs/structural-selection.md), and [workspace control measurements](docs/performance/workspace-e09ade7.json). The documented model-only compiler regression is not hidden by the workspace improvements. These CPU measurements do not claim physical-GPU or end-to-end frame-rate improvements.

**Alt+Shift+Right / Left** and the Edit menu expand/contract selection through C#/VB syntax ranges or negotiated LSP ranges, restoring exact selection/caret state. The reusable selection core and completed-analysis fast path avoid repeated work, while asynchronous replies are rejected if the document, provider or user selection has changed. See [structural selection](docs/structural-selection.md).

## Validation and rendering

`tools/browser-smoke.mjs` and its macro, LSP, review, structural-selection and workspace modules drive the exact published .NET/Uno application through pointer, keyboard, clipboard, file upload and download. LSP tests use a synthetic WebSocket protocol peer, not a mocked language-provider return value. The optional `?test=1` bridge exposes observation only; it has no document mutation commands. Pointer tests wait for stable geometry across distinct native observations. CI runs the complete suite three times in fresh browser contexts and retains all result files, screenshots, protocol transcripts and traces.

Browser CI creates a real WebGL2 Skia context using **software SwiftShader**. That validates the graphics path, not physical-GPU throughput, battery usage or driver compatibility. The host chooses acceleration; this repository does not claim a direct WebGPU renderer. Desktop execution checks currently use Linux/X11. Windows/macOS runtime, device-specific IME/accessibility and pixel-by-pixel Actipro equivalence need separate validation.

## Attribution

The native editor and text engine are built on the MIT UnoEdit port at `wieslawsoltes/AvaloniaEdit`, pinned to `1a2df473f157a0127d2559f241dc712d1ce60188`. Original AvaloniaEdit/AvalonEdit authorship and licenses remain in the submodule and packages. Roslyn supplies .NET compiler services; Uno, SkiaSharp and HarfBuzz supply platform and rendering components. See [third-party notices](docs/third-party-notices.md).
