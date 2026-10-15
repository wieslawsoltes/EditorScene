using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace EditorScene.Controls;

public sealed partial class SyntaxEditor
{
    private void InitializeScrollBarIndicators()
    {
        // The pinned UnoEdit TextArea owns two standalone native ScrollBars.
        // Unlike ScrollViewer, it does not manage their indicator state. WinUI's
        // default None state fades out their template, leaving empty 14-DIP rails.
        // Set a local value once: it survives loading, theme/template changes and
        // document replacement without polling, global styles or new handlers.
        // Only configure the direct native rails, never a search/popup scrollbar.
        if (_editor.TextArea.Content is Panel layout)
            foreach (var bar in layout.Children.OfType<ScrollBar>())
            {
                bar.IndicatorMode = ScrollingIndicatorMode.MouseIndicator;
                AutomationProperties.SetName(bar, bar.Orientation == Orientation.Vertical
                    ? "Editor vertical scrollbar" : "Editor horizontal scrollbar");
            }
    }
}
