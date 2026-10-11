using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace EditorScene.Controls;

public sealed partial class SyntaxEditor
{
    private void FocusAfterEditCommand()
    {
        // Menu closure restores focus to the MenuBarItem after its callback.
        // Defer only menu-originated commands; ordinary keyboard commands
        // do not enqueue work. The existing generation/document guards
        // prevent stale callbacks from stealing a new popup's focus.
        if (IsLoaded && XamlRoot is { } root && VisualTreeHelper.GetOpenPopupsForXamlRoot(root)
            .Any(p => p.IsOpen && p.Child is MenuFlyoutPresenter))
            RestoreMacroFocusAfterMenusClose();
        else FocusEditor();
    }
}
