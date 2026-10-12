# Native command surfaces

This increment follows PR #12, merged as `0d297e0836f2cf59fb799647a8630502bab61c8a`. The native editor's command availability now drives its main command menu items, toolbar buttons and toolbar overflow entries through one reusable binding collection. Previously the context menu could disable an unavailable action while its toolbar button remained visually enabled.

## Visible behavior

Selection, undo history, read-only state and language capabilities update the corresponding command controls. Copy remains available for selected text in a read-only view while Cut/Paste/Delete/Indent/Format are disabled. Undo/Redo follow history; unavailable completion/format commands are disabled for plain text. Switching the active split rebinds the same command surfaces to that view, including its independent read-only state.

Original toolbar vector glyphs dim to 35 percent of their initial opacity when disabled. Enabling restores their original opacity, not a forced full-opacity value. Native menu and overflow items retain shortcut hints and original `PathIcon` glyphs where the shared icon factory defines them. Toolbar tooltips include existing keyboard hints. No external icon/font files or proprietary artwork were added.

The state integration covers application menu entries built through its EditorCommand helper and toolbar entries built through ToolCommand. File/workspace actions, snippet-picker actions, macro session buttons and the explicitly scoped whitespace submenu retain their existing host policy. It does not silently disable the whitespace submenu's documented read-only/empty-selection no-op behavior. Existing toggle menu synchronization remains in place; the reusable collection also supports native checked-state controls for independent consumers.

## Reusable API and ownership

```csharp
var bindings = new EditorCommandBindings { Editor = editor };
var copy = new Button { Content = "Copy" };
var copyMenu = new MenuFlyoutItem { Text = "Copy" };
IDisposable buttonBinding = bindings.Bind(copy, EditorCommand.Copy);
IDisposable menuBinding = bindings.Bind(copyMenu, EditorCommand.Copy);

// Both controls now share one command query per refresh.
bindings.Editor = otherEditor; // detach prior editor notifications
buttonBinding.Dispose();      // detach one target without disposing it
bindings.Editor = null;       // disable targets and clear checked states
bindings.Dispose();           // detach all handlers; caller still owns views/controls
```

Bind accepts ButtonBase and MenuFlyoutItem, including ToggleButton and ToggleMenuFlyoutItem. A target may only appear once within the collection; duplicate or unsupported targets fail before registration. The collection is owned by its creating UI thread. It does not change a native control's parent, text, theme, foreground or automation name.

By default Bind also subscribes to native Click and delegates to the current editor's normal ExecuteAsync path after rechecking advice. `executeOnClick: false` updates state only; the host retains execution responsibility. `TryExecuteAsync` can be used by that host handler. Its boolean reports whether invocation proceeded, not whether the editor's command internally succeeded; normal editor OperationFailed remains the execution-error mechanism. Unexpected exceptions in the binding-owned Click route are published through ExecutionFailed. Do not add a second command executor to a target already bound with executeOnClick enabled.

A removed registration leaves its target at the last displayed state; the host can then repurpose it. Disposal detaches handlers and releases the editor/registration arrays without disposing native targets or documents. The app disconnects its collection on page unload and reconnects on load. No application-global registry retains a closed page or old split view.

## Query reuse, observers and callback safety

Registration changes construct a grouped snapshot. A refresh traverses those prebuilt command groups and queries each distinct command once, then visits all its target controls. Only differing IsEnabled, IsChecked or optional glyph Opacity values are written. No per-refresh LINQ enumeration or registration-array rebuild is needed on the warm path.

The collection listens to editor StateChanged/AnalysisUpdated, direct read-only/wrap/line-number dependency-property transitions, and the active document's IndicatorCollection. Host bookmark additions/removals therefore update navigation commands without editing text or invoking an editor command. When the same editor is rebound to another document, the old indicator subscription is detached. Rebinding or disposing the collection also unregisters its dependency-property tokens and event handlers.

The first integrated native run (38280540165) exposed that the wrapper's direct IsReadOnly setter forwards to its native control without raising StateChanged. The test correctly failed because Cut remained enabled. Explicit wrapper dependency-property observers fix that path, including XAML-set values, without requiring a host to manually refresh or changing the editor's existing general event contract. The original assertion remains enforced. Wrap and line-number checked states use the same observer mechanism.

