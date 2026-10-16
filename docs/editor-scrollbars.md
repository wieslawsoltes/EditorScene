# Native editor scrollbars

`SyntaxEditor` uses the two native WinUI `ScrollBar` controls that the pinned
UnoEdit `TextArea` creates beside its text viewport. They are not DOM scrollbars
and are not owned by a `ScrollViewer`.

## Missing-indicator cause and fix

At UnoEdit revision `1a2df473f157a0127d2559f241dc712d1ce60188`, those standalone
controls keep `IndicatorMode` at its default, `None`. Their native template can
therefore hide the thumb and buttons while the 14-DIP layout rails and the
scrolling offsets still exist. A successful wheel-scroll or a positive
`ActualWidth` is not evidence that a scrollbar is painted.

`SyntaxEditor.ScrollBars.cs` sets a local `MouseIndicator` value on both native
rails during construction and supplies axis-specific accessibility names. This
is a persistent native indicator state, not a synthetic pointer-over state.
Only the existing direct-child scrollbars are configured. There are no global
styles, replacement controls, polling timers, reflection, template-part writes,
or new production event subscriptions. The native range, viewport, wheel,
keyboard, track, arrow and thumb handlers remain the source of scrolling state.
The pinned dependency and its layout dimensions are unchanged.

Local values survive loading and theme/template updates. Every new editor,
including an independently scrolling split view, receives the policy. Document
replacement does not replace the rails. Word wrap retains UnoEdit's existing
behavior of disabling horizontal scrolling; a document with no overflow has a
zero range rather than an artificial scrollable extent.

References:
- [WinUI ScrollBar.IndicatorMode](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.primitives.scrollbar.indicatormode)
- [Pinned TextArea implementation](https://github.com/wieslawsoltes/AvaloniaEdit/blob/1a2df473f157a0127d2559f241dc712d1ce60188/src/UnoEdit.Uno/Editing/TextArea.cs)

## Regression coverage

`NativeScrollBarChecks` is called by the existing actual Uno-host suite. It
checks constructor defaults, local dependency-property values, accessible names,
rail identity and dimensions, Light/Dark/High Contrast changes, view options,
document replacement, split ownership, unrelated controls and repeated teardown.

`tools/browser-scrollbars.mjs` is called by the existing published-WebAssembly
smoke suite, so the normal three-pass browser gate includes it. It opens real
plain-text fixtures through the file picker and observes the opt-in **read-only**
`EditorScrollBars` diagnostics. Each of two fresh browser contexts tests 1x or 2x
pixel density. Both axes and Light/Dark themes exercise actual arrow clicks,
thumb drags and track paging. The suite verifies native thumb opacity after idle
and pointer exit, range/viewport/offset agreement, and changed raster images
cropped strictly inside each scrollbar rail. It also covers wheel scrolling,
wrapping, read-only mode, independent split views, narrow resizing, and
large-to-small-to-large document replacement.

Run the normal gates:

```sh
python3 tools/build.py test
python3 tools/build.py desktop
python3 tools/build.py native-checks
python3 tools/build.py browser --install-workloads
node tools/browser-smoke.mjs artifacts/site
```

Browser screenshots include `scrollbars-<theme>-<scale>x.png`, per-axis start and
scrolled crops, narrow layouts, and failure-state JSON when an assertion fails.
A test file or authored assertion is not proof of a pass: use the results for
the exact candidate commit. CI rendering uses Chromium/Skia with SwiftShader,
not a claim about physical-GPU, mobile-device, or native Windows execution.
