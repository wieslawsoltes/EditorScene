# Compatibility target and remaining work

EditorScene is an independent Uno implementation targeting documented Actipro SyntaxEditor capabilities. **It is not feature-complete, API/binary-compatible with the WPF product, or pixel-identical.** This document distinguishes implemented functionality from open parity boundaries. A CI pass covers its assertions, not every item in the target product.

| Area | Implemented scope | Remaining parity / validation |
| --- | --- | --- |
| Text engine | Persistent rope, UTF-16 offsets, immutable snapshots, owner-thread mutation, disjoint revision-checked edit sets, undo/redo, line indexing, encoding-aware load/save. | Exhaustive behavior comparison with Actipro, document property/service API compatibility, extreme-size memory/latency and file-provider failures on every platform. |
| Snapshot tracking | Versioned points/spans, four boundary-affinity modes, reverse translation, undo/redo, worker-safe reads. | Full Actipro snapshot-reader/search/tagger contracts and migration adapters. |
| Editing | Native keyboard/pointer editing, search/replace, regex support via native search, indentation, case changes, line transforms, overstrike, multiple occurrence selections, additional carets, read-only checks. | Complete rectangular/virtual-space/drag/drop and all command edge cases; macro recording/playback, granular command enablement and full original keymap equivalence. |
| IntelliPrompt | Compiler-backed completion, quick info, overload signature help; native popup sessions. | Rich classification/icon parity within popups, all filtering/matching/commit modes, semantic project-wide/refactoring services and every language context. |
| Code snippets | Visual Studio XML load/save; literals/objects and retained metadata; mirrors, selected/end markers, custom delimiters, field navigation; folder/filter picker; 16 built-ins; atomic import/export and explicit host function/dependency hooks. | All bundled Actipro language catalogs; vendor function implementations; context-sensitive imports/references; full function/dependent-field reevaluation; exhaustive formatting and nested-snippet behavior. |
| C# and VB | Roslyn single-document compilation against embedded .NET references; diagnostics, syntax/classification, symbol navigation, member completion, quick info, signatures, whitespace formatting. | Full multi-project workspace, project references, rename/refactor/code actions, complete editor-context completion matching, language-add-on feature parity. |
| JSON | Strict parse, UTF-8-to-UTF-16 range conversion, navigation, folding, formatting, basic literal completion. | JSON Schema validation/completion and full semantic assistance. |
| XML/XAML | Safe XML parse, tag/attribute classification, hierarchy, folding and formatting. | Schema-aware completion/validation, XAML type/member resolution, namespace/import assistance, complete malformed/mixed-content behavior. |
| Other languages | Plain text plus native lexical grammars where selected by the host. | Complete Actipro language inventory, mixed-language lexer/parser composition, dynamic grammar framework and language-designer tooling. |
| LSP | JSON-RPC stream/WebSocket framing, bounded messages, request cancellation/lifecycle and document/feature request methods. | Full adapter from server responses to editor providers; server management UI, incremental sync negotiation, workspace edits/semantic-token features and complete protocol capability coverage. |
| Rendering | Uno Skia native control, shaped text, viewport line caching, lexical/semantic colors, error underlines, guides, whitespace, bracket and indicator overlays; light/dark/high-contrast palettes. | Physical GPU/driver performance validation, visual-diff baseline against Actipro, full bidi/complex-script/IME/accessibility conformance, custom adornment/tagger/layer API parity, minimap/change-tracking margin. |
| Workspace | Tabs, open/save/upload/download, split views, symbol selector, outline, diagnostics/events, bookmarks and breakpoint markers. | Full floating/docking/MDI variants, persistent workspace state and app-close unsaved-file handling across platforms. Breakpoint indicators do not imply a debugger. |
| Export/printing | Highlighted HTML and Unicode RTF text export. | Native printing, print preview/pagination, PDF output and full rich clipboard format parity. |
| Distribution | Eight EditorScene packages plus three pinned native packages; independent package consumers; solution and CI. | Public package-registry release process, supported-platform installers, complete API documentation and compatibility version guarantees. |

## Evidence rules

Unit results are in `artifacts/tests`; native results in `artifacts/native-checks.json`; package consumer results in `artifacts/packages/validation.json`; browser results/screenshots/trace in `artifacts/browser-tests`. Read the result file and revision, not just the existence of an artifact: failed jobs also upload diagnostic artifacts.

The current native execution runner is Linux/X11. Browser automation uses Chromium with SwiftShader WebGL2, not a physical GPU. Other desktop and browser/device combinations remain unverified unless a separate result explicitly says otherwise. Browser automation has no source mutation API; it changes the app through actual user input and file pickers.

## Primary target references

- Actipro SyntaxEditor product: https://www.actiprosoftware.com/products/controls/wpf/syntaxeditor
- Actipro WPF SyntaxEditor documentation: https://www.actiprosoftware.com/docs/controls/wpf/syntaxeditor/index
- Code snippets: https://www.actiprosoftware.com/docs/controls/wpf/syntaxeditor/user-interface/intelliprompt/code-snippets
- Macro recording: https://www.actiprosoftware.com/docs/controls/wpf/syntaxeditor/user-interface/input-output/macro-recording

These references define target behavior. They are not incorporated source or proof of completed compatibility.
