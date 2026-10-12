# Native context menus and command-state review

This increment replaces the pinned UnoEdit wrapper's unconditional six-command context menu with an editor-owned, reusable native menu. It continues from main `d84888730d0883084bc4bafa36792bde1b388545` and does not establish complete Actipro API, feature or pixel parity.

## Behavior and reference

Actipro documents an enabled-by-default context menu, an option to relocate the caret when the secondary pointer button is outside the current selection, and menu customization hooks. Those are the behavioral reference, not copied WPF implementation or artwork:

- https://www.actiprosoftware.com/docs/controls/wpf/api/actiprosoftware.windows.controls.syntaxeditor.syntaxeditor

The native EditorScene menu opens through right-click, Shift+F10 or the application-menu key. Keyboard invocation anchors to the caret. A pointer request inside the half-open selected range preserves it; an outside request moves to the hit-tested offset unless `CanMoveCaretForSecondaryPointerButton` is false. The target view receives focus before menu opening, including a previously inactive secondary split. Pointer coordinates are view-relative and the menu origin is clamped to the viewport.

The menu groups Undo/Redo, Cut/Copy/Paste/Delete and Select All, with Advanced, IntelliPrompt and Outlining submenus. Native items carry shortcut hints and original monochrome vector icons where defined. No icon fonts, image downloads or proprietary assets are required. The application continues using its scoped compact chrome and light/dark/high-contrast palettes; a standalone host retains control of its native menu styling. Pixel-difference equivalence with an Actipro installation is not claimed.

**View → Default Context Menu** disables the wrapper's default menu without falling back to the pinned ancestor menu. **View → Right-click Moves Caret** controls secondary-pointer relocation. New split views copy these values, not menus or callback handlers; later changes are view-local. The flags are not saved across restarts and are separate from the current options-dialog draft model. Their two appended EditorCommand values expose checked state and use the existing deferred menu-close focus restoration. An initial browser run found that directly setting the flags from a menu left focus on its MenuBarItem; the commands fix that input-lifetime defect, and the regression now verifies real caret navigation after the disabled-menu key request without manually refocusing the editor.

## Reusable APIs

```csharp
using EditorScene.Controls;
using Microsoft.UI.Xaml.Controls;

editor.IsDefaultContextMenuEnabled = true;
editor.CanMoveCaretForSecondaryPointerButton = false;

var menu = editor.EditorContextMenu; // helper allocation, not native menu construction
var custom = new MenuFlyoutItem { Text = "Inspect selection" };
custom.Click += (_, _) => Inspect(editor.SelectedText);
menu.Requested += (_, args) =>
{
    // The same native menu is retained. Avoid appending duplicates on every request.
    if (!args.Menu.Items.Contains(custom)) args.Menu.Items.Add(custom);
    // args.Cancel = true prevents this opening.
};

EditorCommandState copy = editor.GetCommandState(EditorCommand.Copy);
// Programmatic invocation must run on the UI thread after the view is loaded.
editor.ShowEditorContextMenu();
```

`EditorContextMenu` can also be constructed around a caller-owned editor and disposed independently. The property-owned helper is disposed with its editor. Do not dispose a property-owned helper and continue using that editor's menu; use the enable flag to suspend it instead. `Flyout` and `GetCommandItem` create the native menu lazily and then return the retained objects. `BuildCount` and `IsOpen` expose actual construction/lifetime state.

`Requested` receives the native `MenuFlyout`, optional pointer target offset, keyboard-request flag and a cancellation flag. It runs once per attempted opening after hit testing/focus. Host document, revision, provider or selection changes invalidate the old opening. Hosts own the behavior, permission checks and lifetime of their custom menu actions. This is not Actipro's WPF routed-command, complete hit-test or menu-kind API.

## Availability, execution and lifetime

`GetCommandState` is inexpensive view-state advice, not protected-range authorization. The core menu reflects selection, native/wrapper read-only flags, undo history, language capability flags and available folding data. Read-only views retain Copy but disable mutations. Extra-selection and active-snippet operations are conservative. All context actions are suspended during macro sessions; this policy does not disable the existing macro command surfaces themselves.

Paste availability is optimistic: querying a menu does not read the clipboard or request browser permissions. Content, permissions and stale asynchronous targets are checked by the actual execution paths. Likewise, availability queries do not run protected-range callbacks or invoke a language-service completion/formatting request. Not every `EditorCommand` has a complete vendor-equivalent enablement policy; commands not covered by a specialized rule retain general advice. Existing top-level menus are not globally replaced by this policy.

