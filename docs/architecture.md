# EditorScene architecture

## Dependency direction

`EditorScene.Text` wraps the pinned, persistent-rope `UnoEdit.Core` document engine. `LanguageServices` consumes only text snapshots and exposes UI-independent results. DotNet and Structured implement those contracts. Protocol is an independently usable transport/client layer; the sample does not start external language servers implicitly.

`Rendering.Skia` combines semantic classification with the native incremental lexical style source. `Controls` composes these services with the native Uno editor and the UI-independent Snippets package. `App` provides the sample workspace; libraries do not depend on the app. The external dependency is a fixed git submodule rather than a floating download.

## Document ownership and concurrency

An `EditorDocument` has one mutation owner. `Snapshot()` must be captured on that thread before an asynchronous provider call. Reading a captured snapshot is safe on workers. `ApplyEdits` validates all ranges and overlap before starting a single undo group; all edit coordinates refer to the expected revision, and descending application preserves those coordinates.

The analysis coordinator cancels superseded work and checks request generation, document identity and revision on the UI dispatcher before publishing. Formatting, completion and snippet host callbacks similarly guard the insertion target. Cancellation is not a substitute for revision checks: a provider may finish after cancellation or ignore a cancellation request.

`TrackingPoint` and `TrackingSpan` retain immutable version checkpoints and map positions through the core's edit history. Half-open snapshot ranges distinguish character containment from point affinity. A zero-width exclusive span stays zero-width on the positive side of a boundary insertion; inclusive spans include inserted boundary text. Translating through deleted text is deliberately not reversible in the mathematical sense: deleted locations collapse, so the original identity is only recoverable from retained history/checkpoints, not from the collapsed integer alone.

## Views and input

Each `SyntaxEditor` owns its selection, viewport, popup sessions and provider bindings. Externally supplied documents stay caller-owned. Independent views share undo and document indicators, while retaining separate carets and scrolling.

UnoEdit resets transient input handlers when its document changes. EditorScene reattaches exactly one persistent command handler after that reset; completion and snippet handlers remain transient and sit above it. This prevents lost Ctrl+Space and other editor shortcuts after document switching. Snippets use the native insertion context for tracked editable fields, mirrored occurrences, Tab navigation and completion/end-caret positioning.

Snippet import parses and validates a batch before changing the catalog. Catalogs are host-owned and can be shared by split views. Importing metadata never executes functions or resolves external dependencies. Hosts must supply explicit callbacks for such capabilities; those callbacks must not change the document or language during insertion.

## Rendering

The editor uses Uno's Skia renderer and HarfBuzz-backed shaped text. Viewport layout and line caches are provided by the native UnoEdit renderer. EditorScene paints semantic colors, diagnostics, guides, whitespace, bracket highlights and indicators into the borrowed canvas; it does not create a GPU device per control or own the host surface.

Theme palettes are shared with application chrome and vector icons. The glyphs are original vectors rather than extracted Actipro artwork. Current visual similarity is an implementation target, not a completed pixel comparison.

## Packaging and validation

`tools/package.py` builds all project packages and the three native dependency packages. A temporary consumer directory outside the source tree uses a fresh global package cache and source mapping. Both package identities and archive SHA-256 hashes are checked after restore. The headless consumer runs; native desktop/browser consumers compile. This catches missing packaged dependencies and source-tree-only coupling but is not a substitute for device testing.

GitHub Actions runs unit/regression tests, an actual X11 Uno host, isolated package consumers and Chromium on the exact published app. Pages depends on all four jobs and the repository's Pages environment rules. A successful compilation alone cannot publish the preview. `build.json` identifies the source and dependency revisions of the deployed artifact.
