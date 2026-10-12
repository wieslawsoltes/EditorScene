# Whitespace editing: selected text versus selected lines

This increment continues from main `d80936a12a6af376ac1311c4bd811c84f6d5f8c9`. It distinguishes selection-wide whitespace conversion from indentation-only tabification and replaces full-line rewriting in the existing indentation/trailing-trim paths. It does not establish full Actipro API, feature or pixel parity.

Actipro documents separate selected-line tabification and selected-text conversion operations. That behavioral distinction is the reference for the new native **Advanced → Whitespace** submenu:

| Command | Nonempty selection | Empty selection |
| --- | --- | --- |
| Tabify Selected Lines | Canonicalize leading spaces/tabs on intersected lines | Current line only |
| Untabify Selected Lines | Expand indentation tabs on intersected lines | Current line only |
| Convert Selected Spaces to Tabs | Canonicalize ASCII whitespace runs only inside the selection, including inline whitespace | No operation |
| Convert Selected Tabs to Spaces | Expand tabs only inside the selection, including inline whitespace | No operation |
| Trim Trailing Whitespace | Remove trailing ASCII spaces/tabs on intersected lines | Whole document |
| Convert Leading Spaces to Tabs / Convert Leading Tabs to Spaces | Existing indentation-only behavior on selected lines | Existing whole-document behavior retained |

A selection ending at the next line's start excludes that following line for line-based actions. Existing command enum values remain unchanged; four new command IDs are appended. The compact submenu keeps different scopes explicit and reduces the previously long Advanced menu. Browser tests use the actual native submenu in both themes, verify target bounds, and click its real actions.

These are explicit text transformations, **not language-aware semantic refactorings**. Converting inline whitespace inside string literals or other whitespace-sensitive content can change program/data meaning. The operations preserve non-whitespace code units and delimiters, not every language's semantics. Only ASCII space and tab are normalized; nonbreaking spaces and other Unicode whitespace remain untouched.

## Reusable immutable edit plans

`EditorScene.Text.WhitespaceEditPlan` depends only on the text package:

```csharp
var snapshot = document.Snapshot(); // capture on the owning thread
var scope = snapshot.GetSpan(start, length);
var plan = WhitespaceEditPlan.Create(
    scope, WhitespaceConversion.Spaces,
    tabSize: 4, leadingOnly: false,
    cancellationToken: cancellationToken);

// Inspect plan.Edits without modifying a document, or apply later on its owner thread.
// Native hosts must first perform their own read-only/protected-range preflight.
plan.Apply(document);
```

The plan contains document identity, revision, immutable `TextEdit` values, scanned-character count and generated-character count. It does not retain a live view, document or source snapshot. Application verifies document identity and the expected revision and uses the existing one-undo-group edit mechanism. A plan created for a different document or obsolete revision cannot be applied.

`WhitespaceConversion.Tabs` produces canonical tabs followed by residual spaces for each eligible run. Stops are measured from the actual logical line start; an unselected prefix is scanned to determine the correct starting column. The algorithm does not restart tab stops at the selection boundary. Logical columns count UTF-16 units rather than shaped glyph widths or bidi display order. Tab size accepts 1–1000.

`leadingOnly: true` limits Spaces/Tabs modes to the indentation prefix of each line. It is invalid for TrimTrailing. `TrimTrailing` removes only the selected suffix that reaches a real line ending; a selection ending inside a longer whitespace run is not mistaken for end-of-line. Existing CR, LF and CRLF bytes/code units are never rewritten.

### Allocation and work bounds

A pooled 4,096-character reader scans from the first affected line's start through the requested scope end. Ordinary non-whitespace spans are skipped in blocks. The planner tracks whitespace columns/counts without constructing source strings; only changed whitespace runs get replacement strings. Already canonical indentation, tab-free space conversion and unchanged source produce no edits. A changed run is the edit granularity, not a claim of the mathematically smallest edit inside that run.

The default limits are 100,000 changed runs and 16,777,216 generated UTF-16 characters. Callers can explicitly choose other nonnegative output/positive edit budgets. Exceeding a limit throws before returning a plan; no shortened edit list is returned or applied. Trailing deletions generate empty replacements but still count against the edit limit. The buffer is cleared before return to the pool, including on exceptions. Cancellation is checked before reading, per chunk, before replacement allocation and before returning the final plan. Constructing a single allowed replacement string is not preemptible inside the platform allocator.

