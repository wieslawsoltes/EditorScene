using EditorScene.Rendering.Skia;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EditorScene.Controls;

/// <summary>Scoped classic editor chrome. No application-global style mutation and no proprietary assets.</summary>
public static class EditorChrome
{
    /// <summary>Install in a page/control's Resources to keep native focus, input and automation templates.</summary>
    public static ResourceDictionary Create(EditorTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var resources = new ResourceDictionary();
        resources["ControlCornerRadius"] = new CornerRadius(0);
        resources["OverlayCornerRadius"] = new CornerRadius(0);
        resources["ButtonCornerRadius"] = new CornerRadius(0);
        resources["ComboBoxCornerRadius"] = new CornerRadius(0);
        resources["TextControlCornerRadius"] = new CornerRadius(0);
        resources["MenuFlyoutPresenterCornerRadius"] = new CornerRadius(0);
        resources["MenuFlyoutItemCornerRadius"] = new CornerRadius(0);
        resources["MenuFlyoutItemMinHeight"] = 24d;
        resources["MenuFlyoutItemPadding"] = new Thickness(10, 3, 10, 3);
        resources["MenuFlyoutSubItemPadding"] = new Thickness(10, 3, 10, 3);
        resources["MenuBarItemPadding"] = new Thickness(8, 3, 8, 3);
        resources["MenuFlyoutPresenterBackground"] = SyntaxEditor.Brush(theme.Background);
        resources["MenuFlyoutPresenterBorderBrush"] = SyntaxEditor.Brush(theme.Border);
        resources["MenuFlyoutItemForeground"] = SyntaxEditor.Brush(theme.Foreground);
        resources["MenuFlyoutItemBackgroundPointerOver"] = SyntaxEditor.Brush(theme.Selection);
        resources["MenuFlyoutSubItemBackgroundPointerOver"] = SyntaxEditor.Brush(theme.Selection);
        resources["ComboBoxBackground"] = SyntaxEditor.Brush(theme.Background);
        resources["ComboBoxBorderBrush"] = SyntaxEditor.Brush(theme.Border);
        resources["TextControlBackground"] = SyntaxEditor.Brush(theme.Background);
        resources["TextControlBorderBrush"] = SyntaxEditor.Brush(theme.Border);
        Add(typeof(Button), 24, new Thickness(5, 2, 5, 2));
        Add(typeof(ComboBox), 24, new Thickness(6, 0, 6, 0));
        Add(typeof(MenuBarItem), 24, new Thickness(8, 2, 8, 2));
        Add(typeof(MenuFlyoutItem), 24, new Thickness(10, 3, 10, 3));
        Add(typeof(ToggleMenuFlyoutItem), 24, new Thickness(10, 3, 10, 3));
        Add(typeof(MenuFlyoutSubItem), 24, new Thickness(10, 3, 10, 3));
        return resources;

        void Add(Type target, double minHeight, Thickness padding)
        {
            var style = new Style { TargetType = target };
            style.Setters.Add(new Setter(Control.FontSizeProperty, 12d));
            style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, minHeight));
            style.Setters.Add(new Setter(Control.PaddingProperty, padding));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
            resources[target] = style;
        }
    }
}
