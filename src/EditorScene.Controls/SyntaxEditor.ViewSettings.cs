using EditorScene.Rendering.Skia;

namespace EditorScene.Controls;

public sealed partial class SyntaxEditor
{
    private bool _copyingViewSettings;
    /// <summary>Whether this control has released its native view. The host still owns external documents/providers.</summary>
    public bool IsDisposed => _disposed;

    /// <summary>Copy a captured set of view-owned settings, not text, carets, services or event handlers.
    /// Stops if a synchronous host callback changes the target binding/settings or disposes the view.
    /// Already completed settings are not rolled back. Subsequent edits remain independent.</summary>
    public void CopyViewSettingsFrom(SyntaxEditor source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(source);
        ObjectDisposedException.ThrowIf(source._disposed, source);
        Document.VerifyAccess(); source.Document.VerifyAccess();
        if (ReferenceEquals(source, this) || _copyingViewSettings) return;
        var desired = CaptureViewSettings(source); var expected = CaptureViewSettings(this);
        var document = Document; var language = SyntaxLanguage;
        bool Current() => !_disposed && ReferenceEquals(Document, document) && ReferenceEquals(SyntaxLanguage, language) && CaptureViewSettings(this) == expected;
        _copyingViewSettings = true;
        try
        {
            // Each setter can synchronously invoke host code. Compare the entire
            // previously observed settings between stages, not merely this property.
            for (var step = 0; step < 16; step++)
            {
                if (!Current()) return;
                switch (step)
                {
                    case 0: expected = expected with { ReadOnly = desired.ReadOnly }; IsReadOnly = desired.ReadOnly; break;
                    case 1: expected = expected with { Wrap = desired.Wrap }; WordWrap = desired.Wrap; break;
                    case 2: expected = expected with { Numbers = desired.Numbers }; ShowLineNumbers = desired.Numbers; break;
                    case 3: expected = expected with { Whitespace = desired.Whitespace }; ShowWhitespace = desired.Whitespace; break;
                    case 4: expected = expected with { Guides = desired.Guides }; ShowIndentGuides = desired.Guides; break;
                    case 5: expected = expected with { Ruler = desired.Ruler }; ShowRuler = desired.Ruler; break;
                    case 6: expected = expected with { Brackets = desired.Brackets }; AutoCompleteBrackets = desired.Brackets; break;
                    case 7: expected = expected with { Completion = desired.Completion }; AutoShowCompletion = desired.Completion; break;
                    case 8: expected = expected with { FontSize = desired.FontSize }; FontSize = desired.FontSize; break;
                    case 9: expected = expected with { Theme = desired.Theme }; Theme = desired.Theme; break;
                    case 10: expected = expected with { TabSize = desired.TabSize }; TabSize = desired.TabSize; break;
                    case 11: expected = expected with { Spaces = desired.Spaces }; ConvertTabsToSpaces = desired.Spaces; break;
                    case 12: expected = expected with { RulerColumn = desired.RulerColumn }; RulerColumn = desired.RulerColumn; break;
                    case 13: expected = expected with { SoftBackspace = desired.SoftBackspace }; CanBackspaceOverSpacesToTabStop = desired.SoftBackspace; break;
                    case 14: expected = expected with { ContextMenu = desired.ContextMenu }; IsDefaultContextMenuEnabled = desired.ContextMenu; break;
                    case 15: expected = expected with { MoveCaret = desired.MoveCaret }; CanMoveCaretForSecondaryPointerButton = desired.MoveCaret; break;
                }
            }
        }
        finally { _copyingViewSettings = false; }
    }
    private static CopiedViewSettings CaptureViewSettings(SyntaxEditor editor) => new(editor.IsReadOnly, editor.WordWrap, editor.ShowLineNumbers,
        editor.ShowWhitespace, editor.ShowIndentGuides, editor.ShowRuler, editor.AutoCompleteBrackets, editor.AutoShowCompletion, editor.FontSize, editor.Theme,
        editor.TabSize, editor.ConvertTabsToSpaces, editor.RulerColumn, editor.CanBackspaceOverSpacesToTabStop, editor.IsDefaultContextMenuEnabled, editor.CanMoveCaretForSecondaryPointerButton);
    private readonly record struct CopiedViewSettings(bool ReadOnly, bool Wrap, bool Numbers, bool Whitespace, bool Guides, bool Ruler,
        bool Brackets, bool Completion, double FontSize, EditorTheme Theme, int TabSize, bool Spaces, int RulerColumn, bool SoftBackspace, bool ContextMenu, bool MoveCaret);
}
