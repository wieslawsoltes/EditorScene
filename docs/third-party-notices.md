# Third-party provenance

EditorScene's original implementation is MIT-licensed; see the root LICENSE. The following components retain their own licenses and notices.

## UnoEdit / AvaloniaEdit / AvalonEdit

`external/UnoEdit` is the MIT-licensed Uno port in https://github.com/wieslawsoltes/AvaloniaEdit, pinned to commit `1a2df473f157a0127d2559f241dc712d1ce60188`. Its source retains Avalonia Team and AlphaSierraPapa/SharpDevelop/AvalonEdit notices. The repository uses this source with attribution, not a rewritten ownership claim. The included UnoEdit.Core, UnoEdit.Rendering.Skia and UnoEdit.Uno packages carry their own metadata and README.

## Platform, rendering and compiler dependencies

Uno Platform / Uno.Sdk provide native cross-platform controls and application hosting. SkiaSharp and HarfBuzzSharp provide drawing and text-shaping integration. Microsoft.CodeAnalysis C# and Visual Basic provide compiler services; .NET reference metadata is embedded during compilation of the language package so semantic analysis does not depend on runtime assembly file locations in WebAssembly. Their original package licenses and third-party notices remain applicable.

NUnit, the .NET test SDK and Playwright are development/validation dependencies. GitHub Actions workflow components retain their upstream licensing.

## Product identity

Actipro and SyntaxEditor are names used to identify the requested compatibility target. EditorScene is not affiliated with or endorsed by Actipro. No proprietary Actipro implementation, license keys, compiled control assemblies or extracted icon assets are included. EditorScene icons are original vector drawings.
