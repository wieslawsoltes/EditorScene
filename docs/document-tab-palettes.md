# Retained document tab palettes

The document strip retains native button identities when documents are added, activated, saved or closed. Replacing a theme must also update each retained caption's foreground, not just its background. A native content presenter can otherwise retain a foreground resolved under the old theme, leaving nearly white text on a light tab.

`EditorScene.Controls.EditorDocumentTabPalette` owns four native brushes for one immutable `EditorTheme`. A host shares a palette across its tabs and calls `Apply(button, caption, active)` when theme or active state changes. The method sets the actual `TextBlock.Foreground` as well as the button's foreground, background and selection border. Brush instances are shared; callers should not mutate them.

The helper is UI-thread-owned. It does not rebuild controls, change captions, move focus, modify documents or replace input handlers. Same-state calls compare brush identities before writing dependency properties. EditorPage creates a new four-brush palette only when its theme changes, instead of constructing new background and border brushes on every active-tab transition.

## Validation

The native host suite checks all three built-in themes in active and inactive states, retained Unicode/dirty captions, cross-tab brush sharing, stale foreground correction, and 10,000 warmed unchanged applications with zero managed allocations and zero native property writes. This is an assertion for that isolated operation, not an end-to-end UI frame-rate claim.

The complete browser suite switches Light, Dark, High Contrast, Light and Dark using actual input. It checks unchanged tab/menu construction counts and retained dirty captions. `browser-tab-colors.mjs` samples the actual Uno/Skia screenshot inside fully visible tab captions, excluding selection borders, and requires visible foreground ink. It does not read HTML color styles, set native state, or replace the application's rendering. Screenshots are retained for visual review.

The native and browser tests must pass on the published branch head before this change is considered validated. Original Actipro SyntaxEditor parity remains a broader project goal; this change corrects retained-tab readability and unnecessary brush allocation, not the entire product's feature or pixel parity.