Source scanning is still linear in the visited region. Long unselected prefixes may need scanning to establish column/indentation context. Plans retain memory proportional to changed runs and generated output, not constant total memory. A source made almost entirely of changed whitespace can require substantial output. No automatic trimming-on-paste, schema-aware whitespace handling or asynchronous native progress UI is added here.

## Native host integration and review fixes

`SyntaxEditor.ConvertSelectedWhitespace(bool toTabs)`, `TabifySelectedLines(bool toTabs)` and `TrimTrailingWhitespace()` expose native operations. The view captures document/revision, provider, options, protection provider, caret, selection endpoints and selection-collection identity before permission callbacks. Every planned edit is preflighted before any edit is applied. A denied later edit prevents partial conversion.

The old indentation conversion built full-line replacements, even when only the indentation changed. That made a protected code span block an otherwise allowed whitespace-only change and enlarged undo payloads. The new plan requests permission only for changed whitespace runs. A native test protects all non-whitespace code while permitting its indentation and trailing spaces; tabification and trimming then succeed without replacing protected code.

Permission callbacks that change tab settings, selection, read-only state, provider or document invalidate the captured target. Recursive whitespace operations are rejected as no-ops while the current operation is applying. Source edits use the native session's existing forward/reverse selection affinities. The wrapper does not reselect old integer offsets after notifications, so a host's newer caret selection is not overwritten.

Read-only commands are harmless no-ops. Current native whitespace conversion requires one selection and no active snippet field session; unsupported multiple selections or snippet editing are explicitly diagnosed rather than silently converting a different range. The headless planner remains usable on arbitrary valid snapshot spans. Four new command IDs use the existing noninteractive macro recording/playback route and are covered by an actual native macro replay.

## Validation and benchmarks

Model tests cover canonical/tab-stop correctness, exact scopes, prefix context, partial trimming, CR/LF/CRLF, non-ASCII preservation, no-op undo behavior, cross-reader runs, output/edit limits, stale/foreign application and worker use after document disposal. Randomized canonicalization is compared with a separate scalar expansion oracle and checked for idempotence.

Actual Uno-host checks cover current-line versus selection scope, following-line exclusion, protected-code editing, whole-plan permission rejection, host caret preservation, reversed selection and macro replay. Browser tests use real selection keys, menu clicks, clipboard paste, undo/redo and theme changes; their observation bridge cannot mutate the source. Isolated NuGet consumers execute the new planning API and compile the native APIs.

`WhitespaceBenchmarks` adds raw rows to `artifacts/tests/performance.json`:

- `indent_conversion_40_long_lines`: the preceding `ConvertIndentation`/`EditSelectedLines` algorithm from d80936a versus the new plan. Both receive forty lines containing four leading spaces, 100,000 code characters, two trailing spaces and LF. Final output equality is verified outside the timed region. The old route builds forty full-line replacements; the new route builds forty whitespace-only replacements with forty generated characters total.
- `whitespace_scan_4000000_unchanged`: new no-change planning over a four-million-character ordinary-code line, with no baseline speedup claim for this separate fixture.

Measurements retain three warmups, eleven samples, p50, observed p95 and current-thread managed allocations. Initial document/line-index construction, equivalence verification and actual edit application/undo are excluded. The reader pool is warm; baseline/current groups run sequentially. Results are CPU planning costs, not complete native input latency, physical-GPU throughput, frame rate, power consumption or an Actipro performance comparison. Earlier unfavorable compiler and dense-tab constructor measurements remain relevant and are not resolved by this increment.

Read the exact workflow/artifact revisions and pass/fail results before treating these tests or benchmarks as verified. Failed workflows also retain diagnostic artifacts. Local filesystem/execution access was unavailable in this session; implementation and execution use GitHub and Actions, without claiming recovery of unknown local-only files or independent physical-device verification.

## Primary reference and remaining scope

- Actipro whitespace modes and selected-line versus selected-text conversions: https://www.actiprosoftware.com/docs/controls/wpf/syntaxeditor/user-interface/editor-view/whitespace
- Original-product feature inventory: https://www.actiprosoftware.com/docs/controls/wpf/syntaxeditor/index

Full multi-selection transformation policy, automatic whitespace-trim modes, smart language indentation, persistent profiles/workspaces, project/schema/refactoring services, further LSP features, printing/docking and complete API/visual/IME/accessibility parity remain separate work. Existing Uno/Skia dependency attribution and original icon ownership are unchanged.
