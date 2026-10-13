# Snapshot line-ending metadata and native format status

`DocumentSnapshot.GetLineEndings(cancellationToken)` returns immutable CR, LF and CRLF counts, a None/CR/LF/CRLF/Mixed kind, uniformity and an inferred delimiter. A CRLF pair is counted once, even across reader boundaries. The snapshot also exposes `HasUniformLineTerminators` and `InferredLineTerminator` convenience properties.

Inference follows the publicly documented Actipro priority: CRLF before LF before CR, not the first or most frequent terminator. With no standard terminators, it returns `Environment.NewLine`. Empty and unterminated documents are uniform. NEL and Unicode line/paragraph separators are not counted as CR/LF terminators. Reference: https://www.actiprosoftware.com/docs/controls/wpf/syntaxeditor/text-parsing/core-text/line-terminators

The preexisting `EditorDocument.PreferredLineEnding` intentionally retains its first-delimiter and LF-default insertion behavior. Reading metadata does not change source, encoding, undo history, preferred input policy or saved state. The new APIs are not aliases for every vendor text/snapshot interface.

## Ownership and performance

An initial metadata query scans immutable source with a bounded, cleared 4,096-character reader buffer, without materializing the full string or building the lazy line index. It caches a completed immutable result. Cancellation is honored on cached and uncached queries; cancelled scans do not poison future queries. Concurrent first readers may do duplicate bounded scans but publish one result. Old snapshots remain worker-readable after live-document mutation or disposal.

Once metadata has been observed, the document maintains private value-type counts from each edit's removed and inserted text plus one unchanged UTF-16 neighbor on each side. Those neighbors account for CRLF pairs split or joined across edit boundaries. It does not rescan all unchanged text on each keystroke. Small changes avoid reader/buffer allocations. Immutable metadata is materialized only when a new snapshot is requested; a bulk conversion does not allocate a metadata object for every individual delimiter edit. Matching counts reuse the existing immutable result. A worker still computing old metadata is not awaited by the editing thread.

Cold work remains proportional to document length. Maintaining observed counts is proportional to removed/inserted content, plus bounded boundary work. It does not make bulk editing, source allocation, undo, syntax analysis or rendering constant-time. Large replacement scanning still rents buffers; observing changed counts creates a small immutable result. No frame-rate or physical-GPU improvement is inferred. Conversion planning can reuse completed metadata; see cached-delimiter-plans.md for measured-scenario boundaries.

## Native interface

The existing compact status bar now shows UTF-8 Mixed for mixed files instead of presenting the first delimiter as if it described the whole document. None distinguishes text with no standard terminators from an actual LF document. CR, LF and CRLF conversion and undo update this label through normal document events. Its native tooltip and accessibility help report separate counts.

`EditorStatusBar.DocumentFormatLabel`, `DocumentFormatText`, `LineEndingText` and `DocumentFormatRefreshCount` allow native hosts to inspect and reuse this surface. Ordinary content edits and caret movement reuse the format label and its formatted strings. The existing narrow-width rule hides the label below 760 pixels without losing metadata; it reappears when space is available. Encoding changes without editor notifications require the existing explicit `Refresh()` API.

## Validation

Headless tests exercise counts and priority, CRLF reader boundaries, every replacement/undo/redo in short x/CR/LF fixtures, randomized repeated edits, immutable old snapshots, rename reuse, cancellation, concurrent readers after disposal, unbuilt text/index caches and zero-allocation warm queries. Batch storage compares identical 1,000-edit transactions with/without observed counts and requires maintenance to add no more than 4 KB, rather than one metadata object per edit. Captures inside an update remain independently correct. Fourteen new actual Uno-host checks cover format text, exact count help, normalization/undo, ordinary-edit reuse, encoding, readonly/themes, detachment and caller ownership.

The complete browser suite observes the actual native status label through the read-only diagnostic bridge. Actual menu and keyboard input must produce Mixed, CRLF, CR, LF and None as appropriate. It checks format-label refresh counts across ordinary edits, preserves existing canonical no-op and redo assertions, exercises compact resizing, and retains screenshots in both palettes. No HTML renderer, test-state mutation, input retry or assertion removal is used.

Success requires the completed core, native, eleven isolated-package and three full fresh-browser passes for the final source. Test definitions alone are not execution evidence. Broader Actipro feature/API/pixel parity, printing, docking, workspace persistence and full platform/IME/accessibility qualification remain separate work.
