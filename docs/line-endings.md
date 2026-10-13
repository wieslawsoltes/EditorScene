# Minimal line-ending conversion

## User-visible behavior

Advanced contains LF, CRLF and CR conversion commands. They normalize the entire current document, not just selected lines. The CR command is added without renumbering existing EditorCommand values and participates in the same command-state and semantic-macro paths. Read-only command surfaces disable all three choices.

A document already using the selected delimiter is now left alone: its revision, clean/dirty marker, undo and redo history, and selection do not change. Previously the document and native command implementations prepared replacements for every delimiter, including equal replacements. A repeated conversion could therefore create needless edits and destroy a redo branch.

Actipro's documented normalization behavior similarly leaves text unchanged when no normalization is needed. The supported standard terminators are CRLF, LF and CR:

https://www.actiprosoftware.com/docs/controls/wpf/syntaxeditor/text-parsing/core-text/line-terminators

This increment targets those behaviors. It does not change the existing first-delimiter PreferredLineEnding property to the vendor's distinct priority-based inferred-terminator policy, nor does it introduce all vendor snapshot/API aliases.

## Reusable immutable planner

```csharp
var snapshot = document.Snapshot();
var plan = LineEndingEditPlan.Create(snapshot, "\r\n", cancellationToken);
// Hosts without view-specific protection can apply to the matching live document.
bool changed = plan.Apply(document);

// Native hosts retain the view's protection callbacks and read-only policy.
bool changedInView = editor.ChangeLineEndings("\n", cancellationToken);
```

LineEndingEditPlan stores identity, revision, target delimiter, immutable edits and source counts for CR, LF and CRLF. It retains neither a snapshot nor a mutable document. Plan creation reads immutable source in bounded chunks, recognizes CRLF across reader boundaries, and never requests the complete source string or a full snapshot line index. A prepared plan rejects a foreign or changed document, even when it contains no edits.

Each changed delimiter emits at most one inserted, removed or replaced UTF-16 unit. CRLF-to-LF removes only CR; CRLF-to-CR removes only LF. LF-to-CRLF inserts the missing CR, and CR-to-CRLF inserts the missing LF. Correct delimiters and ordinary content are untouched. This preserves shared delimiter units instead of needlessly replacing them and allows permitted CR removal without requiring permission to replace an already-correct protected LF.

Both document-level and native applications use the existing normal undo transaction. A nonempty conversion can be undone once to restore all original mixed delimiters. A canonical conversion performs no transaction. TextAnchor, selection and indicator behavior continues through the native engine's actual minimal edit notifications; this is not a separate emulated undo stack.

## Permissions, cancellation and ownership

The public document plan applies to the document core and does not itself know native protected ranges. EditorScene.Controls.ChangeLineEndings checks the view's read-only state, rejects active snippet editing, then preflights every actual edit with the existing insertion/deletion permission service. It verifies that document, revision, language, provider and read-only state remain current after callbacks. Later denial or cancellation prevents every planned edit rather than committing an accepted prefix. Reentrant conversion requests are not applied.

Cancellation is checked during bounded source scanning, between permission checks and immediately before mutation. Applying normal synchronous edit notifications is not a rollback transaction for arbitrary failing or reentrant host event handlers. A callback that replaces the document or permission provider during preflight invalidates the old plan. The reusable native method does not request focus or force a post-edit caret; newer host selection changes remain authoritative.

CR, LF and CRLF are the entire normalization scope. Unicode line/paragraph separators, NEL, tabs, surrogate code units and ordinary text are not silently normalized. This is a textual transformation, not a language-aware rewrite; newline-sensitive literals may change meaning. Source construction, document owner-thread rules, read-only ownership and caller cancellation remain explicit.

## Performance methodology

Canonical input uses bounded working storage rather than one edit object per line. The scanner rents a 4096-character buffer and clears it before returning it. Time remains proportional to source length; noncanonical input additionally stores its real edits. Immutable-array growth and final edit sorting/application have costs, so this is not a universal zero-allocation or constant-time conversion claim.

LineEndingBenchmarks retains the preceding live-line enumeration and all-delimiter replacement array as the baseline. It compares planning for 100000 canonical LF lines and 100000 mixed lines against the new immutable-source scanner. Three warmups and eleven sequential samples report all timing and current-thread managed-allocation values. Setup and byte-for-byte output verification are outside timing. Actual edit/undo application, permission callbacks, native layout, rendering, physical GPU and end-to-end interaction are excluded. Any unfavorable timings remain in the full report rather than being hidden by the canonical allocation result.

## Validation

Headless tests cover scalar-equivalent normalization, randomized mixtures, CRLF straddling chunks, trailing CR, minimal edits for all six conversions, no-op saved/redo history, stale/foreign plans, cancellation, worker snapshots and non-flattening. A warm canonical 100000-line case verifies that allocation is bounded and the snapshot's lazy line index remains unbuilt.

Native tests exercise real editor undo, canonical command behavior, protected shared LF units, later permission rejection, read-only transitions, document replacement, cancellation, CR macro replay and observer-chosen caret preservation. Browser tests use actual native menus and keyboard undo/redo, enforce no-op revision/selection preservation, test the new CR choice and inspect enabled/disabled menu states in both themes. Prior regression, strict unique native checks, eleven isolated package consumers and three complete fresh-browser runs remain required. Test definitions are not evidence of a passing revision; inspect its completed workflow.

## Remaining scope

This is a normalization and command-surface increment, not full Actipro API/feature/pixel parity. Snapshot inferred/uniform metadata APIs, save-time-only normalization, persistence, docking, printing, broader language/refactoring/LSP coverage and full device/IME/accessibility qualification remain separate work. Uno/Skia remains the real renderer; software SwiftShader browser validation is not a hardware-GPU throughput measurement. Local execution tools were unavailable during this continuation; changes are committed and executed through GitHub Actions, with no claim about unknown local-only files.
