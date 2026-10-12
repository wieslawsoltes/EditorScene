# Native command surfaces

This increment follows PR #12, merged as `0d297e0836f2cf59fb799647a8630502bab61c8a`. Editor command availability now drives main command menu items, toolbar buttons and overflow entries through a reusable binding collection. Previously the context menu could disable an unavailable action while its toolbar button remained visually enabled.

## Visible behavior

Selection, undo history, read-only state and language capabilities update the corresponding native controls. Copy remains available for selected text in a read-only view while Cut/Paste/Delete/Indent/Format are disabled. Undo/Redo follow history; unavailable completion/format commands are disabled for plain text. Switching the active split rebinds the same command surfaces to that view, including its independent read-only state.

Original toolbar vector glyphs dim to 35 percent of their initial opacity when disabled. Enabling restores their original opacity, not a forced full-opacity value. Native menu/overflow items retain keyboard hints and original PathIcon glyphs where the shared factory defines them. Toolbar tooltips include existing shortcuts. No external icon/font files or proprietary artwork were added.

Integration covers application entries built through its EditorCommand and ToolCommand helpers. File/workspace actions, snippet pickers, macro session buttons and the explicitly scoped whitespace submenu retain their existing host policy. Documented read-only/empty-selection whitespace no-ops are not accidentally disabled. Existing application toggle synchronization remains; the reusable collection also handles checked-state controls for independent consumers.

## Reusable API and ownership

```csharp
var bindings = new EditorCommandBindings { Editor = editor };
var copy = new Button { Content = "Copy" };
var copyMenu = new MenuFlyoutItem { Text = "Copy" };
IDisposable buttonBinding = bindings.Bind(copy, EditorCommand.Copy);
IDisposable menuBinding = bindings.Bind(copyMenu, EditorCommand.Copy);

// Both controls share one command query per refresh.
bindings.Editor = otherEditor; // detach prior editor notifications
buttonBinding.Dispose();      // detach one target without disposing it
bindings.Editor = null;       // disable targets and clear checked states
bindings.Dispose();           // caller still owns views, documents and controls
```

Bind accepts ButtonBase and MenuFlyoutItem, including ToggleButton and ToggleMenuFlyoutItem. A target may only appear once within the collection; duplicate or unsupported targets fail before registration. The collection belongs to its creating UI thread. It does not change the control's parent, text, theme, foreground or automation name.

By default Bind also subscribes to native Click and delegates to the current editor's normal ExecuteAsync path after rechecking advice. `executeOnClick: false` updates state only; the host retains execution responsibility and can use `TryExecuteAsync`. Its boolean reports whether invocation proceeded, not whether the editor's command internally succeeded: OperationFailed remains the editor's normal execution-error mechanism. Unexpected exceptions in the binding-owned Click route are published through ExecutionFailed. Do not add a second command executor to a control already bound with executeOnClick enabled.

Removing a registration leaves its control at its last displayed state for the host to repurpose. Disposal detaches handlers and releases editor/registration arrays without disposing native targets or documents. The app disconnects on page unload and reconnects on load. No application-global registry retains a closed page or old split view.

## Query reuse, observers and callback safety

Registration changes construct a grouped array snapshot. Refresh queries each distinct command once, then visits its targets. Only differing IsEnabled, IsChecked or optional glyph Opacity values are written. The warm path does not use transient LINQ enumerations or rebuild registration arrays.

Observers include StateChanged/AnalysisUpdated, wrapper read-only/wrap/line-number dependency-property changes and the active document's IndicatorCollection. Direct host bookmark additions/removals update navigation commands without text edits or command execution. Rebinding the same view to another document detaches the old indicator source. Rebinding/disposal unregisters dependency-property tokens and event handlers.

The first integrated native run, 38280540165, exposed that the wrapper's direct IsReadOnly setter forwards to its native control without raising StateChanged: Cut remained enabled. Explicit DP observers fix that path, including XAML-set values, without requiring a manual Refresh or changing the editor's existing general event contract. The original failed assertion remains enforced. Wrap/line-number checked states use the same observer mechanism.

Every notification still reevaluates command groups. Hosts can explicitly Refresh changes outside the observed contract, such as direct NativeEditor.IsReadOnly changes or ordinary properties that emit no event. Storage is O(binding count), not an unbounded cache of documents/selections. Registration/removal/rebinding allocates structural arrays/tokens; those operations are excluded from warm-refresh allocation claims. Individual command advice can have its own costs, such as scanning indicator kinds.

