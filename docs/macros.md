# Macro recording and playback

EditorScene now provides a UI-independent `EditorScene.Text.Macros` model and recorder plus native editor integration. This is an incremental parity feature, not a claim that every Actipro edit-action extension or serialized vendor macro is compatible.

## User interface

The Macros menu contains Start, Stop, Pause, Resume, Cancel Recording, Run Last Macro, Cancel Playback, Import and Export. While recording or playing, an editor-local pointer shield prevents mouse selection from changing the action sequence; menus remain available. Escape cancels playback between actions. Macro commands have no default key bindings.

Typing, character/word/line/page/document navigation, extended selections, deletion, indentation, overstrike, clipboard commands and noninteractive EditorScene commands are recorded as semantic actions rather than screen coordinates. Text replay uses the current editor's pairing and overstrike options. Stop saves a nonempty recording; Cancel and an empty Stop preserve the previous saved macro. Paused actions are not recorded. Document switching cancels recording and requests playback cancellation.

Automatic completion popups and snippet shortcut expansion are suspended during active recording/playback. Explicit interactive popup commands and adding multiple selections are rejected during recording; pause first to use them. Interactive snippets, search-dialog field changes, file/workspace commands and arbitrary external UnoEdit input-handler side effects are not captured by this implementation. Noninteractive search-next/previous uses the target editor's current search settings. Language and view-dependent commands use the target editor's current context.

## Reusable recorder

```csharp
using EditorScene.Text.Macros;

var recorder = new MacroRecorder();
recorder.Record();
recorder.NotifyEditAction(MacroStep.InsertText("hello"));
recorder.NotifyEditAction(new MacroStep("editor.SelectAll"));
recorder.NotifyEditAction(new MacroStep("editor.Uppercase"));
EditorMacro? macro = recorder.Stop();
```

The immutable `EditorMacro` may be shared between hosts; a mutable `MacroRecorder` belongs to a single owner and is not a concurrent collection. `CurrentMacroAction` is a defensive immutable snapshot. `PlayAsync` executes a host callback sequentially, suppresses recording during playback, rejects recursion, and restores Idle after success, cancellation or failure.

The native `SyntaxEditor` exposes `MacroRecording`, `MacroRecordingStateChanged`, `InsertText`, `RunMacroAsync(repeat, cancellationToken)` and `CancelMacroPlayback`. `RunMacroAsync` validates every imported command against a closed native allowlist before performing any edit. It does not keep a document update scope open across asynchronous clipboard or formatter operations. Completed steps retain their ordinary native undo units; cancellation/failure is not an all-or-nothing transaction.

## Persistence and trust

`MacroSerializer.Load/Save` round-trips versioned EditorScene XML, including Unicode, tabs and CRLF, without owning the caller's stream. Files use `.esmacro`; they are not binary-compatible with Actipro's XML macro format. Maximums are 10,000 actions and 1,048,576 UTF-16 argument units per macro, 8 MiB parsed XML characters, 1,000 repetitions and 100,000 executed actions per playback.

The format contains command IDs and text, never .NET type/assembly names. DTDs and external entity resolution are prohibited. Importing does not execute the macro. The standalone model supports custom IDs for trusted host dispatch; the native editor rejects unrecognized IDs instead of activating types or loading assemblies. Clipboard actions execute only on explicit playback and retain the platform's clipboard permissions.

Primary behavioral reference: https://www.actiprosoftware.com/docs/controls/wpf/syntaxeditor/user-interface/input-output/macro-recording
