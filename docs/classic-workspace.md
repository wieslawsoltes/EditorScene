# Classic editor workspace review

This increment continues from merge `a62fe1871b1bd7f122f674e986de5b44f7c6dc27`. It targets the **SDI Code Editor** reference rather than inventing an unrelated application shell. All previous work visible on GitHub is merged; local filesystem inspection is unavailable in the current session, so an assertion that every possible local file has been recovered would be unwarranted.

## Reference

Actipro's public sample defines compact File / Edit / Advanced / IntelliPrompt / View / Outlining / Language menus, grouped small toolbar commands, paired navigable symbol selectors, bottom Error List and Events tool windows, a Document Outline side pane, and neutral status-bar position/overwrite/zoom controls. The editor supports a horizontal split. These are design/behavior references, not copied control implementations or assets:

- https://github.com/Actipro/WPF-Controls/blob/develop/Samples/SampleBrowser/ProductSamples/SyntaxEditorSamples/Demo/SdiCodeEditor/MainControl.xaml (inspected blob `33d47bf4cc8f6598dfbc4d67a9989000ed71034b`)
- https://www.actiprosoftware.com/docs/controls/wpf/syntaxeditor/index
- https://www.actiprosoftware.com/docs/controls/wpf/syntaxeditor/user-interface/searching/search-overlay-pane

No Actipro or Microsoft sample images, proprietary binaries, or font files are copied. EditorScene retains its own identity. Full API and pixel parity remain unverified.

## Work in progress

Planned scope: reusable compact native chrome, resizable top/bottom and side-by-side splits, usable status-bar zoom, tabular revision-safe diagnostics with filtering, and related correctness/performance regressions. Implementation and actual validation results will replace this section before merge. Unchanged subsystems retain existing regression coverage; this is not an exhaustive manual audit of every platform or source file.