Native property writes can synchronously invoke host code. Registration/active-editor changes advance a generation. A callback that rebinds, unregisters or disposes stops old remaining writes. A bounded loop settles against the new state; feedback that cannot settle within eight passes throws rather than looping forever. Reentrant Refresh requests are coalesced. This is not a guarantee against arbitrary blocking, throwing or intentionally nonsettling host code.

Invocation captures editor/document/revision/provider and rejects a changed context before beginning. Once a command starts, its own asynchronous clipboard/formatting/protection logic remains authoritative. Bindings do not replace authorization, retry edits, inspect the clipboard for availability or bypass read-only checks. Paste advice remains optimistic.

## Macro policy and empty-document indentation

The original `GetCommandState(EditorCommand)` signature retains conservative context-menu policy: context actions are suspended throughout macro sessions. The overload taking `EditorCommandQueryMode.CommandSurface` distinguishes the session phases:

- Recording permits recordable actions and disables interactive/nonrecordable actions.
- Paused recording restores ordinary interactive availability, subject to the same selection, language and read-only rules.
- Playback disables command-surface invocation until playback ends.

This matches the recorder's existing guidance to pause before using an interactive command. A blanket suspension while paused would wrongly disable Find. Native regressions check Pause/Resume/Cancel transitions, and the browser suite actually opens/closes Find from the toolbar while paused before resuming. Merely enabling a model boolean is not considered sufficient.

The preceding advice incorrectly required nonempty text for Indent, despite its ability to insert into an empty document. Indent now follows editable/snippet state and retains the existing per-caret plan. Outdent keeps its native selected-line semantics without a needless nonempty-text prerequisite. Existing command enum values and the original GetCommandState signature are preserved.

## Validation and performance methodology

Actual Uno-host assertions cover grouped duplicate commands, native/read-only state, glyph restoration, checked-state controls, execution/undo, rejected commands, empty indentation, macro policies/replay, duplicate-target validation, rebinding/unbinding, unregistering, property-callback reentrancy, disposal and direct bookmark observers. A warm loop enforces exactly one query per distinct group, zero current-thread managed allocations and no redundant native writes over 1,000 refreshes. The complete native minimum is increased from 306 to 334; a threshold or test description is not itself a successful-run claim.

The browser suite observes actual native IsEnabled and glyph Opacity, clicks enabled/disabled controls, reads the clipboard after Copy, verifies main-menu/overflow state, uses real paused-macro Find input, switches language and independent splits, and checks that unchanged caret events do not rewrite control properties. Theme screenshots are retained. All preceding scenarios remain in three fresh full-suite runs; the opt-in bridge only observes state.

All eleven packages retain isolated consumers using exact restored package bytes, no source ProjectReferences, an executed headless consumer and compiled desktop/browser native consumers. The new native consumer exercises duplicate command registration and the advice overload.

`CommandBindingBenchmarks` retains raw samples in `artifacts/native-checks.binding-performance.json`; native CI logs also print its summaries. Each fixture performs 100 updates over 60 real but unmounted buttons representing three commands. Independent per-target advice makes 6,000 queries; grouped bindings make 300, with identical enabled states. The independent route uses the same new advice API: it is a control experiment, not an exact prior-app implementation (the old app did not synchronize these toolbar states).

Three warmups and eleven samples report median, observed p95 and current-thread allocation. Document/editor/control/binding construction, native layout/painting, clipboard/protection callbacks and physical GPU are excluded. No whole-editor input-latency, frame-rate, power or Actipro speedup is inferred. Earlier unfavorable compiler/dense-tab results remain in the complete regression report. Read exact revision-specific workflow outcomes and raw samples before claiming results.

## Remaining boundaries

Review covers changed binding, command advice, menu/toolbar, observer/lifecycle, native/browser and package-consumer integrations. Unchanged subsystems retain regression coverage, not an exhaustive manual audit of every source file/device. Local filesystem/Python tools failed in this continuation; compilation and package verification use GitHub Actions.

Full Actipro feature/API/pixel parity remains unfinished. Broader command enablement, no-selection line clipboard behavior, rich clipboard/printing, toolbar customization, persistent workspaces, project/refactoring/schema/remaining LSP functionality, docking and full IME/accessibility/platform qualification remain separate work. See compatibility.md and context-menus.md for the broader target and ownership boundaries.
