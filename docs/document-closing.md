# Document-tab closing

EditorScene provides an independent native document-tab context menu and an identity-based close workflow. The reusable pieces live in `EditorScene.Text` and `EditorScene.Controls`; the sample owns actual editor documents, file pickers, confirmation dialogs and language-server connections.

## Commands

Right-click a document tab to open **Close**, **Close All But This**, **Close All**, or **Close Tabs to the Right**. The context target is the clicked tab, not whichever document happens to be active. Opening the menu does not itself activate that document. The menu disables scopes with no targets, such as closing tabs to the right of the last tab.

Middle-click closes its target tab through the same confirmation policy. **Ctrl+F4** closes the active document, alongside the existing Ctrl+W. **Alt+-** opens the active tab's context menu. The Window menu also exposes **Tab Context Menu**, and the File menu exposes **Close Other Documents** and **Close All Documents**. A browser or OS may intercept shortcuts before the application receives them; the visible menus remain alternatives.

The public behavior reference is Actipro's tabbed MDI documentation, which describes middle-click tab closing and Alt+- tab context menus:

https://www.actiprosoftware.com/docs/controls/wpf/docking/workspace-mdi-features/tabbed-mdi

This increment does not add docking, pin/preview states, floating windows, drag reordering or every vendor context command. It uses original code and native menu controls, not proprietary source or copied artwork.

## Saving, cancellation and identity

A close request first freezes its target identities in current tab order. Subsequent document additions are not included in that request. Every remaining identity is resolved before closing it. A missing context target is rejected even for Close All, preventing an obsolete tab from targeting a different workspace.

Each dirty target receives its own **Save / Discard / Cancel** dialog. Saving an inactive target saves that target, not the currently active editor. Save uses the existing exact-revision snapshot and file-provider completion check. A revision changed during an asynchronous save stays dirty and is not closed. Cancelling a picker, declining a dialog or failing to save stops the batch before later targets. Discard also refuses to close a document that changed while its confirmation was open.

This is sequential confirmation, **not an all-or-nothing transaction**. Documents already accepted and closed before a later Cancel stay closed; the cancelled document and all subsequent targets remain open. The sample does not discard later dirty documents automatically. Exceptions propagate to its existing error reporting and release operation coordination.

Save and close requests share a non-queuing operation coordinator. A second operation arriving while the first awaits a dialog, file provider or server shutdown is rejected, not deferred against a potentially different active document. Named File write/close commands, the Save toolbar button and the fixed Close button reflect the busy state. Other entry points still pass through the same coordinator. Ordinary editing and document navigation outside modal dialogs are not globally locked; the save snapshot/revision checks remain necessary.

Closing an inactive document does not reset the active view, selection or undo history. Closing the active document chooses a surviving neighboring document. The last close creates one clean untitled document; its new identity is not part of the frozen close plan. Optional language-server shutdown happens after removing native tab ownership; disposal of the removed document runs even if shutdown fails.

## Reusable APIs

`DocumentClosePlan.Create(ids, targetId, scope)` snapshots membership once, validates every nonempty/unique ID and returns an immutable ordered selection. It holds no editor documents, controls, file handles or callbacks. `DocumentCloseScope` is Current, Others, All or ToRight. Callers retain full authority to authorize or decline each close.

```csharp
var plan = DocumentClosePlan.Create(openDocumentIds, clickedId, DocumentCloseScope.Others);
foreach (var id in plan.DocumentIds)
{
    // Host method performs its own current-membership, save and cancellation checks.
    if (!await TryCloseHostDocumentAsync(id)) break;
}
```

`DocumentOperationCoordinator.TryRunAsync` runs at most one callback at a time. Busy requests return false without invoking or queuing the supplied callback. An operation's own false result also returns false. Cancellation and exceptions propagate and release the coordinator. It is not a document-edit lock or rollback service. It uses an atomic busy flag but does not dispatch callbacks to a UI thread; the host still owns UI thread access and cancellation.

```csharp
var operations = new DocumentOperationCoordinator();
var completed = await operations.TryRunAsync(async cancellationToken =>
{
    cancellationToken.ThrowIfCancellationRequested();
    return await SaveHostDocumentAsync(cancellationToken);
});
```

`EditorDocumentTabMenu` is an actual Uno MenuFlyout. Configure it with a stable target ID, its zero-based index and the current document count before showing it. CloseRequested supplies an immutable ID/scope pair; the host resolves membership again and applies its own policy. The menu does not activate or dispose host documents.

```csharp
var menu = new EditorDocumentTabMenu();
menu.Configure(clickedId, clickedIndex, openDocumentCount);
menu.CloseRequested += (_, request) => HandleHostCloseRequest(request.DocumentId, request.Scope);
menu.ShowAt(tabButton);
// At teardown, close/detach the native flyout and call Dispose().
```

One menu instance retains four native command items across target and theme changes. ClearContext disables requests and hides it; Dispose detaches handlers and rejects future mutations. Native enabled-state callbacks can reconfigure or dispose the menu: a generation check stops remaining writes from the older refresh. Hosts should not directly mutate its owned Items collection, tags or command items.

## Lifetime and performance

Application tab handlers store only stable IDs in native Tag properties, rather than capturing live WorkspaceDocument objects. Removing a tab detaches click, load, context-request and middle-pointer handlers and clears the tag. This avoids keeping a removed document through a native control's callback closure. Surviving tabs retain their original controls.

Planning is O(n) in open document count and allocates an immutable membership/target snapshot plus ID validation storage. Context refresh touches four existing native items and changes IsEnabled only when necessary. The sample still uses list lookups, list removals and reconciliation per accepted close, so large bulk closure can be quadratic. There is no claim of large-workspace virtualization, zero-allocation close operations or measured end-to-end speedup. The scanner measurements from PR #14 are a separate workload and do not establish close-workflow performance.

## Validation

Headless tests cover every close scope, missing targets, malformed later entries, snapshot independence, single enumeration, busy/reentrant rejection, cancellation and exception recovery. Actual Uno-host tests verify menu scopes, stable native object identities, disabled commands, malformed context, reentrant property callbacks and disposal.

The browser regression uses native right/middle clicks and keyboard input, not a command-mutation bridge. It checks partial-batch Cancel, saving an inactive document through a real downloaded file, preserving another document's revision/text and undo history, retaining the chosen dirty tab in Close All But This, Ctrl+F4 confirmation, and the final clean placeholder. Light/dark context screenshots are retained. The isolated desktop/browser package consumer compiles and instantiates the new menu API.

All existing regression, native, package and three-fresh-browser gates remain required. These descriptions specify tests, not proof of a passing run; use the exact source revision's workflow results. Local filesystem access was unavailable during this continuation, so unknown local-only files and independent local archive inspection were not verified.

## Remaining work

Full vendor docking and tab policies, workspace/session persistence, rich switchers, full path/caption disambiguation, printing, remaining advanced language/refactoring/LSP functionality and complete platform/IME/accessibility qualification are separate unfinished work. Save retains the existing byte-array encoding path; very large-file streaming saves need additional implementation. Rendering remains Uno/Skia, with browser CI using WebGL2 through software SwiftShader rather than measured physical-GPU throughput.
