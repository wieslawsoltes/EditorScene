using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace EditorScene.Controls;

public sealed partial class SyntaxEditor
{
    private int _macroFocusGeneration;
    private bool _macroFocusPending;
    private readonly List<Action> _macroFocusUnsubscribe = [];
    /// <summary>
    /// Whether keyboard input can reach the text area after a macro menu transition.
    /// State changes are synchronous; menu closure and focus restoration are not.
    /// This property only observes the real focused element and pending work.
    /// </summary>
    public bool IsMacroInputReady => !_disposed && IsLoaded && !_macroFocusPending && XamlRoot is { } root
        && ReferenceEquals(FocusManager.GetFocusedElement(root), _editor.TextArea)
        && !VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Any(p => p.IsOpen && p.Child is MenuFlyoutPresenter);
    private void OnMacroUnloaded(object sender, RoutedEventArgs args) { CancelPendingMacroFocus(); ResetSelectionExpansion(); }
    private void CancelPendingMacroFocus()
    {
        _macroFocusGeneration++; _macroFocusPending = false;
        foreach (var unsubscribe in _macroFocusUnsubscribe) unsubscribe();
        _macroFocusUnsubscribe.Clear();
    }
    private void RestoreMacroFocusAfterMenusClose()
    {
        CancelPendingMacroFocus();
        if (_disposed || !IsLoaded || XamlRoot is not { } root) return;
        var generation = _macroFocusGeneration; var document = Document;
        _macroFocusPending = true;
        void Restore()
        {
            if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (generation != _macroFocusGeneration) return;
                _macroFocusPending = false;
                if (_disposed || !IsLoaded || !ReferenceEquals(XamlRoot, root) || !ReferenceEquals(Document, document)) return;
                // A user can open another menu/dialog before this deferred work
                // runs. Do not steal that new focus or dismiss the new popup.
                if (VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Any(p => p.IsOpen)) return;
                FocusEditor();
            })) _macroFocusPending = false;
        }
        var menus = VisualTreeHelper.GetOpenPopupsForXamlRoot(root)
            .Where(p => p.IsOpen && p.Child is MenuFlyoutPresenter).ToArray();
        if (menus.Length == 0) { Restore(); return; }
        var remaining = menus.Length;
        foreach (var popup in menus)
        {
            EventHandler<object>? closed = null;
            closed = (_, _) =>
            {
                popup.Closed -= closed;
                if (generation != _macroFocusGeneration) return;
                if (--remaining == 0)
                {
                    foreach (var unsubscribe in _macroFocusUnsubscribe) unsubscribe();
                    _macroFocusUnsubscribe.Clear(); Restore();
                }
            };
            popup.Closed += closed;
            _macroFocusUnsubscribe.Add(() => popup.Closed -= closed);
        }
    }
}
