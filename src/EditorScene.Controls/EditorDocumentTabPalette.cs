using EditorScene.Rendering.Skia;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace EditorScene.Controls;

/// <summary>UI-thread-owned brushes for caller-owned document tabs. Share one palette
/// across tabs using the same theme. Apply the caption foreground explicitly: native
/// content presenters can retain a foreground resolved under the preceding theme.
/// Brushes assigned to controls are shared and should not be mutated by the host.</summary>
public sealed class EditorDocumentTabPalette
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly Brush _foreground, _background, _chrome, _accent;
    public EditorTheme Theme { get; }

    public EditorDocumentTabPalette(EditorTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        Theme = theme;
        _foreground = SyntaxEditor.Brush(theme.Foreground);
        _background = SyntaxEditor.Brush(theme.Background);
        _chrome = SyntaxEditor.Brush(theme.Chrome);
        _accent = SyntaxEditor.Brush(theme.Accent);
    }

    /// <summary>Update active/inactive colors without replacing controls or changing
    /// text, focus, selection, event handlers or automation identities. Unchanged
    /// calls do not allocate brushes or write native dependency properties.</summary>
    public void Apply(Button tab, TextBlock caption, bool active)
    {
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Document tab palettes belong to their creating UI thread.");
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(caption);
        var background = active ? _background : _chrome;
        var border = active ? _accent : _chrome;
        if (!ReferenceEquals(tab.Background, background)) tab.Background = background;
        if (!ReferenceEquals(tab.BorderBrush, border)) tab.BorderBrush = border;
        if (!ReferenceEquals(tab.Foreground, _foreground)) tab.Foreground = _foreground;
        if (!ReferenceEquals(caption.Foreground, _foreground)) caption.Foreground = _foreground;
    }
}
