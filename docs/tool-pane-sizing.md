# Native tool pane sizing

The classic SDI workspace offers a six-pixel native resize grip on the Document Outline's left edge as well as the bottom tool window's top edge. The previous outline width was fixed, and the bottom grip supported only mouse dragging and did not reconcile its size after a viewport shrink. Both grips now support arrow keys, Home/End limits, Enter or double-click reset, and Escape to cancel a current drag. Distinct accessible close names identify the outline and bottom pane; retained heading foregrounds are explicitly updated with the palette.

This follows the public Actipro SDI sample's outline/right and Error List/Events/bottom tool-pane arrangement, not its complete docking implementation. Reference: https://github.com/Actipro/WPF-Controls/blob/develop/Samples/SampleBrowser/ProductSamples/SyntaxEditorSamples/Demo/SdiCodeEditor/MainControl.xaml (reference blob 33d47bf4cc8f6598dfbc4d67a9989000ed71034b). No vendor implementation, icons, binaries or fonts are incorporated.

## Reusable control

`EditorScene.Controls.EditorPaneSplitter` is a UserControl hosting a native Thumb, exposed through `Separator`. Thumb is sealed in Uno, so the control composes it rather than inheriting it or replacing native dragging. Horizontal axis adjusts the width of a trailing/right pane; Vertical adjusts a trailing/bottom pane's height. Physical right/down movement reduces the trailing pane. The wrapper exposes `PreferredExtent`, `EffectiveExtent`, `MinimumExtent`, `MaximumExtent`, `DefaultExtent`, `ResizeBy`, `ResetExtent`, `CancelResize`, `IsResizing`, `ExtentChanged` and `ApplyTheme`. The hosted separator owns keyboard focus and receives the wrapper's automation name when mounted.

The host computes MaximumExtent from available space and applies EffectiveExtent to its own border/grid. The preferred extent is not overwritten merely because a viewport limit became smaller. With insufficient room, the effective minimum becomes the capacity, including zero. Moving a previously constrained grip starts from the effective visible extent to avoid a jump. All assigned extents must be finite and nonnegative; invalid values fail before mutation.

The sample reserves central editing space, caps the outline at 60 percent of width excluding its rail, and caps the bottom pane by both height and measured chrome. Default sizes remain 240 pixels wide and 200 pixels high. Hide/reopen and switching between Error List and Events preserve preferences within the current page lifetime. This is not restart persistence. Extremely small windows still have irreducible menu/title/status content; no universal minimum-window guarantee is implied.

The splitter does not own a document, editor or tool-pane content. Disposal detaches its native event handlers. Hosts own their ExtentChanged registrations and must release them with the host. A cancelled pointer gesture restores its original preferred size; completed native resize events are synchronous and not a rollback transaction for arbitrary host callbacks.

## Performance and validation

Identical extent assignments and zero movement do not format new help strings, publish ExtentChanged or write the host's width/height again. Reapplying the same immutable palette reuses current brushes. Fourteen native assertions cover bounds, preferred-size restoration, effective-size movement, invalid input, themes, teardown and 10,000 unchanged warmed updates with zero current-thread managed allocation and no redundant extent publication. These narrow checks do not imply zero cost for active dragging, layout, virtualized rows, rendering or GPU work.

The initial 625925a candidate correctly failed native compilation in workflow38540425460 because Thumb cannot be inherited. The composition repair retains every assertion, updates focus checks to the actual hosted Thumb and requires fresh validation. No earlier failed candidate is treated as passing.

The complete existing browser suite adds real pointer/keyboard resizing, Escape rollback, both themes, viewport shrink/grow with exact preference restoration, pane hide/reopen and tool switching. Read-only diagnostics expose actual native bounds; no test-only mutation API is used. Test definitions are not execution evidence: the final source must pass all regressions, at least 451 unique actual native checks, all eleven isolated package consumers and three complete fresh browser passes before merge.

Floating/docking/reordering, full auto-hide/pin semantics, persistence, printing, complete Actipro feature/API/pixel parity and full device/IME/accessibility qualification remain separate work. This increment addresses usable pane sizing and retained native chrome rather than claiming the whole workspace is equivalent.