Only open menus subscribe to editor state and unload events. Changes to their captured document/revision/language/selection close the stale surface; ordinary read-only changes refresh enabled states. An item checks its captured context again before execution. Failed/cancelled openings release their captured context, and menu closure releases it after the current input dispatch so framework close-before-click ordering cannot drop a valid command. Generation checks keep an old close notification from clearing a newer request. Disposal detaches subscriptions and item handlers without disposing caller-owned documents/views.

The helper delegates actions to `SyntaxEditor.ExecuteAsync` and its existing protection, undo, clipboard and error handling. It is not a new command execution engine and does not redefine arbitrary host callbacks or all existing command semantics. General IME, rich clipboard, complete multi-caret command behavior and touch/device qualification remain separate work.

## Numeric option failure and repair

Main workflow `38251739048` failed its second browser pass and did not deploy. Its trace contains a real `keyboard.type("80")` action, followed by the native browser input holding `"0"`; Tab/blur then coerced that to ruler minimum 1. This was not a successful Pages publication and is not reclassified as one.

Two layers are addressed. The options panel uses `NumberBoxValidationMode.Disabled` so range coercion cannot silently replace a user's invalid draft. `TryGetOptions` parses each realized numeric input with its configured number parser before validating the draft. It does not trust a stale `Value`: invalid text leaves the upstream NumberBox value unchanged. Empty, nonnumeric, fractional integer and out-of-range drafts show an error and keep the dialog open; valid input is accepted even before blur commits the control value. An unloaded panel without a realized input uses its explicitly assigned Value. Restore Defaults explicitly resets visible numeric text even when the numeric value is unchanged.

The browser driver now observes that exact native input's focus on distinct publication frames, its select-all range, and each actually typed digit before sending the next digit/Tab. It does not set editor/option values through a bridge, retry a failed test, paste over a failed input or weaken the expected value. This fixes an automation race exposed by asynchronous native focus/selection/text synchronization; it is not a claim that every rapid-input/IME timing issue in the upstream NumberBox is solved. The opt-in bridge observes input text, selection and focus only.

## Performance and validation

A menu is built once per helper, rather than once per opening or editor-state event. No menu state subscriptions exist while it is closed. Command queries use value-type results; the native regression enforces zero current-thread managed allocations for 10,000 warm Copy-state queries and proves that protected-range callbacks are not consulted.

`NativeCommandBenchmarks` records three warmups, eleven samples, p50, observed p95 and allocations in `artifacts/native-checks.command-performance.json`. It measures only 10,000 Copy-state queries on an actual native editor with a selection. It excludes setup, menu creation/layout, clipboard access, painting, physical GPU and full input latency. It is a new workload, not a previous-version or Actipro speedup comparison. Existing unfavorable compiler and dense-tab results remain in the full regression benchmark report.

Local Release desktop compilation and real Linux/X11 Uno checks supplement the normal CI gates. The browser suite exercises actual native menu items (including their rendered `IsEnabled` states), clipboard Copy/Cut, undo, disabled clicks, right-click positioning, feature toggles, themes, repeated reuse and secondary-view activation. Existing snippets/macros/LSP/diagnostics/options/files/large-document checks remain in each of three fresh browser runs. All eleven package consumers remain isolated from source project references.

Review covers changed wrapper input, menu, icon, option, observation, test and package integrations and the existing native focus/clipboard/protection paths they call. It is not an exhaustive manual audit of every unchanged source file or device. Tests described here are intent; exact workflow results and revisions establish pass/fail.

## Local-work and publication provenance

At the start of this increment, no working checkout or unpushed local repository was present, and connected GitHub had no open PRs. Retained source/history archives were recovered through a read-only GitHub Actions export. The ZIP digest, embedded archive checksums and exact source/dependency revisions were verified locally before restoring the checkout. Direct local network access remained unavailable, so publication used the connected GitHub actions with patch hash and exact-tree verification. Temporary source-export/integration helpers were removed from the final source.

The local tested source tree was compared to the connected branch tree after publication. GitHub remains the authoritative published history. Main/Pages publication still requires the unchanged four validation gates; pre-merge evidence does not imply a completed deployment.