This is not a promise to skip all future state queries: every notification reevaluates the command groups. Hosts can call Refresh for changes outside the observed contract, such as directly changing NativeEditor.IsReadOnly or ordinary properties that emit no general event. The collection retains O(binding count) state rather than caching every document or selection. Register/unregister/rebind operations allocate structural snapshots/tokens; those are not part of the warm-refresh allocation claim.

Native property updates can synchronously invoke host code. Each registration/active-editor change advances a generation. If a callback rebinds, unregisters or disposes the collection, the old traversal stops before writing remaining stale controls. A bounded refresh loop settles on the new context; feedback that cannot settle within eight passes throws rather than looping forever. Reentrant Refresh requests are coalesced into that loop. This does not guarantee arbitrary malicious or blocking host callbacks will succeed.

Command invocation captures editor/document/revision/provider and rejects a changed context before beginning. Once a command begins, its own asynchronous clipboard/formatting/protection logic remains authoritative. The binding layer does not replace authorization, introduce automatic retries or bypass editor read-only checks. Paste advice is optimistic: displaying a toolbar does not inspect clipboard content or ask browser permission.

## Macro policy and corrected empty-document advice

The existing `GetCommandState(EditorCommand)` signature keeps the conservative context-menu policy: menus are unavailable throughout macro sessions. The new overload takes `EditorCommandQueryMode.CommandSurface`. It allows recordable actions while recording or paused, and disables commands during playback. Interactive/nonrecordable actions remain disabled during a session. Toolbar actions therefore remain recordable without enabling the context menu or bypassing the recorder.

The previous advice grouped Indent with commands requiring nonempty text. This wrongly disabled insertion into an empty document even though the command supports it. Indent now follows editable/snippet state and supports the existing per-caret indentation plan. Outdent retains its native selected-line behavior and no longer requires nonempty text merely to be offered. Existing command enumeration values and the original GetCommandState signature are preserved.

## Validation and measurements

Actual Uno-host regressions cover duplicate command groups, read-only behavior, glyph restoration, native checked-state controls, command execution/undo, unavailable commands, empty-document indentation, recording/playback, duplicate target validation, rebinding/unbinding, unregistering, property-callback reentrancy and disposal. Observer regressions additionally cover direct bookmark changes and old-document detachment. A warm-loop assertion checks that 1,000 refreshes perform exactly one query per registered command group, allocate no managed objects on the current thread, and leave unchanged native properties unwritten.

The browser suite observes native IsEnabled and glyph Opacity rather than only advice-model booleans. It clicks enabled and disabled toolbar/menu commands, reads the actual clipboard after Copy, exercises overflow state, switches language and independent split views, and checks that ordinary caret moves do not cause redundant property writes. Light/dark screenshots are retained. All prior full-suite scenarios remain in each of the three fresh published-browser runs; the observation bridge has no source/option mutation operations.

`CommandBindingBenchmarks` retains raw timing in `artifacts/native-checks.binding-performance.json`. Each fixture runs 100 refreshes over 60 native buttons representing three commands: independent per-target advice makes 6,000 queries; grouped bindings make 300. Both use the same new command advice API and same unchanged control values. The independent path is a control experiment, not an exact reproduction of the old application (which did not synchronize these toolbar states).

Three warmups and eleven samples report median, observed p95 and current-thread allocation. Existing document/editor/control construction, binding setup, layout, painting, clipboard/protection callbacks and physical GPU are excluded. Controls are real native types but unmounted during this measurement. No complete input-latency, frame-rate, hardware or Actipro speedup is claimed. The full regression report retains separate unfavorable compiler/dense-tab measurements. Test descriptions and intended thresholds are not substitutes for the actual final workflow results.

## Review limits and remaining parity

Review covers all changed binding, command-policy, menu/toolbar, lifecycle, observation, native/browser test and package-consumer integrations. Existing unchanged subsystems retain regression coverage; this is not an exhaustive manual audit of every device/source file. Local tools failed in this continuation, so compilation/execution and package verification use GitHub Actions.

This is still an independent Uno implementation, not full Actipro API, feature or pixel parity. Full command enablement parity, no-selection line clipboard behavior, rich clipboard/printing, toolbar customization, persisted workspaces, advanced language/refactoring/schema/LSP features, docking, IME/accessibility and hardware/platform qualification remain distinct work. See compatibility.md and context-menus.md for the broader target and ownership boundaries.
